using System.Text.Json;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.Web;

// Keeps the wizard's in-progress order in the user's session rather than the database.
//
// Session state is deliberately the right home for it: it's per-user, it survives the
// server round trip between wizard steps, and it evaporates by itself when the user walks
// away - which is exactly the requested behaviour that an unfinished order must never
// reach the database.
public class SessionOrderDraftStore : IOrderDraftStore
{
    private const string SessionKey = "hanyoptics.order-draft";

    // Computed properties on the draft (ItemTotal and friends) are read-only, so tell the
    // serializer not to choke trying to write them back on the way in.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpContextAccessor _httpContextAccessor;

    public SessionOrderDraftStore(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession? Session => _httpContextAccessor.HttpContext?.Session;

    public OrderDraft? Get()
    {
        var json = Session?.GetString(SessionKey);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<OrderDraft>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            // A draft left over from an older shape of the model is worthless - drop it
            // rather than blocking the user from starting a new order.
            Clear();
            return null;
        }
    }

    public void Save(OrderDraft draft) =>
        Session?.SetString(SessionKey, JsonSerializer.Serialize(draft, SerializerOptions));

    public void Clear() => Session?.Remove(SessionKey);
}
