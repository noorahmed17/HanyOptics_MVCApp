using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// Mostly read-only: browsing and searching frame stock. Every stock change that happens as
// a *side effect* of something else the shop does (reserve on sale, return on cancel, write
// off on damage) runs through the order flow's stored procedures, never through here - see
// IFrameInventoryService. Receiving new stock (AddFrame) and topping up existing bulk stock
// (BulkRestock) are not side effects of anything else, so those two writes live here.
[Authorize]
public class InventoryController : Controller
{
    private readonly IFrameInventoryService _frames;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(IFrameInventoryService frames, ILogger<InventoryController> logger)
    {
        _frames = frames;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? status, string? category, string? tracking, string? q, int? page)
    {
        var statusFilter = Enum.TryParse<FrameStatus>(status, ignoreCase: true, out var s) ? s : (FrameStatus?)null;
        var categoryFilter = Enum.TryParse<FrameCategory>(category, ignoreCase: true, out var c) ? c : (FrameCategory?)null;
        var trackingFilter = Enum.TryParse<FrameTrackingType>(tracking, ignoreCase: true, out var t) ? t : (FrameTrackingType?)null;

        var frames = await _frames.GetFramesAsync(statusFilter, categoryFilter, trackingFilter, q, page);

        ViewBag.StatusFilter = statusFilter;
        ViewBag.CategoryFilter = categoryFilter;
        ViewBag.TrackingFilter = trackingFilter;
        ViewBag.SearchTerm = q;

        // Summarised over the same filters rather than over the page, so the cards keep
        // describing the whole filtered stock however far into it you have paged.
        ViewBag.Summary = await _frames.SummariseAsync(statusFilter, categoryFilter, trackingFilter, q);

        return View(frames);
    }

    // ── Receiving new stock ────────────────────────────────────────────
    // The frame's details are entered, and the barcode comes back out: sp_generate_barcode
    // derives it from the sell price, and the label is printed afterwards. Nothing is
    // scanned here - there is no label on the frame yet.
    [HttpGet]
    public IActionResult AddFrame() => View(new AddFrameRequest());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFrame(AddFrameRequest model)
    {
        if (!ModelState.IsValid)
            return View(model);

        AddFrameOutcome outcome;
        try
        {
            outcome = await _frames.AddFrameAsync(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error adding frame {Brand}", model.Brand);
            ModelState.AddModelError(string.Empty, "حدث خطأ غير متوقع — حاول مرة أخرى");
            return View(model);
        }

        if (!outcome.Succeeded)
        {
            ModelState.AddModelError(string.Empty, outcome.ErrorMessage!);
            return View(model);
        }

        // Straight back to a blank form - stock arrives in boxes - but carrying the
        // generated barcode, which is the one thing the user still needs: it goes into
        // BarTender to print the label that then goes on the frame.
        TempData["FrameAddedName"] = $"{model.Brand} {model.ModelName}".Trim();
        TempData["FrameAddedBarcode"] = outcome.Barcode;
        return RedirectToAction(nameof(AddFrame));
    }

    // Row-selection toolbar on Inventory/Index - adds the same quantity to every selected
    // frame at once. A plain POST-and-redirect, not AJAX: the list needs to re-render with
    // the now-updated quantities, and the filters are carried through so the redirect lands
    // back on whatever view the selection was made from.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkRestock(int[] frameIds, int qtyToAdd, string? status, string? category, string? tracking, string? q)
    {
        if (frameIds is not { Length: > 0 })
            return RedirectToAction(nameof(Index), new { status, category, tracking, q });

        var result = await _frames.RestockFramesAsync(frameIds, qtyToAdd);

        if (result.Failures.Count == 0)
        {
            TempData["BulkRestockMessage"] = $"تمت زيادة الكمية بمقدار {qtyToAdd} لـ {result.SuccessCount} إطار بنجاح";
        }
        else
        {
            var failureList = string.Join("، ", result.Failures.Select(f => $"{Isolate(f.Barcode)} ({f.ErrorMessage})"));
            TempData["RestockError"] = result.SuccessCount == 0
                ? $"تعذرت زيادة الكمية لأي إطار: {failureList}"
                : $"تمت زيادة الكمية لـ {result.SuccessCount} إطار، وتعذر لـ: {failureList}";
        }

        return RedirectToAction(nameof(Index), new { status, category, tracking, q });
    }

    // Same reasoning as OrdersController.Isolate(): a barcode mixes digits and letters,
    // both weak/neutral under the bidi algorithm, so embedded in this Arabic message it can
    // reorder on screen without these controls. TempData carries plain text (HTML-encoded
    // on render), so the isolation has to be characters, not a <bdi> tag. Built from code
    // points rather than written as literal characters because both are invisible, and
    // unseen bidi controls in source read differently from how they run.
    private const char LeftToRightIsolate = (char)0x2066;
    private const char PopDirectionalIsolate = (char)0x2069;

    private static string Isolate(string text) => LeftToRightIsolate + text + PopDirectionalIsolate;
}
