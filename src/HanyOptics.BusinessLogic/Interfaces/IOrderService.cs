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
    // Paged. Every caller was previously getting an unbounded list, which only stayed small
    // because the orders screen bounded it by date - a search or a customer's history had
    // no ceiling at all.
    Task<PagedResult<OrderListItem>> GetOrderListAsync(
        OrderStatus? status = null,
        DeliveryType? deliveryType = null,
        int? customerId = null,
        DateTime? fromDate = null,
        string? searchTerm = null,
        int? page = null,
        int? pageSize = null);

    Task<StagedEditOutcome> BuildStatusChangeEditAsync(int orderId, OrderStatus newStatus, string? notes);

    // Applies a status change to several orders at once (Orders/Index row-selection
    // toolbar). Each order goes through sp_update_order_status independently, so one
    // order that can't make the transition doesn't block the rest of the batch.
    Task<BulkStatusUpdateResult> BulkUpdateStatusAsync(IReadOnlyList<int> orderIds, OrderStatus newStatus, string? notes);
    // Replaces the frame on an item that already has one. returnOldFrameToStock covers the
    // "customer changed their mind" case - the old frame is intact, goes back on the shelf
    // and the new one is reserved in its place; false writes the old one off as damaged.
    //
    // newFrameId is null when the customer supplies their own frame instead of taking one
    // from stock: the item stops drawing on inventory and becomes استبدال عدسات, carrying
    // externalFrameNotes as the description of what they brought.
    Task<StagedEditOutcome> BuildFrameSwapEditAsync(int itemId, int? newFrameId, decimal newFrameAgreedPrice, bool returnOldFrameToStock, string? externalFrameNotes, string? notes);
    Task<StagedEditOutcome> BuildFrameCompensationEditAsync(int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes);

    // Records a payment against an order that already exists - the customer coming back to
    // pay off the remainder. sp_add_payment writes the row and the T1 trigger re-sums
    // orders.paid_amount from it, which the computed remaining_amount follows.
    Task<StagedEditOutcome> BuildPaymentEditAsync(int orderId, decimal amount, PaymentMethod method, string? notes);

    // Money going back to the customer - a cancelled order, or items cancelled out of one
    // that was already paid for. Recorded as a payment row with payment_type='refund',
    // which the T1 trigger subtracts from orders.paid_amount. It is the only kind of
    // payment sp_add_payment accepts against a cancelled order.
    Task<StagedEditOutcome> BuildRefundEditAsync(int orderId, decimal amount, PaymentMethod method, string? notes);

    // Replaces the lenses on an item that already has some, and what they are charged at.
    // Lenses hold no stock, so there is nothing to reserve or return.
    //
    // The price belongs here rather than in BuildPriceChangeEditAsync: new lenses almost
    // always cost something different, so the type and the price are one decision. Splitting
    // them across two dialogs is how an item ends up described as one thing and priced as
    // another.
    Task<StagedEditOutcome> BuildLensChangeEditAsync(int itemId, string? lensDescription, decimal? lensSellPrice, string? notes);

    // Corrects the frame price without changing what was sold - a mistyped figure, an
    // agreed discount applied after the fact. Frame only, even on an إطار + عدسات item: the
    // lens price moves with the lenses, in BuildLensChangeEditAsync. Null leaves it as it
    // stands. The T2 trigger carries the new total up into the order.
    Task<StagedEditOutcome> BuildPriceChangeEditAsync(int itemId, decimal? frameAgreedPrice, string? notes);

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
