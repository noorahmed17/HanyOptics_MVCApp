using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// التصحيحات. Admin-only, and not only by hiding the link: the sp_admin_* procedures rewrite
// money that has already been counted in a closed day, and the deploy script is explicit
// that only the owner may reach them.
[Authorize(Roles = HanyOptics.BusinessLogic.Auth.Roles.Admin)]
public class CorrectionsController : Controller
{
    private const string OrdersTab = "orders";
    private const string EntriesTab = "entries";

    private readonly ICorrectionService _corrections;
    private readonly IExpenseService _expenses;
    private readonly ISupplierService _suppliers;

    public CorrectionsController(ICorrectionService corrections, IExpenseService expenses, ISupplierService suppliers)
    {
        _corrections = corrections;
        _expenses = expenses;
        _suppliers = suppliers;
    }

    public async Task<IActionResult> Index(string? tab, string? invoice, int? edit, int? page)
    {
        UpdateExpenseRequest? editModel = null;
        if (edit.HasValue && await _expenses.GetEntryAsync(edit.Value) is { IsCancelled: false } entry)
        {
            editModel = new UpdateExpenseRequest
            {
                ExpenseId = entry.ExpenseId,
                EntryType = entry.EntryType,
                Amount = entry.Amount,
                FundingSource = entry.FundingSource,
                PaymentMethod = entry.PaymentMethod,
                Category = entry.Category,
                Description = entry.Description,
                SupplierId = entry.SupplierId,
                // Left empty: sp_update_expense keeps the stored time when none is sent.
                // Pre-filling it would post it back cut to the minute and log a date
                // "change" nobody made.
                PaidAt = null
            };
            ViewBag.EditPaidAt = entry.PaidAt;
        }

        await PrepareAsync(tab, invoice, page, editModel);
        return View();
    }

    // ── Orders ───────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCustomerName(int orderId, string invoice, string? customerName, string? reason)
    {
        Report(await _corrections.SetOrderCustomerNameAsync(orderId, customerName, reason), "تم تعديل اسم العميل على الفاتورة.");
        return BackToOrder(invoice);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CorrectPayment(int paymentId, string invoice, string? newMethod, decimal? newAmount, string? reason)
    {
        Report(await _corrections.CorrectPaymentAsync(paymentId, newMethod, newAmount, reason), "تم تصحيح الدفعة.");
        return BackToOrder(invoice);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePayment(int paymentId, string invoice, string? reason)
    {
        Report(await _corrections.DeletePaymentAsync(paymentId, reason), "تم حذف الدفعة.");
        return BackToOrder(invoice);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevertStatus(int orderId, string invoice, string? reason)
    {
        Report(await _corrections.RevertOrderStatusAsync(orderId, reason), "تم إرجاع حالة الطلب.");
        return BackToOrder(invoice);
    }

    // ── Expenses & income ────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateExpense(UpdateExpenseRequest model, int? page)
    {
        var outcome = await _expenses.UpdateAsync(model);
        if (!outcome.Succeeded)
        {
            // Back to the same form with what was typed, so a refused edit is fixed rather
            // than retyped.
            ModelState.AddModelError(string.Empty, outcome.ErrorMessage!);
            await PrepareAsync(EntriesTab, null, page, model);
            return View(nameof(Index));
        }

        TempData["CorrectionMessage"] = "تم تعديل الحركة.";
        return RedirectToAction(nameof(Index), new { tab = EntriesTab, page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelExpense(int expenseId, string? reason, int? page)
    {
        Report(await _expenses.CancelAsync(expenseId, reason, todayOnly: false), "تم إلغاء الحركة.");
        return RedirectToAction(nameof(Index), new { tab = EntriesTab, page });
    }

    private async Task PrepareAsync(string? tab, string? invoice, int? page, UpdateExpenseRequest? editModel)
    {
        tab = editModel is not null || tab == EntriesTab ? EntriesTab : OrdersTab;
        ViewBag.Tab = tab;
        ViewBag.Invoice = invoice;

        if (tab == OrdersTab)
        {
            ViewBag.Order = string.IsNullOrWhiteSpace(invoice) ? null : await _corrections.FindOrderAsync(invoice);
        }
        else
        {
            ViewBag.Entries = await _expenses.GetEntriesAsync(page);
            ViewBag.EditModel = editModel;
            ViewBag.Suppliers = await _suppliers.GetOptionsAsync();
            ViewBag.RecentCorrections = await _corrections.GetRecentExpenseCorrectionsAsync();
        }
    }

    private IActionResult BackToOrder(string invoice) =>
        RedirectToAction(nameof(Index), new { tab = OrdersTab, invoice });

    private void Report(OperationResult outcome, string success)
    {
        if (outcome.Succeeded)
            TempData["CorrectionMessage"] = success;
        else
            TempData["CorrectionError"] = outcome.ErrorMessage;
    }
}
