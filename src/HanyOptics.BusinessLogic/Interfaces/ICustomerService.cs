using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;

namespace HanyOptics.BusinessLogic.Interfaces;

public interface ICustomerService
{
    // Searched and paged in the database, not in memory. A shop that has been open a while
    // has thousands of customers, and the counter needs to find one person rather than
    // browse everybody - so the search runs server-side and only a page comes back.
    Task<PagedResult<CustomerListItem>> SearchAsync(string? searchTerm, int? page, int? pageSize);

    Task<Customer?> GetByIdAsync(int customerId);

    // Lifetime totals for one customer, over every order they have placed - not just the
    // recent ones the detail panel happens to show.
    Task<CustomerTotals> GetTotalsAsync(int customerId);
}
