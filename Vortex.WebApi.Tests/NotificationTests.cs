using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vortex.Database.Context;
using Vortex.Database.Entities.Groups;
using Vortex.Database.Entities.Moderation;
using Vortex.Database.Entities.Players;
using Vortex.Database.Entities.Room;
using Vortex.Events.Registry;
using Vortex.Primitives.Events;
using Vortex.Primitives.Groups.Enums;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Navigator.Enums;
using Vortex.Primitives.Notifications;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;
using Vortex.WebApi.Notifications;
using Vortex.WebApi.Services;
using Xunit;

namespace Vortex.WebApi.Tests;

/// <summary>
/// The website's notifications, end to end: a domain event goes into a handler and comes out of
/// the list the site reads.
/// </summary>
/// <remarks>
/// The handlers are exercised directly rather than through the bus. What is worth proving here is
/// the translation — which player is told, and what the line carries — and the bus delivering to a
/// registered handler is the event system's own business, tested where it lives.
/// </remarks>
public sealed class NotificationTests
{
    /// <summary>
    /// The case the whole thing exists for: a membership lapses off a timer, so there is no screen
    /// the player was on and no packet that could have told them.
    /// </summary>
    [Fact]
    public async Task AClubExpiryIsWaitingNextTimeILook()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await new ClubExpiredNotificationHandler(Sink(factory)).HandleAsync(
            new ClubExpiredEvent(1, WasVip: true),
            Context(),
            default
        );

        NotificationPage page = await Service(factory).GetAsync(1, 1, 0);

        page.Unread.Should().Be(1);
        page.Items.Should().ContainSingle();
        page.Items[0].Kind.Should().Be(NotificationKind.ClubExpired);
        page.Items[0].Seen.Should().BeFalse();

        // Nobody did it, so nobody is named. The line is the hotel's, not a player's.
        page.Items[0].Actor.Should().BeNull();

        JsonDocument
            .Parse(page.Items[0].Data!)
            .RootElement.GetProperty("wasVip")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    /// <summary>Somebody blocked me while I was away, and the line names them.</summary>
    [Fact]
    public async Task ABlockNamesWhoDidIt()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Autre");

        await new UserBlockedNotificationHandler(Sink(factory)).HandleAsync(
            new UserBlockedEvent(ActorPlayerId: 2, TargetPlayerId: 1),
            Context(),
            default
        );

        NotificationPage mine = await Service(factory).GetAsync(1, 1, 0);

        mine.Items.Should().ContainSingle();
        mine.Items[0].Kind.Should().Be(NotificationKind.Blocked);
        mine.Items[0].Actor!.Name.Should().Be("Autre");
        mine.Items[0].Actor!.FigureString.Should().Be("hr-115");

