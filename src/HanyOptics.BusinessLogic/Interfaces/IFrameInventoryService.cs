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
    Task<IReadOnlyList<FrameListItem>> GetFramesAsync(
        FrameStatus? status = null,
        FrameCategory? category = null,
        FrameTrackingType? trackingType = null,
        string? searchTerm = null);

    // Summarises the same filtered set the listing returns, so the totals on screen always
    // describe the rows the user is actually looking at.
    FrameInventorySummary Summarise(IReadOnlyList<FrameListItem> frames);

    // What a scan means before anything is written: whether this frame is already in stock,
    // and - when the barcode follows sp_generate_barcode's shape - the sell price printed
    // into it, so it does not have to be copied off the label by hand.
    Task<FrameBarcodeLookupResult> LookupBarcodeAsync(string barcode);

    // Adds one frame to stock. This is the only write in this service, and it exists
    // because receiving new stock is the one stock movement that does not happen as a side
    // effect of an order - everything else (reserving, returning, writing off) belongs to
    // the order flow and runs through the stored procedures.
    Task<AddFrameOutcome> AddFrameAsync(AddFrameRequest request);
}
