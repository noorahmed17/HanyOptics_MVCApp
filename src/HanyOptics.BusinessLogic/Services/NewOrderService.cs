using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;

// Backs the "new order" wizard (customer -> items -> payment) on top of the DB's own
// sp_create_order / sp_add_order_item / sp_add_payment stored procedures. Those SPs own
// all the business-rule validation (frame availability via T3, invoice uniqueness, status
// transitions) and raise clear Arabic errors (RAISERROR) when they reject an operation -
// this service passes those messages straight through instead of re-validating the same
// rules in C#.
//
// While the wizard is running nothing is written: the order lives in an OrderDraft held
// by the caller (see IOrderDraftStore). Everything is inserted in one go by
// CommitDraftAsync when the user finishes the last step.
public class NewOrderService : INewOrderService
{
    private const string GenericErrorMessage = "حدث خطأ غير متوقع — حاول مرة أخرى";

    private readonly HanyOpticsDbContext _dbContext;
    private readonly ICurrentUser _currentUser;

    public NewOrderService(HanyOpticsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<CustomerLookupResult> LookupCustomerByPhoneAsync(string phone)
    {
        var customer = await _dbContext.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Phone == phone);

        if (customer is null)
            return new CustomerLookupResult { Found = false };

        return new CustomerLookupResult
        {
            Found = true,
            CustomerId = customer.CustomerId,
            Name = customer.Name,
            Phone = customer.Phone,
            Notes = customer.Notes
        };
    }

