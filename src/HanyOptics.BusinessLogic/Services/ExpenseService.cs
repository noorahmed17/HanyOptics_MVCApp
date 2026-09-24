using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static HanyOptics.BusinessLogic.Services.SqlReader;

namespace HanyOptics.BusinessLogic.Services;

public class ExpenseService : IExpenseService
{
    private const int PageSize = 30;

    // Starters for the category box before the shop has typed any of its own. Whatever
    // has actually been used is offered alongside these (see GetCategorySuggestionsAsync).
    private static readonly string[] ExpenseStarters = ["كهرباء", "إيجار", "رواتب", "نظافة", "شركة عدسات", "تجار إطارات"];
    private static readonly string[] IncomeStarters = ["تصليحات", "إكسسوارات"];

    // Joined rather than read from the table alone so the list shows who and which
    // supplier instead of bare ids.
    private const string EntrySelect = """
        SELECT e.expense_id, e.paid_at, dbo.fn_business_date(e.paid_at) AS business_date,
               e.entry_type, e.amount, e.funding_source, e.payment_method, e.category,
               e.supplier_id, s.name AS supplier_name, e.description,
               u.name AS created_by_name, e.cancelled_at, e.cancel_reason
        FROM dbo.expenses e
        LEFT JOIN dbo.suppliers s ON s.supplier_id = e.supplier_id
        LEFT JOIN dbo.users     u ON u.user_id     = e.created_by
        """;

    private readonly HanyOpticsDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ExpenseService> _logger;

    public ExpenseService(HanyOpticsDbContext dbContext, ICurrentUser currentUser, ILogger<ExpenseService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<DrawerSummary> GetDrawerAsync() =>
        WithConnectionAsync(_dbContext, async connection =>
        {
            // The business day is asked of the database, like the daily close does: the
            // 06:00 cutoff lives in dbo.fn_business_date and nowhere else.
            DateOnly day;
            decimal balance, salesCash, entriesEffect;

            await using (var command = Command(connection, """
                DECLARE @day DATE = dbo.fn_business_date(SYSDATETIME());
                SELECT @day AS business_date,
                       dbo.fn_drawer_balance(@day) AS balance,
                       ISNULL((SELECT cash_net          FROM dbo.vw_daily_close    WHERE business_date = @day), 0) AS sales_cash,
                       ISNULL((SELECT net_drawer_effect FROM dbo.vw_expenses_daily WHERE business_date = @day), 0) AS entries_effect;
                """))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                day = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("business_date")));
                balance = reader.Decimal("balance");
                salesCash = reader.Decimal("sales_cash");
                entriesEffect = reader.Decimal("entries_effect");
            }

            var today = await QueryAsync(connection,
                EntrySelect + " WHERE dbo.fn_business_date(e.paid_at) = @day ORDER BY e.paid_at DESC, e.expense_id DESC;",
                MapEntry,
                new SqlParameter("@day", SqlDbType.Date) { Value = day.ToDateTime(TimeOnly.MinValue) });

