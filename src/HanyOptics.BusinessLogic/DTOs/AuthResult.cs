namespace HanyOptics.BusinessLogic.Models;

public class AuthResult
{
    public bool Succeeded { get; init; }
    public string? Token { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static AuthResult Success(string token, DateTime expiresAtUtc, string displayName) => new()
    {
        Succeeded = true,
        Token = token,
        ExpiresAtUtc = expiresAtUtc,
        DisplayName = displayName
    };

    public static AuthResult Failure(params string[] errors) => new()
    {
        Succeeded = false,
        Errors = errors
    };
}