    public Task<Customer?> GetCustomerAsync(int customerId) =>
        _dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId);

    public async Task<FrameLookupResult> LookupFrameByBarcodeAsync(string barcode)
    {
        var frame = await _dbContext.Frames
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Barcode == barcode);

        if (frame is null)
            return new FrameLookupResult { Found = false, Message = "لا يوجد إطار بهذا الباركود" };

        if (frame.Status != FrameStatus.Available || frame.QtyAvailable <= 0)
            return new FrameLookupResult { Found = false, Message = "الإطار غير متاح حالياً (محجوز أو غير موجود بالمخزون)" };

        return new FrameLookupResult
        {
            Found = true,
            FrameId = frame.FrameId,
            Barcode = frame.Barcode,
            Brand = frame.Brand,
            ModelName = frame.ModelName,
            Color = frame.Color,
            Size = frame.Size,
            SellPrice = frame.SellPrice,
            CostPrice = frame.CostPrice,
            QtyAvailable = frame.QtyAvailable,
            TrackingType = frame.TrackingType.ToString(),
            Category = frame.Category.ToString()
        };
    }

    public async Task<IReadOnlyList<Doctor>> GetDoctorsAsync() =>
        await _dbContext.Doctors.AsNoTracking().OrderBy(d => d.Name).ToListAsync();

    public Task<bool> IsInvoiceNumberTakenAsync(string invoiceNumber) =>
        _dbContext.Orders.AsNoTracking().AnyAsync(o => o.InvoiceNumber == invoiceNumber);

    // ── Step 2: turn one filled-in form into a draft item ─────────────────
    // Pure validation - no reservation, no insert. The frame is only looked up so the
    // draft can carry its id and a label to show back to the user; it stays available to
    // everyone else until this order is actually committed.
    public async Task<DraftItemOutcome> ValidateItemAsync(NewOrderItemRequest request)
    {
        var needsFrame = request.ItemType is OrderItemType.FrameLenses or OrderItemType.FrameOnly;
        var needsLens = request.ItemType is OrderItemType.FrameLenses or OrderItemType.LensesReplace;

        // Whether the customer's own frame is relevant at all - it's an optional free-text
        // note, so this only decides whether the value is kept, never whether it's required.
        var usesExternalFrame = request.ItemType == OrderItemType.LensesReplace;

        var barcodeProvided = !string.IsNullOrWhiteSpace(request.FrameBarcode);

        var isBlank = !barcodeProvided
            && string.IsNullOrWhiteSpace(request.ExternalFrameNotes)
            && string.IsNullOrWhiteSpace(request.LensDescription)
            && !request.FrameAgreedPrice.HasValue
            && !request.LensSellPrice.HasValue;

        if (isBlank)
            return DraftItemOutcome.BlankItem();

        Frame? frame = null;
        if (needsFrame && barcodeProvided)
            frame = await _dbContext.Frames.AsNoTracking().FirstOrDefaultAsync(f => f.Barcode == request.FrameBarcode);

        var errors = new Dictionary<string, string>();

        if (needsFrame)
        {
            if (!barcodeProvided)
                errors[nameof(request.FrameBarcode)] = "أدخل باركود الإطار";
            else if (frame is null)
                errors[nameof(request.FrameBarcode)] = "لا يوجد إطار بهذا الباركود";
            else if (frame.Status != FrameStatus.Available || frame.QtyAvailable <= 0)
                errors[nameof(request.FrameBarcode)] = "الإطار غير متاح حالياً (محجوز أو غير موجود بالمخزون)";

            if (!request.FrameAgreedPrice.HasValue)
                errors[nameof(request.FrameAgreedPrice)] = "أدخل السعر المتفق عليه";
        }

        // For استبدال عدسات the customer brings their own frame, so describing it is a
        // nice-to-have note only - the lens details below are what actually matter.

        if (needsLens)
        {
            if (string.IsNullOrWhiteSpace(request.LensDescription))
                errors[nameof(request.LensDescription)] = "أدخل وصف العدسات";
            if (!request.LensSellPrice.HasValue)
                errors[nameof(request.LensSellPrice)] = "أدخل سعر بيع العدسات";
        }

        if (errors.Count > 0)
            return DraftItemOutcome.Invalid(errors);

        return DraftItemOutcome.Ok(new OrderDraftItem
        {
            ItemType = request.ItemType,
            FrameId = needsFrame ? frame?.FrameId : null,
            FrameBarcode = needsFrame ? frame?.Barcode : null,
            FrameLabel = needsFrame && frame is not null ? $"{frame.Brand} {frame.ModelName}".Trim() : null,
            FrameAgreedPrice = needsFrame ? request.FrameAgreedPrice ?? 0 : 0,
            ExternalFrameNotes = usesExternalFrame ? request.ExternalFrameNotes : null,
            LensDescription = needsLens ? request.LensDescription : null,
            LensSellPrice = needsLens ? request.LensSellPrice ?? 0 : 0,
            LensCostPrice = needsLens ? request.LensCostPrice ?? 0 : 0,
            Notes = request.Notes,
            RightSphere = request.RightSphere,
            RightCylinder = request.RightCylinder,
            RightAxis = request.RightAxis,
            LeftSphere = request.LeftSphere,
            LeftCylinder = request.LeftCylinder,
            LeftAxis = request.LeftAxis,
            Pd = request.Pd,
            AddPower = request.AddPower
        });
    }

    // ── Step 3: the single write ──────────────────────────────────────────
    // Creates the customer (if new), the order, every item and the opening payment as one
    // unit. Everything runs inside a transaction so a failure part-way through - a frame
    // someone else reserved in the meantime, a duplicate invoice number - leaves the
    // database exactly as it was, with no half-built order.
    public async Task<CommitDraftOutcome> CommitDraftAsync(OrderDraft draft, NewOrderPaymentRequest payment)
    {
        if (draft.Items.Count == 0)
            return CommitDraftOutcome.Failure("أضف بندًا واحدًا على الأقل قبل تسجيل الطلب");

        var amount = payment.Amount ?? 0;
        if (amount < 0)
            return CommitDraftOutcome.Failure("المبلغ غير صحيح");
        if (amount > draft.TotalAmount)
            return CommitDraftOutcome.Failure("المبلغ المدخل أكبر من إجمالي الطلب");

        var (phone, name) = ResolveCustomerIdentity(draft.IsWalkIn, draft.Phone, draft.CustomerName);

        Customer customer;
        bool isNewCustomer;
        try
        {
            (customer, isNewCustomer) = await ResolveOrCreateCustomerAsync(phone, name);
        }
        catch (Exception)
        {
            return CommitDraftOutcome.Failure(GenericErrorMessage);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var orderId = await CreateOrderAsync(draft, customer);

            foreach (var item in draft.Items)
                await AddOrderItemAsync(orderId, item);

            if (amount > 0)
                await AddPaymentAsync(orderId, amount, draft.TotalAmount, payment.PaymentMethod);

            await transaction.CommitAsync();
            return CommitDraftOutcome.Success(orderId);
        }
        catch (SqlException ex)
        {
            await SafeRollbackAsync(transaction);
            if (isNewCustomer)
                await TryDeleteOrphanCustomerAsync(customer.CustomerId);
            return CommitDraftOutcome.Failure(FriendlySqlMessage(ex));
        }
        catch (Exception)
        {
            await SafeRollbackAsync(transaction);
            if (isNewCustomer)
                await TryDeleteOrphanCustomerAsync(customer.CustomerId);
            return CommitDraftOutcome.Failure(GenericErrorMessage);
        }
    }

    private async Task<int> CreateOrderAsync(OrderDraft draft, Customer customer)
    {
        var orderIdParam = new SqlParameter("@p_order_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_create_order
                @invoice_number = @p_invoice_number,
                @customer_id    = @p_customer_id,
                @created_by     = @p_created_by,
                @customer_name  = @p_customer_name,
                @delivery_type  = @p_delivery_type,
                @doctor_id      = @p_doctor_id,
                @order_id       = @p_order_id OUTPUT
            """,
            new SqlParameter("@p_invoice_number", draft.InvoiceNumber),
            new SqlParameter("@p_customer_id", customer.CustomerId),
            new SqlParameter("@p_created_by", _currentUser.RequireUserId()),
            new SqlParameter("@p_customer_name", SqlDbType.NVarChar, 100) { Value = (object?)customer.Name ?? DBNull.Value },
            new SqlParameter("@p_delivery_type", DeliveryTypeToDb(draft.DeliveryType)),
            new SqlParameter("@p_doctor_id", SqlDbType.Int) { Value = (object?)draft.DoctorId ?? DBNull.Value },
            orderIdParam);

        return (int)orderIdParam.Value!;
    }

    private Task AddOrderItemAsync(int orderId, OrderDraftItem item)
    {
        var itemIdParam = new SqlParameter("@p_item_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

        return _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_add_order_item
                @order_id             = @p_order_id,
                @item_type            = @p_item_type,
                @frame_id             = @p_frame_id,
                @frame_agreed_price   = @p_frame_agreed_price,
                @external_frame_notes = @p_external_frame_notes,
                @lens_description     = @p_lens_description,
                @lens_sell_price      = @p_lens_sell_price,
                @lens_cost_price      = @p_lens_cost_price,
                @discount_reason      = @p_discount_reason,
                @right_sphere         = @p_right_sphere,
                @right_cylinder       = @p_right_cylinder,
                @right_axis           = @p_right_axis,
                @left_sphere          = @p_left_sphere,
                @left_cylinder        = @p_left_cylinder,
                @left_axis            = @p_left_axis,
                @pd                   = @p_pd,
                @add_power            = @p_add_power,
                @item_id              = @p_item_id OUTPUT
            """,
            new SqlParameter("@p_order_id", orderId),
            new SqlParameter("@p_item_type", ItemTypeToDb(item.ItemType)),
            new SqlParameter("@p_frame_id", SqlDbType.Int) { Value = (object?)item.FrameId ?? DBNull.Value },
            new SqlParameter("@p_frame_agreed_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = item.FrameAgreedPrice },
            new SqlParameter("@p_external_frame_notes", SqlDbType.NVarChar, 200) { Value = (object?)item.ExternalFrameNotes ?? DBNull.Value },
            new SqlParameter("@p_lens_description", SqlDbType.NVarChar, 200) { Value = (object?)item.LensDescription ?? DBNull.Value },
            new SqlParameter("@p_lens_sell_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = item.LensSellPrice },
            new SqlParameter("@p_lens_cost_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = item.LensCostPrice },
            new SqlParameter("@p_discount_reason", SqlDbType.NVarChar, 200) { Value = (object?)item.Notes ?? DBNull.Value },
            new SqlParameter("@p_right_sphere", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)item.RightSphere ?? DBNull.Value },
            new SqlParameter("@p_right_cylinder", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)item.RightCylinder ?? DBNull.Value },
            new SqlParameter("@p_right_axis", SqlDbType.Int) { Value = (object?)item.RightAxis ?? DBNull.Value },
            new SqlParameter("@p_left_sphere", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)item.LeftSphere ?? DBNull.Value },
            new SqlParameter("@p_left_cylinder", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)item.LeftCylinder ?? DBNull.Value },
            new SqlParameter("@p_left_axis", SqlDbType.Int) { Value = (object?)item.LeftAxis ?? DBNull.Value },
            new SqlParameter("@p_pd", SqlDbType.Decimal) { Precision = 5, Scale = 1, Value = (object?)item.Pd ?? DBNull.Value },
            new SqlParameter("@p_add_power", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)item.AddPower ?? DBNull.Value },
            itemIdParam);
    }

    // The only field the staff enters is the amount - payment_type is derived here, never
    // chosen by the user. This is the order's first payment by definition (the order is
    // being created in this same transaction), so it's either the full price or a deposit.
    private Task AddPaymentAsync(int orderId, decimal amount, decimal orderTotal, PaymentMethod method)
    {
        var paymentType = amount == orderTotal ? PaymentType.Full : PaymentType.Deposit;

        return _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_add_payment
                @order_id       = @p_order_id,
                @amount         = @p_amount,
                @payment_type   = @p_payment_type,
                @payment_method = @p_payment_method,
                @received_by    = @p_received_by
            """,
            new SqlParameter("@p_order_id", orderId),
            new SqlParameter("@p_amount", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = amount },
            new SqlParameter("@p_payment_type", PaymentTypeToDb(paymentType)),
            new SqlParameter("@p_payment_method", PaymentMethodToDb(method)),
            new SqlParameter("@p_received_by", _currentUser.RequireUserId()));
    }

    // The stored procedures manage their own BEGIN/COMMIT/ROLLBACK. When one of them rolls
    // back from inside our transaction the transaction is already doomed, so rolling it
    // back again can itself throw - which must not mask the original error.
    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
        }
    }

    // Best-effort cleanup - the NOT EXISTS guard means a customer this attempt just created
    // is only removed if it's still unused, so an existing customer (or the shared walk-in
    // row) can never be deleted by a failed order.
    private async Task TryDeleteOrphanCustomerAsync(int customerId)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "DELETE FROM customers WHERE customer_id = @p_customer_id AND NOT EXISTS (SELECT 1 FROM orders WHERE customer_id = @p_customer_id)",
                new SqlParameter("@p_customer_id", customerId));
        }
        catch
        {
        }
    }

    // Substitutes the placeholder identity for a walk-in, so the rest of the flow never
    // has to special-case it. Anything the form happened to post is ignored when the walk-in
    // box is ticked - the placeholder is what gets saved, by definition.
    private static (string? Phone, string? Name) ResolveCustomerIdentity(bool isWalkIn, string? phone, string? name)
    {
        if (isWalkIn)
            return (WalkInCustomer.Phone, WalkInCustomer.Name);

        return (
            string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            string.IsNullOrWhiteSpace(name) ? null : name.Trim());
    }

    private async Task<(Customer Customer, bool IsNew)> ResolveOrCreateCustomerAsync(string? phone, string? name)
    {
        // Matching on phone is what lets a returning customer (and every walk-in) reuse an
        // existing row. With no phone there's nothing dependable to match on - names repeat
        // and get misspelled - so a name-only customer always gets a fresh row rather than
        // risking silently attaching this order to an unrelated person with the same name.
        var customer = string.IsNullOrWhiteSpace(phone)
            ? null
            : await _dbContext.Customers.FirstOrDefaultAsync(c => c.Phone == phone);

        if (customer is null)
        {
            customer = new Customer
            {
                Phone = phone,
                Name = name,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Customers.Add(customer);
            await _dbContext.SaveChangesAsync();
            return (customer, true);
        }

        if (!string.IsNullOrWhiteSpace(name) && name != customer.Name)
        {
            customer.Name = name;
            await _dbContext.SaveChangesAsync();
        }

        return (customer, false);
    }

    // sp_* calls raise business-rule failures via RAISERROR (e.g. "رقم الفاتورة ده
    // مستخدم بالفعل", "فيه إطار (individual) مش متاح") - that Arabic text is meant to be
    // shown to staff as-is, so it's trusted here instead of being re-validated in C#.
    // Two exceptions get a generic Arabic fallback: a raw unique-constraint violation from
    // a check-then-insert race inside a SP, and SQL Server's "transaction count" complaint,
    // which it appends when a SP rolls back from inside our transaction - technical noise
    // about how the failure was handled rather than what actually went wrong.
    private static string FriendlySqlMessage(SqlException ex)
    {
        if (ex.Number is 2627 or 2601)
            return "البيانات مكررة — تحقق من رقم الفاتورة أو الباركود";

        var message = ex.Message;
        if (message.Contains("Transaction count", StringComparison.OrdinalIgnoreCase))
            return GenericErrorMessage;

        return message;
    }

    private static string DeliveryTypeToDb(DeliveryType type) => type == DeliveryType.Immediate ? "immediate" : "normal";

    private static string ItemTypeToDb(OrderItemType type) => type switch
    {
        OrderItemType.FrameLenses => "frame_lenses",
        OrderItemType.FrameOnly => "frame_only",
        OrderItemType.LensesReplace => "lenses_replace",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static string PaymentTypeToDb(PaymentType type) => type switch
    {
        PaymentType.Deposit => "deposit",
        PaymentType.Final => "final",
        PaymentType.Full => "full",
        PaymentType.Refund => "refund",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static string PaymentMethodToDb(PaymentMethod method) => method == PaymentMethod.Visa ? "visa" : "cash";
}
