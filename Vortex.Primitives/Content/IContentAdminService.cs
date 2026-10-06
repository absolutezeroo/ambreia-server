using System.Threading;
using System.Threading.Tasks;

namespace Vortex.Primitives.Content;

/// <summary>
/// Writes for the content the dashboard's read surfaces describe: achievement ladders, bots and hand
/// items, and the economy's smaller tables.
/// <para>
/// One service rather than one per domain because the split would be arbitrary — each of these is a
/// handful of rows edited from one page, and every one of them lives behind the same
/// <c>dashboard.ops.content.manage</c> capability. What is <em>not</em> uniform is the live-cache
/// obligation, and each implementation documents its own: achievements reload their
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

    Task<ContentAdminResult> CreateCurrencyAsync(CurrencySpec spec, CancellationToken ct);

    Task<ContentAdminResult> UpdateCurrencyAsync(
        int currencyId,
        CurrencySpec spec,
        CancellationToken ct
    );

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
}
