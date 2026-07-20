using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers.Orders;

[Authorize]
public class OrdersController : Controller
{
    private readonly IOrderService _orderService;
    private readonly INewOrderService _newOrderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, INewOrderService newOrderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _newOrderService = newOrderService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? status, string? deliveryType)
    {
        var statusFilter = Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var s) ? s : (OrderStatus?)null;
        var deliveryFilter = Enum.TryParse<DeliveryType>(deliveryType, ignoreCase: true, out var d) ? d : (DeliveryType?)null;

        ViewBag.StatusFilter = statusFilter;
        ViewBag.DeliveryFilter = deliveryFilter;

        var orders = await _orderService.GetOrderListAsync(statusFilter, deliveryFilter);
        return View(orders);
    }

    // ── Step 1: customer ──────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> New()
    {
        await PopulateDoctorsAsync(null);
        return View(new NewOrderCustomerRequest());
    }

    [HttpGet]
    public async Task<IActionResult> LookupCustomer(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return Json(new CustomerLookupResult { Found = false });

        return Json(await _newOrderService.LookupCustomerByPhoneAsync(phone));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(NewOrderCustomerRequest model)
    {
        await PopulateDoctorsAsync(model.DoctorId);

        if (!ModelState.IsValid)
            return View(model);

        var outcome = await SafeAsync(() => _newOrderService.StartOrderAsync(model), "بدء الطلب");
        if (outcome is null)
            return View(model);

        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(nameof(model.InvoiceNumber), outcome.ErrorMessage!);
            return View(model);
        }

        return RedirectToAction(nameof(NewItem), new { orderId = outcome.OrderId, doctorId = model.DoctorId });
    }

    // "رجوع" from step 2 to step 1, for an order that already exists. Only phone
    // (customer) and delivery type are actually editable here - see EditOrderCustomerRequest.
    [HttpGet]
    public async Task<IActionResult> EditOrder(int orderId, int? doctorId)
    {
        var order = await _newOrderService.GetOrderForWizardAsync(orderId);
        if (order is null)
            return NotFound();

        ViewBag.Order = order;
        ViewData["OrderInProgress"] = true;
        var customer = await _newOrderService.GetCustomerAsync(order.CustomerId);

        return View(new EditOrderCustomerRequest
        {
            OrderId = orderId,
            Phone = customer?.Phone ?? string.Empty,
            CustomerName = order.CustomerName,
            DeliveryType = order.DeliveryType,
            DoctorId = doctorId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditOrder(EditOrderCustomerRequest model)
    {
        var order = await _newOrderService.GetOrderForWizardAsync(model.OrderId);
        if (order is null)
            return NotFound();

        ViewBag.Order = order;
        ViewData["OrderInProgress"] = true;

        if (!ModelState.IsValid)
            return View(model);

        var outcome = await SafeAsync(() => _newOrderService.UpdateOrderCustomerAsync(model), "تعديل بيانات الطلب");
        if (outcome is null)
            return View(model);

        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(nameof(model.Phone), outcome.ErrorMessage!);
            return View(model);
        }

        return RedirectToAction(nameof(NewItem), new { orderId = model.OrderId, doctorId = model.DoctorId });
    }

    // ── Step 2: items + prescription ──────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> NewItem(int orderId, int? doctorId)
    {
        var order = await _newOrderService.GetOrderForWizardAsync(orderId);
        if (order is null)
            return NotFound();

        ViewBag.Order = order;
        ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(order.CustomerId))?.Phone;
        ViewData["OrderInProgress"] = true;
        await PopulateDoctorsAsync(doctorId);

        return View(new NewOrderItemRequest
        {
            OrderId = orderId,
            DoctorId = doctorId,
            CustomerNameOnInvoice = order.CustomerName
        });
    }

    [HttpGet]
    public async Task<IActionResult> LookupFrame(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return Json(new FrameLookupResult { Found = false, Message = "أدخل باركود" });

        return Json(await _newOrderService.LookupFrameByBarcodeAsync(barcode));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewItem(NewOrderItemRequest model)
    {
        var exists = await _newOrderService.GetOrderForWizardAsync(model.OrderId);
        if (exists is null)
            return NotFound();

        ViewData["OrderInProgress"] = true;

        var outcome = await SafeAsync(() => _newOrderService.AddOrderItemAsync(model), "إضافة البند");
        if (outcome is null)
        {
            ViewBag.Order = await _newOrderService.GetOrderForWizardAsync(model.OrderId);
            ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(exists.CustomerId))?.Phone;
            await PopulateDoctorsAsync(model.DoctorId);
            return View(model);
        }

        if (!outcome.Added && !outcome.Blank)
        {
            foreach (var (field, message) in outcome.FieldErrors)
                ModelState.AddModelError(field, message);

            ViewBag.Order = await _newOrderService.GetOrderForWizardAsync(model.OrderId);
            ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(exists.CustomerId))?.Phone;
            await PopulateDoctorsAsync(model.DoctorId);
            return View(model);
        }

        if (model.SubmitAction == "next")
        {
            var order = await _newOrderService.GetOrderForWizardAsync(model.OrderId);
            if (order is null || order.OrderItems.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "أضف بندًا واحدًا على الأقل قبل المتابعة");
                ViewBag.Order = order;
                ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(exists.CustomerId))?.Phone;
                await PopulateDoctorsAsync(model.DoctorId);
                return View(model);
            }

            return RedirectToAction(nameof(NewPayment), new { orderId = model.OrderId });
        }

        return RedirectToAction(nameof(NewItem), new { orderId = model.OrderId, doctorId = model.DoctorId });
    }

    // ── Step 3: payment ────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> NewPayment(int orderId)
    {
        var order = await _newOrderService.GetOrderForWizardAsync(orderId);
        if (order is null)
            return NotFound();

        ViewBag.Order = order;
        ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(order.CustomerId))?.Phone;
        ViewData["OrderInProgress"] = true;
        return View(new NewOrderPaymentRequest { OrderId = orderId, Amount = order.RemainingAmount });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewPayment(NewOrderPaymentRequest model)
    {
        var order = await _newOrderService.GetOrderForWizardAsync(model.OrderId);
        if (order is null)
            return NotFound();

        ViewData["OrderInProgress"] = true;

        if (model.Amount < 0)
            ModelState.AddModelError(nameof(model.Amount), "المبلغ غير صحيح");

        if (!ModelState.IsValid)
        {
            ViewBag.Order = order;
            ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(order.CustomerId))?.Phone;
            return View(model);
        }

        var outcome = await SafeAsync(() => _newOrderService.AddPaymentAsync(model), "تسجيل الدفعة");
        if (outcome is null)
        {
            ViewBag.Order = order;
            ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(order.CustomerId))?.Phone;
            return View(model);
        }

        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(string.Empty, outcome.ErrorMessage!);
            ViewBag.Order = order;
            ViewBag.CustomerPhone = (await _newOrderService.GetCustomerAsync(order.CustomerId))?.Phone;
            return View(model);
        }

        TempData["OrderCreated"] = $"تم تسجيل الطلب {order.InvoiceNumber} بنجاح";
        return RedirectToAction(nameof(Index));
    }

    // Abandoning a wizard-in-progress order - the only way out of the wizard once an
    // order row exists, short of finishing it. Actually cancels the order via
    // sp_update_order_status rather than just navigating away and leaving it dangling.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int orderId)
    {
        var outcome = await SafeAsync(() => _newOrderService.CancelOrderAsync(orderId), "إلغاء الطلب");
        TempData["OrderCancelled"] = outcome is { Succeeded: true }
            ? "تم إلغاء الطلب."
            : outcome?.ErrorMessage ?? "تعذر إلغاء الطلب.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDoctorsAsync(int? selectedDoctorId)
    {
        var doctors = await _newOrderService.GetDoctorsAsync();
        ViewBag.Doctors = new SelectListWithSelection(doctors, selectedDoctorId);
    }

    // Last line of defense: nothing this controller does should ever let a raw
    // exception (SQL errors, timeouts, etc.) reach the user as the framework's
    // developer/500 error page. Every wizard-writing call goes through this.
    private async Task<T?> SafeAsync<T>(Func<Task<T>> operation, string actionDescription) where T : class
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during {Action} in the new-order wizard", actionDescription);
            ModelState.AddModelError(string.Empty, $"حدث خطأ غير متوقع أثناء {actionDescription} — حاول مرة أخرى");
            return null;
        }
    }
}

// Small helper so the view can render a <select> without pulling MVC's
// SelectListItem machinery into the DTOs themselves.
public record SelectListWithSelection(IReadOnlyList<HanyOptics.Domain.Entities.Doctor> Doctors, int? SelectedId);
