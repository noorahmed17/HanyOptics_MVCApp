using HanyOptics.BusinessLogic.Auth;
using HanyOptics.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IOrderService _orderService;

    public HomeController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _orderService.GetAllAsync(20);
        return View(orders);
    }

    [Authorize(Roles = Roles.Admin)]
    public IActionResult AdminOnly() => View();

    [AllowAnonymous]
    public IActionResult Error() => View();
}
