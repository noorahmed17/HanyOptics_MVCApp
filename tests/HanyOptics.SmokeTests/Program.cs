using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
Check("sidebar hides لوحة الأدمن from a User", !userSidebar.Contains("/Admin"),
      userSidebar.Contains("/Admin") ? "link present" : "hidden");
var userAdminPage = await user.GetAsync("/Admin");
Check("/Admin is refused for a User",
      userAdminPage.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
      ((int)userAdminPage.StatusCode).ToString());
var userCreate = await user.PostAsync("/Admin/CreateUser", new FormUrlEncodedContent(
    new Dictionary<string, string> { ["Email"] = "sneak@x.local", ["Password"] = "Passw0rd!" }));
Check("a User cannot POST a new account either",
      userCreate.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect
      or HttpStatusCode.BadRequest,
      ((int)userCreate.StatusCode).ToString());
Check("but a User still sees الطلبات / العملاء / المخزون / قفلة اليوم",
      userSidebar.Contains("/Customers") && userSidebar.Contains("/Inventory")
      && userSidebar.Contains("/DailyClose"));

currentRole = "Admin";
var adminSidebar = await factory
    .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
    .GetStringAsync("/Orders");
Check("sidebar DOES show التقارير to an Admin", adminSidebar.Contains("/Reports"));
Check("sidebar DOES show لوحة الأدمن to an Admin", adminSidebar.Contains("/Admin"));

// ── لوحة الأدمن: creating a staff account ──────────────────────────────────
Console.WriteLine();
Console.WriteLine("لوحة الأدمن - user management:");
Console.WriteLine();

currentRole = "Admin";
var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

var adminPage = await adminClient.GetAsync("/Admin");
Check("/Admin returns 200 for an Admin", adminPage.StatusCode == HttpStatusCode.OK,
      adminPage.StatusCode.ToString());
var adminHtml = await adminPage.Content.ReadAsStringAsync();
Check("the page offers the create-user form",
      adminHtml.Contains("CreateUser") && adminHtml.Contains("Password"));
Check("the page lists existing users", adminHtml.Contains("admin@hanyoptics.local"));
Check("the old placeholder route is gone",
      (await adminClient.GetAsync("/Home/AdminOnly")).StatusCode == HttpStatusCode.NotFound);

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
// سعر البيع is quoted to customers, so staff need it - only the cost stays hidden.
Check("User DOES see سعر البيع", userStock.Contains("سعر البيع"));
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

    // The popup for an order that still owes money - this is where the كاش/فيزا box lives.
    using (var dumpScope = factory.Services.CreateScope())
    {
        var dumpDb = dumpScope.ServiceProvider
            .GetRequiredService<HanyOptics.DataAccess.Persistence.HanyOpticsDbContext>();
        var owing = await dumpDb.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Status == HanyOptics.Domain.Enums.OrderStatus.Sold
                                   && o.RemainingAmount > 0);
        if (owing is not null)
        {
            var detail = await dumper.GetStringAsync($"/Orders/Detail?id={owing.OrderId}");
            await File.WriteAllTextAsync(Path.Combine(dir, "order-detail.html"), detail);
            Console.WriteLine($"  wrote order-detail.html  (order {owing.OrderId}, {owing.RemainingAmount:N0} ج owed)");
        }
    }
}

// ── collecting the balance at تسليم ────────────────────────────────────────
//
// Marking an order delivered should offer to take the rest of the money in the same step.
// Driven through the real staging + commit path, then rolled back by hand so the shop's
// data is left as it was.
Console.WriteLine();
Console.WriteLine("Collecting the balance when marking delivered:");
Console.WriteLine();

