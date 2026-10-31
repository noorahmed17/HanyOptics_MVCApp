using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;

    public CustomersController(ICustomerService customerService, IOrderService orderService)
    {
        _customerService = customerService;
        _orderService = orderService;
    }

    public async Task<IActionResult> Index(int? customerId, string? q, int? page)
    {
        var customers = await _customerService.SearchAsync(q, page, null);
        ViewBag.Customers = customers;
        ViewBag.SearchTerm = q;

        if (customerId.HasValue)
        {
            var customer = await _customerService.GetByIdAsync(customerId.Value);
            if (customer is not null)
            {
                ViewBag.SelectedCustomerId = customerId.Value;
                // The panel is a sidebar, not a screen of its own, so it takes the most
                // recent slice rather than every order the customer has ever placed.
                var orders = await _orderService.GetOrderListAsync(
                    customerId: customerId.Value, pageSize: PageSizes.CustomerOrders);

                // Totals come from an aggregate over every order, not from the recent slice
                // above - otherwise a long-standing customer's spend and outstanding balance
                // would both read low.
                var totals = await _customerService.GetTotalsAsync(customerId.Value);

                return View(new CustomerDetailViewModel(customer, orders.Items, totals));
            }
        }

        return View((CustomerDetailViewModel?)null);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int customerId, string? name, string? phone, string? q, int? page)
    {
        var outcome = await _customerService.UpdateAsync(customerId, name, phone);

        if (outcome.Succeeded)
            TempData["CustomerMessage"] = "تم تعديل بيانات العميل.";
        else
            TempData["CustomerError"] = outcome.ErrorMessage;

        return RedirectToAction(nameof(Index), new { customerId, q, page });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? name, string? phone)
    {
        var outcome = await _customerService.CreateAsync(name, phone);
        if (!outcome.Succeeded)
        {
            // Its own TempData keys: CustomerError is only read inside an open customer's
            // panel, so a refusal shown with it would wait there and pop up on the next
            // customer opened instead of now.
            TempData["CustomerCreateError"] = outcome.ErrorMessage;
            TempData["CustomerFormOpen"] = true;
            TempData["NewCustomerName"] = name ?? string.Empty;
            TempData["NewCustomerPhone"] = phone ?? string.Empty;
            return RedirectToAction(nameof(Index));
        }

        TempData["CustomerMessage"] = "تمت إضافة العميل.";
        // Searched by the new number so the customer is in the list, already selected.
        return RedirectToAction(nameof(Index), new { customerId = outcome.Id, q = EgyptianMobile.Normalize(phone) });
    }
}

public record CustomerDetailViewModel(
    Customer Customer, IReadOnlyList<OrderListItem> RecentOrders, CustomerTotals Totals);
