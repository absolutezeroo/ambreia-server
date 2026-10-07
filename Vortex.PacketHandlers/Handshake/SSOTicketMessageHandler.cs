using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Vortex.Messages.Registry;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Fishing;
using Vortex.Primitives.Inventory.Snapshots;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Navigator;
using Vortex.Primitives.Networking;
using Vortex.Primitives.Orleans;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Permissions;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Players.Enums.Wallet;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Players.Wallet;
using Vortex.Protocol.Messages.Incoming.Handshake;
using Vortex.Protocol.Messages.Outgoing.Availability;
using Vortex.Protocol.Messages.Outgoing.Callforhelp;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Protocol.Messages.Outgoing.Collectibles;
using Vortex.Protocol.Messages.Outgoing.Fishing;
using Vortex.Protocol.Messages.Outgoing.Handshake;
using Vortex.Protocol.Messages.Outgoing.Inventory.Achievements;
using Vortex.Protocol.Messages.Outgoing.Inventory.Avatareffect;
using Vortex.Protocol.Messages.Outgoing.Inventory.Clothing;
using Vortex.Protocol.Messages.Outgoing.Inventory.Purse;
using Vortex.Protocol.Messages.Outgoing.Moderation;
using Vortex.Protocol.Messages.Outgoing.Navigator;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Perk;
using Vortex.Protocol.Messages.Outgoing.Room.Engine;
using Vortex.Protocol.Messages.Outgoing.Users;

namespace Vortex.PacketHandlers.Handshake;

