using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Money that moves without a sales invoice: shop expenses, owner draws and other income.
//
// Every write goes through the database's own procedures (sp_add_expense,
// sp_update_expense, sp_cancel_expense), which own the rules - above all "the drawer cannot
// pay out more than it holds" - and raise Arabic messages that are passed through as-is.
public interface IExpenseService
{
    Task<DrawerSummary> GetDrawerAsync();

    Task<PagedResult<ExpenseEntry>> GetEntriesAsync(int? page, int? pageSize = null);

    Task<ExpenseEntry?> GetEntryAsync(int expenseId);

    Task<ExpenseMonthReport> GetMonthReportAsync(int year, int month);

    // Categories already in use for this entry type, plus a few sensible starters, for the
    // form's suggestion list.
    Task<IReadOnlyList<string>> GetCategorySuggestionsAsync(string entryType);

    Task<OperationResult> AddAsync(ExpenseRequest request);

    Task<OperationResult> UpdateAsync(UpdateExpenseRequest request);

    // todayOnly: the drawer screen may only cancel what was entered in the current business
    // day; older entries are a correction, which is an admin's call.
    Task<OperationResult> CancelAsync(int expenseId, string? reason, bool todayOnly);
}
