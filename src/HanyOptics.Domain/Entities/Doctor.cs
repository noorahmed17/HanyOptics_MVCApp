namespace HanyOptics.Domain.Entities;

public class Doctor
{
    public int DoctorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Clinic { get; set; }
    public string? Phone { get; set; }
}
