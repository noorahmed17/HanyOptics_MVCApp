using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

public class NewOrderItemRequest
{
    public int OrderId { get; set; }
    public int? DoctorId { get; set; }

    public string? CustomerNameOnInvoice { get; set; }

    public OrderItemType ItemType { get; set; } = OrderItemType.FrameLenses;
    public string? FrameBarcode { get; set; }
    public int? FrameId { get; set; }
    public decimal? FrameAgreedPrice { get; set; }

    public string? ExternalFrameNotes { get; set; }

    public string? LensDescription { get; set; }
    public decimal? LensSellPrice { get; set; }

    public string? Notes { get; set; }

    public decimal? RightSphere { get; set; }
    public decimal? RightCylinder { get; set; }
    public int? RightAxis { get; set; }
    public decimal? LeftSphere { get; set; }
    public decimal? LeftCylinder { get; set; }
    public int? LeftAxis { get; set; }
    public decimal? Pd { get; set; }
    public decimal? AddPower { get; set; }

    public string SubmitAction { get; set; } = "add";
}
