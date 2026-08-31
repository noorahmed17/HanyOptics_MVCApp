namespace HanyOptics.BusinessLogic.Models;

public class CustomerListItem
{
    public int CustomerId { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public int OrderCount { get; set; }
}

// A customer's lifetime figures, aggregated by the database.
//
// These used to be summed from the orders the detail panel had loaded, which was right
// while it loaded all of them. Now that the panel shows only the most recent, summing what
// is on screen would under-report what a customer has spent and - worse - what they still
// owe. So the totals are asked for separately and cover every order they have ever placed.
public class CustomerTotals
{
    public int OrderCount { get; set; }
    public decimal TotalPurchased { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining { get; set; }
}
