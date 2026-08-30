using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using HanyOptics.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HanyOptics.BusinessLogic.Services;

public class FrameInventoryService : IFrameInventoryService
{
    private readonly HanyOpticsDbContext _dbContext;
    private readonly ILogger<FrameInventoryService> _logger;

    public FrameInventoryService(HanyOpticsDbContext dbContext, ILogger<FrameInventoryService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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

    public async Task<FrameBarcodeLookupResult> LookupBarcodeAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return new FrameBarcodeLookupResult { Message = "امسح الباركود أو اكتبه" };

        var code = barcode.Trim();

        var existing = await _dbContext.Frames.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Barcode == code);

        if (existing is not null)
        {
            return new FrameBarcodeLookupResult
            {
                AlreadyExists = true,
                ExistingFrameId = existing.FrameId,
                ExistingLabel = $"{existing.Brand} {existing.ModelName}".Trim(),
                ExistingQtyAvailable = existing.QtyAvailable,
                ExistingStatus = StatusLabel(existing.Status),
                ExistingBrand = existing.Brand,
                ExistingModelName = existing.ModelName,
                ExistingColor = existing.Color,
                ExistingSize = existing.Size,
                ExistingCategory = existing.Category.ToString(),
                ExistingTrackingType = existing.TrackingType.ToString(),
                ExistingCostPrice = existing.CostPrice,
                ExistingSellPrice = existing.SellPrice,
                ExistingNotes = existing.Notes,
                Message = "الباركود ده مسجّل بالفعل"
            };
        }

        return new FrameBarcodeLookupResult
        {
            AlreadyExists = false,
            DecodedSellPrice = FrameBarcode.TryReadSellPrice(code),
            Message = "إطار جديد — أكمل البيانات"
        };
    }

    public async Task<AddFrameOutcome> AddFrameAsync(AddFrameRequest request)
    {
        var barcode = request.Barcode?.Trim();
        if (string.IsNullOrWhiteSpace(barcode))
            return AddFrameOutcome.Failure("امسح الباركود أو اكتبه");

        if (string.IsNullOrWhiteSpace(request.Brand))
            return AddFrameOutcome.Failure("أدخل الماركة");

        if (request.CostPrice < 0 || request.SellPrice < 0)
            return AddFrameOutcome.Failure("السعر غير صحيح");

        // An individual frame is one physical piece, so its quantity is not the user's to
        // set - whatever the form posted is replaced rather than validated.
        var quantity = request.TrackingType == FrameTrackingType.Individual ? 1 : request.Quantity;
        if (quantity < 1)
            return AddFrameOutcome.Failure("الكمية غير صحيحة");

        try
        {
            var frameIdParam = new SqlParameter("@p_frame_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

            // No stored procedure for this one, so the statement carries the rules an SP
            // normally would. The barcode check is inside the INSERT rather than a separate
            // SELECT first: two people receiving stock at once would both pass a prior
            // check and the second would then hit the unique index as a raw SQL error.
            // Here the insert simply matches nothing, and the THROW says why in Arabic.
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO frames (branch_id, barcode, tracking_type, category, brand, model_name,
                                    color, size, cost_price, sell_price, qty_initial, qty_available,
                                    status, notes)
                SELECT 1, @p_barcode, @p_tracking, @p_category, @p_brand, @p_model,
                       @p_color, @p_size, @p_cost, @p_sell, @p_qty, @p_qty,
                       'available', @p_notes
                WHERE NOT EXISTS (SELECT 1 FROM frames WHERE barcode = @p_barcode);

                IF @@ROWCOUNT = 0
                    THROW 50000, N'الباركود ده مسجّل على إطار تاني بالفعل', 1;

                SET @p_frame_id = SCOPE_IDENTITY();
                """,
                new SqlParameter("@p_barcode", SqlDbType.NVarChar, 50) { Value = barcode },
                new SqlParameter("@p_tracking", SqlDbType.NVarChar, 10) { Value = request.TrackingType == FrameTrackingType.Bulk ? "bulk" : "individual" },
                new SqlParameter("@p_category", SqlDbType.NVarChar, 10) { Value = request.Category == FrameCategory.Sun ? "sun" : "optical" },
                new SqlParameter("@p_brand", SqlDbType.NVarChar, 100) { Value = request.Brand.Trim() },
                new SqlParameter("@p_model", SqlDbType.NVarChar, 100) { Value = (object?)request.ModelName?.Trim() ?? DBNull.Value },
                new SqlParameter("@p_color", SqlDbType.NVarChar, 50) { Value = (object?)request.Color?.Trim() ?? DBNull.Value },
                new SqlParameter("@p_size", SqlDbType.NVarChar, 20) { Value = (object?)request.Size?.Trim() ?? DBNull.Value },
                new SqlParameter("@p_cost", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = request.CostPrice },
                new SqlParameter("@p_sell", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = request.SellPrice },
                new SqlParameter("@p_qty", SqlDbType.Int) { Value = quantity },
                new SqlParameter("@p_notes", SqlDbType.NVarChar, 500) { Value = (object?)request.Notes?.Trim() ?? DBNull.Value },
                frameIdParam);

            var frameId = frameIdParam.Value is int id ? id : 0;
            _logger.LogInformation("Added frame {FrameId} ({Barcode}) to stock, qty {Qty}.", frameId, barcode, quantity);
            return AddFrameOutcome.Success(frameId);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error adding frame {Barcode}. SqlErrors={SqlErrors}",
                barcode, StoredProcedureErrors.Describe(ex));
            return AddFrameOutcome.Failure(StoredProcedureErrors.ToUserMessage(ex, "الباركود ده مسجّل بالفعل"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error adding frame {Barcode}.", barcode);
            return AddFrameOutcome.Failure(StoredProcedureErrors.GenericMessage);
        }
    }

    private static string StatusLabel(FrameStatus s) => s switch
    {
        FrameStatus.Available => "متاح",
        FrameStatus.Reserved => "محجوز",
        FrameStatus.Sold => "مباع",
        FrameStatus.Damaged => "تالف",
        _ => s.ToString()
    };
}
