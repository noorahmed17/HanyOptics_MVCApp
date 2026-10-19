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

    // Three different things can leave us without a usable actor id, and they need
    // different fixes - so they get different messages rather than one blanket
    // "no authenticated user", which is misleading in two of the three cases (the user
    // *is* signed in; it's their id that's unusable) and cost real debugging time once.
    public int RequireUserId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
            throw new InvalidOperationException(
                "No authenticated user on the current request - this code path is only reachable behind [Authorize].");

        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                "The signed-in user has no NameIdentifier claim. The JWT was issued without one - see JwtTokenService.CreateToken.");

        // An account whose Identity Id is not an integer predates the Identity <-> business
        // `users` link (AuthService.RegisterAsync and IdentitySeeder both assign the
        // business user_id as the Identity primary key). Such an account can sign in
        // perfectly well but can never be stamped onto a row as created_by/changed_by, so
        // every write fails. Signing out and in again will not help - the AspNetUsers row
        // itself has to be relinked.
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException(
                $"The signed-in user's id claim ('{raw}') is not an integer, so it cannot be used as a business users.user_id. " +
                "This account predates the Identity/users link and needs its AspNetUsers.Id relinked to its users.user_id.");

        return userId;
    }
}
