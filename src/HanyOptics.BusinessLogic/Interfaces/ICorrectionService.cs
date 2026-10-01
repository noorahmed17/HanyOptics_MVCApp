using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Admin-only fixes to data that was entered wrong. Each one runs a sp_admin_* procedure,
// which writes the before/after and the reason to dbo.corrections_log in the same
// transaction as the change itself.
public interface ICorrectionService
{
    Task<CorrectionOrder?> FindOrderAsync(string invoiceNumber);

    Task<OperationResult> SetOrderCustomerNameAsync(int orderId, string? customerName, string? reason);

    Task<OperationResult> CorrectPaymentAsync(int paymentId, string? newMethod, decimal? newAmount, string? reason);

    Task<OperationResult> DeletePaymentAsync(int paymentId, string? reason);

    Task<OperationResult> RevertOrderStatusAsync(int orderId, string? reason);

    Task<IReadOnlyList<CorrectionLogEntry>> GetRecentExpenseCorrectionsAsync(int take = 20);
}
