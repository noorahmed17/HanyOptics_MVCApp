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
        _ => action
    };

    // Fields worth showing a person, in the order they read naturally. Ids and audit
    // columns in the stored JSON are left out - they mean nothing on this screen.
    private static readonly (string Key, string Label)[] ShownFields =
    [
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
