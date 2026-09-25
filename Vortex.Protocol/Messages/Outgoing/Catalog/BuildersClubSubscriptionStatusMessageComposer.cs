using System;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;

namespace Vortex.Protocol.Messages.Outgoing.Catalog;

[GenerateSerializer, Immutable]
public sealed record BuildersClubSubscriptionStatusMessageComposer : IComposer
{
    [Id(0)]
    public int SecondsLeft { get; init; }

    [Id(1)]
    public int FurniLimit { get; init; }

    [Id(2)]
    public int MaxFurniLimit { get; init; }

    [Id(3)]
    public int? SecondsLeftWithGrace { get; init; }

    /// <summary>The status for a subscription as it stands now. Sent at login and after a purchase.</summary>
    public static BuildersClubSubscriptionStatusMessageComposer From(
        BuildersClubSubscriptionSnapshot subscription
    )
    {
        int secondsLeft = subscription.IsActive
            ? (int)Math.Max(0, (subscription.ExpiresAt!.Value - DateTime.UtcNow).TotalSeconds)
            : 0;

        return new BuildersClubSubscriptionStatusMessageComposer
        {
            SecondsLeft = secondsLeft,
            FurniLimit = subscription.FurniLimit,
            MaxFurniLimit = subscription.FurniLimit,
            SecondsLeftWithGrace = secondsLeft,
        };
    }
}
