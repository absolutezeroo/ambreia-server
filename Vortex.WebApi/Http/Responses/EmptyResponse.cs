namespace Vortex.WebApi.Http.Responses;

/// <summary>
/// <c>{}</c>, a real answer and not an oversight: logout and avatar selection both say "done,
/// nothing to tell you". A record rather than an anonymous object so a handler can declare it in
/// its return type, which is what puts it in the OpenAPI document.
/// </summary>
public sealed record EmptyResponse
{
    public static readonly EmptyResponse Instance = new();
}
