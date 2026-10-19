namespace HanyOptics.BusinessLogic.Models;

// The values dbo.expenses and its stored procedures speak in. Kept as the database's own
// strings rather than enums: they travel straight into sp_add_expense / sp_update_expense,
// and a C# spelling of them would be one more place for the two to drift apart.
public static class ExpenseEntryTypes
{
    public const string Expense = "expense";
    public const string OwnerDraw = "owner_draw";
    public const string Income = "income";

    public static bool IsValid(string? type) => type is Expense or OwnerDraw or Income;
}

public static class FundingSources
{
    public const string Drawer = "drawer";
    public const string Outside = "outside";
}

public static class PaymentMethods
{
    public const string Cash = "cash";
    public const string Visa = "visa";
}

// One row of dbo.expenses, with the names the screen shows instead of the ids it stores.
public class ExpenseEntry
{
    public int ExpenseId { get; init; }
    public DateTime PaidAt { get; init; }
    public DateOnly BusinessDate { get; init; }
    public string EntryType { get; init; } = ExpenseEntryTypes.Expense;
    public decimal Amount { get; init; }
    public string FundingSource { get; init; } = FundingSources.Drawer;
    public string PaymentMethod { get; init; } = PaymentMethods.Cash;
    public string? Category { get; init; }
    public int? SupplierId { get; init; }
    public string? SupplierName { get; init; }
    public string? Description { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CancelReason { get; init; }

    public bool IsCancelled => CancelledAt.HasValue;
    public bool IsIncome => EntryType == ExpenseEntryTypes.Income;

    // What the entry did to the cash in the drawer - the same rule vw_expenses_daily's
    // net_drawer_effect applies: card money never touches the drawer, income adds, and an
    // expense or draw only subtracts when it was paid from the drawer.
    public decimal DrawerEffect =>
        IsCancelled || PaymentMethod != PaymentMethods.Cash ? 0m
        : IsIncome ? Amount
        : FundingSource == FundingSources.Drawer ? -Amount
        : 0m;
}

// Both the add and the edit forms. Amount is nullable so an empty box and an explicit zero
// stay distinguishable, the same reason the order wizard's price fields are.
public class ExpenseRequest
{
    public string EntryType { get; set; } = ExpenseEntryTypes.Expense;
    public decimal? Amount { get; set; }
    public string FundingSource { get; set; } = FundingSources.Drawer;
    public string PaymentMethod { get; set; } = PaymentMethods.Cash;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public int? SupplierId { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class UpdateExpenseRequest : ExpenseRequest
{
    public int ExpenseId { get; set; }
    public string? Reason { get; set; }
}

// حركة الدرج: the drawer right now and what moved through it today.
public class DrawerSummary
{
    public DateOnly BusinessDate { get; init; }

    // dbo.fn_drawer_balance - the one place that sum is defined.
    public decimal Balance { get; init; }

    // Its two halves, shown so the figure is not a number that appears from nowhere.
    public decimal SalesCash { get; init; }
    public decimal EntriesEffect { get; init; }

    public IReadOnlyList<ExpenseEntry> TodayEntries { get; init; } = [];
}

public class ExpenseCategoryTotal
{
    public string Category { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public int Count { get; init; }
}

// vw_expenses_by_category for one month. Shop expenses only - owner draws are not a cost
// of running the shop, and the view leaves them out for that reason.
public class ExpenseMonthReport
{
    public int Year { get; init; }
    public int Month { get; init; }
    public IReadOnlyList<ExpenseCategoryTotal> Categories { get; init; } = [];
    public decimal Total => Categories.Sum(c => c.Total);
}

public record SupplierOption(int SupplierId, string Name);

// A write that produces a new row the screen then wants to select.
public class CreateOutcome
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public int? Id { get; init; }

    public static CreateOutcome Success(int id) => new() { Succeeded = true, Id = id };
    public static CreateOutcome Failure(string message) => new() { ErrorMessage = message };
}
