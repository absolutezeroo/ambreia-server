using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Rpg.Emote;

/// <summary>An avatar does an RP action (9017), shown to the whole room; the client plays it on the
/// avatar (a dance lasts until the avatar walks). Nothing is kept: a player entering later does not
/// see an action already under way.</summary>
[GenerateSerializer, Immutable]
public sealed record RpgEmoteMessageComposer : IComposer
{
    [Id(0)]
    public required int ObjectId { get; init; }

    [Id(1)]
    public required string Key { get; init; }
}
