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
    private readonly IOrderDraftStore _drafts;
    private readonly IPendingOrderEditsStore _pendingEdits;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderService orderService,
        INewOrderService newOrderService,
        IOrderDraftStore drafts,
        IPendingOrderEditsStore pendingEdits,
        ILogger<OrdersController> logger)
    {
        _orderService = orderService;
        _newOrderService = newOrderService;
        _drafts = drafts;
        _pendingEdits = pendingEdits;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? status, string? deliveryType)
    {
        var statusFilter = Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var s) ? s : (OrderStatus?)null;
        var deliveryFilter = Enum.TryParse<DeliveryType>(deliveryType, ignoreCase: true, out var d) ? d : (DeliveryType?)null;
        var fromDate = DateTime.Now.AddDays(-4);

        ViewBag.StatusFilter = statusFilter;
        ViewBag.DeliveryFilter = deliveryFilter;

        var orders = await _orderService.GetOrderListAsync(statusFilter, deliveryFilter, fromDate: fromDate);
        return View(orders);
    }

    // ── Step 1: customer ──────────────────────────────────────────────
    // Nothing here touches the database. The order is assembled in a session-held draft
    // and only written when step 3 is submitted, so abandoning the wizard at any point -
    // إلغاء, navigating away, or closing the browser - leaves no order behind.
    [HttpGet]
    public async Task<IActionResult> New()
    {
        await PopulateDoctorsAsync(null);

        // Coming back here from step 2 ("رجوع") re-opens the draft for editing rather
        // than starting over.
        var draft = _drafts.Get();
        if (draft is null)
            return View(new NewOrderCustomerRequest());

        ViewData["OrderInProgress"] = true;
        await PopulateDoctorsAsync(draft.DoctorId);

        return View(new NewOrderCustomerRequest
        {
            Phone = draft.Phone,
            CustomerName = draft.CustomerName,
            IsWalkIn = draft.IsWalkIn,
            InvoiceNumber = draft.InvoiceNumber,
            DeliveryType = draft.DeliveryType,
            DoctorId = draft.DoctorId
        });
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

        // Reported now rather than after every item has been typed in. sp_create_order
        // re-checks this at commit time, which is what actually closes the race.
        if (await _newOrderService.IsInvoiceNumberTakenAsync(model.InvoiceNumber))
        {
            ModelState.AddModelError(nameof(model.InvoiceNumber), "رقم الفاتورة مستخدم من قبل — اختر رقماً آخر");
            return View(model);
        }

        // Keep any items already entered if the user stepped back to edit the customer.
        var draft = _drafts.Get() ?? new OrderDraft();
        draft.Phone = model.Phone;
        draft.CustomerName = model.CustomerName;
        draft.IsWalkIn = model.IsWalkIn;
        draft.InvoiceNumber = model.InvoiceNumber;
        draft.DeliveryType = model.DeliveryType;
        draft.DoctorId = model.DoctorId;
        _drafts.Save(draft);

        return RedirectToAction(nameof(NewItem));
    }

    // ── Step 2: items + prescription ──────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> NewItem()
    {
        var draft = _drafts.Get();
        if (draft is null)
            return RedirectToAction(nameof(New));

        await PrepareItemStepAsync(draft);

        return View(new NewOrderItemRequest
        {
            DoctorId = draft.DoctorId,
            CustomerNameOnInvoice = draft.CustomerName
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
        var draft = _drafts.Get();
        if (draft is null)
            return RedirectToAction(nameof(New));

        // The invoice name is edited on this step, so keep it on the draft either way.
        draft.CustomerName = model.CustomerNameOnInvoice;

        var outcome = await SafeAsync(() => _newOrderService.ValidateItemAsync(model), "إضافة البند");
        if (outcome is null)
        {
            await PrepareItemStepAsync(draft);
            return View(model);
        }

        if (!outcome.Blank && !outcome.Valid)
        {
            foreach (var (field, message) in outcome.FieldErrors)
                ModelState.AddModelError(field, message);

            await PrepareItemStepAsync(draft);
            return View(model);
        }

        if (outcome.Item is not null)
            draft.Items.Add(outcome.Item);

        _drafts.Save(draft);

        if (model.SubmitAction == "next")
        {
            if (draft.Items.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "أضف بندًا واحدًا على الأقل قبل المتابعة");
                await PrepareItemStepAsync(draft);
                return View(model);
            }

            return RedirectToAction(nameof(NewPayment));
        }

        return RedirectToAction(nameof(NewItem));
    }

    // ── Correcting an item that is already in the draft ────────────────
    // Same promise as the rest of the wizard: this edits the draft held in the session and
    // touches nothing in the database. The order still comes into existence in one piece
    // when step 3 is submitted, so a correction made here is indistinguishable from having
    // typed the item correctly the first time.
    [HttpGet]
    public IActionResult EditDraftItem(int index)
    {
        var draft = _drafts.Get();
        if (draft is null || index < 0 || index >= draft.Items.Count)
            return NotFound();

        ViewBag.ItemIndex = index;
        return PartialView("_EditDraftItem", _newOrderService.ToRequest(draft.Items[index]));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDraftItem(int index, NewOrderItemRequest model)
    {
        var draft = _drafts.Get();
        if (draft is null || index < 0 || index >= draft.Items.Count)
            return NotFound();

        var outcome = await SafeAsync(() => _newOrderService.ValidateItemAsync(model), "تعديل البند");

        if (outcome is null || outcome.Blank || !outcome.Valid)
        {
            if (outcome is not null && !outcome.Blank)
            {
                foreach (var (field, message) in outcome.FieldErrors)
                    ModelState.AddModelError(field, message);
            }
            else if (outcome is { Blank: true })
            {
                ModelState.AddModelError(string.Empty, "بيانات البند ناقصة");
            }

            ViewBag.ItemIndex = index;
            return PartialView("_EditDraftItem", model);
        }

        // Replaced in place so the item keeps its position in the list - staff read these
        // in the order they entered them.
        draft.Items[index] = outcome.Item!;
        _drafts.Save(draft);

        return RedirectToAction(nameof(NewItem));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveDraftItem(int index)
    {
        var draft = _drafts.Get();
        if (draft is null || index < 0 || index >= draft.Items.Count)
            return NotFound();

        draft.Items.RemoveAt(index);
        _drafts.Save(draft);

        return RedirectToAction(nameof(NewItem));
    }

    // ── Step 3: payment, and the only point where anything is saved ────
    [HttpGet]
    public IActionResult NewPayment()
    {
        var draft = _drafts.Get();
        if (draft is null)
            return RedirectToAction(nameof(New));

        if (draft.Items.Count == 0)
            return RedirectToAction(nameof(NewItem));

        PreparePaymentStep(draft);
        return View(new NewOrderPaymentRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewPayment(NewOrderPaymentRequest model)
    {
        var draft = _drafts.Get();
        if (draft is null)
            return RedirectToAction(nameof(New));

        if (model.Amount < 0)
            ModelState.AddModelError(nameof(model.Amount), "المبلغ غير صحيح");

        if (!ModelState.IsValid)
        {
            PreparePaymentStep(draft);
            return View(model);
        }

        var outcome = await SafeAsync(() => _newOrderService.CommitDraftAsync(draft, model), "تسجيل الطلب");
        if (outcome is null || !outcome.Succeeded)
        {
            if (outcome is not null)
                ModelState.AddModelError(string.Empty, outcome.ErrorMessage!);

            PreparePaymentStep(draft);
            return View(model);
        }

        // Saved - the draft has served its purpose.
        var invoiceNumber = draft.InvoiceNumber;
        _drafts.Clear();

        TempData["OrderCreated"] = $"تم تسجيل الطلب {Isolate(invoiceNumber)} بنجاح";
        return RedirectToAction(nameof(Index));
    }

    // Abandoning the wizard. Nothing was ever written, so this just throws the draft away.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel()
    {
        _drafts.Clear();
        TempData["OrderCancelled"] = "تم إلغاء الطلب.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PrepareItemStepAsync(OrderDraft draft)
    {
        ViewBag.Draft = draft;
        ViewData["OrderInProgress"] = true;
        await PopulateDoctorsAsync(draft.DoctorId);
    }

    private void PreparePaymentStep(OrderDraft draft)
    {
        ViewBag.Draft = draft;
        ViewData["OrderInProgress"] = true;
    }

    // Backs the Orders/Index search box. Deliberately does not pass a fromDate - the
    // whole point is finding orders the default last-4-days view doesn't show, so a
    // matching order elsewhere in time still needs to turn up (and be selectable for
    // BulkUpdateStatus like any other row).
    [HttpGet]
    public async Task<IActionResult> SearchOrders(string term, string? status, string? deliveryType)
    {
        var statusFilter = Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var s) ? s : (OrderStatus?)null;
        var deliveryFilter = Enum.TryParse<DeliveryType>(deliveryType, ignoreCase: true, out var d) ? d : (DeliveryType?)null;

        var orders = string.IsNullOrWhiteSpace(term)
            ? await _orderService.GetOrderListAsync(statusFilter, deliveryFilter, fromDate: DateTime.Now.AddDays(-4))
            : await _orderService.GetOrderListAsync(statusFilter, deliveryFilter, searchTerm: term);

        return PartialView("_OrdersTableRows", orders);
    }

    // Row-selection toolbar on Orders/Index - applies one status change to every
    // checked order at once instead of opening each order's popup individually.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkUpdateStatus(int[] orderIds, OrderStatus newStatus, string? notes, string? status, string? deliveryType)
    {
        if (orderIds is not { Length: > 0 })
            return RedirectToAction(nameof(Index), new { status, deliveryType });

        var result = await _orderService.BulkUpdateStatusAsync(orderIds, newStatus, notes);

        if (result.Failures.Count == 0)
        {
            TempData["BulkStatusMessage"] = $"تم تحديث حالة {result.SuccessCount} طلب بنجاح";
        }
        else
        {
            var failureList = string.Join("، ", result.Failures.Select(f => $"{Isolate(f.InvoiceNumber)} ({f.ErrorMessage})"));
            TempData["StatusUpdateError"] = result.SuccessCount == 0
                ? $"تعذر تحديث أي طلب: {failureList}"
                : $"تم تحديث {result.SuccessCount} طلب، وتعذر تحديث: {failureList}";
        }

        return RedirectToAction(nameof(Index), new { status, deliveryType });
    }

    // ── Order detail popup (Orders/Index row click) ─────────────────────
    // The popup can hold several operations at once - a status change, a frame swap -
    // staged here in the session and left untouched until its outer "تأكيد" commits every
    // one of them together. Nothing any individual "confirm" button inside the popup does
    // reaches the database by itself; see ConfirmPendingEdits.
    //
    // Staging a change is done via AJAX (see stageEdit() in Index.cshtml): the browser
    // POSTs the form, gets this same partial back, and swaps it into the still-open popup
    // in place. Nothing here does a RedirectToAction - a redirect means a full page
    // navigation, which is exactly the "popup closes and reopens" flash this is meant to
    // avoid. Only ConfirmPendingEdits (the outer تأكيد) still redirects, because closing
    // the popup on a successful commit is the one point where that's actually wanted.
    [HttpGet]
    public async Task<IActionResult> Detail(int id) => await RenderOrderDetailAsync(id);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StageStatusChange(int orderId, OrderStatus newStatus, string? notes)
    {
        var outcome = await SafeAsync(() => _orderService.BuildStatusChangeEditAsync(orderId, newStatus, notes), "تحديث حالة الطلب");
        if (outcome is { Succeeded: true })
            StageEdit(orderId, outcome.Edit!);

        return await RenderOrderDetailAsync(orderId, outcome is { Succeeded: true } ? null : outcome?.ErrorMessage ?? "تعذر تحديث الحالة.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Covers both reasons a frame gets replaced: the customer changed their mind (the old
    // frame is intact and goes back on the shelf) or it was damaged (written off).
    // newFrameId is left unset when the replacement is the customer's own frame, which is
    // how sp_swap_frame is told the item stops drawing on inventory.
    public async Task<IActionResult> StageFrameSwap(int orderId, int itemId, int? newFrameId, decimal newFrameAgreedPrice, bool returnOldFrameToStock, string? externalFrameNotes, string? notes)
    {
        var action = returnOldFrameToStock ? "تغيير الإطار" : "استبدال إطار تالف";

        var outcome = await SafeAsync(() => _orderService.BuildFrameSwapEditAsync(itemId, newFrameId, newFrameAgreedPrice, returnOldFrameToStock, externalFrameNotes, notes), action);
        if (outcome is { Succeeded: true })
            StageEdit(orderId, outcome.Edit!);

        return await RenderOrderDetailAsync(orderId, outcome is { Succeeded: true } ? null : outcome?.ErrorMessage ?? "تعذر تغيير الإطار.");
    }

    // For items with no inventory frame to begin with (استبدال عدسات, customer's own
    // external frame) - same "الإطار تالف" trigger, different SP underneath since
    // sp_swap_frame requires an existing frame_id to swap out.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StageFrameCompensation(int orderId, int itemId, int newFrameId, decimal newFrameAgreedPrice, string? notes)
    {
        var outcome = await SafeAsync(() => _orderService.BuildFrameCompensationEditAsync(itemId, newFrameId, newFrameAgreedPrice, notes), "تعيين إطار تعويضي");
        if (outcome is { Succeeded: true })
            StageEdit(orderId, outcome.Edit!);

        return await RenderOrderDetailAsync(orderId, outcome is { Succeeded: true } ? null : outcome?.ErrorMessage ?? "تعذر تعيين الإطار.");
    }

    // ── Adding an item to an order that already exists ──────────────────
    // Its own page rather than a popup modal: this is the full item form (frame lookup,
    // lens details, prescription), the same one the wizard uses. It also writes straight
    // away instead of joining the popup's staged edits - a whole item, with its
    // prescription, doesn't fit the shape of a PendingOrderEdit.
    [HttpGet]
    public async Task<IActionResult> AddItem(int id)
    {
        var order = await _orderService.GetByIdAsync(id);
        if (order is null)
            return NotFound();

        ViewBag.Order = order;
        return View(new NewOrderItemRequest { DoctorId = order.DoctorId, CustomerNameOnInvoice = order.CustomerName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(int id, NewOrderItemRequest model)
    {
        var outcome = await SafeAsync(() => _newOrderService.AddItemToExistingOrderAsync(id, model), "إضافة البند");

        if (outcome is { Succeeded: true })
        {
            TempData["BulkStatusMessage"] = "تم إضافة البند للطلب بنجاح";
            return RedirectToAction(nameof(Index), new { openOrder = id });
        }

        if (outcome is not null)
        {
            foreach (var (field, message) in outcome.FieldErrors)
                ModelState.AddModelError(field, message);

            if (!string.IsNullOrWhiteSpace(outcome.ErrorMessage))
                ModelState.AddModelError(string.Empty, outcome.ErrorMessage);
        }

        var order = await _orderService.GetByIdAsync(id);
        if (order is null)
            return NotFound();

        ViewBag.Order = order;
        return View(model);
    }

    // Records a later instalment on an existing order - the customer coming back to settle
    // the rest. Staged like every other edit, so "paid the remainder and collected the
    // glasses" commits as one action alongside the status change.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StagePayment(int orderId, decimal amount, PaymentMethod paymentMethod, string? notes)
    {
        var outcome = await SafeAsync(() => _orderService.BuildPaymentEditAsync(orderId, amount, paymentMethod, notes), "تسجيل الدفعة");
        if (outcome is { Succeeded: true })
            StageEdit(orderId, outcome.Edit!);

        return await RenderOrderDetailAsync(orderId, outcome is { Succeeded: true } ? null : outcome?.ErrorMessage ?? "تعذر تسجيل الدفعة.");
    }

    // Money back to the customer - a cancelled order, or items cancelled out of one that
    // was already paid for. Staged like the rest, so cancelling the item and refunding what
    // it cost commit together instead of leaving the books half-corrected.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StageRefund(int orderId, decimal amount, PaymentMethod paymentMethod, string? notes)
    {
        var outcome = await SafeAsync(() => _orderService.BuildRefundEditAsync(orderId, amount, paymentMethod, notes), "تسجيل الاسترداد");
        if (outcome is { Succeeded: true })
            StageEdit(orderId, outcome.Edit!);

        return await RenderOrderDetailAsync(orderId, outcome is { Succeeded: true } ? null : outcome?.ErrorMessage ?? "تعذر تسجيل الاسترداد.");
    }

    // Cancels a single item without touching the order's other items. The disposition
    // decides what happens to its frame - returned to stock, or written off as damaged.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StageItemCancellation(int orderId, int itemId, CancelledFrameDisposition disposition, string? notes)
    {
        var outcome = await SafeAsync(() => _orderService.BuildItemCancellationEditAsync(itemId, disposition, notes), "إلغاء البند");
        if (outcome is { Succeeded: true })
            StageEdit(orderId, outcome.Edit!);

        return await RenderOrderDetailAsync(orderId, outcome is { Succeeded: true } ? null : outcome?.ErrorMessage ?? "تعذر إلغاء البند.");
    }

    // Lets staff drop one staged operation before confirming the rest.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePendingEdit(int orderId, Guid editId)
    {
        var set = _pendingEdits.Get(orderId);
        if (set is not null)
        {
            set.Edits.RemoveAll(e => e.EditId == editId);
            _pendingEdits.Save(set);
        }

        return await RenderOrderDetailAsync(orderId);
    }

    // The popup's outer "تأكيد" - the only button that actually writes anything. Applies
    // every staged edit as one transaction and only then clears the pending set.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPendingEdits(int orderId)
    {
        var edits = _pendingEdits.Get(orderId)?.Edits ?? new List<PendingOrderEdit>();

        var outcome = await SafeAsync(() => _orderService.CommitPendingEditsAsync(orderId, edits), "تأكيد التغييرات");
        if (outcome is { Succeeded: true })
        {
            _pendingEdits.Clear(orderId);
            return RedirectToAction(nameof(Index));
        }

        // Failure leaves the pending edits exactly as they were - nothing was applied,
        // so nothing about what's staged needs to change.
        TempData["StatusUpdateError"] = outcome?.ErrorMessage ?? "تعذر حفظ التغييرات.";
        return RedirectToAction(nameof(Index), new { openOrder = orderId });
    }

    private async Task<IActionResult> RenderOrderDetailAsync(int orderId, string? error = null)
    {
        var order = await _orderService.GetByIdAsync(orderId);
        if (order is null)
            return NotFound();

        var customer = await _newOrderService.GetCustomerAsync(order.CustomerId);
        var doctor = order.DoctorId.HasValue ? await _orderService.GetDoctorByIdAsync(order.DoctorId.Value) : null;
        var pendingEdits = _pendingEdits.Get(orderId)?.Edits ?? new List<PendingOrderEdit>();

        ViewBag.PendingEditError = error;
        return PartialView("_OrderDetail", new OrderDetailViewModel(order, customer, doctor, pendingEdits));
    }

    // Staging a status change replaces any previously-staged status change (only one
    // final status makes sense at a time); staging a frame edit for an item replaces any
    // previously-staged edit for that same item, for the same reason.
    private void StageEdit(int orderId, PendingOrderEdit edit)
    {
        var set = _pendingEdits.Get(orderId);
        if (set is null || set.OrderId != orderId)
            set = new PendingOrderEditSet { OrderId = orderId };

        set.Edits.RemoveAll(e => e.Kind == edit.Kind && e.ItemId == edit.ItemId);
        set.Edits.Add(edit);
        _pendingEdits.Save(set);
    }

    private async Task PopulateDoctorsAsync(int? selectedDoctorId)
    {
        var doctors = await _newOrderService.GetDoctorsAsync();
        ViewBag.Doctors = new SelectListWithSelection(doctors, selectedDoctorId);
    }

    // An invoice number like "150-999-45" is digits and hyphens only - all weak or neutral
    // under the Unicode bidi algorithm - so dropping one into an Arabic (right-to-left)
    // sentence lets the surrounding direction reorder its groups, and it renders on screen
    // as "45-999-150". Measured in the browser: without isolation the three groups land at
    // x=1093/1061/1038 (backwards); with it, 1038/1070/1102 (correct).
    //
    // Views avoid this by wrapping the number in <bdi>. These messages cannot: they travel
    // through TempData as plain text and are HTML-encoded when rendered, so a tag would
    // show up literally. The isolation therefore has to be characters - which is what <bdi>
    // amounts to anyway.
    //
    // Built from code points rather than written literally because both characters are
    // invisible, and unseen bidi controls in a string literal make source read differently
    // from how it runs.
    private const char LeftToRightIsolate = (char)0x2066;
    private const char PopDirectionalIsolate = (char)0x2069;

    private static string Isolate(string text) => LeftToRightIsolate + text + PopDirectionalIsolate;

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

public record OrderDetailViewModel(
    HanyOptics.Domain.Entities.Order Order,
    HanyOptics.Domain.Entities.Customer? Customer,
    HanyOptics.Domain.Entities.Doctor? Doctor,
    IReadOnlyList<PendingOrderEdit> PendingEdits);
