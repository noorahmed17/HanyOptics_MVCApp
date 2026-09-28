namespace HanyOptics.BusinessLogic.Models;

// One row of vw_daily_summary (or, for a month, the same figures summed - see
// MonthSummary). Every total is the view's own; the only things worked out here are the
// splits the view does not expose but the screen needs to make its sums add up.
public class DaySummary
{
    public DateOnly BusinessDate { get; init; }
    public DateTime DayStartAt { get; init; }
    public DateTime DayEndAt { get; init; }

    public decimal TodayInvoicesCash { get; init; }
    public decimal TodayInvoicesVisa { get; init; }
    public decimal OldInvoicesCash { get; init; }
    public decimal OldInvoicesVisa { get; init; }
    public decimal RefundsCash { get; init; }
    public decimal RefundsVisa { get; init; }
    public decimal OtherIncomeCash { get; init; }
    public decimal OtherIncomeCard { get; init; }

    public decimal IncomeCash { get; init; }
    public decimal IncomeVisa { get; init; }
    public decimal IncomeTotal { get; init; }

    public decimal ExpenseFromDrawer { get; init; }
    public decimal ExpenseFromOutside { get; init; }
    public decimal ExpenseTotal { get; init; }
    public decimal SupplierPaid { get; init; }
    public decimal OwnerDraw { get; init; }

    public decimal DrawerCash { get; init; }
    public decimal Net { get; init; }

    public decimal SalesCash => TodayInvoicesCash + OldInvoicesCash;
    public decimal SalesVisa => TodayInvoicesVisa + OldInvoicesVisa;
    public decimal RefundsTotal => RefundsCash + RefundsVisa;
    public decimal OtherIncomeTotal => OtherIncomeCash + OtherIncomeCard;

    // The draw that actually left the drawer. The view folds it into drawer_cash without
    // exposing it, so it is recovered from drawer_cash itself - which is what makes the
    // "مطابقة النقدية" ledger always land exactly on the drawer figure. Since v8 every
    // draw is from the drawer, so this equals OwnerDraw for anything recorded since then.
    public decimal OwnerDrawFromDrawer =>
        SalesCash - RefundsCash + OtherIncomeCash - ExpenseFromDrawer - DrawerCash;

    public static DaySummary Empty(DateOnly day) => new()
    {
        BusinessDate = day,
        DayStartAt = day.ToDateTime(new TimeOnly(6, 0)),
        DayEndAt = day.AddDays(1).ToDateTime(new TimeOnly(6, 0))
    };
}

// vw_monthly_summary - the day figures summed over a business month.
public class MonthSummary : DaySummary
{
    public int Year { get; init; }
    public int Month { get; init; }
    public DateOnly? FirstDay { get; init; }
    public DateOnly? LastDay { get; init; }
    public int ActiveDays { get; init; }
}

// One expense category over a period (fn_expenses_by_category). "من الإيرادات" is what was
// paid in cash out of the drawer; everything else is "من خارج الإيرادات".
public class CategorySplit
{
    public string Category { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal FromDrawer { get; init; }
    public decimal FromOutside { get; init; }
    public decimal Total { get; init; }
    public decimal SupplierPaid { get; init; }
    public DateTime? LastPaidAt { get; init; }

    // The same category over the comparison period; null when it did not appear there.
    public decimal? PreviousTotal { get; set; }
}

public class SupplierPaymentRow
{
    public int SupplierId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int PaymentsCount { get; init; }
    public decimal PaidTotal { get; init; }
    public DateTime? LastPaidAt { get; init; }
    public decimal BalanceDue { get; init; }
}

public record ReportMonth(int Year, int Month);

// إغلاق اليومية
public class DayReport
{
    public DateOnly BusinessDate { get; init; }
    public DateOnly CurrentBusinessDate { get; init; }
    public bool IsToday => BusinessDate == CurrentBusinessDate;

    public DaySummary Summary { get; init; } = new();

    public IReadOnlyList<DailyCloseOrder> Orders { get; init; } = [];
    public IReadOnlyList<DailyCloseDelivery> Deliveries { get; init; } = [];
    public IReadOnlyList<DailyClosePayment> Payments { get; init; } = [];

    // Expenses, draws and other income of the day (cancelled ones left out).
    public IReadOnlyList<ExpenseEntry> Entries { get; init; } = [];
    public IReadOnlyList<CategorySplit> Categories { get; init; } = [];

    // The days that can be picked: every day with activity, newest first, plus today.
    public IReadOnlyList<DateOnly> AvailableDays { get; init; } = [];
}

// التقرير الشهري
public class MonthReport
{
    public ReportMonth Month { get; init; } = new(0, 0);
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public bool IsCurrentMonth { get; init; }

    public MonthSummary Summary { get; init; } = new();
    public IReadOnlyList<DaySummary> Days { get; init; } = [];
    public IReadOnlyList<CategorySplit> Categories { get; init; } = [];
    public IReadOnlyList<ReportMonth> AvailableMonths { get; init; } = [];
}

// مصروفات الشهر
public class MonthExpensesReport
{
    public ReportMonth Month { get; init; } = new(0, 0);
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public bool IsCurrentMonth { get; init; }

    // The same number of days at the start of the previous month - so a month still in
    // progress is compared like for like, not against a whole month.
    public DateOnly CompareFrom { get; init; }
    public DateOnly CompareTo { get; init; }

    public IReadOnlyList<CategorySplit> Categories { get; init; } = [];
    public IReadOnlyList<SupplierPaymentRow> Suppliers { get; init; } = [];
    public IReadOnlyList<ExpenseEntry> Entries { get; init; } = [];
    public IReadOnlyList<ReportMonth> AvailableMonths { get; init; } = [];

    public int DaysCounted => To.DayNumber - From.DayNumber + 1;
    public int Count => Categories.Sum(c => c.Count);
    public decimal Total => Categories.Sum(c => c.Total);
    public decimal FromDrawer => Categories.Sum(c => c.FromDrawer);
    public decimal FromOutside => Categories.Sum(c => c.FromOutside);
    public decimal SupplierPaid => Categories.Sum(c => c.SupplierPaid);
    public decimal DailyAverage => DaysCounted > 0 ? Total / DaysCounted : 0;
}
