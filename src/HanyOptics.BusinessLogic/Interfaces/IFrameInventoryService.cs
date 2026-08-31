using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Interfaces;

// Read-only view over the frames table for the inventory screen.
//
// Deliberately has no write methods. Every change to frame stock already happens as a
// side effect of something else the shop does - selling an item reserves a frame,
// cancelling returns it, swapping writes one off - and those paths run through the
// stored procedures that keep frames, restock_log and frame_damage_log consistent with
// each other. A write method here would be a second way to move stock that knows none of
// those rules. Adding, restocking and damaging frames are separate deliberate features
// (sp_restock_bulk_frame / sp_record_frame_damage) and belong behind their own methods
// when they are built.
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
}
