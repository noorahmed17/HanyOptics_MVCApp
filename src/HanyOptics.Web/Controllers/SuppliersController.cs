using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// الموردون for the admin, شركات العدسات for the counter. Same page: staff see the lens labs
// only and record an invoice or a payment - never a return, a new supplier, or a frame
// dealer's account, whose figures are frame costs.
[Authorize]
public class SuppliersController : Controller
{
    private readonly ISupplierService _suppliers;
    private readonly IExpenseService _expenses;
    private readonly IDailyCloseService _dailyClose;
    public SuppliersController(ISupplierService suppliers, IExpenseService expenses, IDailyCloseService dailyClose)
    {
        _suppliers = suppliers;
        _expenses = expenses;
        _dailyClose = dailyClose;
    }

    private bool IsAdmin => User.IsInRole(Roles.Admin);

    public async Task<IActionResult> Index(int? id)
    {
        var list = await _suppliers.ListAsync(IsAdmin ? null : SupplierTypes.Lenses);

        // Nothing picked yet - or one this user may not open - lands on the first supplier
        // in the list, which is ordered with the largest amount owed first.
        var selectedId = id.HasValue && list.Any(s => s.SupplierId == id) ? id : list.FirstOrDefault()?.SupplierId;

        ViewBag.IsAdmin = IsAdmin;
        ViewBag.Suppliers = list;
        ViewBag.Detail = selectedId.HasValue ? await _suppliers.GetAsync(selectedId.Value) : null;
        ViewBag.BusinessDate = await _dailyClose.GetCurrentBusinessDateAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create(CreateSupplierRequest model)
    {
        var outcome = await _suppliers.CreateAsync(model);
        if (!outcome.Succeeded)
        {
            TempData["SupplierError"] = outcome.ErrorMessage;
            TempData["SupplierFormOpen"] = true;
            return RedirectToAction(nameof(Index));
        }

        TempData["SupplierMessage"] = $"تمت إضافة المورد {model.Name!.Trim()}.";
        return RedirectToAction(nameof(Index), new { id = outcome.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddInvoice(SupplierInvoiceRequest model)
    {
        var detail = await _suppliers.GetAsync(model.SupplierId);
        if (detail is null)
            return NotFound();
        if (!CanUse(detail))
            return Forbid();

        // The counter records today's invoice; a back-dated one (an opening balance) is the admin's.
        if (!IsAdmin)
            model.PurchaseDate = null;

        // «رصيد افتتاحي» keeps an invoice out of lens profit, so only the admin may write it.
        // Same rule as vw_lens_profit_daily: both words, so «خصم افتتاح فرع» is still a normal invoice.
        var notes = model.Notes ?? "";
        if (!IsAdmin && notes.Contains("رصيد") && notes.Contains("فتتاح"))
        {
            TempData["SupplierError"] = "الرصيد الافتتاحي يسجّله المدير فقط.";
            return RedirectToAction(nameof(Index), new { id = model.SupplierId });
        }

        var outcome = await _suppliers.AddInvoiceAsync(model);
        Report(outcome, $"تم تسجيل فاتورة بقيمة {ExpenseDisplay.Money(model.Amount ?? 0)}.");
        return RedirectToAction(nameof(Index), new { id = model.SupplierId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AddReturn(SupplierReturnRequest model)
    {
        var outcome = await _suppliers.AddReturnAsync(model);
        Report(outcome, $"تم تسجيل مرتجع بقيمة {ExpenseDisplay.Money(model.Amount ?? 0)}.");
        return RedirectToAction(nameof(Index), new { id = model.SupplierId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPayment(SupplierPaymentRequest model)
    {
        var detail = await _suppliers.GetAsync(model.SupplierId);
        if (detail is null)
            return NotFound();
        if (!CanUse(detail))
            return Forbid();

        // An expense carrying the supplier's id - the same row the full form writes - filed
        // under the supplier's name so the monthly report groups it as before.
        var outcome = await _expenses.AddAsync(new ExpenseRequest
        {
            EntryType = ExpenseEntryTypes.Expense,
            Amount = model.Amount,
            FundingSource = model.FundingSource,
            PaymentMethod = model.PaymentMethod,
            Category = detail.Balance.Name,
            Description = model.Notes,
            SupplierId = model.SupplierId
        });
        Report(outcome, $"تم تسجيل دفعة بقيمة {ExpenseDisplay.Money(model.Amount ?? 0)}.");
        return RedirectToAction(nameof(Index), new { id = model.SupplierId });
    }

    private bool CanUse(SupplierDetail detail) =>
        IsAdmin || detail.Balance.SupplierType == SupplierTypes.Lenses;

    private void Report(OperationResult outcome, string success)
    {
        if (outcome.Succeeded)
            TempData["SupplierMessage"] = success;
        else
            TempData["SupplierError"] = outcome.ErrorMessage;
    }
}
