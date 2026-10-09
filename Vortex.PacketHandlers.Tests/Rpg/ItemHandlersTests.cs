using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Items;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Primitives.Rpg.Items;
using Vortex.Protocol.Messages.Incoming.Rpg.Items;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>Spec Objets §17: each bag packet is one call to the player's character, with the
/// cases the client named; nothing without a player.</summary>
public sealed class ItemHandlersTests
{
    private const int PLAYER_ID = 7;

    private readonly List<string> _calls = [];

    [Fact]
    public async Task EachPacket_CallsItsOperationWithItsCases()
    {
        MessageContext ctx = Context(PLAYER_ID);

        await new RpgItemMoveMessageHandler(Grains()).HandleAsync(
            new RpgItemMoveMessage { From = 1, To = 2 },
            ctx,
            CancellationToken.None
        );
        await new RpgItemSplitMessageHandler(Grains()).HandleAsync(
            new RpgItemSplitMessage
            {
                From = 1,
                Quantity = 3,
                To = 2,
            },
            ctx,
            CancellationToken.None
        );
        await new RpgItemEquipMessageHandler(Grains()).HandleAsync(
            new RpgItemEquipMessage { BagSlot = 4 },
            ctx,
            CancellationToken.None
        );
        await new RpgItemUnequipMessageHandler(Grains()).HandleAsync(
            new RpgItemUnequipMessage { EquipmentSlot = 8 },
            ctx,
            CancellationToken.None
        );
        await new RpgItemDestroyMessageHandler(Grains()).HandleAsync(
            new RpgItemDestroyMessage { BagSlot = 4, Quantity = 2 },
            ctx,
            CancellationToken.None
        );
        await new RpgItemClaimParcelMessageHandler(Grains()).HandleAsync(
            new RpgItemClaimParcelMessage { Parcel = 0 },
            ctx,
            CancellationToken.None
        );

        _calls
            .Should()
            .Equal(
                "7:MoveItemAsync(1,2)",
                "7:SplitItemAsync(1,3,2)",
                "7:EquipItemAsync(4)",
                "7:UnequipItemAsync(8)",
                "7:DestroyItemAsync(4,2)",
                "7:ClaimParcelAsync(0)"
            );
    }

    /// <summary>The character screen's socket has an account and no player: a bag packet there
    /// reaches no character.</summary>
    [Fact]
    public async Task WithoutAPlayer_NothingIsCalled()
    {
        await new RpgItemMoveMessageHandler(Grains()).HandleAsync(
            new RpgItemMoveMessage { From = 1, To = 2 },
            Context(0),
            CancellationToken.None
        );
        await new RpgItemEquipMessageHandler(Grains()).HandleAsync(
            new RpgItemEquipMessage { BagSlot = 4 },
            Context(0),
            CancellationToken.None
        );

        _calls.Should().BeEmpty();
    }

    private static MessageContext Context(int playerId) =>
        new(
            FakeProxy.Create<ISessionContext>(_ => null),
            new PlayerId(playerId),
            default,
            accountId: 3
        );

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                {
                    _calls.Add(
                        $"{(long)call.Args![0]!}:{inner.Method.Name}({string.Join(',', inner.Args!)})"
                    );

                    return Task.FromResult(ItemOutcome.Ok);
                })
                : null
        );
}
