namespace HanyOptics.BusinessLogic.Models;

// Result of validating and describing one operation for the popup's pending-changes list.
// Nothing is written to the database at this point.
public class StagedEditOutcome
{
    public bool Succeeded { get; init; }
    public PendingOrderEdit? Edit { get; init; }
    public string? ErrorMessage { get; init; }

    public static StagedEditOutcome Ok(PendingOrderEdit edit) => new() { Succeeded = true, Edit = edit };
    public static StagedEditOutcome Failure(string message) => new() { ErrorMessage = message };
}
