using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

// One row of the frames inventory screen. Flat and read-only: this listing exists to
// answer "what do we have, and how much of it", so it carries no navigation properties
// and nothing that would let a caller write through it.
public class FrameListItem
{
    public int FrameId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? ModelName { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public FrameCategory Category { get; set; }
    public FrameTrackingType TrackingType { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyAvailable { get; set; }
    public int QtyInitial { get; set; }
    public FrameStatus Status { get; set; }
    public string? Notes { get; set; }
}

// The totals strip above the list. Counted over whatever filter is applied, so the
// figures always describe the rows actually on screen rather than the whole table.
public class FrameInventorySummary
{
    public int LineCount { get; set; }
    public int TotalUnitsAvailable { get; set; }

    // Only units still on the shelf are worth anything - reserved, sold and damaged
    // frames are excluded from both figures.
    public decimal StockValueAtCost { get; set; }
    public decimal StockValueAtSell { get; set; }
}
