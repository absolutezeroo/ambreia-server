using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.PacketHandlers.Rpg.Skills;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rpg.Characters.Grains;
using Vortex.Protocol.Messages.Incoming.Rpg.Skills;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.PacketHandlers.Tests.Rpg;

/// <summary>Spec M1b §9: a bar change is one call to the player's own character; nothing without a
/// player.</summary>
public sealed class ActionBarSetHandlerTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task AChange_GoesToThePlayersCharacter()
    {
        await HandleAsync(playerId: 7);

        _calls.Should().Equal("character 7: slot 2 = skill:skill.mage.firebolt");
    }

    [Fact]
    public async Task WithoutAPlayer_NothingIsCalled()
    {
        await HandleAsync(playerId: 0);

        _calls.Should().BeEmpty();
    }

    private Task HandleAsync(int playerId) =>
        new RpgActionBarSetMessageHandler(Grains())
            .HandleAsync(
                new RpgActionBarSetMessage { Slot = 2, ActionId = "skill:skill.mage.firebolt" },
                new MessageContext(
                    FakeProxy.Create<ISessionContext>(_ => null),
                    new PlayerId(playerId),
                    new RoomId(12)
                ),
                CancellationToken.None
            )
            .AsTask();

    private IGrainFactory Grains() =>
        FakeProxy.Create<IGrainFactory>(call =>
            call.Method.Name == nameof(IGrainFactory.GetGrain)
                ? FakeProxy.Create<IPlayerCharacterGrain>(inner =>
                {
                    _calls.Add(
                        $"character {(long)call.Args![0]!}: slot {inner.Args![0]} = {inner.Args[1]}"
                    );

                    return Task.CompletedTask;
                })
                : null
        );
}
