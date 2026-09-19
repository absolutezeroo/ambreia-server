using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// What came of a <c>RenderRoom</c>: a path to fetch the photo from, or the reason there is none.
/// </summary>
/// <remarks>
/// The url is relative — the client prefixes its own <c>stories.image_url_base</c>. Empty is the
/// refusal, because empty is the only refusal the wire can carry.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraRenderOutcome
{
    [Id(0)]
    public required string Url { get; init; }

    [Id(1)]
    public required CameraRenderRefusal Refusal { get; init; }

    public static CameraRenderOutcome Accepted(string url) =>
        new() { Url = url, Refusal = CameraRenderRefusal.None };

    public static CameraRenderOutcome Refused(CameraRenderRefusal refusal) =>
        new() { Url = string.Empty, Refusal = refusal };
}
