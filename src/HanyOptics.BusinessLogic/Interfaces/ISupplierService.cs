using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Each supplier's account: what was bought, what went back, what was paid, what is owed.
//
// Balances are read from vw_supplier_balances. Payments to a supplier are expense rows
// carrying its id (recorded from المصروفات والإيرادات), so nothing here writes money.
public interface ISupplierService
{
    Task<IReadOnlyList<SupplierBalance>> ListAsync();

    Task<IReadOnlyList<SupplierOption>> GetOptionsAsync();

    Task<SupplierDetail?> GetAsync(int supplierId);

    Task<CreateOutcome> CreateAsync(CreateSupplierRequest request);

    Task<OperationResult> AddInvoiceAsync(SupplierInvoiceRequest request);

    Task<OperationResult> AddReturnAsync(SupplierReturnRequest request);
}
