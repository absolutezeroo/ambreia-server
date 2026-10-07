using Orleans;
using Vortex.Primitives.Badges.Grains;
using Vortex.Primitives.Catalog.Grains;
using Vortex.Primitives.Fishing.Grains;
using Vortex.Primitives.FriendList.Grains;
using Vortex.Primitives.Groups.Grains;
using Vortex.Primitives.Inventory.Grains;
using Vortex.Primitives.Marketplace.Grains;
using Vortex.Primitives.Moderation.Grains;
using Vortex.Primitives.Players;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Prizes.Grains;
using Vortex.Primitives.Quests.Grains;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Server.Grains;

namespace Vortex.Primitives.Orleans;

public static class GrainFactoryExtensions
{
    /// <summary>The whole room surface. Prefer one of the <c>GetRoom*</c> facet accessors below;
    /// this is for the few call sites that genuinely span most of the room.</summary>
    public static IRoomGrain GetRoomGrain(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomGrain>((long)roomId.Value);

    // Facets of the room grain. Every one of these resolves to the same RoomGrain activation as
    // GetRoomGrain for the same room id -- RoomGrain is the only implementation of all of them, so
    // asking for a facet is not a different grain, just a narrower view of the same one. Take the
    // narrowest facet a call site actually needs: it makes the dependency legible and keeps the
    // blast radius of a room-interface change proportional to what really uses it.

    public static IRoomCore GetRoomCore(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomCore>((long)roomId.Value);

    public static IRoomAvatars GetRoomAvatars(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomAvatars>((long)roomId.Value);

    public static IRoomMap GetRoomMap(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomMap>((long)roomId.Value);

    public static IRoomFurni GetRoomFurni(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomFurni>((long)roomId.Value);

    public static IRoomPets GetRoomPets(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomPets>((long)roomId.Value);

    public static IRoomBots GetRoomBots(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomBots>((long)roomId.Value);

    public static IRoomSecurity GetRoomSecurity(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomSecurity>((long)roomId.Value);

    public static IRoomSettings GetRoomSettings(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomSettings>((long)roomId.Value);

    public static IRoomModeration GetRoomModeration(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomModeration>((long)roomId.Value);

    public static IRoomTrading GetRoomTrading(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomTrading>((long)roomId.Value);

    public static IRoomDoorbell GetRoomDoorbell(this IGrainFactory factory, RoomId roomId) =>
        factory.GetGrain<IRoomDoorbell>((long)roomId.Value);

    public static IRoomPersistenceGrain GetRoomPersistenceGrain(
        this IGrainFactory factory,
        RoomId roomId
    ) => factory.GetGrain<IRoomPersistenceGrain>((long)roomId.Value);

    public static IRoomDirectoryGrain GetRoomDirectoryGrain(this IGrainFactory factory) =>
        factory.GetGrain<IRoomDirectoryGrain>(SingletonGrainId.GLOBAL);

    /// <summary>The hotel's single CFH queue coordinator — one per hotel, hence the global key.</summary>
    public static IModerationQueueGrain GetModerationQueueGrain(this IGrainFactory factory) =>
        factory.GetGrain<IModerationQueueGrain>(SingletonGrainId.GLOBAL);

    public static IPlayerGrain GetPlayerGrain(this IGrainFactory factory, PlayerId playerId) =>
        factory.GetGrain<IPlayerGrain>((long)playerId.Value);

    public static IPlayerPresenceGrain GetPlayerPresenceGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerPresenceGrain>(playerId.Value);

    public static IPlayerPresenceGrain GetPlayerPresenceGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerPresenceGrain>(playerId);

    public static IPlayerDirectoryGrain GetPlayerDirectoryGrain(this IGrainFactory factory) =>
        factory.GetGrain<IPlayerDirectoryGrain>(SingletonGrainId.GLOBAL);

    public static IPlayerWalletGrain GetPlayerWalletGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerWalletGrain>(playerId.Value);

    public static IPlayerWalletGrain GetPlayerWalletGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerWalletGrain>(playerId);

    public static IInventoryGrain GetInventoryGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IInventoryGrain>(playerId.Value);

    public static IInventoryGrain GetInventoryGrain(this IGrainFactory factory, long playerId) =>
        factory.GetGrain<IInventoryGrain>(playerId);

    public static ICatalogPurchaseGrain GetCatalogPurchaseGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<ICatalogPurchaseGrain>(playerId.Value);

    public static ICatalogPurchaseGrain GetCatalogPurchaseGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<ICatalogPurchaseGrain>(playerId);

    public static IPlayerNavigatorGrain GetPlayerNavigatorGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerNavigatorGrain>(playerId.Value);

    public static IPlayerNavigatorGrain GetPlayerNavigatorGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerNavigatorGrain>(playerId);

    public static IMarketplacePurchaseGrain GetMarketplacePurchaseGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IMarketplacePurchaseGrain>(playerId.Value);

    public static IMarketplacePurchaseGrain GetMarketplacePurchaseGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IMarketplacePurchaseGrain>(playerId);

    public static IMarketplaceSearchGrain GetMarketplaceSearchGrain(this IGrainFactory factory) =>
        factory.GetGrain<IMarketplaceSearchGrain>(SingletonGrainId.GLOBAL);

    public static IPlayerVaultGrain GetPlayerVaultGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerVaultGrain>(playerId.Value);

    public static IPlayerVaultGrain GetPlayerVaultGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerVaultGrain>(playerId);

    public static IMessengerGrain GetMessengerGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IMessengerGrain>(playerId.Value);

    public static IMessengerGrain GetMessengerGrain(this IGrainFactory factory, long playerId) =>
        factory.GetGrain<IMessengerGrain>(playerId);

    public static IGroupGrain GetGroupGrain(this IGrainFactory factory, int groupId) =>
        factory.GetGrain<IGroupGrain>(groupId);

    public static IGroupDirectoryGrain GetGroupDirectoryGrain(this IGrainFactory factory) =>
        factory.GetGrain<IGroupDirectoryGrain>(SingletonGrainId.GLOBAL);

    public static IGroupForumGrain GetGroupForumGrain(this IGrainFactory factory, int groupId) =>
        factory.GetGrain<IGroupForumGrain>(groupId);

    public static IPlayerBadgeGrain GetPlayerBadgeGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerBadgeGrain>(playerId.Value);

    public static IPlayerBadgeGrain GetPlayerBadgeGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerBadgeGrain>(playerId);

    public static IPlayerEffectGrain GetPlayerEffectGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerEffectGrain>(playerId.Value);

    public static IPlayerEffectGrain GetPlayerEffectGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerEffectGrain>(playerId);

    public static IAchievementManagerGrain GetAchievementManagerGrain(this IGrainFactory factory) =>
        factory.GetGrain<IAchievementManagerGrain>(SingletonGrainId.GLOBAL);

    public static IServerConfigGrain GetServerConfigGrain(this IGrainFactory factory) =>
        factory.GetGrain<IServerConfigGrain>(SingletonGrainId.GLOBAL);

    public static IBadgeDirectoryGrain GetBadgeDirectoryGrain(this IGrainFactory factory) =>
        factory.GetGrain<IBadgeDirectoryGrain>(SingletonGrainId.GLOBAL);

    public static IPlayerAchievementGrain GetPlayerAchievementGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerAchievementGrain>(playerId.Value);

    public static IPlayerAchievementGrain GetPlayerAchievementGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerAchievementGrain>(playerId);

    public static IQuestManagerGrain GetQuestManagerGrain(this IGrainFactory factory) =>
        factory.GetGrain<IQuestManagerGrain>(SingletonGrainId.GLOBAL);

    public static IPlayerQuestGrain GetPlayerQuestGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerQuestGrain>(playerId.Value);

    public static IPlayerQuestGrain GetPlayerQuestGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerQuestGrain>(playerId);

    public static IPlayerDailyTaskGrain GetPlayerDailyTaskGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerDailyTaskGrain>(playerId);

    public static IPrizePoolManagerGrain GetPrizePoolManagerGrain(this IGrainFactory factory) =>
        factory.GetGrain<IPrizePoolManagerGrain>(SingletonGrainId.GLOBAL);

    public static IPlayerPrizeGrain GetPlayerPrizeGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerPrizeGrain>(playerId.Value);

    public static IPlayerPrizeGrain GetPlayerPrizeGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerPrizeGrain>(playerId);

    public static IPlayerClothingGrain GetPlayerClothingGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IPlayerClothingGrain>(playerId.Value);

    public static IPlayerClothingGrain GetPlayerClothingGrain(
        this IGrainFactory factory,
        long playerId
    ) => factory.GetGrain<IPlayerClothingGrain>(playerId);

    public static IFishingDefinitionsGrain GetFishingDefinitionsGrain(this IGrainFactory factory) =>
        factory.GetGrain<IFishingDefinitionsGrain>(SingletonGrainId.GLOBAL);

    public static IFishingPlayerGrain GetFishingPlayerGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IFishingPlayerGrain>(playerId.Value);

    // The session grain is keyed by player, not by spot: one player fishes one spot at a time, and
    // the timer and the rod multipliers belong to whoever is holding the rod.
    public static IFishingSessionGrain GetFishingSessionGrain(
        this IGrainFactory factory,
        PlayerId playerId
    ) => factory.GetGrain<IFishingSessionGrain>(playerId.Value);
}
