namespace HanyOptics.BusinessLogic.Models;

// One row of vw_supplier_balances. BalanceDue is the view's own figure
// (invoices - returns - payments), never recomputed here.
public class SupplierBalance
{
    public int SupplierId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public decimal TotalInvoiced { get; init; }
    public decimal TotalReturned { get; init; }
    public decimal TotalPaid { get; init; }
    public decimal BalanceDue { get; init; }
}

public class SupplierInvoiceOption
{
    public int PurchaseId { get; init; }
    public DateOnly PurchaseDate { get; init; }
    public decimal Amount { get; init; }
    public string? Notes { get; init; }
}

public static class SupplierLedgerKinds
{
    public const string Invoice = "invoice";
    public const string Return = "return";
    public const string Payment = "payment";
}

// One line of a supplier's account: a purchase invoice, goods sent back, or money paid
// (an expense row carrying the supplier's id).
public class SupplierLedgerEntry
{
    public DateTime Date { get; init; }
    public string Kind { get; init; } = SupplierLedgerKinds.Invoice;
    public decimal Amount { get; init; }
    public string? Note { get; init; }
}

public class SupplierDetail
{
    public SupplierBalance Balance { get; init; } = new();
    public string? Notes { get; init; }
    public IReadOnlyList<SupplierInvoiceOption> Invoices { get; init; } = [];
    public IReadOnlyList<SupplierLedgerEntry> History { get; init; } = [];
}

public class CreateSupplierRequest
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
}

public class SupplierInvoiceRequest
{
    public int SupplierId { get; set; }
    public decimal? Amount { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public string? Notes { get; set; }
}

public class SupplierReturnRequest
{
    public int SupplierId { get; set; }
    public decimal? Amount { get; set; }
    public int? PurchaseId { get; set; }
    public DateOnly? ReturnDate { get; set; }
    public string? Notes { get; set; }
}
