using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// لوحة الأدمن's counterpart for referring doctors. Admin only, same shape as
// AdminController: list what exists, and a form to add one more. Doctors are pure
// reference data used by the new-order wizard's doctor dropdown - no login, no role - so
// there's no dual-store dance here the way there is for staff accounts.
[Authorize(Roles = Roles.Admin)]
public class DoctorsAdminController : Controller
{
    private readonly IDoctorAdminService _doctors;

    public DoctorsAdminController(IDoctorAdminService doctors)
    {
        _doctors = doctors;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Doctors = await _doctors.ListAsync();
        return View(new CreateDoctorRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateDoctorRequest model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Doctors = await _doctors.ListAsync();
            return View(nameof(Index), model);
        }

        var outcome = await _doctors.CreateAsync(model);

        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(string.Empty, outcome.ErrorMessage!);
            ViewBag.Doctors = await _doctors.ListAsync();
            return View(nameof(Index), model);
        }

        // Redirect rather than render, so a refresh does not try to add the same doctor twice.
        TempData["DoctorCreated"] = $"تم إضافة الطبيب {model.Name}";
        return RedirectToAction(nameof(Index));
    }
}
