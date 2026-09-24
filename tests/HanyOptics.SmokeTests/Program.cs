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

// ── المصروفات: حركة الدرج · المصروفات والإيرادات · الموردون · التصحيحات ─────────────
Console.WriteLine();
Console.WriteLine("المصروفات pages:");
Console.WriteLine();

currentRole = "Admin";
var expAdmin = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
foreach (var (url, marker) in new[]
{
    ("/Drawer", "الرصيد الحالي في الدرج"),
    ("/Expenses", "تقرير المصروفات"),
    ("/Suppliers", "إضافة مورد"),
    ("/Corrections", "رقم الفاتورة"),
    ("/Corrections?tab=entries", "آخر تصحيحات المصروفات"),
})
{
    var res = await expAdmin.GetAsync(url);
    var html = await res.Content.ReadAsStringAsync();
    Check($"Admin: {url} renders", res.StatusCode == HttpStatusCode.OK && html.Contains(marker), res.StatusCode.ToString());
}

var expensesForm = await expAdmin.GetStringAsync("/Expenses");
Check("المصروفات والإيرادات offers no payment-method choice",
      !expensesForm.Contains("طريقة الدفع") && !expensesForm.Contains("type=\"radio\" name=\"PaymentMethod\""));

var someCustomer = await expAdmin.GetStringAsync("/Customers?customerId=2");
Check("a selected customer shows editable name and phone",
      someCustomer.Contains("/Customers/Update") && someCustomer.Contains("name=\"phone\""));

currentRole = "User";
var expUser =factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
var userDrawer = await expUser.GetAsync("/Drawer");
var userDrawerHtml = await userDrawer.Content.ReadAsStringAsync();
Check("User: حركة الدرج is open to staff", userDrawer.StatusCode == HttpStatusCode.OK);
Check("User: the sidebar offers حركة الدرج only",
      userDrawerHtml.Contains("حركة الدرج") && !userDrawerHtml.Contains("/Corrections") && !userDrawerHtml.Contains("/Suppliers"));
foreach (var url in new[] { "/Expenses", "/Suppliers", "/Corrections" })
{
    var res = await expUser.GetAsync(url);
    Check($"User: {url} is refused", res.StatusCode != HttpStatusCode.OK, res.StatusCode.ToString());
}

Console.WriteLine();
Console.WriteLine("المصروفات writes (through the stored procedures, cleaned up afterwards):");
Console.WriteLine();

