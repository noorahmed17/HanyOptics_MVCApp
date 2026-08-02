using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HanyOptics.BusinessLogic;

public static class DependencyInjection
{
    public static IServiceCollection AddHanyOpticsBusinessLogic(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<INewOrderService, NewOrderService>();
        services.AddScoped<ICustomerService, CustomerService>();

        services.AddScoped<IBusinessUserDirectory, BusinessUserDirectory>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IIdentitySeeder, IdentitySeeder>();

        return services;
    }
}
