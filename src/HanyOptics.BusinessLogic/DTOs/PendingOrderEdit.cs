using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

public enum PendingEditKind
{
    StatusChange,
    FrameSwap,
    FrameCompensation,
    ItemCancellation,
    Payment,
    Refund,
    LensChange,
    PriceChange
}

// What happens to the item's frame when the item is cancelled. 'return' puts it back in
// stock as sellable; 'damage' writes it off to frame_damage_log and - per
// sp_cancel_order_item - treats the item's lenses as lost too rather than restocking them.
public enum CancelledFrameDisposition
{
    Return,
    Damage
}

// One operation staged from the order-detail popup but not yet applied to the database.
// The popup can hold several of these at once (a status change plus a frame replacement,
// say); nothing happens to any of them until the popup's outer "تأكيد" commits the whole
// set as one unit - see IPendingOrderEditsStore and OrderService.CommitPendingEditsAsync.
//
// Held in the session as JSON, so - like OrderDraft - this stays a plain data object.
public class PendingOrderEdit
{
    public Guid EditId { get; set; } = Guid.NewGuid();
    public PendingEditKind Kind { get; set; }

    // Arabic, ready to show as-is in the "التغييرات المعلقة" list - built once when the
    // edit is staged (Order/frame lookups needed to phrase it are already available then;
    // deferring that to render time would mean re-querying on every popup reopen).
    public string Summary { get; set; } = string.Empty;

    // StatusChange
    public OrderStatus? NewStatus { get; set; }

    // FrameSwap / FrameCompensation / ItemCancellation
    public int? ItemId { get; set; }
    public int? NewFrameId { get; set; }
    public decimal? NewFrameAgreedPrice { get; set; }

    // Payment / Refund - money moving after the order was created. payment_type is
    // derived, never picked by staff, exactly as it is for the wizard's opening payment.
    public decimal? PaymentAmount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }

    // PriceChange - correcting what the frame was charged, without changing what was sold.
    // Frame only: the lens price travels with LensChange below, so each number has exactly
    // one route to it.
    public decimal? NewFrameAgreedPriceOnly { get; set; }

    // LensChange - new lenses on an item that already has some. Lenses carry no stock of
    // their own (lens_stock is empty and nothing references it), so unlike a frame there is
    // nothing to look up or reserve: the type and the price are the whole of it.
    public string? LensDescription { get; set; }
    public decimal? LensSellPrice { get; set; }

    // FrameSwap: set when the replacement is the customer's own frame rather than one from
    // stock. NewFrameId stays null in that case - sp_swap_frame takes @new_frame_id = NULL
    // to mean "this item no longer draws a frame from inventory" and converts it to
    // lenses_replace via @new_item_type.
    public bool UsesExternalFrame { get; set; }
    public string? ExternalFrameNotes { get; set; }

    // FrameSwap: what becomes of the frame being taken off the item. True when the
    // customer simply changed their mind and the old frame is still sellable, false when
    // it's damaged and gets written off. sp_swap_frame supports both; only the damaged
    // case used to be reachable from the UI.
    public bool? ReturnOldFrameToStock { get; set; }

    // ItemCancellation
    public CancelledFrameDisposition? FrameDisposition { get; set; }

    public string? Notes { get; set; }
}

// The one order currently being edited in this session, and everything staged for it.
// Single-slot like OrderDraft: opening a different order's popup and staging something
// there simply replaces this - the previous order's stale, never-applied edits are
// harmless and just get overwritten.
public class PendingOrderEditSet
{
    public int OrderId { get; set; }
    public List<PendingOrderEdit> Edits { get; set; } = new();
}
