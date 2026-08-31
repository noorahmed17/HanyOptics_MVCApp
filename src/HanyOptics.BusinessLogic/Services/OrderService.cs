using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;
public class OrderService : IOrderService
{
    // Shared with NewOrderService - see StoredProcedureErrors for why this isn't a
    // per-service copy.
    private const string DuplicateDataMessage = "بيانات مكررة";

    private readonly HanyOpticsDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<OrderService> _logger;

    public OrderService(HanyOpticsDbContext dbContext, ICurrentUser currentUser, ILogger<OrderService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<Order?> GetByIdAsync(int orderId) =>
        _dbContext.Orders
            .Include(o => o.OrderItems).ThenInclude(oi => oi.Prescription)
            .Include(o => o.Payments)
            .Include(o => o.StatusLogs)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId);

    public Task<Doctor?> GetDoctorByIdAsync(int doctorId) =>
        _dbContext.Doctors.AsNoTracking().FirstOrDefaultAsync(d => d.DoctorId == doctorId);

    public async Task<IReadOnlyList<Order>> GetAllAsync(int take = 50) =>
        await _dbContext.Orders
            .Include(o => o.OrderItems)
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .Take(take)
            .ToListAsync();

    public async Task<int> CreateOrderAsync(Order order)
    {
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync();
        return order.OrderId;
    }

    public async Task<PagedResult<OrderListItem>> GetOrderListAsync(
        OrderStatus? status = null,
        DeliveryType? deliveryType = null,
        int? customerId = null,
        DateTime? fromDate = null,
        string? searchTerm = null,
        int? page = null,
        int? pageSize = null)
    {
        // No Include here. The list needs each order's item types and a count, not the item
        // rows themselves - Include would drag every column of every item across for every
        // order on the page. The projection below asks the database for just those two
        // things instead.
        var query = _dbContext.Orders.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (deliveryType.HasValue)
            query = query.Where(o => o.DeliveryType == deliveryType.Value);
        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);
        if (fromDate.HasValue)
            query = query.Where(o => o.OrderDate >= fromDate.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm.Trim()}%";

            // Phone lives on customers, not orders, so a phone search needs its own
            // lookup rather than a LIKE directly on the orders query.
            var matchingCustomerIds = await _dbContext.Customers
                .AsNoTracking()
                .Where(c => c.Phone != null && EF.Functions.Like(c.Phone, pattern))
                .Select(c => c.CustomerId)
                .ToListAsync();

