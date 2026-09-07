using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


// ============================================================================
//  Drives the real application in-process: real middleware, real controllers,
//  real Razor views, real database. The only thing replaced is the
//  authentication handler, which hands the pipeline a principal directly
//  instead of validating a JWT - so no password is involved anywhere, and
//  what is being tested is authorisation and rendering, not login.
// ============================================================================

var failures = 0;
void Check(string what, bool ok, string? detail = null)
{
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}{(detail is null ? "" : "   [" + detail + "]")}");
    if (!ok) failures++;
}

// The role this run should present. Set before creating each client.
string currentRole = "Admin";

var factory = new StubAuthFactory(() => currentRole);

// ── reports as an Admin ────────────────────────────────────────────────────
Console.WriteLine("التقارير as Admin:\n");
currentRole = "Admin";
var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

var index = await admin.GetAsync("/Reports");
Check("/Reports returns 200", index.StatusCode == HttpStatusCode.OK, index.StatusCode.ToString());
var indexHtml = await index.Content.ReadAsStringAsync();
Check("the index lists all 15 reports",
      new[] { "daily-sales","monthly-profit","item-profit","staff-sales","outstanding",
              "payments-log","frame-stock","lens-stock","damage-losses","top-brands",
              "orders-summary","frame-swaps","doctors","top-customers","customer-history" }
        .All(k => indexHtml.Contains(k)));

string[] reports =
[
    "daily-sales","monthly-profit","item-profit","staff-sales","outstanding",
    "payments-log","frame-stock","lens-stock","damage-losses","top-brands",
    "orders-summary","frame-swaps","doctors","top-customers","customer-history"
];

Console.WriteLine("\nEvery report actually renders:\n");
foreach (var key in reports)
{
    var res = await admin.GetAsync($"/Reports/Show?id={key}");
    var html = await res.Content.ReadAsStringAsync();

    var ok = res.StatusCode == HttpStatusCode.OK;
    // A page that renders but shows the empty state is not a working report here - every
    // one of these has data, so a table is what proves the query ran and mapped.
    var hasTable = html.Contains("<table");
    var noEmpty = !html.Contains("مفيش بيانات");
    var noError = !html.Contains("An unhandled exception");

    Check($"{key,-18} 200 + table + rows",
          ok && hasTable && noEmpty && noError,
          $"{(int)res.StatusCode} table:{hasTable} rows:{noEmpty}");
}

// ── the details ────────────────────────────────────────────────────────────
Console.WriteLine("\nDetails:\n");

var monthly = await admin.GetStringAsync("/Reports/Show?id=monthly-profit");
Check("year renders without a thousands separator", monthly.Contains(">2026<") || monthly.Contains("2026"),
      monthly.Contains("2٬026") ? "found 2٬026" : "clean");
Check("year is NOT rendered as 2٬026", !monthly.Contains("2٬026"));

var paged = await admin.GetAsync("/Reports/Show?id=orders-summary&page=3");
Check("a deep page renders", paged.StatusCode == HttpStatusCode.OK, paged.StatusCode.ToString());

var past = await admin.GetAsync("/Reports/Show?id=orders-summary&page=99999");
var pastHtml = await past.Content.ReadAsStringAsync();
Check("a page past the end clamps instead of erroring",
      past.StatusCode == HttpStatusCode.OK && pastHtml.Contains("<table"), past.StatusCode.ToString());

var reversed = await admin.GetAsync("/Reports/Show?id=orders-summary&from=2026-08-31&to=2026-08-01");
var revHtml = await reversed.Content.ReadAsStringAsync();
Check("a backwards date range is swapped, not empty",
      reversed.StatusCode == HttpStatusCode.OK && revHtml.Contains("<table"), reversed.StatusCode.ToString());

var filtered = await admin.GetStringAsync("/Reports/Show?id=orders-summary&from=2026-08-01&to=2026-08-31");
Check("a filtered range still renders a table", filtered.Contains("<table"));

var bogus = await admin.GetAsync("/Reports/Show?id=does-not-exist");
Check("an unknown report is 404", bogus.StatusCode == HttpStatusCode.NotFound, bogus.StatusCode.ToString());

// ── CSV export ─────────────────────────────────────────────────────────────
Console.WriteLine("\nExport:\n");
var csv = await admin.GetAsync("/Reports/Export?id=daily-sales");
Check("export returns 200", csv.StatusCode == HttpStatusCode.OK, csv.StatusCode.ToString());
Check("export is text/csv", csv.Content.Headers.ContentType?.MediaType == "text/csv",
      csv.Content.Headers.ContentType?.MediaType);

