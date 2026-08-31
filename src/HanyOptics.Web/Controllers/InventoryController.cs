using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HanyOptics.Web.Controllers;

// Read-only for now: browsing and searching frame stock. Every write to frames happens
// through the order flow's stored procedures (reserve on sale, return on cancel, write
// off on damage), so there is deliberately no editing here - see IFrameInventoryService.
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

    public async Task<IActionResult> Index(string? status, string? category, string? tracking, string? q)
    {
        var statusFilter = Enum.TryParse<FrameStatus>(status, ignoreCase: true, out var s) ? s : (FrameStatus?)null;
        var categoryFilter = Enum.TryParse<FrameCategory>(category, ignoreCase: true, out var c) ? c : (FrameCategory?)null;
        var trackingFilter = Enum.TryParse<FrameTrackingType>(tracking, ignoreCase: true, out var t) ? t : (FrameTrackingType?)null;

        var frames = await _frames.GetFramesAsync(statusFilter, categoryFilter, trackingFilter, q);

        ViewBag.StatusFilter = statusFilter;
        ViewBag.CategoryFilter = categoryFilter;
        ViewBag.TrackingFilter = trackingFilter;
        ViewBag.SearchTerm = q;
        ViewBag.Summary = _frames.Summarise(frames);

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
}
