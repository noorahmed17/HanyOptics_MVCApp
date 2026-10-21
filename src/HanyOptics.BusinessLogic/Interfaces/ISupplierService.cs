using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Each supplier's account: what was bought, what went back, what was paid, what is owed.
//
// Balances are read from vw_supplier_balances. Payments to a supplier are expense rows
// carrying its id (written through IExpenseService), so nothing here writes money.
public interface ISupplierService
{
    // type: null for every supplier, or one of SupplierTypes.
    Task<IReadOnlyList<SupplierBalance>> ListAsync(string? type = null);

    Task<IReadOnlyList<SupplierOption>> GetOptionsAsync();

    Task<SupplierDetail?> GetAsync(int supplierId);

    Task<CreateOutcome> CreateAsync(CreateSupplierRequest request);

    Task<OperationResult> AddInvoiceAsync(SupplierInvoiceRequest request);

    Task<OperationResult> AddReturnAsync(SupplierReturnRequest request);
}