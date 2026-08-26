using System.Text;
using HanyOptics.BusinessLogic;
using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.DataAccess;
using HanyOptics.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Services ---------------------------------------------------------

builder.Services.AddControllersWithViews();

// Infrastructure: EF Core contexts, repositories, Identity store (AspNetUsers/AspNetRoles).
builder.Services.AddHanyOpticsDataAccess(builder.Configuration);
builder.Services.AddHanyOpticsIdentity(builder.Configuration);

// Business rules: order/customer services, JWT issuance, auth orchestration, seeding.
builder.Services.AddHanyOpticsBusinessLogic(builder.Configuration);

// Supplies the acting user's business `users`.user_id to the services, so every stored
// procedure stamps whoever actually performed the operation.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// The JWT lives in a cookie encrypted with the data-protection keys. Those keys are held
// in memory by default, so every restart mints new ones, every existing cookie becomes
// undecryptable, and every signed-in user is silently logged out. That is tolerable on a
// dev machine and not tolerable on a host that recycles the process, so the keys are
// written to disk and outlive the process.
//
// The path sits under the content root because that is the one directory a hosted site can
// reliably write to; on Windows hosting it maps inside the site's own folder.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")))
    .SetApplicationName("HanyOptics");

// The "new order" wizard builds the order in the session and only writes it to the
// database on the final step, so an abandoned wizard leaves nothing behind.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddScoped<IOrderDraftStore, SessionOrderDraftStore>();

// The order-detail popup's "hold several changes, apply them all on the outer تأكيد"
// staging area - same session mechanism as the new-order wizard's draft.
builder.Services.AddScoped<IPendingOrderEditsStore, SessionPendingOrderEditsStore>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

// This is an MVC app: the JWT travels in an HttpOnly cookie instead of an Authorization
// header, and a 401/403 challenge redirects to the login/access-denied page rather than
// returning a bare status code. This wiring is host/middleware-level, so it lives here.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthCookie.Name, out var token))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                context.Response.Redirect("/Account/AccessDenied");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/Account/Login?returnUrl={returnUrl}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(Roles.Admin));
});

var app = builder.Build();

// --- Middleware pipeline -----------------------------------------------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<IIdentitySeeder>();
    await seeder.SeedAsync();
}

app.Run();
