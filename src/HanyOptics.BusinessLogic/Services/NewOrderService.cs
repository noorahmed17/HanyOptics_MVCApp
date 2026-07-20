using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;

// Orchestrates the "new order" wizard (customer -> items -> payment) on top of the
// DB's own sp_create_order / sp_add_order_item / sp_add_payment stored procedures.
// Those SPs own all the business-rule validation (frame availability via T3, status
// transitions, etc.) and raise clear Arabic errors (RAISERROR) when they reject an
// operation - this service passes those messages straight through to the caller
// instead of re-validating the same rules in C#.
public class NewOrderService : INewOrderService
{
    // TODO: the web portal's Identity login (AspNetUsers) has no mapping yet to the
    // business `users` table (POS staff) that orders.created_by / payments.received_by
    // reference. Until that mapping exists, orders/payments created through this wizard
    // are attributed to the seeded staff user_id=1 ("هاني"/admin).
    private const int DefaultStaffUserId = 1;

    private const string GenericErrorMessage = "حدث خطأ غير متوقع — حاول مرة أخرى";

    private readonly HanyOpticsDbContext _dbContext;

    public NewOrderService(HanyOpticsDbContext dbContext)
    {
        _dbContext = dbContext;
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

    public async Task<StartOrderOutcome> StartOrderAsync(NewOrderCustomerRequest request)
    {
        Customer customer;
        try
        {
            customer = await ResolveOrCreateCustomerAsync(request.Phone, request.CustomerName);
        }
        catch (Exception)
        {
            return StartOrderOutcome.Failure(GenericErrorMessage);
        }

        var orderIdParam = new SqlParameter("@p_order_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

        try
        {
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
                new SqlParameter("@p_invoice_number", request.InvoiceNumber),
                new SqlParameter("@p_customer_id", customer.CustomerId),
                new SqlParameter("@p_created_by", DefaultStaffUserId),
                new SqlParameter("@p_customer_name", SqlDbType.NVarChar, 100) { Value = (object?)customer.Name ?? DBNull.Value },
                new SqlParameter("@p_delivery_type", DeliveryTypeToDb(request.DeliveryType)),
                new SqlParameter("@p_doctor_id", SqlDbType.Int) { Value = (object?)request.DoctorId ?? DBNull.Value },
                orderIdParam);

            return StartOrderOutcome.Success((int)orderIdParam.Value!);
        }
        catch (SqlException ex)
        {
            return StartOrderOutcome.Failure(FriendlySqlMessage(ex));
        }
        catch (Exception)
        {
            return StartOrderOutcome.Failure(GenericErrorMessage);
        }
    }

    // "رجوع" from step 2 back to step 1, for an order that already exists. There's no SP
    // for this (sp_create_order only creates), so it stays a direct EF update - only the
    // customer (via phone) and delivery type are mutated; invoice number and doctor aren't
    // touched here at all.
    public async Task<OperationResult> UpdateOrderCustomerAsync(EditOrderCustomerRequest request)
    {
        try
        {
            var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.OrderId == request.OrderId);
            if (order is null)
                return OperationResult.Failure("الطلب غير موجود");

            if (order.Status != OrderStatus.Sold)
                return OperationResult.Failure("لا يمكن تعديل بيانات هذا الطلب بعد تغيير حالته");

            var customer = await ResolveOrCreateCustomerAsync(request.Phone, request.CustomerName);

            order.CustomerId = customer.CustomerId;
            order.CustomerName = customer.Name;
            order.DeliveryType = request.DeliveryType;

            await _dbContext.SaveChangesAsync();
            return OperationResult.Success();
        }
        catch (Exception)
        {
            return OperationResult.Failure(GenericErrorMessage);
        }
    }

