using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.DataAccess.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace HanyOptics.BusinessLogic.Auth;

public class IdentitySeeder : IIdentitySeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;

    public IdentitySeeder(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager, IConfiguration configuration)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _configuration = configuration;
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

        if (await _userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "Hany Optics Admin",
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
                await _userManager.AddToRoleAsync(admin, Roles.Admin);
        }
    }
}
