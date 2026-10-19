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
    public IReadOnlyList<CorrectionItem> ReturnableItems { get; init; } = [];
    public IReadOnlyList<CorrectionLogEntry> Log { get; init; } = [];
}

// A frame-only item on a delivered order - what the return and exchange buttons act on.
public class CorrectionItem
{
    public int ItemId { get; init; }
    public string Barcode { get; init; } = string.Empty;
    public string? Brand { get; init; }
    public string? ModelName { get; init; }
    public decimal Price { get; init; }
}

// مرتجع بعد التسليم. RefundAmount is what was actually handed back to the customer - typed
// by staff, never assumed - and nullable so an empty box and an explicit zero stay
// distinguishable, the same reason ExpenseRequest.Amount is.
public class ReturnFrameRequest
{
    public int ItemId { get; set; }
    public decimal? RefundAmount { get; set; }
    public string RefundMethod { get; set; } = PaymentMethods.Cash;
    public string? Reason { get; set; }
}

// استبدال بعد التسليم. WithLenses is an explicit choice rather than "a lens price above
// zero", so a frame-only exchange never has to fill in lens fields. Amount is what was
// actually paid now (when the customer owes the difference) or handed back now (when the
// customer is owed it); anything left stays on the invoice like any other balance.
public class ExchangeFrameRequest
{
    public int ItemId { get; set; }
    public string? NewBarcode { get; set; }
    public decimal? NewFramePrice { get; set; }
    public bool WithLenses { get; set; }
    public decimal? LensPrice { get; set; }
    public string? LensDescription { get; set; }
    public decimal? Amount { get; set; }
    public string PaymentMethod { get; set; } = PaymentMethods.Cash;
    public string? Reason { get; set; }
}
