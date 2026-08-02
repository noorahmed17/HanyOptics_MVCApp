namespace HanyOptics.BusinessLogic.Models;

// Result of checking one wizard item before it goes into the draft. Nothing is written to
// the database at this point - the item is only being turned into an OrderDraftItem.
public class DraftItemOutcome
{
    // The form was left completely empty: the user pressed "التالي" without starting a new
    // item, which is not an error - there's just nothing to add.
    public bool Blank { get; init; }

    public OrderDraftItem? Item { get; init; }

    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = new Dictionary<string, string>();

    public bool Valid => !Blank && Item is not null && FieldErrors.Count == 0;

    public static DraftItemOutcome BlankItem() => new() { Blank = true };
    public static DraftItemOutcome Ok(OrderDraftItem item) => new() { Item = item };
    public static DraftItemOutcome Invalid(IReadOnlyDictionary<string, string> errors) => new() { FieldErrors = errors };
    public static DraftItemOutcome Invalid(string field, string message) =>
        new() { FieldErrors = new Dictionary<string, string> { [field] = message } };
}

// Result of turning a finished draft into real rows.
public class CommitDraftOutcome
{
    public bool Succeeded { get; init; }
    public int OrderId { get; init; }
    public string? ErrorMessage { get; init; }

    public static CommitDraftOutcome Success(int orderId) => new() { Succeeded = true, OrderId = orderId };
    public static CommitDraftOutcome Failure(string message) => new() { ErrorMessage = message };
}
