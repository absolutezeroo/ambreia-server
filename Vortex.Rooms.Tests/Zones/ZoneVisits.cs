using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>A player walking about a zone room: in through the normal entry, then on foot.</summary>
internal static class ZoneVisits
{
    public static readonly PlayerId Walker = new(7);

    public static async Task<RoomHarness> ZoneAsync(ZoneDocument doc)
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        return harness;
    }

    /// <summary>The same, with the decor loaded first on furni that do whatever they are told.</summary>
    public static async Task<RoomHarness> ZoneWithDecorAsync(ZoneDocument doc)
    {
        RoomHarness harness = await RoomHarness.CreateZoneAsync(doc);

        await harness.Grain.FurniModule.EnsureFurniLoadedAsync(default);
        harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        return harness;
    }

    public static ZoneEntry Entry(string id, string kind, int x, int y) =>
        new()
        {
            Id = id,
            Kind = kind,
            Cell = new ZoneCell(x, y),
        };

    /// <summary>A band of columns <paramref name="x0"/>..<paramref name="x1"/>, full length.</summary>
    public static ZoneArea Area(
        string id,
        string kind,
        string name,
        int x0,
        int x1,
        int priority = 0
    ) =>
        new()
        {
            Id = id,
            Kind = kind,
            Name = name,
            Priority = priority,
            Polygon = [new(x0, 0), new(x1, 0), new(x1, 5), new(x0, 5)],
        };

    /// <summary>Enters like a player does, then lets the spawn step land.</summary>
    public static async Task<IRoomAvatar> EnterAsync(RoomHarness harness)
    {
        await harness.Grain.CreateAvatarFromPlayerAsync(
            harness.ContextFor(Walker),
            new PlayerSummarySnapshot
            {
                PlayerId = Walker,
                Name = "Walker",
                Motto = string.Empty,
                Figure = "hd-180-1",
                Gender = AvatarGenderType.Male,
                AchievementScore = 0,
                CreatedAt = DateTime.UtcNow,
            },
            default
        );

        await harness.TickAvatarsAsync();

        return AvatarOf(harness);
    }

    public static async Task WalkAsync(RoomHarness harness, int x, int y)
    {
        await harness.Grain.WalkAvatarToAsync(
            harness.ContextFor(Walker),
            x,
            y,
            null,
            CancellationToken.None
        );

        await harness.TickAvatarsAsync(12);
    }

    public static IRoomAvatar AvatarOf(RoomHarness harness) =>
        harness.Grain._state.AvatarsByObjectId[harness.Grain._state.AvatarsByPlayerId[Walker]];

    public static T[] Sent<T>(RoomHarness harness) =>
        [
            .. harness
                .ComposersSent.Where(s => s.Target == Walker)
                .Select(s => s.Composer)
                .OfType<T>(),
        ];
}
