using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// المصروفات والإيرادات - the full form (category, supplier, back-dating, card payments),
// every entry ever recorded, and the monthly report. Admin-only: it carries supplier
// payments and the shop's running costs.
[Authorize(Roles = HanyOptics.BusinessLogic.Auth.Roles.Admin)]
public class ExpensesController : Controller
{
    private readonly IExpenseService _expenses;
    private readonly ISupplierService _suppliers;

    public ExpensesController(IExpenseService expenses, ISupplierService suppliers)
    {
        _expenses = expenses;
        _suppliers = suppliers;
    }

    public async Task<IActionResult> Index(int? page, string? month)
    {
        await PrepareAsync(page, month);
        return View(new ExpenseRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(ExpenseRequest model)
    {
        var outcome = await _expenses.AddAsync(model);
        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(string.Empty, outcome.ErrorMessage!);
            await PrepareAsync(null, null);
            return View(nameof(Index), model);
        }

        var (_, label) = ExpenseDisplay.Type(model.EntryType);
        TempData["ExpenseMessage"] = $"تم تسجيل {label} بمبلغ {ExpenseDisplay.Money(model.Amount!.Value)}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int expenseId, string? reason, int? page)
    {
        var outcome = await _expenses.CancelAsync(expenseId, reason, todayOnly: false);

        if (outcome.Succeeded)
            TempData["ExpenseMessage"] = "تم إلغاء الحركة.";
        else
            TempData["ExpenseError"] = outcome.ErrorMessage;

        return RedirectToAction(nameof(Index), new { page });
    }

    private async Task PrepareAsync(int? page, string? month)
    {
        var drawer = await _expenses.GetDrawerAsync();

        // The report defaults to the business month the shop is in, not the calendar one -
        // at 2am on the 1st the shop is still closing last month.
        var (year, monthNo) = DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var picked)
            ? (picked.Year, picked.Month)
            : (drawer.BusinessDate.Year, drawer.BusinessDate.Month);

        ViewBag.Drawer = drawer;
        ViewBag.Entries = await _expenses.GetEntriesAsync(page);
        ViewBag.Report = await _expenses.GetMonthReportAsync(year, monthNo);
        ViewBag.Suppliers = await _suppliers.GetOptionsAsync();
        ViewBag.ExpenseCategories = await _expenses.GetCategorySuggestionsAsync(ExpenseEntryTypes.Expense);
        ViewBag.IncomeCategories = await _expenses.GetCategorySuggestionsAsync(ExpenseEntryTypes.Income);
    }
}
