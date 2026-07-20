namespace HanyOptics.BusinessLogic.Models;

public class AddOrderItemOutcome
{
    public bool Added { get; init; }
    public bool Blank { get; init; }
    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } =
        new Dictionary<string, string>();
}
