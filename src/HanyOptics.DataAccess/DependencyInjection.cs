using HanyOptics.DataAccess.Identity;
using HanyOptics.DataAccess.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HanyOptics.DataAccess;

public static class DependencyInjection
{
    public static IServiceCollection AddHanyOpticsDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<HanyOpticsDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("HanyOpticsDb")));

        return services;
    }
    public static IServiceCollection AddHanyOpticsIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("HanyOpticsDb")));

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return services;
    }
}
