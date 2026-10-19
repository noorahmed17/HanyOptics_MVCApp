using System.Globalization;
using System.Text.Json;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static HanyOptics.BusinessLogic.Services.SqlReader;

namespace HanyOptics.BusinessLogic.Services;

public class CorrectionService : ICorrectionService
{
    private static readonly CultureInfo ArEg = CultureInfo.GetCultureInfo("ar-EG");

    private const string LogSelect = """
        SELECT cl.changed_at, cl.entity, cl.action, cl.old_values, cl.new_values, cl.reason, u.name AS changed_by_name
        FROM dbo.corrections_log cl
        LEFT JOIN dbo.users u ON u.user_id = cl.changed_by
        """;

    private readonly HanyOpticsDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CorrectionService> _logger;

    public CorrectionService(HanyOpticsDbContext dbContext, ICurrentUser currentUser, ILogger<CorrectionService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<CorrectionOrder?> FindOrderAsync(string invoiceNumber) =>
        WithConnectionAsync(_dbContext, async connection =>
        {
            var header = (await QueryAsync(connection,
                """
                SELECT o.order_id, o.invoice_number, o.customer_name, c.phone, o.status, o.order_date, o.delivered_at,
                       o.total_amount, o.paid_amount, o.remaining_amount,
                       (SELECT TOP (1) l.old_status FROM dbo.order_status_log l
                         WHERE l.order_id = o.order_id AND l.new_status = N'delivered' ORDER BY l.log_id DESC) AS pre_delivery_status
                FROM dbo.orders o
                JOIN dbo.customers c ON c.customer_id = o.customer_id
                WHERE o.invoice_number = @inv;
                """,
                r => new
                {
                    OrderId = r.GetInt32(r.GetOrdinal("order_id")),
                    InvoiceNumber = r.GetString(r.GetOrdinal("invoice_number")),
                    CustomerName = r.NullableString("customer_name"),
                    Phone = r.NullableString("phone"),
                    Status = r.GetString(r.GetOrdinal("status")),
                    OrderDate = r.GetDateTime(r.GetOrdinal("order_date")),
                    DeliveredAt = r.NullableDateTime("delivered_at"),
                    Total = r.Decimal("total_amount"),
                    Paid = r.Decimal("paid_amount"),
                    Remaining = r.Decimal("remaining_amount"),
                    PreDelivery = r.NullableString("pre_delivery_status")
                },
                NVarChar("@inv", invoiceNumber.Trim(), 50))).FirstOrDefault();

            if (header is null)
                return null;

            var payments = await QueryAsync(connection,
                """
                SELECT p.payment_id, p.paid_at, p.payment_type, p.payment_method, p.amount, u.name AS received_by
                FROM dbo.payments p LEFT JOIN dbo.users u ON u.user_id = p.received_by
                WHERE p.order_id = @id ORDER BY p.paid_at, p.payment_id;
                """,
                r => new CorrectionPayment
                {
                    PaymentId = r.GetInt32(r.GetOrdinal("payment_id")),
                    PaidAt = r.GetDateTime(r.GetOrdinal("paid_at")),
                    PaymentType = r.GetString(r.GetOrdinal("payment_type")),
                    PaymentMethod = r.GetString(r.GetOrdinal("payment_method")),
                    Amount = r.Decimal("amount"),
                    ReceivedBy = r.NullableString("received_by")
                },
                new SqlParameter("@id", header.OrderId));

            // Only a delivered order can have a frame returned or exchanged from this screen.
            List<CorrectionItem> items = header.Status == "delivered"
                ? await QueryAsync(connection,
                    """
                    SELECT oi.item_id, f.barcode, f.brand, f.model_name, oi.frame_agreed_price
                    FROM dbo.order_items oi JOIN dbo.frames f ON f.frame_id = oi.frame_id
                    WHERE oi.order_id = @id AND oi.status = N'active' AND oi.item_type = N'frame_only'
                    ORDER BY oi.item_id;
                    """,
                    r => new CorrectionItem
                    {
                        ItemId = r.GetInt32(r.GetOrdinal("item_id")),
                        Barcode = r.GetString(r.GetOrdinal("barcode")),
                        Brand = r.NullableString("brand"),
                        ModelName = r.NullableString("model_name"),
                        Price = r.Decimal("frame_agreed_price")
                    },
                    new SqlParameter("@id", header.OrderId))
                : new List<CorrectionItem>();

            var log = await QueryAsync(connection,
                LogSelect + " WHERE cl.order_id = @id ORDER BY cl.correction_id DESC;",
                MapLog,
                new SqlParameter("@id", header.OrderId));

            return new CorrectionOrder
            {
                OrderId = header.OrderId,
                InvoiceNumber = header.InvoiceNumber,
                CustomerName = header.CustomerName,
                CustomerPhone = header.Phone,
                Status = header.Status,
                OrderDate = header.OrderDate,
                DeliveredAt = header.DeliveredAt,
                TotalAmount = header.Total,
                PaidAmount = header.Paid,
                RemainingAmount = header.Remaining,
                RevertTarget = RevertTarget(header.Status, header.PreDelivery),
                Payments = payments,
                ReturnableItems = items,
                Log = log
            };
        });

    public Task<IReadOnlyList<CorrectionLogEntry>> GetRecentExpenseCorrectionsAsync(int take = 20) =>
        WithConnectionAsync<IReadOnlyList<CorrectionLogEntry>>(_dbContext, async connection =>
            await QueryAsync(connection,
                "SELECT TOP (@take) * FROM (" + LogSelect + " WHERE cl.entity = N'expense') x ORDER BY changed_at DESC;",
                MapLog,
                new SqlParameter("@take", take)));

    public Task<OperationResult> SetOrderCustomerNameAsync(int orderId, string? customerName, string? reason) =>
        RunAsync("setting invoice customer name",
            "EXEC dbo.sp_admin_set_order_customer_name @order_id = @p_order, @customer_name = @p_name, @changed_by = @p_user, @reason = @p_reason",
            new SqlParameter("@p_order", orderId),
            NVarChar("@p_name", Clean(customerName), 100),
            new SqlParameter("@p_user", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(reason), 500));

    public Task<OperationResult> CorrectPaymentAsync(int paymentId, string? newMethod, decimal? newAmount, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(OperationResult.Failure("اكتب سبب التصحيح."));

        return RunAsync("correcting payment",
            "EXEC dbo.sp_admin_correct_payment @payment_id = @p_payment, @changed_by = @p_user, @reason = @p_reason, @new_method = @p_method, @new_amount = @p_amount",
            new SqlParameter("@p_payment", paymentId),
            new SqlParameter("@p_user", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(reason), 500),
            NVarChar("@p_method", Clean(newMethod), 10),
            Money("@p_amount", newAmount));
    }

    public Task<OperationResult> DeletePaymentAsync(int paymentId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(OperationResult.Failure("اكتب سبب الحذف."));

        return RunAsync("deleting payment",
            "EXEC dbo.sp_admin_delete_payment @payment_id = @p_payment, @changed_by = @p_user, @reason = @p_reason",
            new SqlParameter("@p_payment", paymentId),
            new SqlParameter("@p_user", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(reason), 500));
    }

    public Task<OperationResult> RevertOrderStatusAsync(int orderId, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Task.FromResult(OperationResult.Failure("اكتب سبب التصحيح."));

        return RunAsync("reverting order status",
            "EXEC dbo.sp_admin_revert_order_status @order_id = @p_order, @changed_by = @p_user, @reason = @p_reason",
            new SqlParameter("@p_order", orderId),
            new SqlParameter("@p_user", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(reason), 500));
    }

    // مرتجع بعد التسليم: sp_admin_return_delivered_frame cancels the item, puts the frame
    // back on sale and records exactly the refund typed here.
    public Task<OperationResult> ReturnDeliveredFrameAsync(ReturnFrameRequest request)
    {
        if (request.RefundAmount is null || request.RefundAmount < 0)
            return Task.FromResult(OperationResult.Failure("اكتب المبلغ المرتجع للعميل، حتى لو صفر."));
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Task.FromResult(OperationResult.Failure("اكتب سبب المرتجع."));

        return RunAsync("returning delivered frame",
            """
            EXEC dbo.sp_admin_return_delivered_frame
                @item_id       = @p_item,
                @refund_amount = @p_amount,
                @refund_method = @p_method,
                @changed_by    = @p_user,
                @reason        = @p_reason
            """,
            new SqlParameter("@p_item", request.ItemId),
            Money("@p_amount", request.RefundAmount),
            NVarChar("@p_method", Clean(request.RefundMethod) ?? PaymentMethods.Cash, 10),
            new SqlParameter("@p_user", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(request.Reason), 500));
    }

    // استبدال بعد التسليم: sp_admin_exchange_delivered_frame swaps the frame, and records
    // exactly the amount typed here - paid or handed back, depending on which way the
    // difference goes. Lens values are only sent for a frame-and-lenses exchange.
    public Task<OperationResult> ExchangeDeliveredFrameAsync(ExchangeFrameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewBarcode))
            return Task.FromResult(OperationResult.Failure("اكتب باركود الإطار الجديد."));
        if (request.NewFramePrice is null || request.NewFramePrice < 0)
            return Task.FromResult(OperationResult.Failure("اكتب سعر الإطار الجديد."));
        if (request.WithLenses && (request.LensPrice is null || request.LensPrice <= 0))
            return Task.FromResult(OperationResult.Failure("اكتب سعر العدسات."));
        if (request.WithLenses && string.IsNullOrWhiteSpace(request.LensDescription))
            return Task.FromResult(OperationResult.Failure("اكتب وصف العدسات."));
        if (request.Amount is null || request.Amount < 0)
            return Task.FromResult(OperationResult.Failure("اكتب المبلغ، حتى لو صفر."));
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Task.FromResult(OperationResult.Failure("اكتب سبب الاستبدال."));

