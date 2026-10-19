using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers.Account;
public class AccountController : Controller
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    // No self-registration: this is a shop's back office, not a public site. Accounts are
    // the owner's to hand out, so the only way in is a login against a user someone with
    // access already created. The admin account is seeded on startup.
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginRequest { ReturnUrl = returnUrl });

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginRequest model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _authService.LoginAsync(model);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(model);
        }

        SetAuthCookie(result);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete(AuthCookie.Name);
        return RedirectToAction("Login");
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private void SetAuthCookie(AuthResult result)
    {
        Response.Cookies.Append(AuthCookie.Name, result.Token!, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = result.ExpiresAtUtc
        });
    }
}
