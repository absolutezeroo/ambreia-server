using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Events.Registry;
using Vortex.Observability.Events;
using Vortex.Primitives.Events.Rpg;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Players;
using Xunit;

namespace Vortex.Database.Tests.Observability;

/// <summary>Spec Objets §13: every grant, destroy, removal and binding of an RPG item lands in
/// item_events, under a type that says its id is an rpg_items id.</summary>
public sealed class RpgItemForensicsTests
{
    private static readonly PlayerId Player = new(7);
    private static readonly EventContext Context = new() { CorrelationId = "test" };

    private readonly RecordingItemForensics _forensics = new();

    [Fact]
    public async Task AGrant_IsRecordedWithItsKey()
    {
        await new RpgItemGrantedForensicsHandler(_forensics).HandleAsync(
            new ItemGrantedEvent(Player, "k1", 42, "item.quest.sealed_letter", 1, true),
            Context,
            CancellationToken.None
        );

        ItemForensicEvent e = _forensics.Recorded.Single();
        (e.ItemId, e.EventType, e.ToOwnerId, e.ActorPlayerId)
            .Should()
            .Be((42L, ItemEventType.RpgGranted, 7L, (long?)null));
        JsonElement data = JsonDocument.Parse(e.Data!).RootElement;
        data.GetProperty("definition").GetString().Should().Be("item.quest.sealed_letter");
        data.GetProperty("quantity").GetInt32().Should().Be(1);
        data.GetProperty("grantKey").GetString().Should().Be("k1");
        data.GetProperty("bound").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ADestroy_IsRecordedAsThePlayers()
    {
        await new RpgItemDestroyedForensicsHandler(_forensics).HandleAsync(
            new ItemDestroyedEvent(Player, 42, "item.material.amber_shard", 4),
            Context,
            CancellationToken.None
        );

        ItemForensicEvent e = _forensics.Recorded.Single();
        (e.ItemId, e.EventType, e.FromOwnerId, e.ActorPlayerId)
            .Should()
            .Be((42L, ItemEventType.RpgDestroyed, 7L, (long?)7));
        JsonDocument.Parse(e.Data!).RootElement.GetProperty("quantity").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task ARemoval_IsRecordedAsTheServers()
    {
        await new RpgItemRemovedForensicsHandler(_forensics).HandleAsync(
            new ItemRemovedEvent(Player, 42, "item.material.amber_shard", 10),
            Context,
            CancellationToken.None
        );

        ItemForensicEvent e = _forensics.Recorded.Single();
        (e.ItemId, e.EventType, e.FromOwnerId, e.ActorPlayerId)
            .Should()
            .Be((42L, ItemEventType.RpgRemoved, 7L, (long?)null));
    }

    [Fact]
    public async Task ABinding_IsRecorded()
    {
        await new RpgItemBoundForensicsHandler(_forensics).HandleAsync(
            new ItemBoundEvent(Player, 42, "item.weapon.amber_blade"),
            Context,
            CancellationToken.None
        );

        ItemForensicEvent e = _forensics.Recorded.Single();
        (e.ItemId, e.EventType, e.ToOwnerId, e.ActorPlayerId)
            .Should()
            .Be((42L, ItemEventType.RpgBound, 7L, (long?)7));
    }
}
