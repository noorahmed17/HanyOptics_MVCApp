using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// لوحة الأدمن. Admin only, and the only way a staff account gets created now that
// self-registration is gone - before this page existed, a second login had to be inserted
// into the database by hand.
[Authorize(Roles = Roles.Admin)]
public class AdminController : Controller
{
    private readonly IUserAdminService _users;

    public AdminController(IUserAdminService users)
    {
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Users = await _users.ListAsync();
        return View(new CreateUserRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(CreateUserRequest model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Users = await _users.ListAsync();
            return View(nameof(Index), model);
        }

        var outcome = await _users.CreateAsync(model);

        if (!outcome.Succeeded)
        {
            foreach (var error in outcome.Errors)
                ModelState.AddModelError(string.Empty, error);

            ViewBag.Users = await _users.ListAsync();
            return View(nameof(Index), model);
        }

        // Redirect rather than render, so a refresh does not try to create the same person
        // twice. The password is deliberately not echoed back anywhere.
        TempData["UserCreated"] = $"تم إنشاء حساب {model.FullName} ({model.Email})";
        return RedirectToAction(nameof(Index));
    }
}
