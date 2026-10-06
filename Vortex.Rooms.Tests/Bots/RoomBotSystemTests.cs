using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Streams;
using Vortex.Database.Context;
using Vortex.Database.Entities.Room;
using Vortex.Primitives;
using Vortex.Primitives.Action;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Events;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Observability;
using Vortex.Primitives.Orleans.Snapshots.Room;
using Vortex.Primitives.Orleans.Snapshots.Room.Settings;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Pets.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Primitives.Rooms.Snapshots;
using Vortex.Primitives.Rooms.Snapshots.Avatars;
using Vortex.Primitives.Rooms.Snapshots.Mapping;
using Vortex.Protocol.Messages.Outgoing.Room.Action;
using Vortex.Protocol.Messages.Outgoing.Room.Chat;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Grains;
using Vortex.Rooms.Grains.Systems;
using Vortex.Rooms.Tests.Support;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Bots;

/// <summary>
/// The decisions RoomBotSystem makes that a wire test cannot see: where a picked-up bot goes, who
/// is allowed to pick it up, and that a bot's room object id cannot collide with a pet's. All three
/// are the sort of thing that looks fine until two players and a pet are in the same room.
/// </summary>
public sealed class RoomBotSystemTests
{
    /// <summary>Past the widest chatter interval, so a scheduled bot has certainly come round.</summary>
    private const long ChatterCertainlyDueMs = 120_000;

    /// <summary>
    /// What the client's chatter dialog actually sends: phrases on their own lines, then auto-chat,
    /// delay and the mix-sentences box, separated by its own marker rather than by semicolons.
    /// </summary>
    private static string Chatter(params string[] phrases) =>
        string.Join('\r', phrases) + ";#;true;#;10;#;false";

    [Fact]
    public void BotObjectIds_CannotCollideWithPetObjectIds()
    {
        // Pets sit at +1,000,000. A bot id low enough to land in that range would have the client
        // drawing one occupant over the other.
        RoomObjectId bot = RoomBotSystem.ToRoomObjectId(1);

        bot.Value.Should().BeGreaterThan(1_000_000 + 999_999);
    }

    [Fact]
    public async Task PlacedBots_BecomeAvatarsCarryingTheirOwnerAndObjectId()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        ImmutableArray<RoomAvatarSnapshot> avatars = await harness
            .Grain.GetPlacedBotAvatarSnapshotsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        RoomBotAvatarSnapshot bot = avatars
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<RoomBotAvatarSnapshot>()
            .Subject;

