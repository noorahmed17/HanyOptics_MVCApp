namespace HanyOptics.Domain.Entities;

public class Customer
{
    public int CustomerId { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }
}