using (var scope = factory.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var orders = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.IOrderService>();
    var db = sp.GetRequiredService<HanyOptics.DataAccess.Persistence.HanyOpticsDbContext>();

    // The write path stamps created_by/changed_by from the signed-in user, which it reads
    // off the current request. A bare DI scope has no request, so one is planted here with
    // a real users.user_id - otherwise every commit fails before it reaches SQL.
    var actorId = (await db.Database
        .SqlQueryRaw<int>("SELECT TOP 1 user_id AS Value FROM users ORDER BY user_id")
        .ToListAsync())[0];

    sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext =
        new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actorId.ToString())], "Stub")),
            RequestServices = sp,
        };

    // A 'sold' order that still owes money - exactly the case the popup is for.
    var target = await db.Orders.AsNoTracking()
        .FirstOrDefaultAsync(o => o.Status == HanyOptics.Domain.Enums.OrderStatus.Sold && o.RemainingAmount > 0);

    if (target is null)
    {
        Check("found a part-paid order to test with", false, "none in the database");
    }
    else
    {
        var owed = target.RemainingAmount;

        // The popup itself: كاش/فيزا must be offered on an order that still owes money, and
        // must not be offered on one that is already fully paid.
        var owingHtml = await admin.GetStringAsync($"/Orders/Detail?id={target.OrderId}");
        Check("the popup offers a payment method when money is owed",
              owingHtml.Contains("statusPaymentSection")
              && owingHtml.Contains("كاش") && owingHtml.Contains("فيزا"));
        Check("it is hidden until تسليم is picked",
              owingHtml.Contains("updateStatusPayment") || owingHtml.Contains("display:none"));

        var settled = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.RemainingAmount == 0 && o.Status == HanyOptics.Domain.Enums.OrderStatus.Sold);
        if (settled is not null)
        {
            var settledHtml = await admin.GetStringAsync($"/Orders/Detail?id={settled.OrderId}");
            Check("no payment box on an order with nothing owed",
                  !settledHtml.Contains("statusPaymentSection"), $"order {settled.OrderId}");
        }

        var pay = await orders.BuildPaymentEditAsync(
            target.OrderId, owed, HanyOptics.Domain.Enums.PaymentMethod.Visa, "تحصيل عند التسليم");
        Check("the balance can be staged as a payment", pay.Succeeded,
              pay.Succeeded ? $"{owed:N0} ج" : pay.ErrorMessage);

        var status = await orders.BuildStatusChangeEditAsync(
            target.OrderId, HanyOptics.Domain.Enums.OrderStatus.Delivered, "تسليم");
        Check("the delivery can be staged alongside it", status.Succeeded, status.ErrorMessage);

        if (pay.Succeeded && status.Succeeded)
        {
            // Payment first, then the status - delivered is terminal, so the other order
            // would leave the money unbookable.
            var commit = await orders.CommitPendingEditsAsync(target.OrderId, [pay.Edit!, status.Edit!]);
            Check("both commit together in one transaction", commit.Succeeded, commit.ErrorMessage);

            var after = await db.Orders.AsNoTracking().FirstAsync(o => o.OrderId == target.OrderId);

            Check("the order is now delivered",
                  after.Status == HanyOptics.Domain.Enums.OrderStatus.Delivered, after.Status.ToString());
            Check("the balance is settled", after.RemainingAmount == 0, $"{after.RemainingAmount:N0} ج");
            Check("delivered_at was stamped", after.DeliveredAt is not null);

            var visaRow = await db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(p => p.OrderId == target.OrderId
                                       && p.PaymentMethod == HanyOptics.Domain.Enums.PaymentMethod.Visa);
            Check("the payment was recorded as visa, as chosen", visaRow is not null);

            // Undo: remove the payment, put the status back. The triggers re-derive the money.
            if (visaRow is not null)
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM payments WHERE payment_id = {0}", visaRow.PaymentId);
            }
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE orders SET status='sold', delivered_at=NULL WHERE order_id={0}", target.OrderId);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM order_status_log WHERE order_id={0} AND new_status='delivered'", target.OrderId);

            var restored = await db.Orders.AsNoTracking().FirstAsync(o => o.OrderId == target.OrderId);
            Check("the order was put back as it was",
                  restored.Status == HanyOptics.Domain.Enums.OrderStatus.Sold
                  && restored.RemainingAmount == owed,
                  $"{restored.Status}, {restored.RemainingAmount:N0} ج owed");
        }
    }
}