    public async Task<AddOrderItemOutcome> AddOrderItemAsync(NewOrderItemRequest request)
    {
        var needsFrame = request.ItemType is OrderItemType.FrameLenses or OrderItemType.FrameOnly;
        var needsExternalFrame = request.ItemType == OrderItemType.LensesReplace;
        var needsLens = request.ItemType is OrderItemType.FrameLenses or OrderItemType.LensesReplace;

        var barcodeProvided = !string.IsNullOrWhiteSpace(request.FrameBarcode);

        var isBlank = !barcodeProvided
            && string.IsNullOrWhiteSpace(request.ExternalFrameNotes)
            && string.IsNullOrWhiteSpace(request.LensDescription)
            && !request.FrameAgreedPrice.HasValue
            && !request.LensSellPrice.HasValue;

        if (isBlank)
            return new AddOrderItemOutcome { Blank = true };

        // Only resolve the barcode to a frame_id here - availability (status/qty) is
        // validated inside sp_add_order_item's own transaction by trigger T3, which
        // closes the race window two staff members reserving the same frame at once
        // used to have against a separate app-side check.
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

            if (!request.FrameAgreedPrice.HasValue)
                errors[nameof(request.FrameAgreedPrice)] = "أدخل السعر المتفق عليه";
        }

        if (needsExternalFrame && string.IsNullOrWhiteSpace(request.ExternalFrameNotes))
            errors[nameof(request.ExternalFrameNotes)] = "أدخل وصف إطار الزبون";

        if (needsLens)
        {
            if (string.IsNullOrWhiteSpace(request.LensDescription))
                errors[nameof(request.LensDescription)] = "أدخل وصف العدسات";
            if (!request.LensSellPrice.HasValue)
                errors[nameof(request.LensSellPrice)] = "أدخل سعر بيع العدسات";
        }

        if (errors.Count > 0)
            return new AddOrderItemOutcome { FieldErrors = errors };

