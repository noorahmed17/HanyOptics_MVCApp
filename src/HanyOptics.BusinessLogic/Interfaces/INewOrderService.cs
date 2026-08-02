using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;

namespace HanyOptics.BusinessLogic.Interfaces;

// Backs the multi-step "new order" wizard (customer -> items -> payment).
//
// The wizard builds the order as an OrderDraft held outside the database; this service
// only reads reference data and validates while that's going on. The single writing
// method is CommitDraftAsync, called once when the user finishes the last step, so an
// abandoned wizard can never leave a half-built order behind.
//
// CommitDraftAsync relies on the DB triggers (T1/T2/T3) to keep orders.total_amount /
// paid_amount and frames.qty_available in sync - it only invokes the stored procedures,
// it never computes or writes those derived values itself. It returns a typed outcome
// (never lets a raw SqlException escape) so the controller can always show a friendly
// Arabic message instead of the framework's exception page.
public interface INewOrderService
{
    Task<CustomerLookupResult> LookupCustomerByPhoneAsync(string phone);
    Task<FrameLookupResult> LookupFrameByBarcodeAsync(string barcode);
    Task<IReadOnlyList<Doctor>> GetDoctorsAsync();
    Task<Customer?> GetCustomerAsync(int customerId);

    // Checked while the user is still on step 1 so a clash is reported immediately rather
    // than after they've entered every item. sp_create_order re-checks it authoritatively
    // at commit time, which is what closes the race between two staff members.
    Task<bool> IsInvoiceNumberTakenAsync(string invoiceNumber);

    // Validates one item and resolves its barcode to a frame, without reserving anything.
    Task<DraftItemOutcome> ValidateItemAsync(NewOrderItemRequest request);

    // The one and only write path: creates the order, its items and the opening payment
    // as a single all-or-nothing unit.
    Task<CommitDraftOutcome> CommitDraftAsync(OrderDraft draft, NewOrderPaymentRequest payment);
}
