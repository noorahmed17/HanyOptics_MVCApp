namespace HanyOptics.BusinessLogic.Models;

// Generic success/failure-with-Arabic-message result for wizard operations that don't
// need to return data (payment, cancel) - so DB errors never bubble up as raw exceptions.
public class OperationResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static OperationResult Success() => new() { Succeeded = true };
    public static OperationResult Failure(string message) => new() { Succeeded = false, ErrorMessage = message };
}
