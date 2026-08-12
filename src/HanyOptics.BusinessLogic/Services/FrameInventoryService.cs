using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HanyOptics.BusinessLogic.Services;

public class FrameInventoryService : IFrameInventoryService
{
    private readonly HanyOpticsDbContext _dbContext;

    public FrameInventoryService(HanyOpticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FrameListItem>> GetFramesAsync(
        FrameStatus? status = null,
        FrameCategory? category = null,
        FrameTrackingType? trackingType = null,
        string? searchTerm = null)
    {
        var query = _dbContext.Frames.AsNoTracking().AsQueryable();

        if (status.HasValue)
            query = query.Where(f => f.Status == status.Value);
        if (category.HasValue)
            query = query.Where(f => f.Category == category.Value);
        if (trackingType.HasValue)
            query = query.Where(f => f.TrackingType == trackingType.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm.Trim()}%";
            query = query.Where(f =>
                EF.Functions.Like(f.Barcode, pattern) ||
                (f.Brand != null && EF.Functions.Like(f.Brand, pattern)) ||
                (f.ModelName != null && EF.Functions.Like(f.ModelName, pattern)) ||
                (f.Color != null && EF.Functions.Like(f.Color, pattern)));
        }

        // Whatever is still sellable first - that is what someone standing at the counter
        // with a customer needs to see - then newest, since recent stock is what staff are
        // least likely to remember.
        return await query
            .OrderByDescending(f => f.QtyAvailable > 0)
            .ThenByDescending(f => f.FrameId)
            .Select(f => new FrameListItem
            {
                FrameId = f.FrameId,
                Barcode = f.Barcode,
                Brand = f.Brand,
                ModelName = f.ModelName,
                Color = f.Color,
                Size = f.Size,
                Category = f.Category,
                TrackingType = f.TrackingType,
                CostPrice = f.CostPrice,
                SellPrice = f.SellPrice,
                QtyAvailable = f.QtyAvailable,
                QtyInitial = f.QtyInitial,
                Status = f.Status,
                Notes = f.Notes
            })
            .ToListAsync();
    }

    // Runs over the already-fetched rows rather than issuing a second aggregate query, so
    // the totals cannot disagree with the list they sit above - a separate COUNT/SUM could
    // land either side of someone else's sale.
    public FrameInventorySummary Summarise(IReadOnlyList<FrameListItem> frames) => new()
    {
        LineCount = frames.Count,
        TotalUnitsAvailable = frames.Sum(f => f.QtyAvailable),
        StockValueAtCost = frames.Sum(f => f.CostPrice * f.QtyAvailable),
        StockValueAtSell = frames.Sum(f => f.SellPrice * f.QtyAvailable)
    };
}
