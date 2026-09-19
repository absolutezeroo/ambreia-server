using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Camera;

/// <summary>
/// The photo was bought.
/// </summary>
/// <remarks>
/// Genuinely body-less, unlike the other three camera replies that looked that way because nobody
/// had read the parser. The client's is empty and <c>CameraWidgetHandler.onPurchaseOK</c> ignores
/// its argument entirely — it plays the success animation and nothing more. The item itself reaches
/// the player through the inventory, not through this.
/// </remarks>
[GenerateSerializer, Immutable]
public sealed record CameraPurchaseOKMessageComposer : IComposer;
