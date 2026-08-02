namespace HanyOptics.BusinessLogic.Models;

// Bulk status changes are applied order-by-order (sp_update_order_status is the sole
// authority on which transitions are valid), so a batch that mixes orders in different
// current statuses is expected to partially fail - e.g. selecting a "cancelled" order
// alongside "sold" ones when marking everything "ready". Partial success is reported
// rather than treated as an all-or-nothing failure, since the alternative would block
// the whole batch over one order that was never going to succeed anyway.
public class BulkStatusUpdateResult
{
    public int SuccessCount { get; init; }
    public IReadOnlyList<BulkStatusUpdateFailure> Failures { get; init; } = Array.Empty<BulkStatusUpdateFailure>();
}

public record BulkStatusUpdateFailure(int OrderId, string InvoiceNumber, string ErrorMessage);
