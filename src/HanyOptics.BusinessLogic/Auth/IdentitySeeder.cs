using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.DataAccess.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Auth;

public class IdentitySeeder : IIdentitySeeder
{
    // The schema script ships one staff row in the business `users` table (username
    // "admin") that all pre-existing operational data is attributed to. The seeded login
    // adopts that row instead of creating a second admin, so historical created_by values
    // keep pointing at the same person. Override with SeedAdmin:BusinessUsername.
    private const string DefaultAdminBusinessUsername = "admin";

    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBusinessUserDirectory _businessUsers;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IBusinessUserDirectory businessUsers,
        IConfiguration configuration,
        ILogger<IdentitySeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _businessUsers = businessUsers;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        foreach (var role in new[] { Roles.Admin, Roles.User })
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));
        }

        // No hardcoded fallback on purpose - configure SeedAdmin:Email/Password locally
        // (e.g. appsettings.Development.json, gitignored) to get an initial admin account.
        var adminEmail = _configuration["SeedAdmin:Email"];
        var adminPassword = _configuration["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            return;

        if (await _userManager.FindByEmailAsync(adminEmail) is not null)
            return;

        const string adminFullName = "Hany Optics Admin";

        // Adopt the pre-seeded business staff row when it's there, otherwise create one -
        // either way the Identity account ends up carrying that row's user_id as its key.
        var businessUsername = _configuration["SeedAdmin:BusinessUsername"] ?? DefaultAdminBusinessUsername;
        var businessUserId = await _businessUsers.FindIdByUsernameAsync(businessUsername)
            ?? await _businessUsers.CreateAsync(adminFullName, businessUsername, isAdmin: true);

        var admin = new ApplicationUser
        {
            Id = businessUserId.ToString(),
            UserName = adminEmail,
            Email = adminEmail,
            FullName = adminFullName,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(admin, adminPassword);

        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(admin, Roles.Admin);
            _logger.LogInformation("Seeded the admin account {Email}.", adminEmail);
            return;
        }

        // Silence here is worse than a crash. The app starts, serves the login page and
        // looks healthy - but no admin exists, so nobody can get in and nothing says why.
        // The usual cause is a configured password that fails the policy: RequiredLength
        // is relaxed to 6 and uppercase/non-alphanumeric are not required, but a digit and
        // a lowercase letter still are.
        _logger.LogError(
            "Could not seed the admin account {Email} - nobody will be able to sign in. Identity rejected it: {Errors}",
            adminEmail,
            string.Join(" | ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
    }
}
