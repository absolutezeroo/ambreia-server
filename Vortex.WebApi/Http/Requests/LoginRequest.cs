namespace Vortex.WebApi.Http.Requests;

/// <summary>
/// A request body of the web API. Each one belongs to exactly one route and exposes an
/// <c>IsValid</c> predicate so the endpoint can reject a malformed payload with a clean 400 before
/// any service work happens.
/// </summary>
/// <remarks>
/// <c>IsValid</c> is shape, never policy. It asks whether the endpoint can proceed at all — a field
/// present, a name that could be a player's name — and never whether the caller is allowed: that
/// answer needs the database and belongs to the service, which refuses again at the sink.
/// </remarks>
public sealed record LoginRequest(string? Email, string? Password, string? Code = null)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}
