using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object.Logic.Avatars;
using Vortex.Primitives.Rpg.Quests;
using Vortex.Primitives.World;
using Vortex.Primitives.World.Layers;
using Vortex.Protocol.Messages.Outgoing.Rpg.Quests;
using Vortex.Rooms.Tests.Support;
using Vortex.Rpg.Dialogues;
using Vortex.Rpg.Npcs;
using Vortex.Tests.Support;
using Vortex.Tests.Support.World;
using Xunit;

namespace Vortex.Rooms.Tests.Zones;

/// <summary>Spec M2a §3.3: talking to an NPC opens its dialogue; a choice moves it on, from close enough only.</summary>
public sealed class RoomDialogueTests
{
    private static readonly DialogueNodeView Start = new(
        "start",
        "dialogue.camp.elder.name",
        "t.start",
        [new DialogueChoiceView(0, "t.ask", "next"), new DialogueChoiceView(2, "t.bye", "close")]
    );

    private static readonly DialogueNodeView Offer = new(
        "offer",
        "dialogue.camp.elder.name",
        "t.offer",
        [new DialogueChoiceView(0, "t.accept", "accept")]
    );

    private readonly List<string> _calls = [];
    private RoomHarness _harness = null!;
    private int _elder;

    private async Task ZoneAsync(string? dialogue = "dialogue.camp.elder")
    {
        ZoneDocument zone = ZoneSamples.Minimal("camp") with
        {
            Npcs =
            [
                new ZoneNpc
                {
                    Id = "elder",
                    Template = "npc.elder",
                    Name = "Ancien",
                    Cell = new ZoneCell(2, 2),
                    Dialogue = dialogue,
                },
            ],
        };
        _harness = await RoomHarness.CreateZoneAsync(zone);
        _harness.LogicFactory = () => FakeProxy.Create<IRoomPlayerLogic>(_ => null);
        _harness.NpcDialogues = call =>
        {
            NpcConversation c = (NpcConversation)call.Args![0]!;

            switch (call.Method.Name)
            {
                case nameof(INpcDialogues.OpenAsync):
                    _calls.Add($"open {c.Player.Value} {c.Zone} {c.NpcId} {c.DialogueId}");

                    return Task.FromResult<DialogueNodeView?>(Start);
                case nameof(INpcDialogues.ChooseAsync):
                    _calls.Add($"choose {call.Args[1]} {call.Args[2]}");

                    return Task.FromResult(
                        (int)call.Args[2]! == 0 && (string)call.Args[1]! == "start"
                            ? DialogueStep.Show(Offer)
                        : (string)call.Args[1]! == "offer"
                            ? DialogueStep.Close("accepted", QuestCodes.Ok, "quest.camp.wolves")
                        : DialogueStep.Close(null, QuestCodes.Ok, string.Empty)
                    );
                case nameof(INpcDialogues.Marker):
                    return c.QuestStates.GetValueOrDefault("quest.camp.wolves") == "ready"
                        ? "ready"
                        : "available";
                default:
                    return null;
            }
        };
        _harness.Grain.NpcModule.EnsureSpawned();
        _elder = _harness.Grain.NpcModule.Npcs.Single().ObjectId.Value;
    }

    private async Task EnterAtAsync(PlayerId player, int x, int y)
    {
        await _harness.Grain.CreateAvatarFromPlayerAsync(
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
        _harness.TeleportPlayer(player, x, y);
    }

    private T[] Sent<T>(PlayerId player) =>
        [
            .. _harness
                .ComposersSent.Where(s => s.Target == player)
                .Select(s => s.Composer)
                .OfType<T>(),
        ];

    [Fact]
    public async Task TalkingFromNextToTheNpc_OpensItsDialogue()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 3, 2);

        await _harness.Grain.TalkToNpcAsync(
            _harness.ContextFor(RoomHarness.Owner),
            _elder,
            CancellationToken.None
        );

