using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Where the order-detail popup's staged-but-not-yet-applied edits live between requests.
// Mirrors IOrderDraftStore's shape and reasoning: session-backed, per-user, and evaporates
// on its own if the popup is closed without confirming.
public interface IPendingOrderEditsStore
{
    // Null if nothing is staged, or if what's staged belongs to a different order.
    PendingOrderEditSet? Get(int orderId);

    void Save(PendingOrderEditSet set);

    void Clear(int orderId);
}
