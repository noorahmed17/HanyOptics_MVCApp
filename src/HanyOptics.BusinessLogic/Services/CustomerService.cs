using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static HanyOptics.BusinessLogic.Services.SqlReader;

namespace HanyOptics.BusinessLogic.Services;

public class CustomerService : ICustomerService
{
    private readonly HanyOpticsDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(HanyOpticsDbContext dbContext, ICurrentUser currentUser, ILogger<CustomerService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // sp_update_customer owns the rules: the phone must not belong to another customer,
    // and the shared walk-in row can never be edited. It also writes the before/after to
    // corrections_log. Orders keep the name printed on them - only the customer row changes.
    public async Task<OperationResult> UpdateAsync(int customerId, string? name, string? phone)
    {
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                "EXEC dbo.sp_update_customer @customer_id = @p_customer, @changed_by = @p_user, @name = @p_name, @phone = @p_phone",
                new SqlParameter("@p_customer", customerId),
                new SqlParameter("@p_user", _currentUser.RequireUserId()),
                NVarChar("@p_name", Clean(name), 100),
                NVarChar("@p_phone", Clean(phone), 20));
            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "SQL error updating customer {CustomerId}. SqlErrors={SqlErrors}",
                customerId, StoredProcedureErrors.Describe(ex));
            return OperationResult.Failure(StoredProcedureErrors.ToUserMessage(ex, "هذا الرقم مسجّل لعميل آخر."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error updating customer {CustomerId}.", customerId);
            return OperationResult.Failure(StoredProcedureErrors.GenericMessage);
        }
    }

    public async Task<PagedResult<CustomerListItem>> SearchAsync(string? searchTerm, int? page, int? pageSize)
    {
        var query = _dbContext.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm.Trim()}%";
            query = query.Where(c =>
                (c.Name != null && EF.Functions.Like(c.Name, pattern)) ||
                (c.Phone != null && EF.Functions.Like(c.Phone, pattern)));
        }

        // Counted first so the pager knows the size of the whole result, and so a page
        // number past the end lands on the last real page instead of an empty screen that
        // looks like the search failed.
        var total = await query.CountAsync();
        var (currentPage, size) = PagedResult<CustomerListItem>.Normalise(page, pageSize ?? PageSizes.Customers, total);

        // The order count is a correlated subquery rather than a second pass over every
        // order in the database: it is evaluated for the rows on this page only.
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((currentPage - 1) * size)
            .Take(size)
            .Select(c => new CustomerListItem
            {
                CustomerId = c.CustomerId,
                Name = c.Name,
                Phone = c.Phone,
                OrderCount = _dbContext.Orders.Count(o => o.CustomerId == c.CustomerId)
            })
            .ToListAsync();

        return new PagedResult<CustomerListItem>
        {
            Items = items,
            TotalCount = total,
            Page = currentPage,
            PageSize = size
        };
    }

    public Task<Customer?> GetByIdAsync(int customerId) =>
        _dbContext.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == customerId);

    public async Task<CustomerTotals> GetTotalsAsync(int customerId)
    {
        // Cancelled orders are excluded from the money but still counted, matching how the
        // rest of the app reads them: the order happened, it just did not sell anything.
        var totals = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Purchased = (decimal?)g.Sum(o => o.TotalAmount),
                Paid = (decimal?)g.Sum(o => o.PaidAmount),
                Remaining = (decimal?)g.Sum(o => o.RemainingAmount)
            })
            .FirstOrDefaultAsync();

        return new CustomerTotals
        {
            OrderCount = totals?.Count ?? 0,
            TotalPurchased = totals?.Purchased ?? 0m,
            TotalPaid = totals?.Paid ?? 0m,
            TotalRemaining = totals?.Remaining ?? 0m
        };
    }
}
