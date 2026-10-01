using System.Data;
using HanyOptics.BusinessLogic.Interfaces;
using HanyOptics.BusinessLogic.Models;
using HanyOptics.DataAccess.Persistence;
using Microsoft.Data.SqlClient;
using static HanyOptics.BusinessLogic.Services.SqlReader;

namespace HanyOptics.BusinessLogic.Services;

public class DailyCloseReportService : IDailyCloseReportService
{
    private readonly HanyOpticsDbContext _dbContext;
    private readonly IDailyCloseService _dailyClose;
    private readonly IExpenseService _expenses;

    public DailyCloseReportService(HanyOpticsDbContext dbContext, IDailyCloseService dailyClose, IExpenseService expenses)
    {
        _dbContext = dbContext;
        _dailyClose = dailyClose;
        _expenses = expenses;
    }

    public async Task<DayReport> GetDayAsync(DateOnly? day)
    {
        var current = await _dailyClose.GetCurrentBusinessDateAsync();
        var date = day ?? current;

        // The invoice / delivery / payment lists are the daily close's own (vw_daily_close_*),
        // so this screen and anything else built on them always agree on what happened.
        var close = await _dailyClose.GetAsync(date);
        var entries = await _expenses.GetEntriesBetweenAsync(date, date);

        var (summary, categories) = await WithConnectionAsync(_dbContext, async connection =>
        {
            var summary = (await QueryAsync(connection,
                "SELECT * FROM dbo.vw_daily_summary WHERE business_date = @day;",
                r => MapDay(r), DateParam("@day", date))).FirstOrDefault();

            var categories = await ReadCategoriesAsync(connection, date, date);

            return (summary, categories);
        });

        return new DayReport
        {
            BusinessDate = date,
            CurrentBusinessDate = current,
            Summary = summary ?? DaySummary.Empty(date),
            Orders = close.Orders,
            Deliveries = close.Deliveries,
            Payments = close.Payments,
            Entries = entries,
            Categories = categories
        };
    }

    public async Task<MonthReport> GetMonthAsync(ReportMonth? month)
    {
        var current = await _dailyClose.GetCurrentBusinessDateAsync();
        var (m, from, to, isCurrent) = Window(month, current);

        return await WithConnectionAsync(_dbContext, async connection =>
        {
            var summary = (await QueryAsync(connection,
                "SELECT * FROM dbo.vw_monthly_summary WHERE [year] = @y AND [month] = @m;",
                MapMonth, new SqlParameter("@y", m.Year), new SqlParameter("@m", m.Month))).FirstOrDefault();

            var days = await QueryAsync(connection,
                "SELECT * FROM dbo.vw_daily_summary WHERE business_date BETWEEN @from AND @to ORDER BY business_date DESC;",
                r => MapDay(r), DateParam("@from", from), DateParam("@to", to));

            return new MonthReport
            {
                Month = m,
                From = from,
                To = to,
                IsCurrentMonth = isCurrent,
                Summary = summary ?? new MonthSummary { Year = m.Year, Month = m.Month },
                Days = days,
                Categories = await ReadCategoriesAsync(connection, from, to),
                AvailableMonths = await ReadMonthsAsync(connection, current)
            };
        });
    }

    public async Task<MonthExpensesReport> GetMonthExpensesAsync(ReportMonth? month)
    {
        var current = await _dailyClose.GetCurrentBusinessDateAsync();
        var (m, from, to, isCurrent) = Window(month, current);

        // The same stretch of days at the start of the month before, capped at its end.
        var prevFirst = from.AddMonths(-1);
        var prevLast = new DateOnly(prevFirst.Year, prevFirst.Month, DateTime.DaysInMonth(prevFirst.Year, prevFirst.Month));
        var compareTo = prevFirst.AddDays(to.DayNumber - from.DayNumber);
        if (compareTo > prevLast) compareTo = prevLast;

        var entries = (await _expenses.GetEntriesBetweenAsync(from, to))
            .Where(e => e.EntryType == ExpenseEntryTypes.Expense)
            .OrderByDescending(e => e.PaidAt)
            .ToList();

        return await WithConnectionAsync(_dbContext, async connection =>
        {
            var categories = await ReadCategoriesAsync(connection, from, to);
            var previous = (await ReadCategoriesAsync(connection, prevFirst, compareTo))
                .ToDictionary(c => c.Category, c => c.Total);
            foreach (var c in categories)
                c.PreviousTotal = previous.TryGetValue(c.Category, out var p) ? p : null;

            var suppliers = await QueryAsync(connection,
                "SELECT * FROM dbo.fn_supplier_payments(@from, @to) ORDER BY paid_total DESC;",
                r => new SupplierPaymentRow
                {
                    SupplierId = r.GetInt32(r.GetOrdinal("supplier_id")),
                    Name = r.GetString(r.GetOrdinal("name")),
                    PaymentsCount = r.GetInt32(r.GetOrdinal("payments_count")),
                    PaidTotal = r.Decimal("paid_total"),
                    LastPaidAt = r.NullableDateTime("last_paid_at"),
                    BalanceDue = r.Decimal("balance_due")
                },
                DateParam("@from", from), DateParam("@to", to));

            return new MonthExpensesReport
            {
                Month = m,
                From = from,
                To = to,
                IsCurrentMonth = isCurrent,
                CompareFrom = prevFirst,
                CompareTo = compareTo,
                Categories = categories,
                Suppliers = suppliers,
                Entries = entries,
                AvailableMonths = await ReadMonthsAsync(connection, current)
            };
        });
    }

