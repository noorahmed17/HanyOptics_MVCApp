using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// التقارير: إغلاق اليومية, التقرير الشهري, مصروفات الشهر. Admin-only - it carries the
// shop's income, costs and the owner's draws.
[Authorize(Roles = Roles.Admin)]
public class ReportsController : Controller
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports)
    {
        _reports = reports;
    }

    public IActionResult Index() => RedirectToAction(nameof(Day));

    public async Task<IActionResult> Day(DateOnly? date) =>
        View(await _reports.GetDayAsync(date));

    public async Task<IActionResult> Month(string? month) =>
        View(await _reports.GetMonthAsync(ParseMonth(month)));

    public async Task<IActionResult> Expenses(string? month) =>
        View(await _reports.GetMonthExpensesAsync(ParseMonth(month)));

    // "2026-09" from the month picker; anything else means the current month.
    private static ReportMonth? ParseMonth(string? month) =>
        DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var d) ? new ReportMonth(d.Year, d.Month) : null;
}
