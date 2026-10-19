using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Interfaces;

// Mostly a read-only view over the frames table for the inventory screen.
//
// Every change to frame stock that happens as a *side effect* of something else the shop
// does - selling an item reserves a frame, cancelling returns it, swapping writes one off -
// runs through the order flow's own stored procedures, never through here; a write method
// for those would be a second way to move stock that knows none of those rules. Adding and
// restocking frames are different: receiving new stock is not a side effect of anything
// else, so those two writes (AddFrameAsync, RestockFramesAsync) belong here, each behind
// the one stored procedure that owns its rules (sp_generate_barcode+INSERT, and
// sp_restock_bulk_frame). Recording damage outside an order (sp_record_frame_damage) is
// still unbuilt.
public interface IFrameInventoryService
{
    Task<PagedResult<FrameListItem>> GetFramesAsync(
        FrameStatus? status = null,
        FrameCategory? category = null,
        FrameTrackingType? trackingType = null,
        string? searchTerm = null,
        int? page = null,
        int? pageSize = null);

    // Summarises the whole filtered set, not the page on screen.
    //
    // This used to run over the fetched rows, which was right while the list was everything
    // there is. Once the list is one page of a few thousand frames, summing what was fetched
    // would turn "قيمة المخزون" into "value of these fifty rows" - a figure that changes as
    // you page and means nothing. So it is now an aggregate over the same filters instead.
    Task<FrameInventorySummary> SummariseAsync(
        FrameStatus? status = null,
        FrameCategory? category = null,
        FrameTrackingType? trackingType = null,
        string? searchTerm = null);

    // Adds one frame to stock and returns the barcode generated for it. This is the only
    // write in this service, and it exists because receiving new stock is the one stock
    // movement that does not happen as a side effect of an order - everything else
    // (reserving, returning, writing off) belongs to the order flow and runs through the
    // stored procedures.
    //
    // The barcode is not supplied by the caller: sp_generate_barcode derives it from the
    // sell price, because the shop prints the label after the frame is in the system.
    Task<AddFrameOutcome> AddFrameAsync(AddFrameRequest request);

    // Row-selection toolbar on Inventory/Index - adds the same quantity to every selected
    // frame's qty_available (and qty_initial, so the "X / Y" the list shows still reflects
    // what was actually ever stocked) in one go. Only tracking_type='bulk' frames accept
    // this - sp_restock_bulk_frame refuses an individual one outright - so a batch that
    // includes one reports it as a failure rather than failing the whole batch, same
    // reasoning as BulkUpdateStatusAsync on the orders side.
    Task<BulkRestockResult> RestockFramesAsync(IReadOnlyList<int> frameIds, int qtyToAdd);
}