    // A month runs from its 1st to its last day - or to today, while it is still going.
    private static (ReportMonth Month, DateOnly From, DateOnly To, bool IsCurrent) Window(ReportMonth? month, DateOnly current)
    {
        var m = month ?? new ReportMonth(current.Year, current.Month);
        var from = new DateOnly(m.Year, m.Month, 1);
        var last = new DateOnly(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
        var isCurrent = m.Year == current.Year && m.Month == current.Month;
        return (m, from, isCurrent ? current : last, isCurrent);
    }

    private static async Task<List<CategorySplit>> ReadCategoriesAsync(SqlConnection connection, DateOnly from, DateOnly to) =>
        await QueryAsync(connection,
            "SELECT * FROM dbo.fn_expenses_by_category(@from, @to) ORDER BY total_amount DESC;",
            r => new CategorySplit
            {
                Category = r.GetString(r.GetOrdinal("category")),
                Count = r.GetInt32(r.GetOrdinal("entries_count")),
                FromDrawer = r.Decimal("from_drawer"),
                FromOutside = r.Decimal("from_outside"),
                Total = r.Decimal("total_amount"),
                SupplierPaid = r.Decimal("supplier_paid"),
                LastPaidAt = r.NullableDateTime("last_paid_at")
            },
            DateParam("@from", from), DateParam("@to", to));

    // Every month with activity, newest first, plus the current one.
    private static async Task<IReadOnlyList<ReportMonth>> ReadMonthsAsync(SqlConnection connection, DateOnly current)
    {
        var months = await QueryAsync(connection,
            "SELECT [year], [month] FROM dbo.vw_monthly_summary;",
            r => new ReportMonth(r.GetInt32(0), r.GetInt32(1)));
        return months.Append(new ReportMonth(current.Year, current.Month))
            .Distinct()
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .ToList();
    }

    private static SqlParameter DateParam(string name, DateOnly value) =>
        new(name, SqlDbType.Date) { Value = value.ToDateTime(TimeOnly.MinValue) };

    private static DaySummary MapDay(SqlDataReader r) => new()
    {
        BusinessDate = DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("business_date"))),
        DayStartAt = r.GetDateTime(r.GetOrdinal("day_start_at")),
        DayEndAt = r.GetDateTime(r.GetOrdinal("day_end_at")),
        TodayInvoicesCash = r.Decimal("today_invoices_cash"),
        TodayInvoicesVisa = r.Decimal("today_invoices_visa"),
        OldInvoicesCash = r.Decimal("old_invoices_cash"),
        OldInvoicesVisa = r.Decimal("old_invoices_visa"),
        RefundsCash = r.Decimal("refunds_cash"),
        RefundsVisa = r.Decimal("refunds_visa"),
        OtherIncomeCash = r.Decimal("other_income_cash"),
        OtherIncomeCard = r.Decimal("other_income_card"),
        IncomeCash = r.Decimal("income_cash"),
        IncomeVisa = r.Decimal("income_visa"),
        IncomeTotal = r.Decimal("income_total"),
        ExpenseFromDrawer = r.Decimal("expense_from_drawer"),
        ExpenseFromOutside = r.Decimal("expense_from_outside"),
        ExpenseTotal = r.Decimal("expense_total"),
        SupplierPaid = r.Decimal("supplier_paid"),
        OwnerDraw = r.Decimal("owner_draw"),
        DrawerCash = r.Decimal("drawer_cash"),
        Net = r.Decimal("net_day")
    };

    private static MonthSummary MapMonth(SqlDataReader r) => new()
    {
        Year = r.GetInt32(r.GetOrdinal("year")),
        Month = r.GetInt32(r.GetOrdinal("month")),
        FirstDay = DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("first_day"))),
        LastDay = DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("last_day"))),
        ActiveDays = r.GetInt32(r.GetOrdinal("active_days")),
        TodayInvoicesCash = r.Decimal("today_invoices_cash"),
        TodayInvoicesVisa = r.Decimal("today_invoices_visa"),
        OldInvoicesCash = r.Decimal("old_invoices_cash"),
        OldInvoicesVisa = r.Decimal("old_invoices_visa"),
        RefundsCash = r.Decimal("refunds_cash"),
        RefundsVisa = r.Decimal("refunds_visa"),
        OtherIncomeCash = r.Decimal("other_income_cash"),
        OtherIncomeCard = r.Decimal("other_income_card"),
        IncomeCash = r.Decimal("income_cash"),
        IncomeVisa = r.Decimal("income_visa"),
        IncomeTotal = r.Decimal("income_total"),
        ExpenseFromDrawer = r.Decimal("expense_from_drawer"),
        ExpenseFromOutside = r.Decimal("expense_from_outside"),
        ExpenseTotal = r.Decimal("expense_total"),
        SupplierPaid = r.Decimal("supplier_paid"),
        OwnerDraw = r.Decimal("owner_draw"),
        DrawerCash = r.Decimal("drawer_cash_taken"),
        Net = r.Decimal("net_month")
    };
}
