using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;

namespace HanyOptics.BusinessLogic.Interfaces;

// Orchestrates the multi-step "new order" wizard (customer -> items -> payment).
// Relies on the DB triggers (T1/T2/T3) to keep orders.total_amount/paid_amount and
// frames.qty_available in sync - this service only inserts rows, it never computes
// or writes those derived values itself.
//
// Every DB-writing method returns a typed outcome (never lets a raw DbUpdateException/
// SqlException escape) so the controller can always show a friendly Arabic message
// instead of the framework's exception page.
public interface INewOrderService
{
    Task<CustomerLookupResult> LookupCustomerByPhoneAsync(string phone);
    Task<FrameLookupResult> LookupFrameByBarcodeAsync(string barcode);
    Task<IReadOnlyList<Doctor>> GetDoctorsAsync();
    Task<Customer?> GetCustomerAsync(int customerId);

    Task<StartOrderOutcome> StartOrderAsync(NewOrderCustomerRequest request);
    Task<OperationResult> UpdateOrderCustomerAsync(EditOrderCustomerRequest request);
    Task<AddOrderItemOutcome> AddOrderItemAsync(NewOrderItemRequest request);
    Task<OperationResult> AddPaymentAsync(NewOrderPaymentRequest request);
    Task<OperationResult> CancelOrderAsync(int orderId);

    Task<Order?> GetOrderForWizardAsync(int orderId);
}
