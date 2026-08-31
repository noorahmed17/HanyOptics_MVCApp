namespace HanyOptics.BusinessLogic.Models;

// One shop day, as the owner counts it.
//
// "Day" here is the business day, not the calendar day: the shop closes after midnight, so
// dbo.fn_business_date runs it from 06:00 to 05:59 the next morning. A sale at 1:20am
// belongs to the night it was part of, not to the morning that happened to follow it.
// Every view behind these types groups by that function, so the whole screen agrees.
public class DailyCloseSummary
{
    public DateOnly BusinessDate { get; set; }
    public DateTime DayStartAt { get; set; }
    public DateTime DayEndAt { get; set; }

    // ── What ended up in the till ──
    public decimal CashNet { get; set; }
    public decimal VisaNet { get; set; }
    public decimal NetTotal { get; set; }
    public decimal FromTodayInvoices { get; set; }
    public decimal FromOldInvoices { get; set; }
    public decimal RefundsTotal { get; set; }
    public int RefundsCount { get; set; }
    public int PaymentsCount { get; set; }
    public DateTime? FirstPaymentAt { get; set; }
    public DateTime? LastPaymentAt { get; set; }

    // ── Orders opened during the day ──
    public int OrdersCount { get; set; }
    public decimal OrdersTotal { get; set; }
    public decimal OrdersPaid { get; set; }
    public decimal OrdersRemaining { get; set; }
    public int OrdersWithBalance { get; set; }
    public int CancelledCount { get; set; }

    // ── Handed over during the day, whatever day the invoice is from ──
    public int DeliveriesCount { get; set; }
    public decimal DeliveriesTotal { get; set; }

    public DateTime? LastActivityAt { get; set; }

    // Share of the till split, for the bar on screen. Guarded because a day with no money
    // taken is perfectly normal - the shop can open and sell nothing - and a bar drawn from
    // a division by zero would be a crash on an ordinary quiet day.
    public decimal CashShare => NetTotal <= 0 ? 0 : Math.Round(CashNet / NetTotal * 100, 1);
    public decimal VisaShare => NetTotal <= 0 ? 0 : Math.Round(VisaNet / NetTotal * 100, 1);

    // The gross taken before refunds came back out of the drawer.
    public decimal CollectedBeforeRefunds => FromTodayInvoices + FromOldInvoices;
}

public class DailyCloseOrder
{
    public int OrderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DeliveryType { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? CreatedBy { get; set; }

    // Rung up after midnight and still counted on the same working night - the 🌙 marker.
    public bool IsAfterMidnight { get; set; }

    public int ItemsCount { get; set; }
    public string? ItemTypes { get; set; }
}

public class DailyClosePayment
{
    public int PaymentId { get; set; }
    public DateTime PaidAt { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    // Refunds carry a minus here, so the column sums to exactly what is in the drawer.
    public decimal SignedAmount { get; set; }

    // A payment against an invoice from an earlier day - someone came in to settle up.
    public bool IsOldInvoice { get; set; }
    public bool IsAfterMidnight { get; set; }
    public string? ReceivedBy { get; set; }
    public string? Notes { get; set; }
}

public class DailyCloseDelivery
{
    public int OrderId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public DateTime DeliveredAt { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal RemainingAmount { get; set; }

    // Sold and collected the same night, rather than an older order being picked up.
    public bool IsSameDay { get; set; }
    public bool IsAfterMidnight { get; set; }
    public decimal CollectedToday { get; set; }
}

public class DailyCloseReport
{
    public DateOnly BusinessDate { get; set; }

    // Null when nothing at all happened on that day - no orders, no payments, no
    // deliveries. The screen says so rather than printing a wall of zeros that looks
    // like a broken query.
    public DailyCloseSummary? Summary { get; set; }

    public IReadOnlyList<DailyCloseOrder> Orders { get; set; } = [];
    public IReadOnlyList<DailyClosePayment> Payments { get; set; } = [];
    public IReadOnlyList<DailyCloseDelivery> Deliveries { get; set; } = [];

    // The business day the shop is currently in, so the screen can say "today" rather
    // than making someone work out whether 2am counts as tomorrow yet.
    public DateOnly CurrentBusinessDate { get; set; }
    public bool IsCurrentDay => BusinessDate == CurrentBusinessDate;
}
