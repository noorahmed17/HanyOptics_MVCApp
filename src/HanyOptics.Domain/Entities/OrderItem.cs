using HanyOptics.Domain.Enums;
namespace HanyOptics.Domain.Entities;
public class OrderItem
{
    public int ItemId { get; set; }
    public int OrderId { get; set; }
    public OrderItemType ItemType { get; set; }
    public int? FrameId { get; set; }
    public decimal FrameAgreedPrice { get; set; }
    public string? ExternalFrameNotes { get; set; }
    public string? LensDescription { get; set; }
    public int? RightLensStockId { get; set; }
    public int? LeftLensStockId { get; set; }
    public decimal LensSellPrice { get; set; }
    public decimal LensCostPrice { get; set; }
    public string? DiscountReason { get; set; }
    public bool WasFrameSwapped { get; set; }
    public OrderItemStatus Status { get; set; } = OrderItemStatus.Active;
    public int? CompensationFrameId { get; set; }
    public string? CompensationNotes { get; set; }
    public decimal ItemTotal { get; set; }

    public Order? Order { get; set; }
    public Prescription? Prescription { get; set; }
}
