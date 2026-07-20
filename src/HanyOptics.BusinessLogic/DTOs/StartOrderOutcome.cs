namespace HanyOptics.BusinessLogic.Models;

public class StartOrderOutcome
{
    public bool Succeeded { get; init; }
    public int OrderId { get; init; }
    public string? ErrorMessage { get; init; }

    public static StartOrderOutcome Success(int orderId) => new() { Succeeded = true, OrderId = orderId };
    public static StartOrderOutcome Failure(string message) => new() { Succeeded = false, ErrorMessage = message };
}
