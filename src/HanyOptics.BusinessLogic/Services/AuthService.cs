using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Identity;
using Microsoft.AspNetCore.Identity;

namespace HanyOptics.BusinessLogic.Services;
public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IBusinessUserDirectory _businessUsers;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtTokenService jwtTokenService,
        IBusinessUserDirectory businessUsers)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtTokenService = jwtTokenService;
        _businessUsers = businessUsers;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
            return AuthResult.Failure("An account with this email already exists.");

        // Order matters: the business `users` row is created first so its
        // IDENTITY-generated user_id can be reused verbatim as the Identity primary key.
        // That single shared id is what lets every stored procedure stamp created_by /
        // received_by / changed_by straight from the JWT's NameIdentifier claim.
        var businessUserId = await _businessUsers.CreateAsync(request.FullName, request.Email, isAdmin: false);

        var user = new ApplicationUser
        {
            Id = businessUserId.ToString(),
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Don't leave a staff row behind that no one can ever log in as.
            await _businessUsers.DeleteIfUnreferencedAsync(businessUserId);
            return AuthResult.Failure(result.Errors.Select(e => e.Description).ToArray());
        }

        await _userManager.AddToRoleAsync(user, Roles.User);

        return await IssueTokenAsync(user);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return AuthResult.Failure("Invalid email or password.");

        var check = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
            return AuthResult.Failure("Invalid email or password.");

        return await IssueTokenAsync(user);
    }

    private async Task<AuthResult> IssueTokenAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var (token, expiresAtUtc) = _jwtTokenService.CreateToken(user, roles);
        return AuthResult.Success(token, expiresAtUtc, user.FullName);
    }
}
