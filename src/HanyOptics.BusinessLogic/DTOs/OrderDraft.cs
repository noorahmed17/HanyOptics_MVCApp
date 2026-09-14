using HanyOptics.Domain.Enums;

namespace HanyOptics.BusinessLogic.Models;

// An order being built in the wizard, held outside the database until the user finishes
// the last step. Nothing here exists in `orders`/`order_items` yet, so abandoning the
// wizard - pressing إلغاء, navigating away, or just closing the browser - leaves no trace.
//
// Serialized into the session, so it has to stay a plain data object: public settable
// properties only, no behaviour that depends on services.
public class OrderDraft
{
    public string? Phone { get; set; }

    // The customer's own name - identifies them across every order they've ever placed
    // (matched by phone in ResolveOrCreateCustomerAsync) and is what gets written back to
    // customers.name. Deliberately never touched by CustomerNameOnInvoice below: a phone
    // number belongs to one customer, but that customer's orders can each be for a
    // different named person (a family member, someone they're buying for), and typing
    // that person's name on this particular invoice must not rename the customer.
    public string? CustomerName { get; set; }

    // The name printed on this specific invoice (orders.customer_name). Defaults to
    // CustomerName when the wizard reaches step 2, but edited independently from then on.
    public string? CustomerNameOnInvoice { get; set; }

    public bool IsWalkIn { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Normal;
    public int? DoctorId { get; set; }

    public List<OrderDraftItem> Items { get; set; } = new();

    public decimal TotalAmount => Items.Sum(i => i.ItemTotal);
    public decimal FrameTotal => Items.Sum(i => i.FrameAgreedPrice);
    public decimal LensTotal => Items.Sum(i => i.LensSellPrice);
}

public class OrderDraftItem
{
    public OrderItemType ItemType { get; set; }

    // Resolved from the barcode when the item is added, but the frame is NOT reserved
    // until the order is committed - see CommitDraftAsync.
    public int? FrameId { get; set; }
    public string? FrameBarcode { get; set; }
    public string? FrameLabel { get; set; }

    public decimal FrameAgreedPrice { get; set; }
    public string? ExternalFrameNotes { get; set; }

    public string? LensDescription { get; set; }
    public decimal LensSellPrice { get; set; }
    public decimal LensCostPrice { get; set; }

    public string? Notes { get; set; }

    public decimal? RightSphere { get; set; }
    public decimal? RightCylinder { get; set; }
    public int? RightAxis { get; set; }
    public decimal? LeftSphere { get; set; }
    public decimal? LeftCylinder { get; set; }
    public int? LeftAxis { get; set; }
    public decimal? Pd { get; set; }
    public decimal? AddPower { get; set; }

    public decimal ItemTotal => FrameAgreedPrice + LensSellPrice;
}