        return RunAsync("exchanging delivered frame",
            """
            EXEC dbo.sp_admin_exchange_delivered_frame
                @item_id          = @p_item,
                @new_barcode      = @p_barcode,
                @new_frame_price  = @p_price,
                @with_lenses      = @p_with_lenses,
                @lens_price       = @p_lens_price,
                @lens_description = @p_lens_desc,
                @amount           = @p_amount,
                @payment_method   = @p_method,
                @changed_by       = @p_user,
                @reason           = @p_reason
            """,
            new SqlParameter("@p_item", request.ItemId),
            NVarChar("@p_barcode", Clean(request.NewBarcode), 50),
            Money("@p_price", request.NewFramePrice),
            new SqlParameter("@p_with_lenses", request.WithLenses),
            Money("@p_lens_price", request.WithLenses ? request.LensPrice : null),
            NVarChar("@p_lens_desc", request.WithLenses ? Clean(request.LensDescription) : null, 200),
            Money("@p_amount", request.Amount),
            NVarChar("@p_method", Clean(request.PaymentMethod) ?? PaymentMethods.Cash, 10),
            new SqlParameter("@p_user", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(request.Reason), 500));
    }

    // Mirrors sp_admin_revert_order_status so the button can say where it will go before
    // it is pressed; the procedure still decides for itself when it runs.
    private static string? RevertTarget(string status, string? preDelivery) => status switch
    {
        "delivered" => preDelivery is "sold" or "ready" ? preDelivery : "ready",
        "ready" => "sold",
        _ => null
    };

    private async Task<OperationResult> RunAsync(string what, string sql, params SqlParameter[] parameters)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(sql, parameters);
            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "SQL error {What}. SqlErrors={SqlErrors}", what, StoredProcedureErrors.Describe(ex));
            return OperationResult.Failure(StoredProcedureErrors.ToUserMessage(ex, "بيانات مكررة"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error {What}.", what);
            return OperationResult.Failure(StoredProcedureErrors.GenericMessage);
        }
    }

    private static CorrectionLogEntry MapLog(SqlDataReader r)
    {
        var action = r.GetString(r.GetOrdinal("action"));
        var entity = r.GetString(r.GetOrdinal("entity"));
        return new CorrectionLogEntry
        {
            ChangedAt = r.GetDateTime(r.GetOrdinal("changed_at")),
            Entity = entity,
            Action = action,
            ActionLabel = ActionLabel(entity, action),
            Before = Describe(r.NullableString("old_values")),
            After = Describe(r.NullableString("new_values")),
            Reason = r.GetString(r.GetOrdinal("reason")),
            ChangedBy = r.NullableString("changed_by_name")
        };
    }

    private static string ActionLabel(string entity, string action) => (entity, action) switch
    {
        ("order", "set_customer_name") => "اسم العميل على الفاتورة",
        ("order", "revert_status") => "رجوع حالة الطلب",
        ("payment", "correct") => "تصحيح دفعة",
        ("payment", "delete") => "حذف دفعة",
        ("expense", "update") => "تعديل حركة",
        ("customer", "update") => "تعديل بيانات عميل",
        ("order", "return_item") => "مرتجع بعد التسليم",
        ("order", "exchange_frame") => "استبدال بعد التسليم",
        _ => action
    };

    // Fields worth showing a person, in the order they read naturally. Ids and audit
    // columns in the stored JSON are left out - they mean nothing on this screen.
    private static readonly (string Key, string Label)[] ShownFields =
    [
        ("barcode", "الإطار"),
        ("customer_name", "الاسم"),
        ("name", "الاسم"),
        ("phone", "الهاتف"),
        ("status", "الحالة"),
        ("entry_type", "النوع"),
        ("payment_type", "النوع"),
        ("payment_method", "الطريقة"),
        ("funding_source", "المصدر"),
        ("amount", "المبلغ"),
        ("category", "التصنيف"),
        ("description", "الوصف"),
        ("paid_at", "التاريخ")
    ];

    private static string? Describe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var parts = new List<string>();
            foreach (var (key, label) in ShownFields)
            {
                if (!doc.RootElement.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
                    continue;

                parts.Add($"{label}: {FormatValue(key, value)}");
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string FormatValue(string key, JsonElement value)
    {
        if (key == "amount" && value.TryGetDecimal(out var amount))
            return amount.ToString("N0", ArEg) + " ج";

        if (key == "paid_at" && value.TryGetDateTime(out var at))
            return at.ToString("d MMM yyyy h:mm tt", ArEg);

        var text = value.ToString();
        return text switch
        {
            "cash" => "نقدًا",
            "visa" => "بطاقة",
            "drawer" => "من الدرج",
            "outside" => "من خارج الدرج",
            "expense" => "مصروف",
            "owner_draw" => "مسحوبات المالك",
            "income" => "إيراد آخر",
            "deposit" => "عربون",
            "final" => "باقي",
            "full" => "كامل",
            "refund" => "استرجاع",
            "sold" => "تم البيع",
            "ready" => "جاهز",
            "delivered" => "تم التسليم",
            "cancelled" => "ملغي",
            _ => text
        };
    }
}
