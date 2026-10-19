using HanyOptics.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// قفلة اليوم (إغلاق اليومية). Open to every signed-in user, as it always was: whoever is
// closing up needs it, and that is not always the owner. The month and the expense
// breakdown built on the same figures live under التقارير, which is admin-only.
[Authorize]
public class DailyCloseController : Controller
{
    private readonly IDailyCloseReportService _reports;

    public DailyCloseController(IDailyCloseReportService reports)
    {
        _reports = reports;
    }

    public async Task<IActionResult> Index(DateOnly? date) =>
        View(await _reports.GetDayAsync(date));
}
