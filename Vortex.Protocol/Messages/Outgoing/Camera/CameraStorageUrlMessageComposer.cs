using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Camera;

/// <summary>
/// Where the rendered photo can be fetched, answering <c>RenderRoom</c>.
/// </summary>
/// <remarks>
/// <para>
/// The client prepends its own <c>stories.image_url_base</c> external variable and loads the result
/// as a PNG, so what goes out here is a path relative to that base and the two have to agree. A
/// hotel that leaves that variable pointing at Sulake's bucket will fetch every photo from a host
/// that has never heard of it.
/// </para>
/// <para>
/// An <em>empty</em> url is the failure case, and it is the only one the wire can express: the
/// client alerts <c>camera.render.count.info</c> and gives up. Every refusal — too big, too complex,
/// too fast, too late — arrives at the player as that one message, which is why the server's own
/// reason is worth a log line.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraStorageUrlMessageComposer : IComposer
{
    [Id(0)]
    public required string Url { get; init; }
}
