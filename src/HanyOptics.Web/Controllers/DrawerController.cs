using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// حركة الدرج - the counter's screen. Open to every signed-in user, like قفلة اليوم: whoever
// is at the till pays the electricity man or hands the owner his draw, and it has to be
// recorded then, not later by someone else.
[Authorize]
public class DrawerController : Controller
{
    private readonly IExpenseService _expenses;

    public DrawerController(IExpenseService expenses)
    {
        _expenses = expenses;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Drawer = await _expenses.GetDrawerAsync();
        return View(new ExpenseRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(ExpenseRequest model)
    {
        // The counter form has a single free-text box. It doubles as the category so a
        // counter entry still groups sensibly in the monthly report next to the ones the
        // admin files by category.
        model.Category = model.Description;
        model.SupplierId = null;
        model.PaidAt = null;

        var outcome = await _expenses.AddAsync(model);
        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(string.Empty, outcome.ErrorMessage!);
            ViewBag.Drawer = await _expenses.GetDrawerAsync();
            return View(nameof(Index), model);
        }

        TempData["DrawerMessage"] = $"تم تسجيل {Label(model.EntryType)} بمبلغ {Models.ExpenseDisplay.Money(model.Amount!.Value)}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int expenseId, string? reason)
    {
        // Anyone can take back what was entered today by mistake; reaching further back is
        // a correction, and corrections belong to the admin.
        var outcome = await _expenses.CancelAsync(expenseId, reason, todayOnly: !User.IsInRole(Roles.Admin));

        if (outcome.Succeeded)
            TempData["DrawerMessage"] = "تم إلغاء الحركة.";
        else
            TempData["DrawerError"] = outcome.ErrorMessage;

        return RedirectToAction(nameof(Index));
    }

    private static string Label(string entryType) => entryType switch
    {
        ExpenseEntryTypes.OwnerDraw => "سحب للمالك",
        ExpenseEntryTypes.Income => "إيراد",
        _ => "مصروف"
    };
}