            return new DrawerSummary
            {
                BusinessDate = day,
                Balance = balance,
                SalesCash = salesCash,
                EntriesEffect = entriesEffect,
                TodayEntries = today
            };
        });

    public Task<PagedResult<ExpenseEntry>> GetEntriesAsync(int? page, int? pageSize = null) =>
        WithConnectionAsync(_dbContext, async connection =>
        {
            int total;
            await using (var count = Command(connection, "SELECT COUNT(*) FROM dbo.expenses;"))
                total = (int)(await count.ExecuteScalarAsync())!;

            var (current, size) = PagedResult<ExpenseEntry>.Normalise(page, pageSize ?? PageSize, total);

            var rows = await QueryAsync(connection,
                EntrySelect + " ORDER BY e.paid_at DESC, e.expense_id DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;",
                MapEntry,
                new SqlParameter("@skip", (current - 1) * size),
                new SqlParameter("@take", size));

            return new PagedResult<ExpenseEntry> { Items = rows, TotalCount = total, Page = current, PageSize = size };
        });

    public Task<ExpenseEntry?> GetEntryAsync(int expenseId) =>
        WithConnectionAsync(_dbContext, async connection =>
            (await QueryAsync(connection, EntrySelect + " WHERE e.expense_id = @id;", MapEntry,
                new SqlParameter("@id", expenseId))).FirstOrDefault());

    public Task<ExpenseMonthReport> GetMonthReportAsync(int year, int month) =>
        WithConnectionAsync(_dbContext, async connection => new ExpenseMonthReport
        {
            Year = year,
            Month = month,
            Categories = await QueryAsync(connection,
                "SELECT category, total_amount, entries_count FROM dbo.vw_expenses_by_category WHERE [year] = @y AND [month] = @m ORDER BY total_amount DESC;",
                r => new ExpenseCategoryTotal
                {
                    Category = r.GetString(r.GetOrdinal("category")),
                    Total = r.Decimal("total_amount"),
                    Count = r.GetInt32(r.GetOrdinal("entries_count"))
                },
                new SqlParameter("@y", year),
                new SqlParameter("@m", month))
        });

    public Task<IReadOnlyList<string>> GetCategorySuggestionsAsync(string entryType) =>
        WithConnectionAsync<IReadOnlyList<string>>(_dbContext, async connection =>
        {
            var used = await QueryAsync(connection,
                "SELECT TOP (30) category FROM dbo.expenses WHERE entry_type = @type AND category IS NOT NULL AND cancelled_at IS NULL GROUP BY category ORDER BY COUNT(*) DESC;",
                r => r.GetString(0),
                NVarChar("@type", entryType, 12));

            var starters = entryType switch
            {
                ExpenseEntryTypes.Expense => ExpenseStarters,
                ExpenseEntryTypes.Income => IncomeStarters,
                _ => []
            };

            return used.Concat(starters).Distinct().ToList();
        });

    public async Task<OperationResult> AddAsync(ExpenseRequest request)
    {
        var problem = Normalise(request);
        if (problem is not null)
            return OperationResult.Failure(problem);

        var idParam = new SqlParameter("@p_expense_id", SqlDbType.Int) { Direction = ParameterDirection.Output };

        return await RunAsync("adding expense", () => _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC dbo.sp_add_expense
                @entry_type     = @p_entry_type,
                @amount         = @p_amount,
                @funding_source = @p_funding_source,
                @created_by     = @p_created_by,
                @payment_method = @p_payment_method,
                @category       = @p_category,
                @description    = @p_description,
                @supplier_id    = @p_supplier_id,
                @paid_at        = @p_paid_at,
                @expense_id     = @p_expense_id OUTPUT
            """,
            NVarChar("@p_entry_type", request.EntryType, 12),
            Money("@p_amount", request.Amount),
            NVarChar("@p_funding_source", request.FundingSource, 10),
            new SqlParameter("@p_created_by", _currentUser.RequireUserId()),
            NVarChar("@p_payment_method", request.PaymentMethod, 10),
            NVarChar("@p_category", Clean(request.Category), 100),
            NVarChar("@p_description", Clean(request.Description), 500),
            IntParam("@p_supplier_id", request.SupplierId),
            new SqlParameter("@p_paid_at", SqlDbType.DateTime2) { Value = (object?)request.PaidAt ?? DBNull.Value },
            idParam));
    }

    public async Task<OperationResult> UpdateAsync(UpdateExpenseRequest request)
    {
        var problem = Normalise(request);
        if (problem is not null)
            return OperationResult.Failure(problem);

        if (string.IsNullOrWhiteSpace(request.Reason))
            return OperationResult.Failure("اكتب سبب التعديل.");

        return await RunAsync("updating expense", () => _dbContext.Database.ExecuteSqlRawAsync(
            """
            EXEC dbo.sp_update_expense
                @expense_id     = @p_expense_id,
                @entry_type     = @p_entry_type,
                @amount         = @p_amount,
                @funding_source = @p_funding_source,
                @payment_method = @p_payment_method,
                @changed_by     = @p_changed_by,
                @reason         = @p_reason,
                @category       = @p_category,
                @description    = @p_description,
                @supplier_id    = @p_supplier_id,
                @paid_at        = @p_paid_at
            """,
            new SqlParameter("@p_expense_id", request.ExpenseId),
            NVarChar("@p_entry_type", request.EntryType, 12),
            Money("@p_amount", request.Amount),
            NVarChar("@p_funding_source", request.FundingSource, 10),
            NVarChar("@p_payment_method", request.PaymentMethod, 10),
            new SqlParameter("@p_changed_by", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(request.Reason), 500),
            NVarChar("@p_category", Clean(request.Category), 100),
            NVarChar("@p_description", Clean(request.Description), 500),
            IntParam("@p_supplier_id", request.SupplierId),
            new SqlParameter("@p_paid_at", SqlDbType.DateTime2) { Value = (object?)request.PaidAt ?? DBNull.Value }));
    }

    public async Task<OperationResult> CancelAsync(int expenseId, string? reason, bool todayOnly)
    {
        if (todayOnly)
        {
            var entry = await GetEntryAsync(expenseId);
            if (entry is null)
                return OperationResult.Failure("الحركة غير موجودة.");

            var drawer = await GetDrawerAsync();
            if (entry.BusinessDate != drawer.BusinessDate)
                return OperationResult.Failure("يمكن إلغاء حركات اليوم فقط من هنا. لحركة سابقة، اطلب من المدير تصحيحها.");
        }

        return await RunAsync("cancelling expense", () => _dbContext.Database.ExecuteSqlRawAsync(
            "EXEC dbo.sp_cancel_expense @expense_id = @p_expense_id, @cancelled_by = @p_cancelled_by, @reason = @p_reason",
            new SqlParameter("@p_expense_id", expenseId),
            new SqlParameter("@p_cancelled_by", _currentUser.RequireUserId()),
            NVarChar("@p_reason", Clean(reason), 500)));
    }

    // Brings a posted form into the shape the procedures accept, so a combination the
    // screen hides (income "from outside", a card payment out of the drawer) cannot arrive
    // just because a stale field was still in the post. Returns a message only for what
    // the form itself must fix; every business rule is left to the procedure.
    private static string? Normalise(ExpenseRequest request)
    {
        if (!ExpenseEntryTypes.IsValid(request.EntryType))
            return "نوع الحركة غير صحيح.";

        if (request.Amount is null or <= 0)
            return "أدخل المبلغ.";

        if (request.EntryType == ExpenseEntryTypes.Income)
            request.FundingSource = FundingSources.Drawer;
        else if (request.FundingSource == FundingSources.Drawer)
            request.PaymentMethod = PaymentMethods.Cash;

        if (request.EntryType != ExpenseEntryTypes.Expense)
            request.SupplierId = null;

        return null;
    }

    private async Task<OperationResult> RunAsync(string what, Func<Task> write)
    {
        try
        {
            await write();
            return OperationResult.Success();
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "SQL error {What}. SqlErrors={SqlErrors}", what, StoredProcedureErrors.Describe(ex));
            return OperationResult.Failure(StoredProcedureErrors.ToUserMessage(ex, "بيانات مكررة"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error {What}.", what);
            return OperationResult.Failure(StoredProcedureErrors.GenericMessage);
        }
    }

    private static ExpenseEntry MapEntry(SqlDataReader r) => new()
    {
        ExpenseId = r.GetInt32(r.GetOrdinal("expense_id")),
        PaidAt = r.GetDateTime(r.GetOrdinal("paid_at")),
        BusinessDate = DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("business_date"))),
        EntryType = r.GetString(r.GetOrdinal("entry_type")),
        Amount = r.Decimal("amount"),
        FundingSource = r.GetString(r.GetOrdinal("funding_source")),
        PaymentMethod = r.GetString(r.GetOrdinal("payment_method")),
        Category = r.NullableString("category"),
        SupplierId = r.NullableInt("supplier_id"),
        SupplierName = r.NullableString("supplier_name"),
        Description = r.NullableString("description"),
        CreatedBy = r.NullableString("created_by_name"),
        CancelledAt = r.NullableDateTime("cancelled_at"),
        CancelReason = r.NullableString("cancel_reason")
    };
}
