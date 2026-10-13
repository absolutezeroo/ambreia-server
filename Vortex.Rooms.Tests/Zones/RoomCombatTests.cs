using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rpg.Characters;
using Vortex.Primitives.World;
using Vortex.Protocol.Messages.Outgoing.Rpg.Combat;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Zones;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>Spec M1a §4.3–§4.4: a zone room runs its fights — a player joins them on entry and
/// leaves them (awaited) on exit, a click on a monster goes to them first, walking by hand stops the
/// attack, the combat step ticks, the entry burst carries every fighter's health.</summary>
public sealed class RoomCombatTests
{
    private readonly List<string> _calls = [];

    private RoomHarness _harness = null!;

    private async Task ZoneAsync()
    {
        _harness = await RoomHarness.CreateZoneAsync(ZoneSamples.Minimal(), (_, _) => Recording());
        _harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
    }

    private async Task EnterAsync(PlayerId player)
    {
        bool created = await _harness.Grain.CreateAvatarFromPlayerAsync(
            _harness.ContextFor(player),
            new PlayerSummarySnapshot
            {
                PlayerId = player,
                Name = "P" + player.Value,
                Motto = string.Empty,
                Figure = "hd-180-1",
                Gender = AvatarGenderType.Male,
                AchievementScore = 0,
                CreatedAt = DateTime.UtcNow,
            },
            CancellationToken.None
        );

        created.Should().BeTrue();
    }

    [Fact]
    public async Task EnteringAZoneRoom_JoinsItsFights()
    {
        await ZoneAsync();

        await EnterAsync(RoomHarness.Owner);

        _calls
            .Should()
            .ContainSingle(c => c.StartsWith("entered"))
            .Which.Should()
            .Be($"entered {RoomHarness.Owner.Value}");
    }

    /// <summary>A room with no zone runs no fight, so no zone tells the HUD the player's health
    /// and mana: the character does.</summary>
    [Fact]
    public async Task EnteringARoomWithoutAZone_HasTheCharacterPushItsVitals()
    {
        _harness = await RoomHarness.CreateAsync();
        _harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);

        await EnterAsync(RoomHarness.Owner);

