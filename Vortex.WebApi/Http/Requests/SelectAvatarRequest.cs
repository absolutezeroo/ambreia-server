namespace Vortex.WebApi.Http.Requests;

public sealed record SelectAvatarRequest(string? UniqueId)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(UniqueId);
}
