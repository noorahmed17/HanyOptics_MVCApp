using System.Text;
using HanyOptics.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// Open to any signed-in user for now, by the owner's decision. Worth remembering what that
// means: these screens show cost prices, margins and per-staff performance, so anyone who
// can log in can see what the shop pays for a frame and what each colleague sold. Putting
// it back behind the owner is a one-line change - [Authorize(Roles = Roles.Admin)] - plus
// the matching condition on the sidebar link in _Layout.
[Authorize]
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
