using System.Text.Json;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.Web;

// Keeps the order-detail popup's staged edits in the user's session rather than the
// database - mirrors SessionOrderDraftStore's reasoning exactly: per-user, survives the
// round trip between staging one operation and staging the next, and evaporates on its
// own if the popup is closed without pressing the outer "تأكيد".
public class SessionPendingOrderEditsStore : IPendingOrderEditsStore
{
    private const string SessionKey = "hanyoptics.pending-order-edits";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionPendingOrderEditsStore(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession? Session => _httpContextAccessor.HttpContext?.Session;

    public PendingOrderEditSet? Get(int orderId)
    {
        var json = Session?.GetString(SessionKey);
        if (string.IsNullOrEmpty(json))
            return null;

        PendingOrderEditSet? set;
        try
        {
            set = JsonSerializer.Deserialize<PendingOrderEditSet>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            Session?.Remove(SessionKey);
            return null;
        }

        // Whatever's staged belongs to a different order - not this popup's to see.
        return set?.OrderId == orderId ? set : null;
    }

    public void Save(PendingOrderEditSet set) =>
        Session?.SetString(SessionKey, JsonSerializer.Serialize(set, SerializerOptions));

    public void Clear(int orderId)
    {
        var json = Session?.GetString(SessionKey);
        if (string.IsNullOrEmpty(json))
            return;

        // Only clear if it's actually this order's set - never let closing order A's
        // popup accidentally wipe edits someone just started staging for order B.
        try
        {
            var set = JsonSerializer.Deserialize<PendingOrderEditSet>(json, SerializerOptions);
            if (set?.OrderId == orderId)
                Session?.Remove(SessionKey);
        }
        catch (JsonException)
        {
            Session?.Remove(SessionKey);
        }
    }
}