using (var scope = factory.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var db = sp.GetRequiredService<HanyOptics.DataAccess.Persistence.HanyOpticsDbContext>();
    sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext =
        new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "Stub")),
            RequestServices = sp,
        };

    var expenses = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.IExpenseService>();
    var suppliersSvc = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.ISupplierService>();
    var corrections = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.ICorrectionService>();
    var tag = "SMOKE-" + Guid.NewGuid().ToString("N")[..8];

    async Task<int> TaggedId() => (await db.Database
        .SqlQueryRaw<int>("SELECT TOP 1 expense_id AS Value FROM expenses WHERE description = {0} ORDER BY expense_id DESC", tag)
        .ToListAsync()).FirstOrDefault();

    try
    {
        var before = (await expenses.GetDrawerAsync()).Balance;

        var card = await expenses.AddAsync(new HanyOptics.BusinessLogic.Models.ExpenseRequest
        {
            EntryType = "income", Amount = 200, FundingSource = "drawer", PaymentMethod = "visa", Description = tag
        });
        var afterCard = (await expenses.GetDrawerAsync()).Balance;
        Check("card income is recorded", card.Succeeded, card.ErrorMessage);
        Check("card income leaves the drawer unchanged", afterCard == before, $"{before} → {afterCard}");

        var id = await TaggedId();
        var edit = await expenses.UpdateAsync(new HanyOptics.BusinessLogic.Models.UpdateExpenseRequest
        {
            ExpenseId = id, EntryType = "income", Amount = 150, FundingSource = "drawer", PaymentMethod = "cash",
            Description = tag, Reason = "smoke test"
        });
        var afterEdit = (await expenses.GetDrawerAsync()).Balance;
        Check("editing it to cash 150 goes through sp_update_expense", edit.Succeeded, edit.ErrorMessage);
        Check("…and the drawer rises by exactly 150", afterEdit == before + 150, $"{before} → {afterEdit}");

        var tooMuch = await expenses.AddAsync(new HanyOptics.BusinessLogic.Models.ExpenseRequest
        {
            EntryType = "owner_draw", Amount = afterEdit + 1000, FundingSource = "drawer", Description = tag
        });
        Check("a draw larger than the drawer is refused with the procedure's message",
              !tooMuch.Succeeded && (tooMuch.ErrorMessage ?? "").Contains("الدرج"), tooMuch.ErrorMessage);

        var cancel = await expenses.CancelAsync(id, "smoke test", todayOnly: true);
        Check("today's entry can be cancelled from the drawer screen", cancel.Succeeded, cancel.ErrorMessage);
        Check("…and the drawer is back where it started", (await expenses.GetDrawerAsync()).Balance == before);

        // Suppliers: create, invoice, return - the balance is invoice - return.
        var supplier = await suppliersSvc.CreateAsync(new HanyOptics.BusinessLogic.Models.CreateSupplierRequest { Name = tag });
        Check("a supplier can be added", supplier.Succeeded, supplier.ErrorMessage);
        var dupSupplier = await suppliersSvc.CreateAsync(new HanyOptics.BusinessLogic.Models.CreateSupplierRequest { Name = tag });
        Check("the same supplier name cannot be added twice", !dupSupplier.Succeeded, dupSupplier.ErrorMessage);

        if (supplier.Id is int sid)
        {
            var inv = await suppliersSvc.AddInvoiceAsync(new HanyOptics.BusinessLogic.Models.SupplierInvoiceRequest { SupplierId = sid, Amount = 1000 });
            var ret = await suppliersSvc.AddReturnAsync(new HanyOptics.BusinessLogic.Models.SupplierReturnRequest { SupplierId = sid, Amount = 300 });
            var detail = await suppliersSvc.GetAsync(sid);
            Check("invoice and return are recorded", inv.Succeeded && ret.Succeeded, inv.ErrorMessage ?? ret.ErrorMessage);
            Check("vw_supplier_balances shows 700 owed", detail?.Balance.BalanceDue == 700, detail?.Balance.BalanceDue.ToString());
            Check("the account history lists both lines", detail?.History.Count == 2, detail?.History.Count.ToString());
        }

        // Corrections: a payment's method flipped and flipped back, both logged.
        var payment = (await db.Database.SqlQueryRaw<int>(
            "SELECT TOP 1 p.payment_id AS Value FROM payments p JOIN orders o ON o.order_id = p.order_id WHERE p.payment_method = 'cash' AND p.payment_type <> 'refund' AND o.status <> 'cancelled' ORDER BY p.payment_id DESC")
            .ToListAsync()).FirstOrDefault();
        if (payment > 0)
        {
            var toVisa = await corrections.CorrectPaymentAsync(payment, "visa", null, tag);
            var method = (await db.Database.SqlQueryRaw<string>("SELECT payment_method AS Value FROM payments WHERE payment_id = {0}", payment).ToListAsync())[0];
            var back = await corrections.CorrectPaymentAsync(payment, "cash", null, tag);
            Check("sp_admin_correct_payment changes the method", toVisa.Succeeded && method == "visa", toVisa.ErrorMessage);
            Check("…and changes it back", back.Succeeded, back.ErrorMessage);

            var invoiceNo = (await db.Database.SqlQueryRaw<string>(
                "SELECT o.invoice_number AS Value FROM payments p JOIN orders o ON o.order_id = p.order_id WHERE p.payment_id = {0}", payment).ToListAsync())[0];
            var found = await corrections.FindOrderAsync(invoiceNo);
            Check("the order's correction log shows both changes",
                  found is not null && found.Log.Count(l => l.Reason == tag) == 2, found?.Log.Count.ToString());
        }

        // العملاء: name and phone edited through sp_update_customer, then put back.
        var customersSvc = sp.GetRequiredService<HanyOptics.BusinessLogic.Interfaces.ICustomerService>();
        var pair = await db.Customers.AsNoTracking()
            .Where(c => c.Phone != null && c.Phone != "01000000000")
            .OrderByDescending(c => c.CustomerId).Take(2).ToListAsync();
        if (pair.Count == 2)
        {
            var (c1, c2) = (pair[0], pair[1]);
            var renamed = await customersSvc.UpdateAsync(c1.CustomerId, tag, c1.Phone);
            var nameNow = (await db.Customers.AsNoTracking().FirstAsync(c => c.CustomerId == c1.CustomerId)).Name;
            Check("a customer's name can be edited", renamed.Succeeded && nameNow == tag, renamed.ErrorMessage);

            var clash = await customersSvc.UpdateAsync(c1.CustomerId, tag, c2.Phone);
            Check("a phone that belongs to another customer is refused",
                  !clash.Succeeded && (clash.ErrorMessage ?? "").Contains("مسجّل لعميل آخر"), clash.ErrorMessage);

            var restored = await customersSvc.UpdateAsync(c1.CustomerId, c1.Name ?? c1.Phone, c1.Phone);
            Check("…and put back", restored.Succeeded, restored.ErrorMessage);
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM corrections_log WHERE entity = 'customer' AND entity_id = {0} AND changed_at >= DATEADD(minute, -5, GETDATE())", c1.CustomerId);
        }

        var walkIn = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Phone == "01000000000");
        if (walkIn is not null)
        {
            var refused = await customersSvc.UpdateAsync(walkIn.CustomerId, "x", walkIn.Phone);
            Check("the shared walk-in customer cannot be edited", !refused.Succeeded, refused.ErrorMessage);
        }

        var noReason = await corrections.RevertOrderStatusAsync(1, " ");
        Check("a revert without a reason is refused before reaching the database", !noReason.Succeeded);
    }
    finally
    {
        // Everything this block wrote carries the tag; remove it all.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM corrections_log WHERE reason = {0} OR (entity = 'expense' AND entity_id IN (SELECT expense_id FROM expenses WHERE description = {0}))", tag);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM expenses WHERE description = {0}", tag);
        await db.Database.ExecuteSqlRawAsync("DELETE r FROM purchase_returns r JOIN suppliers s ON s.supplier_id = r.supplier_id WHERE s.name = {0}", tag);
        await db.Database.ExecuteSqlRawAsync("DELETE i FROM purchase_invoices i JOIN suppliers s ON s.supplier_id = i.supplier_id WHERE s.name = {0}", tag);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM suppliers WHERE name = {0}", tag);

        var leftovers = (await db.Database.SqlQueryRaw<int>(
            "SELECT (SELECT COUNT(*) FROM expenses WHERE description = {0}) + (SELECT COUNT(*) FROM suppliers WHERE name = {0}) + (SELECT COUNT(*) FROM corrections_log WHERE reason = {0}) AS Value", tag)
            .ToListAsync())[0];
        Check("the test rows were removed again", leftovers == 0, leftovers.ToString());
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
