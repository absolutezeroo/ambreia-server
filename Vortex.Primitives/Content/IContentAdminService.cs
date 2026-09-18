using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Content;

/// <summary>
/// Writes for the content the dashboard's read surfaces describe: achievement ladders, bots and hand
/// items, NFT collections, and the economy's smaller tables.
/// <para>
/// One service rather than one per domain because the split would be arbitrary — each of these is a
/// handful of rows edited from one page, and every one of them lives behind the same
/// <c>dashboard.ops.content.manage</c> capability. What is <em>not</em> uniform is the live-cache
/// obligation, and each implementation documents its own: achievements and collections reload their
/// manager grain, currencies reload the reference-data provider, hand items are read per use and
/// need nothing.
/// </para>
/// </summary>
public interface IContentAdminService
{
    Task<ContentAdminResult> CreateAchievementAsync(AchievementSpec spec, CancellationToken ct);

    Task<ContentAdminResult> UpdateAchievementAsync(
        int achievementId,
        AchievementSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteAchievementAsync(int achievementId, CancellationToken ct);

    /// <summary>Creates the level or overwrites the existing one with the same number.</summary>
    Task<ContentAdminResult> UpsertAchievementLevelAsync(
        int achievementId,
        AchievementLevelSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteAchievementLevelAsync(int levelId, CancellationToken ct);

    Task<ContentAdminResult> UpsertHandItemAsync(HandItemSpec spec, CancellationToken ct);

    Task<ContentAdminResult> DeleteHandItemAsync(int handItemId, CancellationToken ct);

    Task<ContentAdminResult> UpdateBotAsync(int botId, BotSpec spec, CancellationToken ct);

    Task<ContentAdminResult> DeleteBotAsync(int botId, CancellationToken ct);

    Task<ContentAdminResult> CreateCollectionAsync(NftCollectionSpec spec, CancellationToken ct);

    Task<ContentAdminResult> UpdateCollectionAsync(
        int collectionId,
        NftCollectionSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteCollectionAsync(int collectionId, CancellationToken ct);

    Task<ContentAdminResult> CreateCollectionItemAsync(
        int collectionId,
        NftCollectionItemSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> UpdateCollectionItemAsync(
        int itemId,
        NftCollectionItemSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteCollectionItemAsync(int itemId, CancellationToken ct);

    Task<ContentAdminResult> CreateStoreOfferAsync(NftStoreOfferSpec spec, CancellationToken ct);

    Task<ContentAdminResult> UpdateStoreOfferAsync(
        int offerId,
        NftStoreOfferSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteStoreOfferAsync(int offerId, CancellationToken ct);

    Task<ContentAdminResult> CreateMintableItemTypeAsync(
        NftMintableItemTypeSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> UpdateMintableItemTypeAsync(
        int typeId,
        NftMintableItemTypeSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteMintableItemTypeAsync(int typeId, CancellationToken ct);

    Task<ContentAdminResult> CreateMintTokenOfferAsync(
        NftMintTokenOfferSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> UpdateMintTokenOfferAsync(
        int offerId,
        NftMintTokenOfferSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteMintTokenOfferAsync(int offerId, CancellationToken ct);

    Task<ContentAdminResult> CreateClaimAsync(NftClaimSpec spec, CancellationToken ct);

    Task<ContentAdminResult> DeleteClaimAsync(int claimId, CancellationToken ct);

    Task<ContentAdminResult> CreateCurrencyAsync(CurrencySpec spec, CancellationToken ct);

    Task<ContentAdminResult> UpdateCurrencyAsync(
        int currencyId,
        CurrencySpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> UpsertBuildersClubTierAsync(
        BuildersClubTierSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteBuildersClubTierAsync(int tierId, CancellationToken ct);

    Task<ContentAdminResult> UpsertRentableSpaceTermsAsync(
        RentableSpaceTermsSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteRentableSpaceTermsAsync(int termsId, CancellationToken ct);

    /// <summary>Grants a badge to a player, live: the badge grain is told, not just the table.</summary>
    Task<ContentAdminResult> GrantBadgeAsync(int playerId, string badgeCode, CancellationToken ct);

    Task<ContentAdminResult> RevokeBadgeAsync(int playerId, string badgeCode, CancellationToken ct);

    Task<ContentAdminResult> GrantEffectAsync(
        int playerId,
        int effectId,
        int durationSeconds,
        CancellationToken ct
    );

    Task<ContentAdminResult> RevokeEffectAsync(int playerId, int effectId, CancellationToken ct);

    /// <summary>
    /// Unlocks a chat-bubble style for a player, live: the grain is told, and it re-sends the list
    /// the client filters its selector with.
    /// </summary>
    /// <remarks>
    /// <paramref name="clientStyleId"/> is the id in the client's own style library, not a row id.
    /// Only 1000..9999 can be unlocked — below that the client gates the style itself and never
    /// asks, above it reads a message this hotel does not compose.
    /// </remarks>
    Task<ContentAdminResult> GrantChatStyleAsync(
        int playerId,
        int clientStyleId,
        CancellationToken ct
    );

    Task<ContentAdminResult> RevokeChatStyleAsync(
        int playerId,
        int clientStyleId,
        CancellationToken ct
    );

    /// <summary>
    /// Takes one item back off a player: the counterpart of the item grant.
    /// </summary>
    /// <remarks>
    /// Refuses while the item stands in a room, which is that room's live state to own, and when
    /// the id does not belong to <paramref name="playerId"/> -- an id alone would let a typo delete
    /// a stranger's furniture.
    /// </remarks>
    Task<ContentAdminResult> RevokeFurnitureAsync(int playerId, int itemId, CancellationToken ct);

    /// <summary>Moves one item to another player; refused while it stands in a room.</summary>
    Task<ContentAdminResult> TransferFurnitureAsync(
        int playerId,
        int toPlayerId,
        int itemId,
        CancellationToken ct
    );

    /// <summary>
    /// Takes one item back and credits the player what the catalogue asks for it today. Refused for
    /// an item the catalogue does not sell, rather than paying nothing.
    /// </summary>
    Task<ContentAdminResult> RefundFurnitureAsync(int playerId, int itemId, CancellationToken ct);

    Task<ContentAdminResult> CreateNftAvatarAsync(NftAvatarSpec spec, CancellationToken ct);

    Task<ContentAdminResult> UpdateNftAvatarAsync(
        int avatarId,
        NftAvatarSpec spec,
        CancellationToken ct
    );

    Task<ContentAdminResult> DeleteNftAvatarAsync(int avatarId, CancellationToken ct);

    /// <summary>
    /// Hands a copy of an avatar to a player. This is the entire acquisition path: with no chain to
    /// hold ownership, the row this writes is what makes the avatar theirs.
    /// </summary>
    Task<ContentAdminResult> GrantNftAvatarAsync(
        int avatarId,
        int playerId,
        string note,
        CancellationToken ct
    );

    /// <summary>
    /// Takes a copy back. The copy's number is not reused and the edition does not grow back — a
    /// revoked one is a gap in the run, which is what a revoke should look like.
    /// </summary>
    Task<ContentAdminResult> RevokeNftAvatarAsync(int copyId, CancellationToken ct);
}