            query = query.Where(o =>
                EF.Functions.Like(o.InvoiceNumber, pattern) ||
                (o.CustomerName != null && EF.Functions.Like(o.CustomerName, pattern)) ||
                matchingCustomerIds.Contains(o.CustomerId));
        }

        var total = await query.CountAsync();
        var (currentPage, size) = PagedResult<OrderListItem>.Normalise(page, pageSize ?? PageSizes.Orders, total);

        // Ordered by date then id: two orders rung up in the same second would otherwise be
        // free to swap places between one page and the next, so a row could show twice while
        // another never appeared.
        //
        // The phone is a join rather than a second round trip, and the item types come back
        // as a small projected list - so one query returns exactly what the page renders.
        var items = await query
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.OrderId)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(o => new OrderListItem
            {
                OrderId = o.OrderId,
                InvoiceNumber = o.InvoiceNumber,
                OrderDate = o.OrderDate,
                CustomerName = o.CustomerName,
                CustomerPhone = _dbContext.Customers
                    .Where(c => c.CustomerId == o.CustomerId)
                    .Select(c => c.Phone)
                    .FirstOrDefault(),
                ItemTypes = o.OrderItems.Select(i => i.ItemType).Distinct().ToList(),
                ItemCount = o.OrderItems.Count,
                DeliveryType = o.DeliveryType,
                Status = o.Status,
                TotalAmount = o.TotalAmount,
                PaidAmount = o.PaidAmount,
                RemainingAmount = o.RemainingAmount
            })
            .ToListAsync();

        return new PagedResult<OrderListItem>
        {
            Items = items,
            TotalCount = total,
            Page = currentPage,
            PageSize = size
        };
    }

    // ── Staging: validate + describe an edit for the popup's pending list ─────────
    // Nothing here writes to the database - see CommitPendingEditsAsync for the one
    // place that actually does.

    public async Task<StagedEditOutcome> BuildStatusChangeEditAsync(int orderId, OrderStatus newStatus, string? notes)
    {
        var orderExists = await _dbContext.Orders.AsNoTracking().AnyAsync(o => o.OrderId == orderId);
        if (!orderExists)
            return StagedEditOutcome.Failure("الطلب غير موجود");

        // Every status is offered regardless of the order's current one -
        // sp_update_order_status is the sole authority on which transitions are actually
        // valid, and rejects anything else with a clear Arabic RAISERROR message at
        // commit time (see CommitPendingEditsAsync / StoredProcedureErrors.ToUserMessage).
        var summary = $"تحديث الحالة إلى: {StatusLabel(newStatus)}";
        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" — {notes}";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.StatusChange,
            NewStatus = newStatus,
            Notes = notes,
            Summary = summary
        });
    }

    public async Task<StagedEditOutcome> BuildFrameSwapEditAsync(int itemId, int? newFrameId, decimal newFrameAgreedPrice, bool returnOldFrameToStock, string? externalFrameNotes, string? notes)
    {
        var item = await _dbContext.OrderItems.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == itemId);
        if (item is null)
            return StagedEditOutcome.Failure("العنصر غير موجود");

        if (item.Status != OrderItemStatus.Active)
            return StagedEditOutcome.Failure("هذا البند ملغي");

        var usesExternalFrame = newFrameId is null;
        string replacementLabel;

        if (usesExternalFrame)
        {
            // Dropping the inventory frame turns the item into استبدال عدسات, which only
            // makes sense while it still has lenses to fit. A إطار فقط item with no frame
            // has nothing left in it - that is a cancellation, not a frame change.
            if (item.ItemType == OrderItemType.FrameOnly)
                return StagedEditOutcome.Failure("بند «إطار فقط» لا يمكن تحويله لإطار الزبون — ألغِ البند بدلاً من ذلك");

            replacementLabel = string.IsNullOrWhiteSpace(externalFrameNotes)
                ? "إطار الزبون"
                : $"إطار الزبون ({externalFrameNotes})";
        }
        else
        {
            var frame = await _dbContext.Frames.AsNoTracking().FirstOrDefaultAsync(f => f.FrameId == newFrameId);
            if (frame is null)
                return StagedEditOutcome.Failure("الإطار غير موجود");

            replacementLabel = FrameLabel(frame);
        }

        // The two dispositions have very different consequences for stock, so the summary
        // says which one is staged rather than leaving it to be found after تأكيد.
        var oldFrameNote = item.FrameId.HasValue
            ? (returnOldFrameToStock ? " (الإطار القديم يعود للمخزون)" : " (الإطار القديم يُشطب)")
            : string.Empty;

        var summary = $"تغيير الإطار إلى {replacementLabel} — {newFrameAgreedPrice:N0} ج{oldFrameNote}";

        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.FrameSwap,
            ItemId = itemId,
            NewFrameId = newFrameId,
            NewFrameAgreedPrice = newFrameAgreedPrice,
            ReturnOldFrameToStock = returnOldFrameToStock,
            UsesExternalFrame = usesExternalFrame,
            ExternalFrameNotes = externalFrameNotes,
            Notes = notes,
            Summary = summary
        });
    }

    public async Task<StagedEditOutcome> BuildFrameCompensationEditAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes)
    {
        var itemExists = await _dbContext.OrderItems.AsNoTracking().AnyAsync(i => i.ItemId == itemId);
        if (!itemExists)
            return StagedEditOutcome.Failure("العنصر غير موجود");

        var frame = await _dbContext.Frames.AsNoTracking().FirstOrDefaultAsync(f => f.FrameId == newFrameId);
        if (frame is null)
            return StagedEditOutcome.Failure("الإطار غير موجود");

        var summary = $"تعيين إطار تعويضي: {FrameLabel(frame)} — {newFrameAgreedPrice:N0} ج";
        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.FrameCompensation,
            ItemId = itemId,
            NewFrameId = newFrameId,
            NewFrameAgreedPrice = newFrameAgreedPrice,
            Notes = notes,
            Summary = summary
        });
    }

    public async Task<StagedEditOutcome> BuildPaymentEditAsync(int orderId, decimal amount, PaymentMethod method, string? notes)
    {
        var order = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order is null)
            return StagedEditOutcome.Failure("الطلب غير موجود");

        if (amount <= 0)
            return StagedEditOutcome.Failure("المبلغ غير صحيح");

        // sp_add_payment only allows refunds against a cancelled order, and this path never
        // creates one - caught here so it's said plainly rather than at commit time.
        if (order.Status == OrderStatus.Cancelled)
            return StagedEditOutcome.Failure("الطلب ملغي — لا يمكن تسجيل دفعة عليه");

        if (order.RemainingAmount <= 0)
            return StagedEditOutcome.Failure("الطلب مدفوع بالكامل — لا يوجد مبلغ متبقٍ");

        // sp_add_payment permits overpayment (it leaves correcting it to a refund), but at
        // this screen a number bigger than what's owed is almost always a typo, and the
        // app has no notion of customer credit to put the excess into.
        if (amount > order.RemainingAmount)
            return StagedEditOutcome.Failure($"المبلغ أكبر من المتبقي ({order.RemainingAmount:N0} ج)");

        // Derived, not chosen - same rule as the wizard's opening payment. 'full' only fits
        // a payment that covers the whole order on its own; anything settling the rest of a
        // part-paid order is 'final', and anything short of that is another deposit.
        var paymentType = amount == order.RemainingAmount
            ? (order.PaidAmount == 0 ? PaymentType.Full : PaymentType.Final)
            : PaymentType.Deposit;

        var remainingAfter = order.RemainingAmount - amount;
        var summary = $"تسجيل دفعة: {amount:N0} ج ({PaymentTypeLabel(paymentType)}، {(method == PaymentMethod.Visa ? "فيزا" : "نقدي")})"
            + (remainingAfter > 0 ? $" — المتبقي بعدها {remainingAfter:N0} ج" : " — يكتمل السداد");

        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.Payment,
            PaymentAmount = amount,
            PaymentMethod = method,
            Notes = notes,
            Summary = summary
        });
    }

    public async Task<StagedEditOutcome> BuildRefundEditAsync(int orderId, decimal amount, PaymentMethod method, string? notes)
    {
        var order = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId);
        if (order is null)
            return StagedEditOutcome.Failure("الطلب غير موجود");

        if (amount <= 0)
            return StagedEditOutcome.Failure("المبلغ غير صحيح");

        if (order.PaidAmount <= 0)
            return StagedEditOutcome.Failure("لا يوجد مبلغ مدفوع لاسترداده");

        // Mirrors sp_add_payment's own guard - you can never hand back more than was
        // actually taken. Caught here so the figure is shown before تأكيد rather than after.
        if (amount > order.PaidAmount)
            return StagedEditOutcome.Failure($"قيمة الاسترداد أكبر من المدفوع ({order.PaidAmount:N0} ج)");

        var remainingPaid = order.PaidAmount - amount;
        var summary = $"استرداد: {amount:N0} ج ({(method == PaymentMethod.Visa ? "فيزا" : "نقدي")})"
            + (remainingPaid > 0 ? $" — يتبقى مدفوع {remainingPaid:N0} ج" : " — يُرد كامل المدفوع");

        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.Refund,
            PaymentAmount = amount,
            PaymentMethod = method,
            Notes = notes,
            Summary = summary
        });
    }

    public async Task<StagedEditOutcome> BuildLensChangeEditAsync(int itemId, string? lensDescription, string? notes)
    {
        var item = await _dbContext.OrderItems.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == itemId);
        if (item is null)
            return StagedEditOutcome.Failure("العنصر غير موجود");

        if (item.Status != OrderItemStatus.Active)
            return StagedEditOutcome.Failure("هذا البند ملغي");

        // إطار فقط has no lens line to replace. Changing that would be changing what kind of
        // item it is, which is a different operation from swapping lenses.
        if (item.ItemType == OrderItemType.FrameOnly)
            return StagedEditOutcome.Failure("بند «إطار فقط» لا يحتوي على عدسات");

        if (string.IsNullOrWhiteSpace(lensDescription))
            return StagedEditOutcome.Failure("أدخل نوع العدسات");

        var order = await _dbContext.Orders.AsNoTracking().FirstAsync(o => o.OrderId == item.OrderId);
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            return StagedEditOutcome.Failure("لا يمكن تعديل بند في طلب تم تسليمه أو إلغاؤه");

        // The price is left exactly as it was - if the new lenses cost something different,
        // that is a separate, deliberate edit through تعديل الأسعار.
        var summary = $"تغيير العدسات إلى: {lensDescription.Trim()} (كانت: {item.LensDescription})";

        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.LensChange,
            ItemId = itemId,
            LensDescription = lensDescription.Trim(),
            Notes = notes,
            Summary = summary
        });
    }

    public async Task<StagedEditOutcome> BuildPriceChangeEditAsync(int itemId, decimal? frameAgreedPrice, decimal? lensSellPrice, string? notes)
    {
        var item = await _dbContext.OrderItems.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == itemId);
        if (item is null)
            return StagedEditOutcome.Failure("العنصر غير موجود");

        if (item.Status != OrderItemStatus.Active)
            return StagedEditOutcome.Failure("هذا البند ملغي");

        // Matches what the popup offers: استبدال عدسات is priced when the lenses are
        // chosen and draws no frame from stock, so it is not repriced here. Enforced rather
        // than left to the UI, so a request that skips the screen cannot do it either.
        if (item.ItemType == OrderItemType.LensesReplace)
            return StagedEditOutcome.Failure("بند «استبدال عدسات» لا يتم تعديل أسعاره من هنا");

        if (frameAgreedPrice is null && lensSellPrice is null)
            return StagedEditOutcome.Failure("أدخل سعراً واحداً على الأقل");

        if (frameAgreedPrice < 0 || lensSellPrice < 0)
            return StagedEditOutcome.Failure("السعر غير صحيح");

        // إطار فقط has no lens line, and a lens-replacement item draws no frame from stock -
        // setting the missing side would put money against something the item does not have.
        if (lensSellPrice.HasValue && item.ItemType == OrderItemType.FrameOnly)
            return StagedEditOutcome.Failure("بند «إطار فقط» لا يحتوي على عدسات");

        if (frameAgreedPrice.HasValue && item.ItemType == OrderItemType.LensesReplace
            && !item.FrameId.HasValue && !item.CompensationFrameId.HasValue)
            return StagedEditOutcome.Failure("هذا البند لا يحتوي على إطار");

        var order = await _dbContext.Orders.AsNoTracking().FirstAsync(o => o.OrderId == item.OrderId);
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            return StagedEditOutcome.Failure("لا يمكن تعديل بند في طلب تم تسليمه أو إلغاؤه");

        // Spell out both the old and the new figure: this edit changes money without
        // changing anything visible about the item, so the summary is the only place the
        // difference can be seen before تأكيد commits it.
        var parts = new List<string>();
        if (frameAgreedPrice.HasValue)
            parts.Add($"سعر الإطار {item.FrameAgreedPrice:N0} ← {frameAgreedPrice.Value:N0} ج");
        if (lensSellPrice.HasValue)
            parts.Add($"سعر العدسات {item.LensSellPrice:N0} ← {lensSellPrice.Value:N0} ج");

        var newTotal = (frameAgreedPrice ?? item.FrameAgreedPrice) + (lensSellPrice ?? item.LensSellPrice);
        var summary = $"تعديل الأسعار: {string.Join("، ", parts)} — إجمالي البند {item.ItemTotal:N0} ← {newTotal:N0} ج";

        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.PriceChange,
            ItemId = itemId,
            NewFrameAgreedPriceOnly = frameAgreedPrice,
            NewLensSellPriceOnly = lensSellPrice,
            Notes = notes,
            Summary = summary
        });
    }

    public async Task<StagedEditOutcome> BuildItemCancellationEditAsync(int itemId, CancelledFrameDisposition disposition, string? notes)
    {
        var item = await _dbContext.OrderItems.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == itemId);
        if (item is null)
            return StagedEditOutcome.Failure("العنصر غير موجود");

        if (item.Status != OrderItemStatus.Active)
            return StagedEditOutcome.Failure("هذا البند ملغي بالفعل");

        // Worth saying up front rather than letting it come as a surprise after تأكيد:
        // sp_cancel_order_item cancels the whole order once nothing active is left in it.
        var otherActiveItems = await _dbContext.OrderItems
            .AsNoTracking()
            .CountAsync(i => i.OrderId == item.OrderId && i.ItemId != itemId && i.Status == OrderItemStatus.Active);

        var summary = $"إلغاء البند: {ItemTypeLabel(item.ItemType)} — {item.ItemTotal:N0} ج"
            + (disposition == CancelledFrameDisposition.Damage ? " (الإطار تالف)" : " (إرجاع الإطار للمخزون)");

        if (otherActiveItems == 0)
            summary += " ⚠️ آخر بند نشط — سيُلغى الطلب بالكامل";

        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.ItemCancellation,
            ItemId = itemId,
            FrameDisposition = disposition,
            Notes = notes,
            Summary = summary
        });
    }

    // Independent per-order attempts, not one shared transaction - a batch that mixes
    // orders in different current statuses is expected to partially fail (e.g. a
    // "cancelled" order can never move to "ready"), and failing the whole batch over
    // one order that was never going to succeed would be worse than reporting exactly
    // which ones didn't make it.
    public async Task<BulkStatusUpdateResult> BulkUpdateStatusAsync(IReadOnlyList<int> orderIds, OrderStatus newStatus, string? notes)
    {
        var invoiceNumbers = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId, o => o.InvoiceNumber);

        var failures = new List<BulkStatusUpdateFailure>();
        var successCount = 0;

        foreach (var orderId in orderIds)
        {
            try
            {
                await ExecUpdateStatusAsync(orderId, newStatus, notes);
                successCount++;
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex,
                    "SQL error in bulk status update. OrderId={OrderId} NewStatus={NewStatus} SqlErrors={SqlErrors}",
                    orderId, newStatus, StoredProcedureErrors.Describe(ex));

                failures.Add(new BulkStatusUpdateFailure(
                    orderId,
                    invoiceNumbers.GetValueOrDefault(orderId, orderId.ToString()),
                    StoredProcedureErrors.ToUserMessage(ex, DuplicateDataMessage)));
            }
        }

        return new BulkStatusUpdateResult { SuccessCount = successCount, Failures = failures };
    }

    // ── Commit: the only place any of this actually reaches the database ──────────
    // Every staged edit is applied inside one transaction, so the popup's "several
    // operations, one تأكيد" promise is a real all-or-nothing guarantee: if the third
    // edit fails (a frame someone else took in the meantime, say), the first two are
    // rolled back too rather than left half-applied.
    public async Task<OperationResult> CommitPendingEditsAsync(int orderId, IReadOnlyList<PendingOrderEdit> edits)
    {
        if (edits.Count == 0)
            return OperationResult.Success();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        try
        {
            foreach (var edit in edits)
            {
                switch (edit.Kind)
                {
                    case PendingEditKind.StatusChange:
                        await ExecUpdateStatusAsync(orderId, edit.NewStatus!.Value, edit.Notes);
                        break;
                    case PendingEditKind.FrameSwap:
                        await ExecSwapFrameAsync(edit.ItemId!.Value, edit.NewFrameId, edit.NewFrameAgreedPrice!.Value, edit.ReturnOldFrameToStock ?? false, edit.UsesExternalFrame, edit.ExternalFrameNotes, edit.Notes);
                        break;
                    case PendingEditKind.FrameCompensation:
                        await ExecAssignCompensationAsync(edit.ItemId!.Value, edit.NewFrameId!.Value, edit.NewFrameAgreedPrice!.Value, edit.Notes);
                        break;
                    case PendingEditKind.ItemCancellation:
                        await ExecCancelItemAsync(edit.ItemId!.Value, edit.FrameDisposition!.Value, edit.Notes);
                        break;
                    case PendingEditKind.Payment:
                        await ExecAddPaymentAsync(orderId, edit.PaymentAmount!.Value, edit.PaymentMethod!.Value, edit.Notes);
                        break;
                    case PendingEditKind.Refund:
                        await ExecAddRefundAsync(orderId, edit.PaymentAmount!.Value, edit.PaymentMethod!.Value, edit.Notes);
                        break;
                    case PendingEditKind.LensChange:
                        await ExecUpdateLensesAsync(edit.ItemId!.Value, edit.LensDescription);
                        break;
                    case PendingEditKind.PriceChange:
                        await ExecUpdateItemPricesAsync(edit.ItemId!.Value, edit.NewFrameAgreedPriceOnly, edit.NewLensSellPriceOnly);
                        break;
                }
            }

            await transaction.CommitAsync();
            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex,
                "SQL error committing pending edits. OrderId={OrderId} Edits={Edits} SqlErrors={SqlErrors}",
                orderId, DescribeEdits(edits), StoredProcedureErrors.Describe(ex));

            await StoredProcedureErrors.SafeRollbackAsync(transaction, _logger);
            return OperationResult.Failure(StoredProcedureErrors.ToUserMessage(ex, DuplicateDataMessage));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error committing pending edits. OrderId={OrderId} Edits={Edits}",
                orderId, DescribeEdits(edits));

            await StoredProcedureErrors.SafeRollbackAsync(transaction, _logger);
            return OperationResult.Failure(StoredProcedureErrors.GenericMessage);
        }
    }

    private Task ExecUpdateStatusAsync(int orderId, OrderStatus newStatus, string? notes) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            "EXEC sp_update_order_status @order_id=@p_order_id, @new_status=@p_new_status, @changed_by=@p_changed_by, @notes=@p_notes",
            new SqlParameter("@p_order_id", orderId),
            new SqlParameter("@p_new_status", StatusToDb(newStatus)),
            new SqlParameter("@p_changed_by", _currentUser.RequireUserId()),
            new SqlParameter("@p_notes", SqlDbType.NVarChar, 500) { Value = (object?)notes ?? DBNull.Value });

    // 'available' hands the old frame back to stock as sellable; 'damaged' writes it off to
    // frame_damage_log. Either way sp_swap_frame repoints the item and the T2 trigger
    // re-sums the order total off the new agreed price.
    //
    // A null @new_frame_id is how the SP is told the item no longer draws a frame from
    // inventory - the customer brought their own - so it also gets @new_item_type
    // 'lenses_replace' and the description of what they brought. Sending the item type only
    // in that direction matters: an item keeping an inventory frame must keep its own type.
    private Task ExecSwapFrameAsync(int itemId, int? newFrameId, decimal newFrameAgreedPrice, bool returnOldFrameToStock, bool usesExternalFrame, string? externalFrameNotes, string? notes) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_swap_frame
                @item_id = @p_item_id, @new_frame_agreed_price = @p_new_frame_agreed_price,
                @new_frame_id = @p_new_frame_id, @old_frame_disposition = @p_old_frame_disposition,
                @new_item_type = @p_new_item_type, @external_frame_notes = @p_external_frame_notes,
                @discount_reason = @p_discount_reason, @changed_by = @p_changed_by
            """,
            new SqlParameter("@p_item_id", itemId),
            new SqlParameter("@p_new_frame_agreed_price", newFrameAgreedPrice),
            new SqlParameter("@p_new_frame_id", SqlDbType.Int) { Value = (object?)newFrameId ?? DBNull.Value },
            new SqlParameter("@p_old_frame_disposition", returnOldFrameToStock ? "available" : "damaged"),
            new SqlParameter("@p_new_item_type", SqlDbType.NVarChar, 20) { Value = usesExternalFrame ? "lenses_replace" : DBNull.Value },
            new SqlParameter("@p_external_frame_notes", SqlDbType.NVarChar, 200) { Value = usesExternalFrame ? (object?)externalFrameNotes ?? DBNull.Value : DBNull.Value },
            new SqlParameter("@p_discount_reason", SqlDbType.NVarChar, 200) { Value = (object?)notes ?? DBNull.Value },
            new SqlParameter("@p_changed_by", _currentUser.RequireUserId()));

    private Task ExecAssignCompensationAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_assign_compensation_frame
                @item_id = @p_item_id, @frame_id = @p_frame_id,
                @price_option = @p_price_option, @custom_price = @p_custom_price,
                @notes = @p_notes, @changed_by = @p_changed_by
            """,
            new SqlParameter("@p_item_id", itemId),
            new SqlParameter("@p_frame_id", newFrameId),
            new SqlParameter("@p_price_option", "custom"),
            new SqlParameter("@p_custom_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = newFrameAgreedPrice },
            new SqlParameter("@p_notes", SqlDbType.NVarChar, 200) { Value = (object?)notes ?? DBNull.Value },
            new SqlParameter("@p_changed_by", _currentUser.RequireUserId()));

    // payment_type is worked out here rather than carried on the staged edit: another edit
    // in the same batch (a frame swap, a cancelled item) can move the order total before
    // this runs, so the figure the popup showed when the payment was staged may no longer
    // hold. Reading the order inside the transaction means the type always matches what is
    // actually owed at the moment the payment lands.
    private async Task ExecAddPaymentAsync(int orderId, decimal amount, PaymentMethod method, string? notes)
    {
        var order = await _dbContext.Orders.AsNoTracking().FirstAsync(o => o.OrderId == orderId);

        var paymentType = amount >= order.RemainingAmount
            ? (order.PaidAmount == 0 ? PaymentType.Full : PaymentType.Final)
            : PaymentType.Deposit;

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_add_payment
                @order_id = @p_order_id, @amount = @p_amount,
                @payment_type = @p_payment_type, @payment_method = @p_payment_method,
                @received_by = @p_received_by, @notes = @p_notes
            """,
            new SqlParameter("@p_order_id", orderId),
            new SqlParameter("@p_amount", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = amount },
            new SqlParameter("@p_payment_type", PaymentTypeToDb(paymentType)),
            new SqlParameter("@p_payment_method", method == PaymentMethod.Visa ? "visa" : "cash"),
            new SqlParameter("@p_received_by", _currentUser.RequireUserId()),
            new SqlParameter("@p_notes", SqlDbType.NVarChar, 500) { Value = (object?)notes ?? DBNull.Value });
    }

    // One of two writes in the app with no stored procedure behind it (the other is
    // ExecUpdateItemPricesAsync), so this statement carries the responsibility an SP
    // normally would.
    //
    // The guards live in the WHERE clause rather than in C# alone: the checks in
    // BuildLensChangeEditAsync happen when the edit is staged, which can be minutes before
    // تأكيد commits it, and an order delivered by someone else in between must not be
    // quietly edited. When those guards match nothing, the statement raises rather than
    // reporting success for a write that did not happen - which also routes the message
    // through the same path as every SP rejection, so it reaches the user in Arabic.
    //
    // Only the description is written. The price is deliberately untouched here; changing
    // it is a separate edit so there is one place, not two, where a price can move.
    private Task ExecUpdateLensesAsync(int itemId, string? lensDescription) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE oi
            SET oi.lens_description = @p_lens_description
            FROM order_items oi
            INNER JOIN orders o ON o.order_id = oi.order_id
            WHERE oi.item_id = @p_item_id
              AND oi.status  = 'active'
              AND o.status NOT IN ('delivered', 'cancelled');

            IF @@ROWCOUNT = 0
                THROW 50000, N'تعذر تغيير العدسات — البند ملغي أو الطلب تم تسليمه أو إلغاؤه', 1;
            """,
            new SqlParameter("@p_item_id", itemId),
            new SqlParameter("@p_lens_description", SqlDbType.NVarChar, 200) { Value = (object?)lensDescription ?? DBNull.Value });

    // Like ExecUpdateLensesAsync, this has no stored procedure behind it, so the statement
    // carries the guards itself rather than trusting the checks made when the edit was
    // staged - which can be minutes earlier, and an order delivered by someone else in the
    // meantime must not be quietly repriced.
    //
    // COALESCE leaves either figure untouched when null: an إطار فقط item never has a lens
    // price set on it, and a lens-replacement item never has a frame price.
    //
    // Nothing recomputes item_total or the order total here - the computed column and
    // trigger T2 do that, exactly as they do for the stored procedures.
    private Task ExecUpdateItemPricesAsync(int itemId, decimal? frameAgreedPrice, decimal? lensSellPrice) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE oi
            SET oi.frame_agreed_price = COALESCE(@p_frame_price, oi.frame_agreed_price),
                oi.lens_sell_price    = COALESCE(@p_lens_price,  oi.lens_sell_price)
            FROM order_items oi
            INNER JOIN orders o ON o.order_id = oi.order_id
            WHERE oi.item_id = @p_item_id
              AND oi.status  = 'active'
              AND o.status NOT IN ('delivered', 'cancelled');

            IF @@ROWCOUNT = 0
                THROW 50000, N'تعذر تعديل الأسعار — البند ملغي أو الطلب تم تسليمه أو إلغاؤه', 1;
            """,
            new SqlParameter("@p_item_id", itemId),
            new SqlParameter("@p_frame_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = (object?)frameAgreedPrice ?? DBNull.Value },
            new SqlParameter("@p_lens_price", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = (object?)lensSellPrice ?? DBNull.Value });

    // A refund is a payment row with payment_type='refund'; trigger T1 subtracts it when it
    // re-sums orders.paid_amount, so remaining_amount follows on its own. No amount is
    // re-derived here the way a payment's type is - a refund is only ever the figure staff
    // entered, and sp_add_payment caps it at what was actually paid.
    private Task ExecAddRefundAsync(int orderId, decimal amount, PaymentMethod method, string? notes) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_add_payment
                @order_id = @p_order_id, @amount = @p_amount,
                @payment_type = @p_payment_type, @payment_method = @p_payment_method,
                @received_by = @p_received_by, @notes = @p_notes
            """,
            new SqlParameter("@p_order_id", orderId),
            new SqlParameter("@p_amount", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = amount },
            new SqlParameter("@p_payment_type", "refund"),
            new SqlParameter("@p_payment_method", method == PaymentMethod.Visa ? "visa" : "cash"),
            new SqlParameter("@p_received_by", _currentUser.RequireUserId()),
            new SqlParameter("@p_notes", SqlDbType.NVarChar, 500) { Value = (object?)notes ?? DBNull.Value });

    // Cancelling the item flips order_items.status to 'cancelled', which the T2 trigger
    // picks up: it re-sums item_total across the order's remaining active items into
    // orders.total_amount, so the cancelled item's money drops out of the order total
    // automatically. Nothing here needs to compute that.
    private Task ExecCancelItemAsync(int itemId, CancelledFrameDisposition disposition, string? notes) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            "EXEC sp_cancel_order_item @item_id=@p_item_id, @frame_disposition=@p_frame_disposition, @changed_by=@p_changed_by, @notes=@p_notes",
            new SqlParameter("@p_item_id", itemId),
            new SqlParameter("@p_frame_disposition", disposition == CancelledFrameDisposition.Damage ? "damage" : "return"),
            new SqlParameter("@p_changed_by", _currentUser.RequireUserId()),
            new SqlParameter("@p_notes", SqlDbType.NVarChar, 500) { Value = (object?)notes ?? DBNull.Value });

    private static string DescribeEdits(IReadOnlyList<PendingOrderEdit> edits) =>
        string.Join(", ", edits.Select(e => $"{e.Kind}(item:{e.ItemId})"));

    private static string StatusLabel(OrderStatus s) => s switch
    {
        OrderStatus.Sold => "بيع",
        OrderStatus.Ready => "جاهز",
        OrderStatus.Delivered => "تسليم",
        OrderStatus.Cancelled => "ملغي",
        _ => s.ToString()
    };

    private static string PaymentTypeLabel(PaymentType t) => t switch
    {
        PaymentType.Deposit => "عربون",
        PaymentType.Final => "دفعة نهائية",
        PaymentType.Full => "دفع كامل",
        PaymentType.Refund => "استرداد",
        _ => t.ToString()
    };

    private static string PaymentTypeToDb(PaymentType t) => t switch
    {
        PaymentType.Deposit => "deposit",
        PaymentType.Final => "final",
        PaymentType.Full => "full",
        PaymentType.Refund => "refund",
        _ => throw new ArgumentOutOfRangeException(nameof(t))
    };

    private static string ItemTypeLabel(OrderItemType t) => t switch
    {
        OrderItemType.FrameLenses => "إطار+عدسات",
        OrderItemType.FrameOnly => "إطار فقط",
        OrderItemType.LensesReplace => "استبدال عدسات",
        _ => t.ToString()
    };

    private static string FrameLabel(Frame frame) => $"{frame.Brand} {frame.ModelName}".Trim();

    private static string StatusToDb(OrderStatus status) => status switch
    {
        OrderStatus.Sold => "sold",
        OrderStatus.Ready => "ready",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
