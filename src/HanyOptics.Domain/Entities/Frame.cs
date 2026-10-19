using HanyOptics.Domain.Enums;

namespace HanyOptics.Domain.Entities;

public class Frame
{
    public int FrameId { get; set; }
    public int BranchId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public FrameTrackingType TrackingType { get; set; }
    public FrameCategory Category { get; set; }
    public string? Brand { get; set; }
    public string? ModelName { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public int QtyInitial { get; set; }
    public int QtyAvailable { get; set; }
    public FrameStatus Status { get; set; }
    public int? SupplierId { get; set; }
    public int? PurchaseInvoiceId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}
