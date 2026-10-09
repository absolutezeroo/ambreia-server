using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Action;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Protocol.Messages.Outgoing.Rpg.Interaction;
using Vortex.Rooms.Object.Avatars.Player;
using Vortex.Rooms.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Avatars;

/// <summary>
/// Spec F3b §6: a player's target is kept by the room — an avatar here, oneself included; anything
/// else clears it; it clears for whoever aimed at someone who leaves; it is private and never stored.
/// </summary>
public sealed class RoomTargetTests
{
    private static readonly PlayerId Owner = RoomHarness.Owner;
    private static readonly PlayerId Stranger = RoomHarness.Stranger;

    private RoomHarness _harness = null!;
    private RoomPlayerAvatar _owner = null!;
    private RoomPlayerAvatar _stranger = null!;

    private async Task TwoPlayersAsync()
    {
        _harness = await RoomHarness.CreateAsync();
        _owner = _harness.PutRealPlayerInRoom(Owner, 1, 1);
        _stranger = _harness.PutRealPlayerInRoom(Stranger, 2, 2);
    }

    private Task SelectAsync(PlayerId by, int objectId) =>
        _harness.Grain.SelectTargetAsync(_harness.ContextFor(by), objectId, CancellationToken.None);

    private List<RpgTargetMessageComposer> TargetsSentTo(PlayerId player) =>
        [
            .. _harness
                .ComposersSent.Where(s => s.Target == player)
                .Select(s => s.Composer)
                .OfType<RpgTargetMessageComposer>(),
        ];

    [Fact]
    public async Task ChoosingAPlayer_AnswersHisNameLevelAndClass()
    {
        await TwoPlayersAsync();
        _harness.Characters[Stranger] = new CharacterSummary
        {
            ClassId = "class.warrior",
            SpecializationId = "spec.warrior.guardian",
            Level = 3,
            Xp = 0,
        };

        await SelectAsync(Owner, _stranger.ObjectId.Value);

        RpgTargetMessageComposer target = TargetsSentTo(Owner).Single();
        (target.ObjectId, target.Kind, target.Name, target.Level, target.ClassId)
            .Should()
            .Be(
                (
                    _stranger.ObjectId.Value,
                    RpgTargetMessageComposer.KindPlayer,
                    _stranger.Name,
                    3,
                    "class.warrior"
                )
            );
        TargetsSentTo(Stranger).Should().BeEmpty("a target is private");
    }

    [Fact]
    public async Task ChoosingOneself_IsAllowed()
    {
        await TwoPlayersAsync();

        await SelectAsync(Owner, _owner.ObjectId.Value);

        TargetsSentTo(Owner).Single().ObjectId.Should().Be(_owner.ObjectId.Value);
    }

    [Fact]
    public async Task ZeroClearsTheTarget()
    {
        await TwoPlayersAsync();
        await SelectAsync(Owner, _stranger.ObjectId.Value);

        await SelectAsync(Owner, 0);

        TargetsSentTo(Owner).Last().Should().Be(RpgTargetMessageComposer.None);
        _harness.Grain._state.Targets.Should().NotContainKey(Owner);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(9999)]
    [InlineData(int.MaxValue)]
    public async Task AnIdThatIsNotAPlayer_ClearsTheTarget(int objectId)
    {
        await TwoPlayersAsync();
        await SelectAsync(Owner, _stranger.ObjectId.Value);

        await SelectAsync(Owner, objectId);

        TargetsSentTo(Owner).Last().Should().Be(RpgTargetMessageComposer.None);
        _harness.Grain._state.Targets.Should().NotContainKey(Owner);
    }

    [Fact]
    public async Task ATargetThatLeaves_IsClearedForWhoeverAimedAtIt()
    {
        await TwoPlayersAsync();
        await SelectAsync(Owner, _stranger.ObjectId.Value);

        await _harness.Grain.RemoveAvatarFromPlayerAsync(
            _harness.ContextFor(Stranger),
            Stranger,
            CancellationToken.None
        );

        TargetsSentTo(Owner).Last().Should().Be(RpgTargetMessageComposer.None);
        _harness.Grain._state.Targets.Should().NotContainKey(Owner);
    }

    [Fact]
    public async Task TheOneWhoLeaves_IsForgotten()
    {
        await TwoPlayersAsync();
        await SelectAsync(Owner, _stranger.ObjectId.Value);

        await _harness.Grain.RemoveAvatarFromPlayerAsync(
            _harness.ContextFor(Owner),
            Owner,
            CancellationToken.None
        );

        _harness.Grain._state.Targets.Should().BeEmpty();
        TargetsSentTo(Stranger).Should().BeEmpty();
    }

    [Fact]
    public async Task ACharacterThatDoesNotAnswer_LeavesTheTargetChosen()
    {
        await TwoPlayersAsync();
        _harness.CharacterFails = true;

        await SelectAsync(Owner, _stranger.ObjectId.Value);

        RpgTargetMessageComposer target = TargetsSentTo(Owner).Single();
        (target.ObjectId, target.Level, target.ClassId)
            .Should()
            .Be((_stranger.ObjectId.Value, 0, string.Empty));
        _harness.Grain._state.Targets.Should().ContainKey(Owner);
    }

    /// <summary>A zone republished rebuilds the room: what the old one held is gone.</summary>
    [Fact]
    public async Task ATargetDoesNotSurviveTheRoomBeingRebuilt()
    {
        RoomHarness rebuilt = await RoomHarness.CreateAsync();

        rebuilt.Grain._state.Targets.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAPlayer_NothingHappens()
    {
        await TwoPlayersAsync();

        await _harness.Grain.SelectTargetAsync(
            ActionContext.System,
            _stranger.ObjectId.Value,
            CancellationToken.None
        );

        _harness.ComposersSent.Should().BeEmpty();
        _harness.Grain._state.Targets.Should().BeEmpty();
    }

    /// <summary>A choice that arrives after its sender left (a Quit racing it), or before the
    /// sender's avatar exists: nothing is kept — a target left behind would clear the player's
    /// target in another zone later — and nothing about the target is told.</summary>
    [Fact]
    public async Task APlayerWithNoAvatarHere_KeepsNoTarget()
    {
        await TwoPlayersAsync();
        PlayerId elsewhere = new(303);

        await SelectAsync(elsewhere, _stranger.ObjectId.Value);

        _harness.Grain._state.Targets.Should().BeEmpty();
        _harness.ComposersSent.Should().BeEmpty();
    }
}
