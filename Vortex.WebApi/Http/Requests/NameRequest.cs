namespace Vortex.WebApi.Http.Requests;

public sealed record NameRequest(string? Name)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Name);
}