public class SSOTicketMessageHandler(
    IAuthenticationService authService,
    ISessionGateway sessionGateway,
    IGrainFactory grainFactory,
    IPermissionService permissionService,
    ICfhTicketService cfhTickets,
    ILogger<SSOTicketMessageHandler> logger
) : IMessageHandler<SSOTicketMessage>
{
    private static readonly ImmutableArray<string> DefaultMessageTemplates =
    [
        "Please mind your language.",
        "This behaviour is not tolerated on this hotel.",
        "Please treat other users with respect.",
    ];

    private readonly IAuthenticationService _authService = authService;
    private readonly ISessionGateway _sessionGateway = sessionGateway;
    private readonly IGrainFactory _grainFactory = grainFactory;
    private readonly IPermissionService _permissionService = permissionService;
    private readonly ICfhTicketService _cfhTickets = cfhTickets;
    private readonly ILogger<SSOTicketMessageHandler> _logger = logger;

    public async ValueTask HandleAsync(
        SSOTicketMessage message,
        MessageContext ctx,
        CancellationToken ct
    )
    {
        try
        {
            string ticket = message.SSO;
            int playerId = await _authService
                .GetPlayerIdFromTicketAsync(ticket, ctx.RemoteIpAddress, ct)
                .ConfigureAwait(false);

            if (playerId <= 0)
            {
                await ctx.CloseSessionAsync().ConfigureAwait(false);

                return;
            }

            DateTime? banExpiry = await _grainFactory
                .GetPlayerGrain(PlayerId.Parse(playerId))
                .GetActiveBanExpiryAsync(ct)
                .ConfigureAwait(false);

            if (banExpiry is not null)
            {
                string banMessage = SanctionDuration.IsPermanent(banExpiry.Value)
                    ? "You have been permanently banned from this hotel."
                    : $"You are banned until {banExpiry.Value:yyyy-MM-dd HH:mm} UTC.";

                await ctx.SendComposerAsync(
                        new UserBannedMessageComposer { Message = banMessage },
                        ct
                    )
                    .ConfigureAwait(false);
                await ctx.CloseSessionAsync().ConfigureAwait(false);

                return;
            }

            await _sessionGateway
                .AddSessionToPlayerAsync(ctx.SessionKey, playerId, ct)
                .ConfigureAwait(false);

            // Stamped before anything else that can fail: the mod tool's "minutes since last login"
            // is only meaningful if every successful handshake records one. It is the one call in
            // this handler that writes, so it stays on its own ahead of the batch below rather than
            // joining it — otherwise any one of fifteen reads throwing would cost the stamp.
            bool isFirstLoginOfDay = await _grainFactory
                .GetPlayerGrain(PlayerId.Parse(playerId))
                .MarkLoggedInAsync(ct)
                .ConfigureAwait(false);

            // Everything the rest of this handler needs, asked for at once.
            //
            // These reads have no dependency on each other -- effects do not need the home room,
            // the wallet does not need the mystery box -- and each is a grain call that activates a
            // grain and reads the database. Awaited one after another they stacked into a login
            // slow enough that a hundred arrivals over ten seconds produced ten-second round trips
            // for everybody already in the room, and starved the room's own tick down to half rate.
            // The measurement is in logs/benchmark: the hotel carried those hundred players at two
            // milliseconds once they were in. It was the arriving that hurt.
            //
            // Issued together, awaited once. Calls to one grain still run one at a time inside it --
            // that is what a grain is -- but they no longer pay a round trip each, and calls to
            // different grains genuinely overlap. Nothing below this changes the order a single
            // composer goes out in: the client's login sequence is a protocol, not an implementation
            // detail, and it is untouched.
            IPlayerGrain player = _grainFactory.GetPlayerGrain(PlayerId.Parse(playerId));
            IPlayerWalletGrain wallet = _grainFactory.GetPlayerWalletGrain(playerId);

            Task<MembershipSnapshot> subTask = player.GetMembershipAsync(ct);
            Task<PermissionSet> permissionsTask = _permissionService.ResolveForPlayerAsync(
                playerId,
                ct
            );
            Task<bool> nuxTask = player.IsNuxCompletedAsync(ct);
            Task<PlayerSummarySnapshot> summaryTask = player.GetSummaryAsync(ct);
            Task<int> homeRoomTask = _grainFactory
                .GetPlayerNavigatorGrain(playerId)
                .GetHomeRoomIdAsync(ct);
            Task<ImmutableArray<AvatarEffectSnapshot>> effectsTask = _grainFactory
                .GetPlayerEffectGrain(playerId)
                .GetEffectsAsync(ct);
            // The ticket's player id, like every other read here. ctx.PlayerId is -1 for the whole
            // of this handler -- the MessageContext was built before the session was bound to a
            // player -- so this one line was asking the clothing grain of a player that does not
            // exist, and every returning player logged in with an empty wardrobe.
            Task<PlayerClothingSnapshot> clothingTask = _grainFactory
                .GetPlayerClothingGrain(playerId)
                .GetUnlockedAsync(ct);
            Task<int> creditsTask = wallet.GetAmountForCurrencyAsync(
                new CurrencyKind { CurrencyType = CurrencyType.Credits },
                ct
            );
            Task<Dictionary<int, int>> activityPointsTask = wallet.GetActivityPointsAsync(ct);
            Task<int> silverTask = wallet.GetAmountForCurrencyAsync(
                new CurrencyKind { CurrencyType = CurrencyType.Silver },
                ct
            );
            Task<int> emeraldsTask = wallet.GetAmountForCurrencyAsync(
                new CurrencyKind { CurrencyType = CurrencyType.Emeralds },
                ct
            );

            await Task.WhenAll(
                    subTask,
                    permissionsTask,
                    nuxTask,
                    summaryTask,
                    homeRoomTask,
                    effectsTask,
                    clothingTask,
                    creditsTask,
                    activityPointsTask,
                    silverTask,
                    emeraldsTask
                )
                .ConfigureAwait(false);

            MembershipSnapshot sub = await subTask.ConfigureAwait(false);

            MembershipLevelType membershipLevel = sub.IsActive
                ? (sub.IsVip ? MembershipLevelType.Vip : MembershipLevelType.Membership)
                : MembershipLevelType.None;

            PermissionSet permissions = await permissionsTask.ConfigureAwait(false);

            SecurityLevelType securityLevel = SecurityLevelPolicy.Resolve(permissions);

            bool nuxCompleted = await nuxTask.ConfigureAwait(false);
            int currentHomeRoomId = await homeRoomTask.ConfigureAwait(false);

            // The client's new-user flow is driven entirely by these actions: 0 asks it to run the
            // look-and-name onboarding, 1 to pick a starter room. Both are read once, here — an
            // empty array means the player goes straight to the hotel view.
            // (WIN63 HabboLandingView.as::isOnboardingRequired(), and OnBoardingHcFlow's
            // AVATAR_NAME_CHANGE / NEW_ROOM_SELECT constants.)
            List<short> suggestedLoginActions = [];

            if (!nuxCompleted)
            {
                suggestedLoginActions.Add(SuggestedLoginAction.AvatarNameChange);
            }

            if (currentHomeRoomId <= 0)
            {
                suggestedLoginActions.Add(SuggestedLoginAction.NewRoomSelect);
            }

            await ctx.SendComposerAsync(
                    new AuthenticationOKMessage
                    {
                        AccountId = playerId,
                        SuggestedLoginActions = [.. suggestedLoginActions],
                        IdentityId = playerId,
                    },
                    ct
                )
                .ConfigureAwait(false);
            ImmutableArray<AvatarEffectSnapshot> effects = await effectsTask.ConfigureAwait(false);
            await ctx.SendComposerAsync(new AvatarEffectsMessageComposer { Effects = effects }, ct)
                .ConfigureAwait(false);
            int homeRoomId = currentHomeRoomId;

            await ctx.SendComposerAsync(
                    new NavigatorSettingsMessageComposer
                    {
                        HomeRoomId = homeRoomId,
                        RoomIdToEnter = 0,
                    },
                    ct
                )
                .ConfigureAwait(false);
            // Favourites are gone, but the client still reads this message at login: an empty list and
            // a limit of 0 (frozen), so it shows "favourites full" instead of offering an add that no
            // handler answers.
            await ctx.SendComposerAsync(
                    new FavouritesMessageComposer { Limit = 0, FavoriteRoomIds = [] },
                    ct
                )
                .ConfigureAwait(false);
            // unseen items
            // The clothing the account has unlocked. This used to be two empty arrays, which told
            // every player at every login that they own no wearable sets -- the avatar editor then
            // greys out everything they have ever redeemed, and a clothing furni they have already
            // bound asks to be redeemed a second time.
            PlayerClothingSnapshot clothing = await clothingTask.ConfigureAwait(false);

            await ctx.SendComposerAsync(
                    new FigureSetIdsEventMessageComposer
                    {
                        FigureSetIds = clothing.FigureSetIds,
                        BoundFurnitureNames = clothing.BoundFurnitureNames,
                    },
                    ct
                )
                .ConfigureAwait(false);
            // SessionDataManager.isNoob is `level != 0`, and the client softens several surfaces on
            // it (RoomUI's noob room handling, the avatar info card). Whether somebody is new is
            // exactly what the NUX flag already read above says, so this stops claiming every
            // account is a veteran on its first second.
            await ctx.SendComposerAsync(
                    new NoobnessLevelMessage
                    {
                        NoobnessLevel = nuxCompleted
                            ? NoobnessLevelType.NotNoob
                            : NoobnessLevelType.Noob,
                    },
                    ct
                )
                .ConfigureAwait(false);
            await ctx.SendComposerAsync(
                    new UserRightsMessage
                    {
                        MembershipLevel = membershipLevel,
                        SecurityLevel = securityLevel,
                        IsAmbassador = false,
                    },
                    ct
                )
                .ConfigureAwait(false);

            if (
                permissions.HasAny(
                    Capabilities.Moderation.Cfh,
                    Capabilities.Moderation.Chatlogs,
                    Capabilities.Moderation.Kick,
                    Capabilities.Moderation.Mute,
                    Capabilities.Moderation.Alert,
                    Capabilities.Moderation.Ban,
                    Capabilities.Room.ModerateAny
                )
            )
            {
                await SendModeratorBootstrapAsync(ctx, playerId, permissions, ct)
                    .ConfigureAwait(false);
            }

            // Vortex-specific: tells the client whether to offer the in-client furni editor's
            // button. Sent unconditionally, including the false case, so the client never has to
            // infer the answer from a missing packet. It is a UI hint only — both furni-editor
            // handlers re-check the capability on every request.
            await ctx.SendComposerAsync(
                    new VortexFurniEditorRightsMessageComposer
                    {
                        CanEdit = permissions.Has(Capabilities.Room.FurniEdit),
                    },
                    ct
                )
                .ConfigureAwait(false);

            if (sub.IsActive)
            {
                await ctx.SendComposerAsync(BuildScrSendUserInfo(sub), ct).ConfigureAwait(false);
            }

            if (sub.GiftsAvailable > 0)
            {
                await ctx.SendComposerAsync(
                        new ClubGiftNotificationEventMessageComposer
                        {
                            GiftsAvailable = sub.GiftsAvailable,
                        },
                        ct
                    )
                    .ConfigureAwait(false);
            }

            int credits = await creditsTask.ConfigureAwait(false);
            Dictionary<int, int> activityPoints = await activityPointsTask.ConfigureAwait(false);

            await ctx.SendComposerAsync(
                    new CreditBalanceEventMessageComposer { Balance = $"{credits}.0" },
                    ct
                )
                .ConfigureAwait(false);
            await ctx.SendComposerAsync(
                    new ActivityPointsMessageComposer { PointsByCategoryId = activityPoints },
                    ct
                )
                .ConfigureAwait(false);

            // Silver and emeralds go out here with the credits rather than being left to the
            // client to ask for. It only asks once, while its inventory component is starting up,
            // and it keeps the answer in the catalogue purse for the whole session -- so a player
            // who opens the Collectors Guild before their inventory would read both as zero, with a
            // wallet that actually holds thousands.
            int silver = await silverTask.ConfigureAwait(false);
            int emeralds = await emeraldsTask.ConfigureAwait(false);

            await ctx.SendComposerAsync(
                    new SilverBalanceMessageComposer { SilverBalance = silver },
                    ct
                )
                .ConfigureAwait(false);
            await ctx.SendComposerAsync(
                    new EmeraldBalanceMessageComposer { EmeraldBalance = emeralds },
                    ct
                )
                .ConfigureAwait(false);

            await ctx.SendComposerAsync(
                    new AvailabilityStatusMessageComposer
                    {
                        IsOpen = true,
                        OnShutDown = false,
                        IsAuthenticHabbo = true,
                    },
                    ct
                )
                .ConfigureAwait(false);
            await ctx.SendComposerAsync(new InfoFeedEnableMessageComposer { Enabled = true }, ct)
                .ConfigureAwait(false);
            // The score the toolbar badge shows. It is already maintained (PlayerAchievementGrain
            // adds to it) and already sent on the profile and on the room avatar -- login was the
            // one place that reported zero, so the badge read empty until a profile was opened.
            PlayerSummarySnapshot summary = await summaryTask.ConfigureAwait(false);

            await ctx.SendComposerAsync(
                    new AchievementsScoreEventMessageComposer { Score = summary.AchievementScore },
                    ct
                )
                .ConfigureAwait(false);
            await ctx.SendComposerAsync(
                    new IsFirstLoginOfDayMessage { IsFirstLoginOfDay = isFirstLoginOfDay },
                    ct
                )
                .ConfigureAwait(false);
            PlayerPerkFlags perks = await _grainFactory
                .GetPlayerGrain(playerId)
                .GetPerksAsync(ct)
                .ConfigureAwait(false);

            await ctx.SendComposerAsync(
                    new PerkAllowancesMessageComposer { Perks = BuildPerkAllowances(perks) },
                    ct
                )
                .ConfigureAwait(false);

            await SendFishingBootstrapAsync(ctx, playerId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CloseSessionSafelyAsync(ctx).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to complete SSO handshake for session {SessionKey}",
                ctx.SessionKey
            );

            await CloseSessionSafelyAsync(ctx).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The fishing tables and where this player stands in them. Vortex-specific: no AS3 or Habbo
    /// equivalent — see the client's <c>docs/vortex-original/fishing.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sent here rather than answered on request because the client never asks: it holds no fishing
    /// state of its own, and a spot panel opened before the tables arrived would have no zone name
    /// and no level gate to show.
    /// </para>
    /// <para>
    /// The player grain owns the state and records push, so a player who logs in and a player who
    /// just landed a fish receive byte-identical messages.
    /// </para>
    /// <para>
    /// <strong>Takes <paramref name="playerId"/> rather than reading <c>ctx.PlayerId</c>.</strong>
    /// The context is built once when the packet is dispatched, and an SSO packet arrives on a
    /// session that is not bound to a player yet — so <c>ctx.PlayerId</c> is <c>-1</c> for the whole
    /// of this handler, even after <c>AddSessionToPlayerAsync</c>. Every other call in here already
    /// uses the local id for that reason; this one did not, and it pushed a fishing state to grain
    /// -1 while the real player received nothing. It left a <c>fishing_player_state</c> row for
    /// player -1 behind, which is how it was caught.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The hotel's perk defaults, with anything this account has been granted turned on over the
    /// top.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defaults are what every account gets without earning anything, and they are unchanged
    /// from when this list was written out by hand. What is new is the second half: a perk the
    /// player holds on their account is allowed even when the default says no, which is what makes
    /// <see cref="PlayerPerkFlags"/> an entitlement rather than a column nobody reads.
    /// </para>
    /// <para>
    /// A granted perk never turns one off — the flags are additive by construction, so an operator
    /// cannot revoke a default here by accident.
    /// </para>
    /// </remarks>
    private static ImmutableArray<PerkAllowanceItem> BuildPerkAllowances(PlayerPerkFlags perks)
    {
        (string Code, PlayerPerkFlags Flag, string Error, bool AllowedByDefault)[] defaults =
        [
            (
                "NAVIGATOR_ROOM_THUMBNAIL_CAMERA",
                PlayerPerkFlags.NavigatorRoomThumbnailCamera,
                "",
                false
            ),
            (
                "JUDGE_CHAT_REVIEWS",
                PlayerPerkFlags.JudgeChatReviews,
                "requirement.unfulfilled.helper_level_6",
                false
            ),
            ("MOUSE_ZOOM", PlayerPerkFlags.MouseZoom, "", true),
            ("HABBO_CLUB_OFFER_BETA", PlayerPerkFlags.MembershipOfferBeta, "", true),
            ("TRADE", PlayerPerkFlags.Trade, "requirement.unfulfilled.citizenship_level_3", true),
            ("CAMERA", PlayerPerkFlags.Camera, "", false),
            ("NAVIGATOR_PHASE_TWO_2014", PlayerPerkFlags.NavigatorPhaseTwo2014, "", true),
            (
                "BUILDER_AT_WORK",
                PlayerPerkFlags.BuilderAtWork,
                "requirement.unfulfilled.group_membership",
                false
            ),
            ("CALL_ON_HELPERS", PlayerPerkFlags.CallOnHelpers, "", true),
            ("CITIZEN", PlayerPerkFlags.Citizen, "", true),
            (
                "USE_GUIDE_TOOL",
                PlayerPerkFlags.UseGuideTool,
                "requirement.unfulfilled.helper_level_4",
                false
            ),
            (
                "VOTE_IN_COMPETITIONS",
                PlayerPerkFlags.VoteInCompetitions,
                "requirement.unfulfilled.helper_level_2",
                false
            ),
        ];

        // The camera and the room-thumbnail camera were removed by this fork (M0), the guide tool,
        // the call on helpers and the chat reviews with the guides (M0 bis). Still listed, so the
        // client's perk list keeps its shape, but never allowed -- not even by a grant -- since
        // nothing on the server answers what they unlock.
        const PlayerPerkFlags removedPerks =
            PlayerPerkFlags.Camera
            | PlayerPerkFlags.NavigatorRoomThumbnailCamera
            | PlayerPerkFlags.UseGuideTool
            | PlayerPerkFlags.CallOnHelpers
            | PlayerPerkFlags.JudgeChatReviews;

        ImmutableArray<PerkAllowanceItem>.Builder builder =
            ImmutableArray.CreateBuilder<PerkAllowanceItem>(defaults.Length);

        foreach (
            (string code, PlayerPerkFlags flag, string error, bool allowedByDefault) in defaults
        )
        {
            bool allowed = (allowedByDefault || perks.HasFlag(flag)) && (flag & removedPerks) == 0;

            builder.Add(
                new PerkAllowanceItem
                {
                    Code = code,
                    ErrorMessage = allowed ? string.Empty : error,
                    IsAllowed = allowed,
                }
            );
        }

        return builder.MoveToImmutable();
    }

    private async Task SendFishingBootstrapAsync(
        MessageContext ctx,
        int playerId,
        CancellationToken ct
    )
    {
        // A session grain is keyed by player and outlives the connection, so anyone who dropped
        // mid-cast still has one open and would be refused with "already fishing" on their way back
        // in. Dropped here because logging in is the one moment the answer is certain.
        await _grainFactory
            .GetFishingSessionGrain(new PlayerId(playerId))
            .AbandonAsync(ct)
            .ConfigureAwait(false);

        FishingDefinitionsSnapshot definitions = await _grainFactory
            .GetFishingDefinitionsGrain()
            .GetDefinitionsAsync(ct)
            .ConfigureAwait(false);

        await ctx.SendComposerAsync(
                new VortexFishingDefinitionsMessageComposer
                {
                    Version = definitions.Version,
                    Species = definitions.Species,
                    RodLevels = definitions.RodTiers,
                    FishingLevels = definitions.Levels,
                    Zones = definitions.Zones,
                },
                ct
            )
            .ConfigureAwait(false);

        // Zero, not a session count: nobody is fishing at the moment they log in.
        await _grainFactory
            .GetFishingPlayerGrain(PlayerId.Parse(playerId))
            .PushStateAsync(0, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Pushes the staff mod tool's bootstrap payload proactively at login, matching the
    /// real client: nothing in the WIN63 source ever requests ModeratorInit/CfhTopicsInit — the
    /// server just sends them to whoever has moderation rights.</summary>
    private async Task SendModeratorBootstrapAsync(
        MessageContext ctx,
        int playerId,
        PermissionSet permissions,
        CancellationToken ct
    )
    {
        ImmutableArray<CfhIssueQueueEntrySnapshot> issues = await _cfhTickets
            .GetOpenQueueAsync(ct)
            .ConfigureAwait(false);
        ImmutableArray<CfhCategorySnapshot> catalog = await _cfhTickets
            .GetCatalogAsync(ct)
            .ConfigureAwait(false);

        bool alertPermission = permissions.HasAny(Capabilities.Moderation.Alert);
        bool kickPermission = permissions.HasAny(Capabilities.Moderation.Kick);

        await ctx.SendComposerAsync(
                new ModeratorInitMessageComposer
                {
                    Issues = issues,
                    MessageTemplates = DefaultMessageTemplates,
                    CfhPermission = permissions.HasAny(Capabilities.Moderation.Cfh),
                    ChatlogsPermission = permissions.HasAny(Capabilities.Moderation.Chatlogs),
                    AlertPermission = alertPermission,
                    KickPermission = kickPermission,
                    BanPermission = permissions.HasAny(Capabilities.Moderation.Ban),
                    RoomAlertPermission = alertPermission,
                    RoomKickPermission = kickPermission,
                    RoomMessageTemplates = DefaultMessageTemplates,
                },
                ct
            )
            .ConfigureAwait(false);
        await ctx.SendComposerAsync(new CfhTopicsInitMessageComposer { Categories = catalog }, ct)
            .ConfigureAwait(false);

        // They now hold a snapshot of the queue, so from here on they need the changes to it. The
        // matching Unsubscribe hangs off the disconnect event, not off this handler.
        await _grainFactory
            .GetModerationQueueGrain()
            .SubscribeAsync(PlayerId.Parse(playerId))
            .ConfigureAwait(false);

        // Restore where they left the window. Skipped when never positioned — a rectangle of zeroes
        // would collapse the tool to nothing on open.
        // Uses the ticket's player id, never ctx.PlayerId: the MessageContext for this packet was
        // built before the handler bound the session to a player, so ctx.PlayerId is still -1 here.
        PlayerModToolPreferencesSnapshot modToolPreferences = await _grainFactory
            .GetPlayerGrain(PlayerId.Parse(playerId))
            .GetModToolPreferencesAsync(ct)
            .ConfigureAwait(false);

        if (modToolPreferences.IsSet)
        {
            await ctx.SendComposerAsync(
                    new ModeratorToolPreferencesEventMessageComposer
                    {
                        WindowX = modToolPreferences.WindowX,
                        WindowY = modToolPreferences.WindowY,
                        WindowWidth = modToolPreferences.WindowWidth,
                        WindowHeight = modToolPreferences.WindowHeight,
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
    }

    private async Task CloseSessionSafelyAsync(MessageContext ctx)
    {
        try
        {
            await ctx.CloseSessionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to close session {SessionKey} after SSO handshake failure",
                ctx.SessionKey
            );
        }
    }

    private static ScrSendUserInfoMessageComposer BuildScrSendUserInfo(MembershipSnapshot sub)
    {
        int daysLeft = sub.DaysLeft;
        int rem = daysLeft % 31;

        return new ScrSendUserInfoMessageComposer
        {
            ProductName = "habbo_club",
            DaysToPeriodEnd = rem == 0 ? 31 : rem,
            MemberPeriods = sub.TotalMonths,
            PeriodsSubscribedAhead = daysLeft / 31 - (rem == 0 ? 1 : 0),
            ResponseType = 2,
            HasEverBeenMember = sub.TotalMonths > 0 || sub.IsActive,
            IsVIP = sub.IsVip,
            PastMembershipDays = sub.PastMembershipDays,
            PastVipDays = sub.PastVipDays,
            MinutesUntilExpiration = sub.IsActive
                ? (int)(sub.ExpiresAt - DateTime.UtcNow).TotalMinutes
                : 0,
            MinutesSinceLastModified = 0,
        };
    }
}