        bot.WebId.Should().Be(7);
        bot.OwnerId.Should().Be(RoomHarness.Owner.Value);
        bot.OwnerName.Should()
            .Be(RoomHarness.OwnerName, "the client shows the owner on the bot's menu");
        bot.AvatarType.Should().Be(RoomObjectType.Bot);
        bot.ObjectId.Should().Be(RoomBotSystem.ToRoomObjectId(7));
    }

    [Fact]
    public async Task RemovingABotThatIsNotHere_DoesNothing()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool removed = await harness
            .Grain.RemoveBotAsync(
                harness.ContextFor(RoomHarness.Owner),
                999,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        removed.Should().BeFalse();
        harness.ComposersSentTo.Should().BeEmpty("nothing happened, so nobody should be told");
    }

    [Fact]
    public async Task RemovingABot_ReturnsItToItsOwnersHandNotTheRemovers()
    {
        // The room owner clearing up after a visitor must not end up owning the visitor's bot.
        RoomHarness harness = await RoomHarness
            .CreateAsync(canManipulate: true)
            .ConfigureAwait(true);

        bool removed = await harness
            .Grain.RemoveBotAsync(
                harness.ContextFor(RoomHarness.Stranger),
                7,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        removed.Should().BeTrue();
        harness
            .ComposersSentTo.Should()
            .Contain(RoomHarness.Owner, "the bot goes back to the hand it came from");
        harness.ComposersSentTo.Should().NotContain(RoomHarness.Stranger);
    }

    [Fact]
    public async Task RemovingABot_ClearsItsRoomSoTheOwnerSeesItInTheirInventoryAgain()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.RemoveBotAsync(harness.ContextFor(RoomHarness.Owner), 7, CancellationToken.None)
            .ConfigureAwait(true);

        await using VortexDbContext dbCtx = harness.NewDbContext();

        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == 7).ConfigureAwait(true);

        bot.RoomEntityId.Should().BeNull("a picked-up bot is back in the hand, not in the room");
    }

    [Fact]
    public async Task AStrangerWithoutRights_CannotPickUpSomebodyElsesBot()
    {
        RoomHarness harness = await RoomHarness
            .CreateAsync(canManipulate: false)
            .ConfigureAwait(true);

        bool removed = await harness
            .Grain.RemoveBotAsync(
                harness.ContextFor(RoomHarness.Stranger),
                7,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        removed.Should().BeFalse();

        await using VortexDbContext dbCtx = harness.NewDbContext();

        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == 7).ConfigureAwait(true);

        bot.RoomEntityId.Should()
            .Be(RoomHarness.RoomIdValue, "the bot must still be standing where it was");
    }

    [Fact]
    public async Task ConfiguringASkill_StoresItAndReadsItBack()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        string configured = Chatter("hello", "bye");

        bool set = await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                2,
                configured,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        set.Should().BeTrue();

        string? data = await harness
            .Grain.GetBotSkillAsync(7, 2, CancellationToken.None)
            .ConfigureAwait(true);

        data.Should().Be(configured, "the command's own encoding is stored verbatim");
    }

    [Fact]
    public async Task AnUnconfiguredSkill_ReadsBackEmptyRatherThanNull()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        string? data = await harness
            .Grain.GetBotSkillAsync(7, 2, CancellationToken.None)
            .ConfigureAwait(true);

        data.Should().BeEmpty("the dialog opens either way and needs something to render");
    }

    [Fact]
    public async Task ABotInAnotherRoom_ReadsBackNullSoTheDialogStaysShut()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        string? data = await harness
            .Grain.GetBotSkillAsync(999, 2, CancellationToken.None)
            .ConfigureAwait(true);

        data.Should().BeNull();
    }

    [Fact]
    public async Task AVisitorWithRoomRights_StillCannotReprogramSomebodyElsesBot()
    {
        // Rights let a visitor's bot be cleared away, not made to say what they typed.
        RoomHarness harness = await RoomHarness
            .CreateAsync(canManipulate: true)
            .ConfigureAwait(true);

        bool set = await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Stranger),
                7,
                2,
                "mine now",
                CancellationToken.None
            )
            .ConfigureAwait(true);

        set.Should().BeFalse();

        string? data = await harness
            .Grain.GetBotSkillAsync(7, 2, CancellationToken.None)
            .ConfigureAwait(true);

        data.Should().BeEmpty("the owner's bot must be untouched");
    }

    [Fact]
    public async Task AConfiguredBot_SaysOneOfItsPhrasesOnceItsTurnComesRound()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                2,
                Chatter("hello", "welcome"),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        // The first tick schedules rather than speaks, so a room full of bots does not greet a
        // visitor in chorus the moment it activates.
        await harness.Grain.ProcessBotsForTestAsync(0).ConfigureAwait(true);

        harness
            .BroadcastToRoom.Should()
            .BeEmpty("first sight of a bot schedules it, it does not make it talk");

        // Far enough ahead that any scheduled slot has come round.
        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs).ConfigureAwait(true);

        ChatMessageComposer spoken = harness
            .BroadcastToRoom.OfType<ChatMessageComposer>()
            .Should()
            .ContainSingle()
            .Which;

        spoken.Text.Should().BeOneOf("hello", "welcome");
        spoken.ObjectId.Should().Be(RoomBotSystem.ToRoomObjectId(7));
    }

    [Fact]
    public async Task ABotWithNoChatterConfigured_StaysQuiet()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.Grain.ProcessBotsForTestAsync(0).ConfigureAwait(true);
        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs).ConfigureAwait(true);

        harness.BroadcastToRoom.OfType<ChatMessageComposer>().Should().BeEmpty();
    }

    [Fact]
    public async Task ABotWithNoWanderConfigured_StaysPut()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.Grain.ProcessBotsForTestAsync(0).ConfigureAwait(true);
        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs).ConfigureAwait(true);

        harness
            .BroadcastToRoom.OfType<UserUpdateMessageComposer>()
            .Should()
            .BeEmpty("wandering is opt-in, an unconfigured bot is furniture that talks");
    }

    [Fact]
    public async Task ReconfiguringASkill_DropsTheCachedPlanRatherThanFinishingTheOldOne()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                2,
                Chatter("first"),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        await harness.Grain.ProcessBotsForTestAsync(0).ConfigureAwait(true);

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                2,
                Chatter("second"),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs).ConfigureAwait(true);
        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs * 2).ConfigureAwait(true);

        harness
            .BroadcastToRoom.OfType<ChatMessageComposer>()
            .Select(chat => chat.Text)
            .Should()
            .NotContain("first", "a reconfigured bot must stop saying its old lines");
    }

    [Fact]
    public async Task APlacedBot_CarriesTheSkillIdsThatDrawItsMenu()
    {
        // The client builds the bot's menu from the ids on the avatar block. Serialising none of
        // them leaves the owner a menu with nothing on it but "pick up".
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        ImmutableArray<RoomAvatarSnapshot> avatars = await harness
            .Grain.GetPlacedBotAvatarSnapshotsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        RoomBotAvatarSnapshot bot = avatars.OfType<RoomBotAvatarSnapshot>().Single();

        bot.SkillIds.Should()
            .Contain([
                (short)BotSkillId.Chatter,
                (short)BotSkillId.RandomWalk,
                (short)BotSkillId.Dance,
                (short)BotSkillId.ChangeName,
                (short)BotSkillId.DressUp,
            ]);

        bot.SkillIds.Should()
            .NotContain(
                (short)BotSkillId.NoPickUp,
                "sending that id hides the pick-up button rather than adding one"
            );
    }

    [Fact]
    public async Task TheWalkButton_TogglesRatherThanTurningWanderingOnForever()
    {
        // The client sends empty data on every click and shows no state of its own, so the second
        // click has to switch wandering back off here or it never stops.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.RandomWalk,
                string.Empty,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.Grain.ProcessBotsForTestAsync(0).ConfigureAwait(true);
        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs).ConfigureAwait(true);

        harness
            .BroadcastToRoom.OfType<UserUpdateMessageComposer>()
            .Should()
            .NotBeEmpty("one click sets the bot walking");

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.RandomWalk,
                string.Empty,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs * 2).ConfigureAwait(true);
        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs * 3).ConfigureAwait(true);

        harness
            .BroadcastToRoom.OfType<UserUpdateMessageComposer>()
            .Should()
            .BeEmpty("the second click stops it again");
    }

    [Fact]
    public async Task TheDanceButton_TellsTheRoomAndSurvivesARedraw()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.Dance,
                string.Empty,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness
            .BroadcastToRoom.OfType<DanceMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.DanceType.Should()
            .Be(AvatarDanceType.Dance);

        // Somebody walking in later reads the dance off the snapshot, because the avatar block
        // itself carries no dance.
        ImmutableArray<RoomAvatarSnapshot> avatars = await harness
            .Grain.GetPlacedBotAvatarSnapshotsAsync(CancellationToken.None)
            .ConfigureAwait(true);

        avatars
            .OfType<RoomBotAvatarSnapshot>()
            .Single()
            .DanceType.Should()
            .Be(AvatarDanceType.Dance);
    }

    [Fact]
    public async Task ClickingDanceTwice_StopsTheBotDancing()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        for (int click = 0; click < 2; click++)
        {
            await harness
                .Grain.SetBotSkillAsync(
                    harness.ContextFor(RoomHarness.Owner),
                    7,
                    BotSkillId.Dance,
                    string.Empty,
                    CancellationToken.None
                )
                .ConfigureAwait(true);
        }

        harness
            .BroadcastToRoom.OfType<DanceMessageComposer>()
            .Last()
            .DanceType.Should()
            .Be(AvatarDanceType.None);
    }

    [Fact]
    public async Task RenamingABot_WritesTheNameAndRedrawsIt()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        bool set = await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.ChangeName,
                "  Doorman  ",
                CancellationToken.None
            )
            .ConfigureAwait(true);

        set.Should().BeTrue();

        await using VortexDbContext dbCtx = harness.NewDbContext();
        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == 7).ConfigureAwait(true);

        bot.Name.Should().Be("Doorman", "the name is trimmed, not stored as typed");
        harness
            .BroadcastToRoom.OfType<UserChangeMessageComposer>()
            .Should()
            .ContainSingle("the room has to redraw a bot that changed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task AnUnusableName_IsRefusedAndLeavesTheBotAlone(string name)
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool set = await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.ChangeName,
                name,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        set.Should().BeFalse();

        await using VortexDbContext dbCtx = harness.NewDbContext();
        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == 7).ConfigureAwait(true);

        bot.Name.Should().Be("Bartender");
    }

    [Fact]
    public async Task DressingUpABotWhoseOwnerIsNotInTheRoom_IsRefusedRatherThanClearingItsLook()
    {
        // The look is taken off the owner's avatar as it stands in this room. With no avatar to
        // read, writing anything at all would leave the bot with no appearance.
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        bool set = await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.DressUp,
                string.Empty,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        set.Should().BeFalse();

        await using VortexDbContext dbCtx = harness.NewDbContext();
        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == 7).ConfigureAwait(true);

        bot.Figure.Should().Be("hd-180-1");
    }

    [Fact]
    public async Task DressingUpABot_GivesItTheLookItsOwnerIsWearingInTheRoom()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.PutOwnerInRoomWearing("hr-100.ch-210-66", AvatarGenderType.Female);
        harness.BroadcastToRoom.Clear();

        bool set = await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.DressUp,
                string.Empty,
                CancellationToken.None
            )
            .ConfigureAwait(true);

        set.Should().BeTrue();

        await using VortexDbContext dbCtx = harness.NewDbContext();
        BotEntity bot = await dbCtx.Bots.SingleAsync(b => b.Id == 7).ConfigureAwait(true);

        bot.Figure.Should().Be("hr-100.ch-210-66");
        bot.Gender.Should().Be(AvatarGenderType.Female, "a look and its gender go together");

        harness
            .BroadcastToRoom.OfType<UserChangeMessageComposer>()
            .Should()
            .ContainSingle()
            .Which.ObjectId.Should()
            .Be(RoomBotSystem.ToRoomObjectId(7));
    }

    [Fact]
    public async Task ABotWhoseOwnerTurnedAutoChatOff_StaysQuietThoughItHasPhrases()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.Chatter,
                "hello;#;false;#;10;#;false",
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.Grain.ProcessBotsForTestAsync(0).ConfigureAwait(true);
        await harness.Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs).ConfigureAwait(true);

        harness
            .BroadcastToRoom.OfType<ChatMessageComposer>()
            .Should()
            .BeEmpty("the dialog's automatic-chat box is the owner saying no");
    }

    [Fact]
    public async Task ABotDoesNotReciteItsOwnSettings()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        await harness
            .Grain.SetBotSkillAsync(
                harness.ContextFor(RoomHarness.Owner),
                7,
                BotSkillId.Chatter,
                Chatter("hello"),
                CancellationToken.None
            )
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness.Grain.ProcessBotsForTestAsync(0).ConfigureAwait(true);

        for (int tick = 1; tick <= 6; tick++)
        {
            await harness
                .Grain.ProcessBotsForTestAsync(ChatterCertainlyDueMs * tick)
                .ConfigureAwait(true);
        }

        harness
            .BroadcastToRoom.OfType<ChatMessageComposer>()
            .Select(chat => chat.Text)
            .Should()
            .AllBe("hello", "the trailing fields are settings, not lines to say");
    }

    [Fact]
    public async Task ABotSay_GoesToTheRoomAsTheBot()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness
            .Grain.BotSystem.SayAsync(7, "everybody out", CancellationToken.None)
            .ConfigureAwait(true);

        ChatMessageComposer chat = harness
            .BroadcastToRoom.OfType<ChatMessageComposer>()
            .Should()
            .ContainSingle()
            .Which;

        chat.Text.Should().Be("everybody out");
        chat.ObjectId.Should().Be(RoomBotSystem.ToRoomObjectId(7));
    }

    [Fact]
    public async Task ABotSayNamingABotThatIsNotHere_SaysNothing()
    {
        RoomHarness harness = await RoomHarness.CreateAsync().ConfigureAwait(true);

        harness.BroadcastToRoom.Clear();

        await harness
            .Grain.BotSystem.SayAsync(999, "hello", CancellationToken.None)
            .ConfigureAwait(true);

        harness.BroadcastToRoom.Should().BeEmpty();
    }
}
