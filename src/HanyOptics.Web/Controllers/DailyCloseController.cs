using HanyOptics.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// End of day. Open to any signed-in user, like the reports section - whoever is closing up
// is the one who needs it, and that is not always the owner.
[Authorize]
public class DailyCloseController : Controller
{
    private readonly IDailyCloseService _dailyClose;

    public DailyCloseController(IDailyCloseService dailyClose)
    {
        _dailyClose = dailyClose;
    }

    // No date means the business day the shop is currently in - which at 2am is still
    // yesterday's date, and is exactly what someone closing up wants to see.
    public async Task<IActionResult> Index(DateOnly? date)
        => View(await _dailyClose.GetAsync(date));
}
