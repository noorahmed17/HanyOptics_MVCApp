using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;

namespace HanyOptics.BusinessLogic.Interfaces;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerListItem>> GetAllAsync();
    Task<Customer?> GetByIdAsync(int customerId);
}
