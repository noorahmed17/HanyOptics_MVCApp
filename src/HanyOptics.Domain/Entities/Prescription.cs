namespace HanyOptics.Domain.Entities;

public class Prescription
{
    public int PrescriptionId { get; set; }
    public int ItemId { get; set; }

    public decimal? RightSphere { get; set; }
    public decimal? RightCylinder { get; set; }
    public int? RightAxis { get; set; }

    public decimal? LeftSphere { get; set; }
    public decimal? LeftCylinder { get; set; }
    public int? LeftAxis { get; set; }

    public decimal? Pd { get; set; }
    public decimal? AddPower { get; set; }
    public string? Notes { get; set; }

    public OrderItem? OrderItem { get; set; }
}
