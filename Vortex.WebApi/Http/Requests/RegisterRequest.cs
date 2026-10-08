using Vortex.WebApi.Http.Validation;

namespace Vortex.WebApi.Http.Requests;

public sealed record RegisterRequest(string? Email, string? Password, string? PasswordRepeated)
{
    public bool IsValid =>
        EmailShape.IsWellFormed(Email)
        && !string.IsNullOrWhiteSpace(Password)
        // The confirmation field is optional (some clients omit it), but when supplied it
        // must match the password.
        && (PasswordRepeated is null || Password == PasswordRepeated);
}
