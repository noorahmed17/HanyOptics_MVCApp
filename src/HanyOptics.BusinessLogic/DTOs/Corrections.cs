namespace HanyOptics.BusinessLogic.Models;

public class CorrectionPayment
{
    public int PaymentId { get; init; }
    public DateTime PaidAt { get; init; }
    public string PaymentType { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? ReceivedBy { get; init; }

    public bool IsRefund => PaymentType == "refund";
}

// One row of dbo.corrections_log, with its before/after JSON already turned into a line a
// person can read.
public class CorrectionLogEntry
{
    public DateTime ChangedAt { get; init; }
    public string Entity { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ActionLabel { get; init; } = string.Empty;
    public string? Before { get; init; }
    public string? After { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? ChangedBy { get; init; }
}

// An order as the corrections screen needs it: the money, the payments that can be
// corrected, and where sp_admin_revert_order_status would take it.
public class CorrectionOrder
{
    public int OrderId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime OrderDate { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal RemainingAmount { get; init; }

    // null when there is no step back: a sold order has none, and a cancelled one is
    // refused by the procedure because its stock has already been returned.
    public string? RevertTarget { get; init; }

    public IReadOnlyList<CorrectionPayment> Payments { get; init; } = [];
    public IReadOnlyList<CorrectionLogEntry> Log { get; init; } = [];
}
