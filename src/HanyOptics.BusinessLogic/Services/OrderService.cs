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

    public async Task<IReadOnlyList<OrderListItem>> GetOrderListAsync(OrderStatus? status = null, DeliveryType? deliveryType = null, int? customerId = null, DateTime? fromDate = null, string? searchTerm = null)
    {
        var query = _dbContext.Orders.Include(o => o.OrderItems).AsNoTracking().AsQueryable();

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

        var orders = await query.OrderByDescending(o => o.OrderDate).ToListAsync();

        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var phones = await _dbContext.Customers
            .AsNoTracking()
            .Where(c => customerIds.Contains(c.CustomerId))
            .ToDictionaryAsync(c => c.CustomerId, c => c.Phone);

        return orders.Select(o => new OrderListItem
        {
            OrderId = o.OrderId,
            InvoiceNumber = o.InvoiceNumber,
            OrderDate = o.OrderDate,
            CustomerName = o.CustomerName,
            CustomerPhone = phones.GetValueOrDefault(o.CustomerId),
            ItemTypes = o.OrderItems.Select(i => i.ItemType).Distinct().ToList(),
            ItemCount = o.OrderItems.Count,
            DeliveryType = o.DeliveryType,
            Status = o.Status,
            TotalAmount = o.TotalAmount,
            PaidAmount = o.PaidAmount,
            RemainingAmount = o.RemainingAmount
        }).ToList();
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

    public async Task<StagedEditOutcome> BuildFrameSwapEditAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes)
    {
        var itemExists = await _dbContext.OrderItems.AsNoTracking().AnyAsync(i => i.ItemId == itemId);
        if (!itemExists)
            return StagedEditOutcome.Failure("العنصر غير موجود");

        var frame = await _dbContext.Frames.AsNoTracking().FirstOrDefaultAsync(f => f.FrameId == newFrameId);
        if (frame is null)
            return StagedEditOutcome.Failure("الإطار غير موجود");

        var summary = $"استبدال الإطار التالف بـ {FrameLabel(frame)} — {newFrameAgreedPrice:N0} ج";
        if (!string.IsNullOrWhiteSpace(notes))
            summary += $" ({notes})";

        return StagedEditOutcome.Ok(new PendingOrderEdit
        {
            Kind = PendingEditKind.FrameSwap,
            ItemId = itemId,
            NewFrameId = newFrameId,
            NewFrameAgreedPrice = newFrameAgreedPrice,
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
                        await ExecSwapFrameAsync(edit.ItemId!.Value, edit.NewFrameId!.Value, edit.NewFrameAgreedPrice!.Value, edit.Notes);
                        break;
                    case PendingEditKind.FrameCompensation:
                        await ExecAssignCompensationAsync(edit.ItemId!.Value, edit.NewFrameId!.Value, edit.NewFrameAgreedPrice!.Value, edit.Notes);
                        break;
                    case PendingEditKind.ItemCancellation:
                        await ExecCancelItemAsync(edit.ItemId!.Value, edit.FrameDisposition!.Value, edit.Notes);
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

    private Task ExecSwapFrameAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes) =>
        _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC sp_swap_frame
                @item_id = @p_item_id, @new_frame_agreed_price = @p_new_frame_agreed_price,
                @new_frame_id = @p_new_frame_id, @old_frame_disposition = @p_old_frame_disposition,
                @discount_reason = @p_discount_reason, @changed_by = @p_changed_by
            """,
            new SqlParameter("@p_item_id", itemId),
            new SqlParameter("@p_new_frame_agreed_price", newFrameAgreedPrice),
            new SqlParameter("@p_new_frame_id", newFrameId),
            new SqlParameter("@p_old_frame_disposition", "damaged"),
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
