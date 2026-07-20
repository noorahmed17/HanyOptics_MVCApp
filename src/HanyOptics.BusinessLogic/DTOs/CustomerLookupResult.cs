namespace HanyOptics.BusinessLogic.Models;

public class CustomerLookupResult
{
    public bool Found { get; set; }
    public int CustomerId { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
}