var bytes = await csv.Content.ReadAsByteArrayAsync();
Check("export starts with a UTF-8 BOM so Excel reads Arabic",
      bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

var csvText = System.Text.Encoding.UTF8.GetString(bytes);
var lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
Check("export has a header row plus data", lines.Length > 1, $"{lines.Length} lines");
Check("export header is the Arabic column labels", lines[0].Contains("التاريخ"), lines[0].Trim());

// ── the same screens as a non-admin ────────────────────────────────────────
Console.WriteLine("\nSame screens as a plain User:\n");
currentRole = "User";
var user = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

var userIndex = await user.GetAsync("/Reports");
Check("/Reports is refused (403 or redirect to AccessDenied)",
      userIndex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
      $"{(int)userIndex.StatusCode} {userIndex.Headers.Location}");

var userReport = await user.GetAsync("/Reports/Show?id=item-profit");
Check("a report page is refused too",
      userReport.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
      $"{(int)userReport.StatusCode}");

var userExport = await user.GetAsync("/Reports/Export?id=daily-sales");
Check("the export endpoint is refused too - not just the page",
      userExport.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
      $"{(int)userExport.StatusCode}");

var userOrders = await user.GetAsync("/Orders");
Check("but the ordinary screens still work for a User",
      userOrders.StatusCode == HttpStatusCode.OK, userOrders.StatusCode.ToString());

var userDaily = await user.GetAsync("/DailyClose");
Check("and قفلة اليوم stays open to a User",
      userDaily.StatusCode == HttpStatusCode.OK, userDaily.StatusCode.ToString());

// The sidebar must not offer a door the user cannot open. Checked on rendered HTML rather
// than by reading the view, because the point is what actually reaches the browser.
var userSidebar = await user.GetStringAsync("/Orders");
Check("sidebar hides التقارير from a User", !userSidebar.Contains("/Reports"),
      userSidebar.Contains("/Reports") ? "link present" : "hidden");
Check("لوحة الأدمن is gone from the sidebar entirely", !userSidebar.Contains("AdminOnly"));
Check("but a User still sees الطلبات / العملاء / المخزون / قفلة اليوم",
      userSidebar.Contains("/Customers") && userSidebar.Contains("/Inventory")
      && userSidebar.Contains("/DailyClose"));

currentRole = "Admin";
var adminSidebar = await factory
    .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
    .GetStringAsync("/Orders");
Check("sidebar DOES show التقارير to an Admin", adminSidebar.Contains("/Reports"));
Check("لوحة الأدمن is gone for an Admin too - the page was removed",
      !adminSidebar.Contains("AdminOnly"));

// The route itself must be gone, not merely unlinked.
var goneForAdmin = await factory
    .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
    .GetAsync("/Home/AdminOnly");
Check("/Home/AdminOnly returns 404", goneForAdmin.StatusCode == HttpStatusCode.NotFound,
      goneForAdmin.StatusCode.ToString());

// ── المخزون: the money columns belong to the owner ─────────────────────────
Console.WriteLine();
Console.WriteLine("المخزون money columns:");
Console.WriteLine();

currentRole = "Admin";
var adminStock = await factory
    .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
    .GetStringAsync("/Inventory");
Check("Admin sees التكلفة", adminStock.Contains("التكلفة"));
Check("Admin sees سعر البيع", adminStock.Contains("سعر البيع"));
Check("Admin sees the stock-value cards", adminStock.Contains("قيمة المخزون"));

currentRole = "User";
var userStock = await factory
    .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
    .GetStringAsync("/Inventory");
Check("User does NOT see التكلفة", !userStock.Contains("التكلفة"),
      userStock.Contains("التكلفة") ? "column present" : "hidden");
Check("User does NOT see سعر البيع", !userStock.Contains("سعر البيع"),
      userStock.Contains("سعر البيع") ? "column present" : "hidden");
Check("User does NOT see the stock-value cards", !userStock.Contains("قيمة المخزون"));
Check("but a User still sees the rest of المخزون",
      userStock.Contains("الباركود") && userStock.Contains("المتاح")
      && userStock.Contains("الحالة") && userStock.Contains("القطع المتاحة"));

// Optional: write the rendered HTML of a few screens to a folder, so they can be looked at
// without signing in - useful when the browser cannot reach the app but the app itself is
// fine, which is otherwise very hard to tell apart.
if (args.Length >= 2 && args[0] == "--dump")
{
    var dir = args[1];
    Directory.CreateDirectory(dir);
    currentRole = "Admin";
    var dumper = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    foreach (var (name, url) in new[]
    {
        ("reports-index",  "/Reports"),
        ("monthly-profit", "/Reports/Show?id=monthly-profit"),
        ("daily-sales",    "/Reports/Show?id=daily-sales"),
        ("outstanding",    "/Reports/Show?id=outstanding"),
        ("orders",         "/Orders"),
    })
    {
        var page = await dumper.GetStringAsync(url);
        await File.WriteAllTextAsync(Path.Combine(dir, name + ".html"), page);
        Console.WriteLine($"  wrote {name}.html  ({page.Length:N0} bytes)");
    }
}

Console.WriteLine(failures == 0
    ? $"\nALL PASS"
    : $"\n{failures} FAILED");

Environment.Exit(failures == 0 ? 0 : 1);


// ── the host, with authentication stubbed ──────────────────────────────────
internal sealed class StubAuthFactory(Func<string> roleProvider)
    : WebApplicationFactory<HanyOptics.Web.Controllers.ReportsController>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        builder.ConfigureTestServices(services =>
        {
            // Replaces JWT validation only. Everything downstream - [Authorize],
            // role checks, controllers, views - is the application's own.
            services.AddAuthentication("Stub")
                    .AddScheme<AuthenticationSchemeOptions, StubHandler>("Stub", _ => { });

            services.AddSingleton(new RoleHolder(roleProvider));
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = "Stub";
                o.DefaultChallengeScheme = "Stub";
                o.DefaultScheme = "Stub";
            });
        });
    }
}

internal sealed class RoleHolder(Func<string> provider)
{
    public string Role => provider();
}

internal sealed class StubHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    RoleHolder roles) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // user_id 1 is the seeded admin's business row - the same id the real JWT carries
        // in NameIdentifier, which is what stamps created_by on anything written.
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "Test Principal"),
            new Claim(ClaimTypes.Role, roles.Role)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Stub"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Stub")));
    }
}