        // The blocker is told nothing: they were there when they did it.
        (await Service(factory).GetAsync(2, 1, 0))
            .Items.Should()
            .BeEmpty();
    }

    /// <summary>
    /// A player is never told about their own doing. Guarded once in the sink rather than in each
    /// of the two dozen handlers, so this holds for every kind.
    /// </summary>
    [Fact]
    public async Task IAmNotNotifiedOfMyOwnDoing()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        await Sink(factory).NotifyAsync(1, NotificationKind.RespectReceived, actorPlayerId: 1);

        (await Service(factory).GetAsync(1, 1, 0)).Items.Should().BeEmpty();
    }

    /// <summary>
    /// Rating a room tells its OWNER, who is not on the event — the lookup is the point.
    /// </summary>
    [Fact]
    public async Task RatingARoomTellsItsOwner()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Proprio");
        await PlayerAsync(factory, 2, "Visiteur");
        await RoomAsync(factory, 10, ownerId: 1);

        await new RoomRatedNotificationHandler(Sink(factory), factory.DbContexts).HandleAsync(
            new RoomRatedEvent(2, 10, 1),
            Context(),
            default
        );

        NotificationPage owner = await Service(factory).GetAsync(1, 1, 0);

        owner.Items.Should().ContainSingle();
        owner.Items[0].Kind.Should().Be(NotificationKind.RoomRated);
        owner.Items[0].TargetId.Should().Be(10);
        owner.Items[0].Actor!.Name.Should().Be("Visiteur");
    }

    /// <summary>
    /// Reading clears the counter for the WHOLE list, not for the page that was on screen — a bell
    /// that will not go out is worse than one that is occasionally too eager.
    /// </summary>
    [Fact]
    public async Task ReadingClearsEveryUnreadAndNotJustThePageIAmOn()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        for (int n = 0; n < 5; n++)
        {
            await Sink(factory).NotifyAsync(1, NotificationKind.BadgeGranted);
        }

        (await Service(factory).GetAsync(1, 1, 2)).Unread.Should().Be(5);

        (await Service(factory).MarkSeenAsync(1)).Should().Be(5);

        NotificationPage after = await Service(factory).GetAsync(1, 1, 0);

        after.Unread.Should().Be(0);
        after.Total.Should().Be(5);
        after.Items.Should().OnlyContain(n => n.Seen);

        // A second read moves nothing rather than failing or re-stamping the dates.
        (await Service(factory).MarkSeenAsync(1))
            .Should()
            .Be(0);
    }

    /// <summary>Another player's notifications are not mine, whatever the list is asked for.</summary>
    [Fact]
    public async Task IOnlyEverSeeMyOwn()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Autre");

        await Sink(factory).NotifyAsync(2, NotificationKind.ClubPayday);

        (await Service(factory).GetAsync(1, 1, 0)).Items.Should().BeEmpty();
        (await Service(factory).GetAsync(2, 1, 0)).Items.Should().ContainSingle();
    }

    /// <summary>
    /// A join request reaches every admin AND the owner, once each — the only kind that fans out.
    /// </summary>
    [Fact]
    public async Task AJoinRequestReachesEveryAdminAndTheOwner()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Proprio");
        await PlayerAsync(factory, 2, "Admin");
        await PlayerAsync(factory, 3, "Membre");
        await PlayerAsync(factory, 4, "Demandeur");

        // The owner also holds an admin row, which is the ordinary case — and the reason the
        // handler deduplicates rather than just concatenating the two lists.
        await GroupAsync(factory, 7, ownerId: 1, admins: [1, 2], members: [3]);

        await new GroupJoinRequestNotificationHandler(
            Sink(factory),
            factory.DbContexts
        ).HandleAsync(new GroupMembershipRequestedEvent(4, 7), Context(), default);

        (await Service(factory).GetAsync(1, 1, 0)).Items.Should().ContainSingle();
        (await Service(factory).GetAsync(2, 1, 0)).Items.Should().ContainSingle();

        // An ordinary member cannot act on it, so is not told.
        (await Service(factory).GetAsync(3, 1, 0))
            .Items.Should()
            .BeEmpty();

        // Nor is the person who asked.
        (await Service(factory).GetAsync(4, 1, 0))
            .Items.Should()
            .BeEmpty();
    }

    /// <summary>
    /// An owner who holds no admin row is still told. That is the case the union exists for: rely
    /// on group_members alone and the one person who can always act hears nothing.
    /// </summary>
    [Fact]
    public async Task AnOwnerWithNoAdminRowIsStillTold()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Proprio");
        await PlayerAsync(factory, 4, "Demandeur");
        await GroupAsync(factory, 7, ownerId: 1, admins: [], members: []);

        await new GroupJoinRequestNotificationHandler(
            Sink(factory),
            factory.DbContexts
        ).HandleAsync(new GroupMembershipRequestedEvent(4, 7), Context(), default);

        (await Service(factory).GetAsync(1, 1, 0)).Items.Should().ContainSingle();
    }

    /// <summary>
    /// Closing reports tells whoever filed them, once per person however many tickets they had —
    /// and says nothing about whether anybody was punished.
    /// </summary>
    [Fact]
    public async Task ClosingReportsTellsWhoeverFiledThem()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Temoin");
        await PlayerAsync(factory, 2, "AutreTemoin");
        await PlayerAsync(factory, 9, "Staff");

        await TicketAsync(factory, 10, reporter: 1, closed: true);
        await TicketAsync(factory, 11, reporter: 1, closed: true);
        await TicketAsync(factory, 12, reporter: 2, closed: true);

        // Still open, and in the batch: it must not produce a line.
        await TicketAsync(factory, 13, reporter: 2, closed: false);

        await new CfhClosedNotificationHandler(Sink(factory), factory.DbContexts).HandleAsync(
            new CfhTicketsClosedEvent(9, [10, 11, 12, 13], "spam", Sanctioned: true),
            Context(),
            default
        );

        // Two tickets, one person, one line.
        NotificationPage first = await Service(factory).GetAsync(1, 1, 0);

        first.Items.Should().ContainSingle();
        first.Items[0].Kind.Should().Be(NotificationKind.CfhClosed);

        // Staff is not named: who moderated is not the reporter's business.
        first.Items[0].Actor.Should().BeNull();

        (await Service(factory).GetAsync(2, 1, 0)).Items.Should().ContainSingle();
    }

    /// <summary>
    /// A notification pushes its new unread count to the tabs already listening.
    /// </summary>
    /// <remarks>
    /// The point of the whole broadcaster: without it the bell can only find out by asking, and
    /// asking on a timer is a request per visitor per interval for a number that is usually zero.
    /// </remarks>
    [Fact]
    public async Task WritingANotificationPushesTheCountToAListeningTab()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        using NotificationBroadcaster.Subscription tab = factory.Broadcast.Subscribe(1);

        await Sink(factory).NotifyAsync(1, NotificationKind.BadgeGranted);
        await Sink(factory).NotifyAsync(1, NotificationKind.ClubPayday);

        // The channel is bounded at one and drops what was not read, so what a tab receives is
        // the LATEST count and never a backlog -- two writes, one value waiting, and it is 2.
        tab.Reader.TryRead(out int unread).Should().BeTrue();
        unread.Should().Be(2);
        tab.Reader.TryRead(out _).Should().BeFalse();
    }

    /// <summary>Another player's notification is not pushed to my tab.</summary>
    [Fact]
    public async Task APushGoesOnlyToItsOwnPlayersTabs()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");
        await PlayerAsync(factory, 2, "Autre");

        using NotificationBroadcaster.Subscription mine = factory.Broadcast.Subscribe(1);

        await Sink(factory).NotifyAsync(2, NotificationKind.ClubPayday);

        mine.Reader.TryRead(out _).Should().BeFalse();
    }

    /// <summary>
    /// Closing a tab unsubscribes it, and a push after that reaches nobody rather than piling up
    /// in a channel no one will ever read.
    /// </summary>
    [Fact]
    public async Task AClosedTabStopsReceiving()
    {
        await using WebApiTestFactory factory = new();
        await PlayerAsync(factory, 1, "Prodiigy");

        NotificationBroadcaster.Subscription tab = factory.Broadcast.Subscribe(1);
        tab.Dispose();

        await Sink(factory).NotifyAsync(1, NotificationKind.BadgeGranted);

        tab.Reader.TryRead(out _).Should().BeFalse();
    }

    private static EventContext Context() => new() { CorrelationId = "test" };

    private static INotificationSink Sink(WebApiTestFactory factory) =>
        new NotificationSink(
            factory.DbContexts,
            factory.Broadcast,
            NullLogger<NotificationSink>.Instance
        );

    private static IWebApiNotificationService Service(WebApiTestFactory factory) =>
        new WebApiNotificationService(factory.DbContexts);

    private static async Task PlayerAsync(WebApiTestFactory factory, int id, string name)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.Players.Add(
            new PlayerEntity
            {
                Id = id,
                Name = name,
                Figure = "hr-115",
                Gender = AvatarGenderType.Male,
                PlayerStatus = PlayerStatusType.Offline,
                PlayerPerks = PlayerPerkFlags.None,
                ProfileVisible = true,
            }
        );

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A group with its owner, its admins and its plain members.
    /// </summary>
    /// <remarks>
    /// A group requires a room, which requires a model, so this seeds one of each — none of which
    /// the handler reads. What it reads is <c>group_members.rank</c> and the group's owner column.
    /// </remarks>
    private static async Task GroupAsync(
        WebApiTestFactory factory,
        int id,
        int ownerId,
        int[] admins,
        int[] members
    )
    {
        await RoomAsync(factory, id + 500, ownerId);

        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        PlayerEntity owner = await db.Players.FirstAsync(p => p.Id == ownerId);
        RoomEntity room = await db.Rooms.FirstAsync(r => r.Id == id + 500);

        GroupEntity group = new()
        {
            Id = id,
            Name = "Les amis",
            Badge = "b0001",
            RoomEntityId = room.Id,
            OwnerPlayerEntityId = ownerId,
            Type = GroupType.Open,
            ColorOne = "1",
            ColorTwo = "2",
            AdminOnlyDecoration = false,
            RoomEntity = room,
            OwnerPlayerEntity = owner,
        };

        db.Groups.Add(group);

        foreach (
            (int playerId, GroupMemberRank rank) in admins
                .Select(a => (a, GroupMemberRank.Admin))
                .Concat(members.Select(m => (m, GroupMemberRank.Member)))
        )
        {
            db.GroupMembers.Add(
                new GroupMemberEntity
                {
                    GroupEntityId = id,
                    PlayerEntityId = playerId,
                    Rank = rank,
                    GroupEntity = group,
                    PlayerEntity = await db.Players.FirstAsync(p => p.Id == playerId),
                }
            );
        }

        await db.SaveChangesAsync();
    }

    private static async Task TicketAsync(
        WebApiTestFactory factory,
        int id,
        int reporter,
        bool closed
    )
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        db.CfhTickets.Add(
            new CfhTicketEntity
            {
                Id = id,
                State = closed ? CfhTicketState.Closed : CfhTicketState.Open,
                CfhTopicEntityId = 1,
                ReporterPlayerEntityId = reporter,
                Message = "au secours",
            }
        );

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A room, spelled out in full because <c>RoomEntity</c> requires most of its settings. Only
    /// <c>PlayerEntityId</c> matters here — it is the column the handlers look the owner up by.
    /// </summary>
    private static async Task RoomAsync(WebApiTestFactory factory, int id, int ownerId)
    {
        await using VortexDbContext db = await factory.DbContexts.CreateDbContextAsync();

        RoomModelEntity model = new()
        {
            Id = id + 1000,
            Name = $"model-{id}",
            Model = "0",
            DoorX = 0,
            DoorY = 0,
            DoorRotation = Rotation.North,
            Enabled = true,
            Custom = false,
        };

        // Both navigation properties are `required`, so the owner has to be the tracked instance
        // rather than just its id — seeded by PlayerAsync in an earlier context, loaded back here.
        PlayerEntity owner = await db.Players.FirstAsync(p => p.Id == ownerId);

        db.RoomModels.Add(model);
        db.Rooms.Add(
            new RoomEntity
            {
                Id = id,
                Name = "Chez moi",
                PlayerEntityId = ownerId,
                PlayerEntity = owner,
                RoomModelEntity = model,
                DoorMode = RoomDoorModeType.Open,
                RoomModelEntityId = model.Id,
                UsersNow = 0,
                PlayersMax = 50,
                WallHeight = -1,
                HideWalls = false,
                ThicknessWall = RoomThicknessType.Normal,
                ThicknessFloor = RoomThicknessType.Normal,
                AllowBlocking = false,
                AllowPets = false,
                AllowPetsEat = false,
                TradeType = RoomTradeModeType.Disabled,
                MuteType = ModSettingType.Owner,
                KickType = ModSettingType.Owner,
                BanType = ModSettingType.Owner,
                ChatModeType = ChatModeType.FreeFlow,
                ChatBubbleType = ChatBubbleWidthType.Normal,
                ChatSpeedType = ChatScrollSpeedType.Normal,
                ChatFloodType = ChatFloodSensitivityType.Minimal,
                ChatDistance = 50,
                Score = 0,
                IsStaffPick = false,
            }
        );

        await db.SaveChangesAsync();
    }
}
