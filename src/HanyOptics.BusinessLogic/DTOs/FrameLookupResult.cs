namespace HanyOptics.BusinessLogic.Models;

public class FrameLookupResult
{
    public bool Found { get; set; }
    public string? Message { get; set; }

    public int FrameId { get; set; }
    public string? Barcode { get; set; }
    public string? Brand { get; set; }
    public string? ModelName { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public decimal SellPrice { get; set; }
    public decimal CostPrice { get; set; }
    public int QtyAvailable { get; set; }
    public string? TrackingType { get; set; }
    public string? Category { get; set; }
}
