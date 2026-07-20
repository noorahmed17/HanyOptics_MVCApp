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

    public async Task<IActionResult> Index(int? customerId)
    {
        ViewBag.Customers = await _customerService.GetAllAsync();

        if (customerId.HasValue)
        {
            var customer = await _customerService.GetByIdAsync(customerId.Value);
            if (customer is not null)
            {
                ViewBag.SelectedCustomerId = customerId.Value;
                var orders = await _orderService.GetOrderListAsync(customerId: customerId.Value);
                return View(new CustomerDetailViewModel(customer, orders));
            }
        }

        return View((CustomerDetailViewModel?)null);
    }
}

public record CustomerDetailViewModel(Customer Customer, IReadOnlyList<OrderListItem> Orders);