        var itemIdParam = new SqlParameter("@p_item_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
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
                new SqlParameter("@p_order_id", request.OrderId),
                new SqlParameter("@p_item_type", ItemTypeToDb(request.ItemType)),
                new SqlParameter("@p_frame_id", SqlDbType.Int) { Value = (object?)frame?.FrameId ?? DBNull.Value },
                new SqlParameter("@p_frame_agreed_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = needsFrame ? request.FrameAgreedPrice ?? 0 : 0 },
                new SqlParameter("@p_external_frame_notes", SqlDbType.NVarChar, 200) { Value = needsExternalFrame ? (object?)request.ExternalFrameNotes ?? DBNull.Value : DBNull.Value },
                new SqlParameter("@p_lens_description", SqlDbType.NVarChar, 200) { Value = needsLens ? (object?)request.LensDescription ?? DBNull.Value : DBNull.Value },
                new SqlParameter("@p_lens_sell_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = needsLens ? request.LensSellPrice ?? 0 : 0 },
                new SqlParameter("@p_lens_cost_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = needsLens ? request.LensCostPrice ?? 0 : 0 },
                new SqlParameter("@p_discount_reason", SqlDbType.NVarChar, 200) { Value = (object?)request.Notes ?? DBNull.Value },
                new SqlParameter("@p_right_sphere", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)request.RightSphere ?? DBNull.Value },
                new SqlParameter("@p_right_cylinder", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)request.RightCylinder ?? DBNull.Value },
                new SqlParameter("@p_right_axis", SqlDbType.Int) { Value = (object?)request.RightAxis ?? DBNull.Value },
                new SqlParameter("@p_left_sphere", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)request.LeftSphere ?? DBNull.Value },
                new SqlParameter("@p_left_cylinder", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)request.LeftCylinder ?? DBNull.Value },
                new SqlParameter("@p_left_axis", SqlDbType.Int) { Value = (object?)request.LeftAxis ?? DBNull.Value },
                new SqlParameter("@p_pd", SqlDbType.Decimal) { Precision = 5, Scale = 1, Value = (object?)request.Pd ?? DBNull.Value },
                new SqlParameter("@p_add_power", SqlDbType.Decimal) { Precision = 5, Scale = 2, Value = (object?)request.AddPower ?? DBNull.Value },
                itemIdParam);

            if (!string.IsNullOrWhiteSpace(request.CustomerNameOnInvoice))
            {
                var order = await _dbContext.Orders.FirstAsync(o => o.OrderId == request.OrderId);
                if (order.CustomerName != request.CustomerNameOnInvoice)
                {
                    order.CustomerName = request.CustomerNameOnInvoice;
                    await _dbContext.SaveChangesAsync();
                }
            }

            return new AddOrderItemOutcome { Added = true };
        }
        catch (SqlException ex)
        {
            return new AddOrderItemOutcome
            {
                FieldErrors = new Dictionary<string, string> { [string.Empty] = FriendlySqlMessage(ex) }
            };
        }
        catch (Exception)
        {
            return new AddOrderItemOutcome
            {
                FieldErrors = new Dictionary<string, string> { [string.Empty] = GenericErrorMessage }
            };
        }
    }

    // The only field the staff enters is the amount - payment_type is derived here from
    // the order's current paid/remaining state and passed to sp_add_payment, never chosen
    // by the user. See the rule table: first payment covering the full total -> full,
    // first payment less than the total -> deposit, later payment that closes the
    // remaining balance exactly -> final, later partial payment -> deposit.
    public async Task<OperationResult> AddPaymentAsync(NewOrderPaymentRequest request)
    {
        if (request.Amount <= 0)
            return OperationResult.Success();

        var order = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == request.OrderId);
        if (order is null)
            return OperationResult.Failure("الطلب غير موجود");

        var remaining = order.TotalAmount - order.PaidAmount;
        if (request.Amount > remaining)
            return OperationResult.Failure("المبلغ المدخل أكبر من المتبقي على الطلب");

        var paymentType = order.PaidAmount > 0
            ? (request.Amount == remaining ? PaymentType.Final : PaymentType.Deposit)
            : (request.Amount == remaining ? PaymentType.Full : PaymentType.Deposit);

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                EXEC sp_add_payment
                    @order_id       = @p_order_id,
                    @amount         = @p_amount,
                    @payment_type   = @p_payment_type,
                    @payment_method = @p_payment_method,
                    @received_by    = @p_received_by
                """,
                new SqlParameter("@p_order_id", request.OrderId),
                new SqlParameter("@p_amount", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = request.Amount },
                new SqlParameter("@p_payment_type", PaymentTypeToDb(paymentType)),
                new SqlParameter("@p_payment_method", PaymentMethodToDb(request.PaymentMethod)),
                new SqlParameter("@p_received_by", DefaultStaffUserId));

            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            return OperationResult.Failure(FriendlySqlMessage(ex));
        }
        catch (Exception)
        {
            return OperationResult.Failure(GenericErrorMessage);
        }
    }

    public Task<Order?> GetOrderForWizardAsync(int orderId) =>
        _dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

    // Abandoning a wizard-in-progress order goes through the real sp_update_order_status
    // stored procedure (not a plain UPDATE) so it gets the same guards, order_status_log
    // entry, and stock-return behavior as any other cancellation in the app.
    public async Task<OperationResult> CancelOrderAsync(int orderId)
    {
        const string notes = "تم الإلغاء من معالج الطلب الجديد قبل الاكتمال";
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "EXEC sp_update_order_status @order_id=@p_order_id, @new_status=@p_new_status, @changed_by=@p_changed_by, @notes=@p_notes",
                new SqlParameter("@p_order_id", orderId),
                new SqlParameter("@p_new_status", "cancelled"),
                new SqlParameter("@p_changed_by", DefaultStaffUserId),
                new SqlParameter("@p_notes", notes));
            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            return OperationResult.Failure(FriendlySqlMessage(ex));
        }
        catch (Exception)
        {
            return OperationResult.Failure(GenericErrorMessage);
        }
    }

    private async Task<Customer> ResolveOrCreateCustomerAsync(string phone, string? name)
    {
        var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.Phone == phone);
        if (customer is null)
        {
            customer = new Customer
            {
                Phone = phone,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Customers.Add(customer);
            await _dbContext.SaveChangesAsync();
        }
        else if (!string.IsNullOrWhiteSpace(name) && name != customer.Name)
        {
            customer.Name = name;
            await _dbContext.SaveChangesAsync();
        }

        return customer;
    }

    // sp_* calls raise business-rule failures via RAISERROR (e.g. "رقم الفاتورة ده
    // مستخدم بالفعل", "فيه إطار (individual) مش متاح") - that Arabic text is meant to be
    // shown to staff as-is, so it's trusted here instead of being re-validated in C#.
    // The one exception is a raw unique-constraint violation slipping through a rare
    // check-then-insert race inside a SP, which arrives as an English SqlException with
    // no RAISERROR behind it - that gets a generic Arabic fallback instead.
    private static string FriendlySqlMessage(SqlException ex) =>
        ex.Number is 2627 or 2601 ? "البيانات مكررة — تحقق من رقم الفاتورة أو الباركود" : ex.Message;

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
