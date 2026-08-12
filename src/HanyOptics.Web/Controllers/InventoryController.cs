using HanyOptics.BusinessLogic.Interfaces;
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

    public InventoryController(IFrameInventoryService frames)
    {
        _frames = frames;
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
}