        _calls.Should().Equal($"open {RoomHarness.Owner.Value} camp elder dialogue.camp.elder");
        RpgDialogueMessageComposer shown = Sent<RpgDialogueMessageComposer>(RoomHarness.Owner)
            .Single();
        (shown.NpcObjectId, shown.Text).Should().Be((_elder, "t.start"));
        shown.Choices.Select(c => c.Kind).Should().Equal("next", "close");
    }

    [Fact]
    public async Task AChoice_IsThePositionInTheShownList_AndAnAcceptClosesWithItsResult()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 3, 2);
        await _harness.Grain.TalkToNpcAsync(
            _harness.ContextFor(RoomHarness.Owner),
            _elder,
            CancellationToken.None
        );

        await _harness.Grain.ChooseDialogueAsync(
            _harness.ContextFor(RoomHarness.Owner),
            0,
            CancellationToken.None
        );
        await _harness.Grain.ChooseDialogueAsync(
            _harness.ContextFor(RoomHarness.Owner),
            0,
            CancellationToken.None
        );

        _calls.Skip(1).Should().Equal("choose start 0", "choose offer 0");
        Sent<RpgDialogueCloseMessageComposer>(RoomHarness.Owner).Should().ContainSingle();
        RpgQuestResultMessageComposer result = Sent<RpgQuestResultMessageComposer>(
                RoomHarness.Owner
            )
            .Single();
        (result.Action, result.Code, result.QuestId)
            .Should()
            .Be(("accepted", "ok", "quest.camp.wolves"));
    }

    [Fact]
    public async Task ThePositionMapsToTheContentsIndex()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 3, 2);
        await _harness.Grain.TalkToNpcAsync(
            _harness.ContextFor(RoomHarness.Owner),
            _elder,
            CancellationToken.None
        );

        await _harness.Grain.ChooseDialogueAsync(
            _harness.ContextFor(RoomHarness.Owner),
            1,
            CancellationToken.None
        );

        _calls.Last().Should().Be("choose start 2", "position 1 shows the content's choice 2");
    }

    [Fact]
    public async Task AChoiceOutOfTheShownList_IsIgnored()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 3, 2);
        await _harness.Grain.TalkToNpcAsync(
            _harness.ContextFor(RoomHarness.Owner),
            _elder,
            CancellationToken.None
        );

        await _harness.Grain.ChooseDialogueAsync(
            _harness.ContextFor(RoomHarness.Owner),
            5,
            CancellationToken.None
        );
        await _harness.Grain.ChooseDialogueAsync(
            _harness.ContextFor(RoomHarness.Stranger),
            0,
            CancellationToken.None
        );

        _calls.Should().ContainSingle("only the open");
    }

    [Fact]
    public async Task AChoiceFromTooFar_ClosesTheDialogue()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 3, 2);
        await _harness.Grain.TalkToNpcAsync(
            _harness.ContextFor(RoomHarness.Owner),
            _elder,
            CancellationToken.None
        );
        _harness.TeleportPlayer(RoomHarness.Owner, 5, 5);

        await _harness.Grain.ChooseDialogueAsync(
            _harness.ContextFor(RoomHarness.Owner),
            0,
            CancellationToken.None
        );

        _calls.Should().ContainSingle("no choice reaches the dialogue service");
        Sent<RpgDialogueCloseMessageComposer>(RoomHarness.Owner).Should().ContainSingle();
    }

    [Fact]
    public async Task TalkingFromAfar_WalksUpFirst()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 5, 5);

        await _harness.Grain.TalkToNpcAsync(
            _harness.ContextFor(RoomHarness.Owner),
            _elder,
            CancellationToken.None
        );

        _calls.Should().BeEmpty("not yet: the player walks");
        await _harness.TickUntilIdleAsync();
        _calls.Should().ContainSingle().Which.Should().StartWith("open");
    }

    [Fact]
    public async Task AnNpcWithoutDialogue_SaysNothing()
    {
        await ZoneAsync(dialogue: null);
        await EnterAtAsync(RoomHarness.Owner, 3, 2);

        await _harness.Grain.TalkToNpcAsync(
            _harness.ContextFor(RoomHarness.Owner),
            _elder,
            CancellationToken.None
        );

        _calls.Should().BeEmpty();
        Sent<RpgDialogueMessageComposer>(RoomHarness.Owner).Should().BeEmpty();
    }

    [Fact]
    public async Task Markers_AreEachPlayersOwn()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 3, 2);
        await EnterAtAsync(RoomHarness.Stranger, 4, 4);

        await _harness.Grain.UpdatePlayerFactsAsync(
            RoomHarness.Owner,
            new QuestZoneFacts
            {
                Sequence = 99,
                States = ImmutableDictionary<string, string>.Empty.Add(
                    "quest.camp.wolves",
                    "ready"
                ),
            }
        );

        Sent<RpgNpcsMessageComposer>(RoomHarness.Owner)
            .Last()
            .Npcs.Single()
            .Should()
            .Be(new RpgNpcMarker { ObjectId = _elder, Marker = "ready" });
        Sent<RpgNpcsMessageComposer>(RoomHarness.Stranger)
            .Last()
            .Npcs.Single()
            .Marker.Should()
            .Be("available");
    }

    /// <summary>Back within the grace period no avatar is made, so nothing else would send them.</summary>
    [Fact]
    public async Task APlayerBackWithinGrace_GetsTheMarkersInTheEntryBurst()
    {
        await ZoneAsync();
        await EnterAtAsync(RoomHarness.Owner, 3, 2);

        RoomAdmissionSnapshot back = await _harness.Grain.AdmitAsync(
            _harness.ContextFor(RoomHarness.Owner),
            string.Empty,
            resumed: true,
            CancellationToken.None
        );

        back.Burst.OfType<RpgNpcsMessageComposer>()
            .Single()
            .Npcs.Select(n => (n.ObjectId, n.Marker))
            .Should()
            .Equal((_elder, "available"));
    }

    /// <summary>A fresh entry gets them when its avatar is made, after the burst.</summary>
    [Fact]
    public async Task AFreshEntry_HasNoMarkersInTheBurst()
    {
        await ZoneAsync();

        RoomAdmissionSnapshot entry = await _harness.Grain.AdmitAsync(
            _harness.ContextFor(RoomHarness.Owner),
            string.Empty,
            resumed: false,
            CancellationToken.None
        );

        entry.Burst.OfType<RpgNpcsMessageComposer>().Should().BeEmpty();
    }
}
