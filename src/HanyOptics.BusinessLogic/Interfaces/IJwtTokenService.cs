using HanyOptics.DataAccess.Identity;

namespace HanyOptics.BusinessLogic.Auth;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateToken(ApplicationUser user, IList<string> roles);
}
