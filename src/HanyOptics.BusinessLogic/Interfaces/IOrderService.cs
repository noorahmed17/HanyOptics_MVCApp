using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Interfaces;

// The order-detail popup works in two phases: staging (Build*) and commit
// (CommitPendingEditsAsync). Staging only validates and describes an edit for the
// popup's pending-changes list - nothing reaches the database until the popup's outer
// "تأكيد" commits every staged edit as one atomic unit. The underlying stored
// procedures (sp_update_order_status / sp_swap_frame / sp_assign_compensation_frame)
// remain the sole authority on every business rule at commit time; staging only catches
// the obvious cases early so staff aren't surprised at the last step.
public interface IOrderService
{
    // Full detail (items + prescriptions, payments, status log) for the order-detail popup.
    Task<Order?> GetByIdAsync(int orderId);
    Task<IReadOnlyList<Order>> GetAllAsync(int take = 50);
    Task<int> CreateOrderAsync(Order order);
    Task<Doctor?> GetDoctorByIdAsync(int doctorId);

    // Flat, filterable listing used by Orders/Index and the Customers/Index detail panel.
    // searchTerm matches invoice number, customer name, or phone - used by the Orders/Index
    // search box to reach orders outside the default fromDate window (see SearchOrders
    // on OrdersController, which calls this without a fromDate on purpose).
    Task<IReadOnlyList<OrderListItem>> GetOrderListAsync(OrderStatus? status = null, DeliveryType? deliveryType = null, int? customerId = null, DateTime? fromDate = null, string? searchTerm = null);

    Task<StagedEditOutcome> BuildStatusChangeEditAsync(int orderId, OrderStatus newStatus, string? notes);

    // Applies a status change to several orders at once (Orders/Index row-selection
    // toolbar). Each order goes through sp_update_order_status independently, so one
    // order that can't make the transition doesn't block the rest of the batch.
    Task<BulkStatusUpdateResult> BulkUpdateStatusAsync(IReadOnlyList<int> orderIds, OrderStatus newStatus, string? notes);
    Task<StagedEditOutcome> BuildFrameSwapEditAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes);
    Task<StagedEditOutcome> BuildFrameCompensationEditAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes);

    // Cancels one item out of an order, leaving the order's other items untouched.
    // sp_cancel_order_item handles the stock consequences (frame returned or written off,
    // lenses restocked or treated as lost) and cancels the whole order only if this was
    // its last active item.
    Task<StagedEditOutcome> BuildItemCancellationEditAsync(int itemId, CancelledFrameDisposition disposition, string? notes);

    // The one and only write path for all three edit kinds - applies every edit in the
    // list inside a single transaction, so a failure partway through leaves nothing
    // half-applied.
    Task<OperationResult> CommitPendingEditsAsync(int orderId, IReadOnlyList<PendingOrderEdit> edits);
}
