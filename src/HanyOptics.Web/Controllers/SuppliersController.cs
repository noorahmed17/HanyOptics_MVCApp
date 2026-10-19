using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

[Authorize(Roles = HanyOptics.BusinessLogic.Auth.Roles.Admin)]
public class SuppliersController : Controller
{
    private readonly ISupplierService _suppliers;

    public SuppliersController(ISupplierService suppliers)
    {
        _suppliers = suppliers;
    }

    public async Task<IActionResult> Index(int? id)
    {
        var list = await _suppliers.ListAsync();

        // Nothing picked yet: open on the first supplier in the list, which is ordered with
        // the largest amount owed first.
        var selectedId = id ?? list.FirstOrDefault()?.SupplierId;

        ViewBag.Suppliers = list;
        ViewBag.Detail = selectedId.HasValue ? await _suppliers.GetAsync(selectedId.Value) : null;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
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
        var outcome = await _suppliers.AddInvoiceAsync(model);
        Report(outcome, $"تم تسجيل فاتورة بقيمة {ExpenseDisplay.Money(model.Amount ?? 0)}.");
        return RedirectToAction(nameof(Index), new { id = model.SupplierId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddReturn(SupplierReturnRequest model)
    {
        var outcome = await _suppliers.AddReturnAsync(model);
        Report(outcome, $"تم تسجيل مرتجع بقيمة {ExpenseDisplay.Money(model.Amount ?? 0)}.");
        return RedirectToAction(nameof(Index), new { id = model.SupplierId });
    }

    private void Report(OperationResult outcome, string success)
    {
        if (outcome.Succeeded)
            TempData["SupplierMessage"] = success;
        else
            TempData["SupplierError"] = outcome.ErrorMessage;
    }
}
