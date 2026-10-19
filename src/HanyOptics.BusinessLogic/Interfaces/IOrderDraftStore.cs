using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// Where the in-progress order lives between wizard steps. Implemented by the host over
// the user's session, so a draft is per-user, survives the round trips between steps, and
// disappears on its own if the wizard is simply abandoned.
public interface IOrderDraftStore
{
    OrderDraft? Get();

    // Convenience for the many call sites that only care whether a wizard is underway.
    bool HasDraft => Get() is not null;

    void Save(OrderDraft draft);
    void Clear();
}
