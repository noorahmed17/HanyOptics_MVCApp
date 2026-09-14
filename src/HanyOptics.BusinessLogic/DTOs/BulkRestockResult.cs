namespace HanyOptics.BusinessLogic.Models;

// Bulk restocks are applied frame-by-frame (sp_restock_bulk_frame is the sole authority on
// whether a given frame accepts it - it refuses anything that isn't tracking_type='bulk'),
// so a batch that mixes bulk and individually-tracked frames is expected to partially fail.
// Partial success is reported rather than treated as an all-or-nothing failure, the same
// reasoning as BulkStatusUpdateResult on the orders side.
public class BulkRestockResult
{
    public int SuccessCount { get; init; }
    public IReadOnlyList<BulkRestockFailure> Failures { get; init; } = Array.Empty<BulkRestockFailure>();
}

public record BulkRestockFailure(int FrameId, string Barcode, string ErrorMessage);
