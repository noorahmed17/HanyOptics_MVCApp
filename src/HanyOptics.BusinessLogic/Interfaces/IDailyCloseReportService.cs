using HanyOptics.BusinessLogic.Models;

namespace HanyOptics.BusinessLogic.Interfaces;

// قفلة اليوم: إغلاق اليومية, التقرير الشهري, مصروفات الشهر.
//
// Every figure comes from the database's own report objects (v10): vw_daily_summary,
// vw_monthly_summary, fn_expenses_by_category and fn_supplier_payments - plus the
// vw_daily_close_* views for the day's invoice, delivery and payment lists. The rules
// (what counts as income, that an owner's draw is not an expense, the 06:00 day cutoff)
// live there and are not repeated here.
public interface IDailyCloseReportService
{
    // A null day means the business day the shop is in right now.
    Task<DayReport> GetDayAsync(DateOnly? day);

    // A null month means the business month the shop is in right now.
    Task<MonthReport> GetMonthAsync(ReportMonth? month);

    Task<MonthExpensesReport> GetMonthExpensesAsync(ReportMonth? month);
}
