namespace Vortex.WebApi.Http;

/// <summary>Body of <c>POST /api/editor/zones</c>; the key's shape is checked by the service.</summary>
public sealed record CreateZoneRequest(string? Key, string? Name, string? FromTemplate)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Name);
}
