using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;

public class CustomerService : ICustomerService
{
    private readonly HanyOpticsDbContext _dbContext;

    public CustomerService(HanyOpticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<CustomerListItem>> GetAllAsync()
    {
        var customers = await _dbContext.Customers.AsNoTracking().ToListAsync();

        var counts = await _dbContext.Orders
            .AsNoTracking()
            .GroupBy(o => o.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count);

        return customers
            .Select(c => new CustomerListItem
            {
                CustomerId = c.CustomerId,
                Name = c.Name,
                Phone = c.Phone,
                OrderCount = counts.GetValueOrDefault(c.CustomerId)
            })
            .OrderByDescending(c => c.OrderCount)
            .ThenBy(c => c.Name)
            .ToList();
    }

    public Task<Customer?> GetByIdAsync(int customerId) =>
        _dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId);
}
