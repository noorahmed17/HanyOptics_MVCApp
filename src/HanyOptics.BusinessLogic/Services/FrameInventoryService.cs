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

    public async Task<AddFrameOutcome> AddFrameAsync(AddFrameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Brand))
            return AddFrameOutcome.Failure("أدخل الماركة");

        // sp_generate_barcode hides the sell price inside the code, so there is no barcode
        // to generate without one. Left blank is not the same as zero, but neither can be
        // saved: zero would produce a code meaning "free".
        if (request.SellPrice is null or <= 0)
            return AddFrameOutcome.Failure("أدخل سعر البيع — الباركود بيتولّد منه");

        // Cost is genuinely optional - stock sometimes arrives before the invoice does -
        // so a blank box means "not known yet" and stores as 0 rather than blocking a save.
        var costPrice = request.CostPrice ?? 0m;
        if (costPrice < 0)
            return AddFrameOutcome.Failure("التكلفة غير صحيحة");

        // An individual frame is one physical piece, so its quantity is not the user's to
        // set - whatever the form posted is replaced rather than validated.
        var quantity = request.TrackingType == FrameTrackingType.Individual ? 1 : request.Quantity;
        if (quantity < 1)
            return AddFrameOutcome.Failure("الكمية غير صحيحة");

        try
        {
            var frameIdParam = new SqlParameter("@p_frame_id", SqlDbType.Int) { Direction = ParameterDirection.Output };
            var barcodeOut = new SqlParameter("@p_barcode_out", SqlDbType.NVarChar, 50) { Direction = ParameterDirection.Output };

            // Generating and inserting in one batch, with a retry, rather than two round
            // trips. sp_generate_barcode checks the code is unused, but that check and our
            // INSERT are not one instant: another till receiving stock at the same moment
            // could take the code in between. The conditional INSERT catches that, and the
            // loop simply asks for another code instead of failing in the user's face.
            await _dbContext.Database.ExecuteSqlRawAsync(
                """
                SET NOCOUNT ON;

                DECLARE @attempt INT = 0, @barcode NVARCHAR(50), @done BIT = 0;

                WHILE @attempt < 5 AND @done = 0
                BEGIN
                    EXEC sp_generate_barcode @sell_price = @p_sell, @barcode = @barcode OUTPUT;

                    INSERT INTO frames (branch_id, barcode, tracking_type, category, brand, model_name,
                                        color, size, cost_price, sell_price, qty_initial, qty_available,
                                        status, notes)
                    SELECT 1, @barcode, @p_tracking, @p_category, @p_brand, @p_model,
                           @p_color, @p_size, @p_cost, @p_sell, @p_qty, @p_qty,
                           'available', @p_notes
                    WHERE NOT EXISTS (SELECT 1 FROM frames WHERE barcode = @barcode);

                    IF @@ROWCOUNT = 1
                    BEGIN
                        SET @p_frame_id     = SCOPE_IDENTITY();
                        SET @p_barcode_out  = @barcode;
                        SET @done = 1;
                    END

                    SET @attempt = @attempt + 1;
                END

                IF @done = 0
                    THROW 50000, N'تعذّر توليد باركود فريد للإطار — حاول تاني', 1;
                """,
                new SqlParameter("@p_tracking", SqlDbType.NVarChar, 10) { Value = request.TrackingType == FrameTrackingType.Bulk ? "bulk" : "individual" },
                new SqlParameter("@p_category", SqlDbType.NVarChar, 10) { Value = request.Category == FrameCategory.Sun ? "sun" : "optical" },
                new SqlParameter("@p_brand", SqlDbType.NVarChar, 100) { Value = request.Brand.Trim() },
                new SqlParameter("@p_model", SqlDbType.NVarChar, 100) { Value = (object?)request.ModelName?.Trim() ?? DBNull.Value },
                new SqlParameter("@p_color", SqlDbType.NVarChar, 50) { Value = (object?)request.Color?.Trim() ?? DBNull.Value },
                new SqlParameter("@p_size", SqlDbType.NVarChar, 20) { Value = (object?)request.Size?.Trim() ?? DBNull.Value },
                new SqlParameter("@p_cost", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = costPrice },
                new SqlParameter("@p_sell", SqlDbType.Decimal) { Precision = 10, Scale = 2, Value = request.SellPrice.Value },
                new SqlParameter("@p_qty", SqlDbType.Int) { Value = quantity },
                new SqlParameter("@p_notes", SqlDbType.NVarChar, 500) { Value = (object?)request.Notes?.Trim() ?? DBNull.Value },
                frameIdParam,
                barcodeOut);

            var frameId = frameIdParam.Value is int id ? id : 0;
            var barcode = barcodeOut.Value as string;

            _logger.LogInformation("Added frame {FrameId} to stock as {Barcode}, qty {Qty}.", frameId, barcode, quantity);
            return AddFrameOutcome.Success(frameId, barcode ?? string.Empty);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "SQL error adding frame {Brand}. SqlErrors={SqlErrors}",
                request.Brand, StoredProcedureErrors.Describe(ex));
            return AddFrameOutcome.Failure(StoredProcedureErrors.ToUserMessage(ex, "الباركود ده مسجّل بالفعل"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error adding frame {Brand}.", request.Brand);
            return AddFrameOutcome.Failure(StoredProcedureErrors.GenericMessage);
        }
    }
}
