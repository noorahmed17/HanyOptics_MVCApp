using System.Globalization;
using System.Text;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
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

    public async Task<IActionResult> Show(string id, DateOnly? from, DateOnly? to)
    {
        var definition = _reports.Find(id);
        if (definition is null)
            return NotFound();

        return View(await _reports.RunAsync(definition, from, to));
    }

    // The owner will want these numbers in Excel sooner or later, and retyping a hundred
    // rows off a screen is how figures get transcribed wrong.
    public async Task<IActionResult> Export(string id, DateOnly? from, DateOnly? to)
    {
        var definition = _reports.Find(id);
        if (definition is null)
            return NotFound();

        var result = await _reports.RunAsync(definition, from, to);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', result.Definition.Columns.Select(c => Escape(c.Label))));

        foreach (var row in result.Rows)
            csv.AppendLine(string.Join(',', row.Select(FormatForCsv).Select(Escape)));

        // Excel opens a UTF-8 CSV as the system codepage unless it sees a BOM, which turns
        // every Arabic name into mojibake. The BOM is what makes the file readable.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        var name = $"{definition.Key}-{DateTime.Now:yyyy-MM-dd}.csv";

        return File(bytes, "text/csv", name);
    }

    private static string FormatForCsv(object? value) => value switch
    {
        null => string.Empty,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
