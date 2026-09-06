using System.Text;
using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// Admin only. These screens carry cost prices, per-item margins and what each member of
// staff sold - the owner's view of the business, not something the counter needs. The
// sidebar link in _Layout is gated on the same role, so a salesperson is not shown a door
// they cannot open.
[Authorize(Roles = Roles.Admin)]
public class ReportsController : Controller
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports)
    {
        _reports = reports;
    }

    public IActionResult Index() => View(_reports.Catalog);

    public async Task<IActionResult> Show(string id, DateOnly? from, DateOnly? to, int? page)
    {
        var definition = _reports.Find(id);
        if (definition is null)
            return NotFound();

        return View(await _reports.RunAsync(definition, from, to, page, pageSize: null));
    }

    // The owner will want these numbers in Excel sooner or later, and retyping a hundred
    // rows off a screen is how figures get transcribed wrong. Unlike the screen, this takes
    // the whole range rather than one page.
    public async Task<IActionResult> Export(string id, DateOnly? from, DateOnly? to)
    {
        var definition = _reports.Find(id);
        if (definition is null)
            return NotFound();

        var csv = await _reports.ExportCsvAsync(definition, from, to);

        // Excel reads a UTF-8 CSV as the system codepage unless it sees a BOM, which turns
        // every Arabic name into mojibake. The BOM is what makes the file readable.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv", $"{definition.Key}-{DateTime.Now:yyyy-MM-dd}.csv");
    }
}