// ── the round trip that matters: created here, can log in there ────────────
//
// Creating an account is only useful if the person can then sign in with it, and the two
// halves live in different stores - the Identity login and the business `users` row, tied
// together by a shared id. This drives the real services end to end and then removes the
// account again, so the database is left as it was found.
Console.WriteLine();
Console.WriteLine("Created account can actually log in:");
Console.WriteLine();

using (var scope = factory.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var userAdmin = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.IUserAdminService>();
    var auth = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.IAuthService>();
    var directory = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.IBusinessUserDirectory>();
    var userMgr = sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<
        HanyOptics.DataAccess.Identity.ApplicationUser>>();

    // Unique per run so repeated runs never collide, and prefixed so a leftover row is
    // obvious if cleanup ever fails.
    var email = $"zz-smoke-{Guid.NewGuid():N}"[..20] + "@hanyoptics.local";
    const string password = "Smoke#12345";

    var outcome = await userAdmin.CreateAsync(new HanyOptics.BusinessLogic.Models.CreateUserRequest
    {
        FullName = "موظف اختبار مؤقت",
        Email = email,
        Password = password,
        ConfirmPassword = password,
        IsAdmin = false
    });

    Check("the admin page creates the account", outcome.Succeeded,
          outcome.Succeeded ? $"user_id {outcome.UserId}" : string.Join("; ", outcome.Errors));

    if (outcome.Succeeded)
    {
        var login = await auth.LoginAsync(new HanyOptics.BusinessLogic.Models.LoginRequest
        {
            Email = email,
            Password = password
        });

        Check("that account can log in", login.Succeeded,
              login.Succeeded ? "token issued" : string.Join("; ", login.Errors));

        var wrong = await auth.LoginAsync(new HanyOptics.BusinessLogic.Models.LoginRequest
        {
            Email = email,
            Password = password + "x"
        });
        Check("the wrong password is refused", !wrong.Succeeded);

        // The id in the token is what every stored procedure stamps as created_by, so it has
        // to be the business users.user_id - not some unrelated Identity guid.
        var businessId = await directory.FindIdByUsernameAsync(email);
        Check("a matching business users row exists", businessId == outcome.UserId,
              $"identity {outcome.UserId} vs users {businessId}");

        var created = await userMgr.FindByEmailAsync(email);
        Check("the Identity id equals the business user_id",
              created is not null && created.Id == outcome.UserId.ToString(),
              created?.Id);

        var roles = created is null ? [] : await userMgr.GetRolesAsync(created);
        Check("it was given the User role, not Admin",
              roles.Contains("User") && !roles.Contains("Admin"), string.Join(",", roles));

        var duplicate = await userAdmin.CreateAsync(new HanyOptics.BusinessLogic.Models.CreateUserRequest
        {
            FullName = "تاني", Email = email, Password = password,
            ConfirmPassword = password, IsAdmin = false
        });
        Check("the same email cannot be used twice", !duplicate.Succeeded,
              string.Join("; ", duplicate.Errors));

        // Clean up - this is a throwaway account and must not be left in the shop's staff list.
        if (created is not null) await userMgr.DeleteAsync(created);
        await directory.DeleteIfUnreferencedAsync(outcome.UserId);

        var goneIdentity = await userMgr.FindByEmailAsync(email);
        var goneBusiness = await directory.FindIdByUsernameAsync(email);
        Check("the test account was removed again",
              goneIdentity is null && goneBusiness is null,
              $"identity:{(goneIdentity is null ? "gone" : "left")} users:{(goneBusiness is null ? "gone" : "left")}");
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