        _harness.VitalsPushed.Should().Equal(RoomHarness.Owner);
    }

    [Fact]
    public async Task EnteringAZoneRoom_LeavesTheVitalsToItsFights()
    {
        await ZoneAsync();

        await EnterAsync(RoomHarness.Owner);

        _harness.VitalsPushed.Should().BeEmpty();
    }

    [Fact]
    public async Task LeavingTheRoom_LeavesItsFights_BeforeTheAvatarGoes()
    {
        await ZoneAsync();
        await EnterAsync(RoomHarness.Owner);
        _calls.Clear();

        await _harness.Grain.RemoveAvatarFromPlayerAsync(
            _harness.ContextFor(RoomHarness.Owner),
            RoomHarness.Owner,
            CancellationToken.None
        );

        _calls.Should().Equal($"left {RoomHarness.Owner.Value}");
    }

    [Fact]
    public async Task ClickingAMonster_GoesToTheFights()
    {
        await ZoneAsync();
        await EnterAsync(RoomHarness.Owner);

        await _harness.Grain.SelectTargetAsync(
            _harness.ContextFor(RoomHarness.Owner),
            99,
            CancellationToken.None
        );

        _calls.Should().Contain($"select {RoomHarness.Owner.Value} 99");
    }

    [Fact]
    public async Task WalkingByHand_StopsTheAttack()
    {
        await ZoneAsync();
        await EnterAsync(RoomHarness.Owner);
        _calls.Clear();

        await _harness.Grain.WalkAvatarToAsync(
            _harness.ContextFor(RoomHarness.Owner),
            4,
            4,
            null,
            CancellationToken.None
        );

        _calls.Should().Contain($"stop {RoomHarness.Owner.Value}");
    }

    [Fact]
    public async Task ARoomGoingAway_GivesEveryPlayersVitalsBack()
    {
        // Spec M1a §4.4: an editor republish or a silo shutdown deactivates the room with players in
        // it; their health is written back first, or the next entry reads it from before this one.
        await ZoneAsync();
        await EnterAsync(RoomHarness.Owner);
        await EnterAsync(RoomHarness.Stranger);
        _calls.Clear();

        await _harness.Grain.OnDeactivateAsync(
            new DeactivationReason(DeactivationReasonCode.ShuttingDown, "test"),
            CancellationToken.None
        );

        _calls
            .Should()
            .BeEquivalentTo(
                $"left {RoomHarness.Owner.Value}",
                $"left {RoomHarness.Stranger.Value}"
            );
    }

    [Fact]
    public async Task AStatsChangeInZone_ReachesTheFights()
    {
        await ZoneAsync();

        await _harness.Grain.UpdateCombatantStatsAsync(
            RoomHarness.Owner,
            new CharacterCombatProfile
            {
                Stats = new CharacterStatsSnapshot { MaxHealth = 120 },
                Health = 120,
                Mana = 0,
                Level = 1,
                Range = 1,
                ThreatMultiplier = 1,
                ClassId = "class.warrior",
                SpecializationId = "spec.warrior.guardian",
            },
            CancellationToken.None
        );

        _calls.Should().Contain($"stats {RoomHarness.Owner.Value} 120");
    }

    /// <summary>Spec M1b §10.1: a cast, a cancel and a manual walk reach the zone's fights.</summary>
    [Fact]
    public async Task ACastACancelAndAWalk_GoToTheFights()
    {
        await ZoneAsync();
        await EnterAsync(RoomHarness.Owner);
        _calls.Clear();

        await _harness.Grain.CastSkillAsync(
            _harness.ContextFor(RoomHarness.Owner),
            "skill.mage.firebolt",
            9,
            -1,
            -1,
            CancellationToken.None
        );
        await _harness.Grain.CancelCastAsync(
            _harness.ContextFor(RoomHarness.Owner),
            CancellationToken.None
        );
        await _harness.Grain.WalkAvatarToAsync(
            _harness.ContextFor(RoomHarness.Owner),
            4,
            4,
            null,
            CancellationToken.None
        );

        _calls
            .Should()
            .ContainInOrder(
                $"cast {RoomHarness.Owner.Value} skill.mage.firebolt 9 -1 -1",
                $"cancel {RoomHarness.Owner.Value}",
                $"walked {RoomHarness.Owner.Value}"
            );
    }

    [Fact]
    public async Task WithoutAnAvatarHere_OrWithoutAPlayer_NothingIsCast()
    {
        await ZoneAsync();

        await _harness.Grain.CastSkillAsync(
            _harness.ContextFor(RoomHarness.Owner),
            "skill.mage.firebolt",
            9,
            -1,
            -1,
            CancellationToken.None
        );
        await _harness.Grain.CastSkillAsync(
            _harness.ContextFor(PlayerId.Invalid),
            "skill.mage.firebolt",
            9,
            -1,
            -1,
            CancellationToken.None
        );

        _calls.Should().NotContain(c => c.StartsWith("cast"));
    }

    [Fact]
    public async Task TheEntryBurst_CarriesEveryFightersHealth()
    {
        await ZoneAsync();

        RoomAdmissionSnapshot admission = await _harness.Grain.AdmitAsync(
            _harness.ContextFor(RoomHarness.Owner),
            string.Empty,
            false,
            CancellationToken.None
        );

        admission.Burst.OfType<RpgVitalsMessageComposer>().Should().ContainSingle();
    }

    private IZoneSimulation Recording() =>
        FakeProxy.Create<IZoneSimulation>(call =>
        {
            switch (call.Method.Name)
            {
                case nameof(IZoneSimulation.PlayerEnteredAsync):
                    _calls.Add($"entered {((PlayerId)call.Args![0]!).Value}");
                    return Task.CompletedTask;
                case nameof(IZoneSimulation.PlayerLeftAsync):
                    _calls.Add($"left {((PlayerId)call.Args![0]!).Value}");
                    return Task.CompletedTask;
                case nameof(IZoneSimulation.SelectTargetAsync):
                    _calls.Add($"select {((PlayerId)call.Args![0]!).Value} {call.Args[1]}");
                    return Task.FromResult(true);
                case nameof(IZoneSimulation.StopAttack):
                    _calls.Add($"stop {((PlayerId)call.Args![0]!).Value}");
                    return null;
                case nameof(IZoneSimulation.PlayerStatsChanged):
                    _calls.Add(
                        $"stats {((PlayerId)call.Args![0]!).Value} {((CharacterCombatProfile)call.Args[1]!).Stats.MaxHealth}"
                    );
                    return null;
                case nameof(IZoneSimulation.EntryVitals):
                    return new IComposer[] { new RpgVitalsMessageComposer { Combatants = [] } };
                case nameof(IZoneSimulation.CastSkillAsync):
                    _calls.Add(
                        $"cast {((PlayerId)call.Args![0]!).Value} {call.Args[1]} {call.Args[2]} {call.Args[3]} {call.Args[4]}"
                    );
                    return Task.CompletedTask;
                case nameof(IZoneSimulation.CancelCast):
                    _calls.Add($"cancel {((PlayerId)call.Args![0]!).Value}");
                    return null;
                case nameof(IZoneSimulation.PlayerWalked):
                    _calls.Add($"walked {((PlayerId)call.Args![0]!).Value}");
                    return null;
                default:
                    return null;
            }
        });
}
