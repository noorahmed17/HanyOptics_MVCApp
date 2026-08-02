using System.Security.Claims;
using HanyOptics.BusinessLogic.Interfaces;

namespace HanyOptics.Web;

// Reads the acting user straight out of the JWT claims - no database round-trip.
//
// This works because an account's Identity Id *is* its business `users`.user_id: the
// registration flow creates the business row first and then hands that id to Identity as
// the primary key (see AuthService.RegisterAsync). So the standard NameIdentifier claim
// already carries the value every stored procedure wants for created_by/changed_by/etc.
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? UserId
    {
        get
        {
            var raw = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(raw, out var userId) ? userId : null;
        }
    }

    public int RequireUserId() =>
        UserId ?? throw new InvalidOperationException("No authenticated user on the current request.");
}
