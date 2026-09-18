using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Orleans;
using Vortex.Primitives.Commerce;
using Vortex.Primitives.Orleans.Snapshots.Players;
using Vortex.Primitives.Players.Enums;
using Vortex.Primitives.Rooms.Enums;

namespace Vortex.Primitives.Players.Grains;

public interface IPlayerGrain : IGrainWithIntegerKey
{
    public Task SetFigureAsync(string figure, AvatarGenderType gender, CancellationToken ct);
    public Task SetNameAsync(string name, CancellationToken ct);

    /// <summary>Whether the new-user flow has already been completed by this player.</summary>
    public Task<bool> IsNuxCompletedAsync(CancellationToken ct);

    /// <summary>
    /// Stamps the new-user flow as done, so the client stops being sent the
    /// <c>AVATAR_NAME_CHANGE</c> login action. Idempotent: a second call keeps the first stamp.
    /// </summary>
    public Task MarkNuxCompletedAsync(CancellationToken ct);
    public Task SetMottoAsync(string text, CancellationToken ct);

    /// <summary>
    /// Whether the owning account's safety lock is on. Answered from grain state, because every
    /// spending handler asks it on every purchase.
    /// </summary>
    public Task<bool> IsSafetyLockedAsync(CancellationToken ct);

    /// <summary>
    /// Tells a live player that the account's lock moved, after the website has written it.
    /// </summary>
    /// <remarks>
    /// Writes nothing: the account row has one owner, <c>IAccountSafetyLockService</c>, and this
    /// only refreshes the copy in state and tells the connected client. It is the half that matters
    /// most — a lock that waited for the next login would leave the thief connected and spending in
    /// the meantime.
    /// </remarks>
    public Task OnAccountSafetyLockChangedAsync(bool locked, CancellationToken ct);

    /// <summary>Persists the player's preferred chat-bubble style (SetChatStylePreference, header
    /// 2634). No-op when the style is unchanged so a repeated toggle doesn't touch the database.</summary>
    public Task SetChatStylePreferenceAsync(int chatStyle, CancellationToken ct);

    /// <summary>The player's persisted preferred chat-bubble style (0 = default). Surfaced back to the
    /// client in the account-preferences packet so the settings UI shows the saved selection.</summary>
    public Task<int> GetChatStylePreferenceAsync(CancellationToken ct);

    /// <summary>
    /// Every chat-bubble style this player has been given, as the client numbers them.
    /// </summary>
    /// <remarks>
    /// Answers <c>GetUserNftChatStyles</c>. The client keeps a style out of the selector unless its
    /// id came back in this list, so an empty answer is what a player with no unlocks should see —
    /// and, until this existed, what every player saw.
    /// </remarks>
    public Task<List<int>> GetOwnedChatStyleIdsAsync(CancellationToken ct);

    /// <summary>
    /// Sends both chat-style bands to this player, unasked.
    /// </summary>
    /// <remarks>
    /// The client rebuilds its style selector from scratch every time a room's chat input is
    /// created — <c>RoomChatInputView.createOrUpdateChatStylesView()</c> walks its style library and
    /// keeps every unlockable id out unless <c>hasNftChatStyle</c> says otherwise, and every
    /// purchasable one out unless <c>hasPurchasableChatStyle</c> does. Both sets live on
    /// <c>SessionDataManager</c> and are only ever filled by these two messages, so whatever is in
    /// them at that moment is the picker for the whole visit. The client asks once, at session init;
    /// a room entered before that answer lands builds the selector without them. That is why the
    /// official server repeats this on entry, and why we do.
    /// </remarks>
    public Task PushOwnedChatStylesAsync(CancellationToken ct);

    /// <summary>
    /// Gives the player the chat-bubble style the client knows as <paramref name="clientStyleId"/>,
    /// and pushes the refreshed list if they are connected.
    /// </summary>
    /// <remarks>
    /// Idempotent: a style already held is a no-op that still reports success, because every caller
    /// (a reward track step, an operator) can be retried.
    /// </remarks>
    /// <returns>False only when the id is one no unlock can reach — see the implementation.</returns>
    public Task<bool> GrantChatStyleAsync(int clientStyleId, CancellationToken ct);

    /// <summary>Takes the style back, and pushes the shortened list if the player is connected.</summary>
    /// <returns>False when the player did not have it.</returns>
    public Task<bool> RevokeChatStyleAsync(int clientStyleId, CancellationToken ct);

    /// <summary>
    /// Adjusts the player's persisted achievement score by <paramref name="delta"/> and returns the
    /// new total. Owned here (not by the achievement grain) because the score is cached in this
    /// grain's state and surfaced on the profile.
    /// </summary>
    public Task<int> AddAchievementScoreAsync(int delta, CancellationToken ct);

    /// <summary>
    /// Consumes one of the player's daily respect points if any remain (resetting the budget on a new
    /// day). Returns true if a respect could be given, false if the daily limit is reached.
    /// </summary>
    public Task<bool> TryGiveRespectAsync(int dailyLimit, CancellationToken ct);

    /// <summary>Increments the player's total received respect and returns the new total.</summary>
    public Task<int> AddRespectReceivedAsync(CancellationToken ct);

    public Task<PlayerSummarySnapshot> GetSummaryAsync(CancellationToken ct);

    /// <summary>
    /// Sets the guild whose badge this player's avatar displays (0 clears it), persists it, and
    /// re-badges the avatar in the room the player is currently standing in. Callers must have
    /// already checked that the player is a member of <paramref name="groupId"/>.
    /// </summary>
    public Task SetFavouriteGroupAsync(int groupId, CancellationToken ct);

    /// <summary>
    /// The profile of this player as seen by <paramref name="viewerId"/>. The viewer is a parameter
    /// because "is this my friend" and "have I already asked" are relationships, not properties of
    /// the player being looked at — they were hardcoded false for want of it.
    /// </summary>
    public Task<PlayerExtendedProfileSnapshot> GetExtendedProfileSnapshotAsync(
        PlayerId viewerId,
        CancellationToken ct
    );

    public Task<ClubSubscriptionSnapshot> GetClubSubscriptionAsync(CancellationToken ct);

    public Task<ClubPurchaseResult> PurchaseClubAsync(
        int months,
        bool isVip,
        int costCredits,
        CancellationToken ct
    );

    /// <summary>Extends the membership without taking payment -- a prize or a staff award. Applies
    /// the same bookkeeping as <see cref="PurchaseClubAsync"/> (streak, gifts, badges, kickback).</summary>
    /// <param name="operation">
    /// The operation the months belong to, or <see cref="CommerceOperationId.None"/> for a grant
    /// that answers to nobody. Named, the months and a receipt for them commit together, so the
    /// call can be repeated as often as a provider repeats its webhook and still add them once.
    /// Without that, a caller had to claim the step before calling — and a crash in between left
    /// the months owed with the step already marked done, which reads from the outside exactly like
    /// a delivery that happened.
    /// </param>
    public Task<ClubPurchaseResult> GrantClubMonthsAsync(
        int months,
        bool isVip,
        CommerceOperationId operation,
        CancellationToken ct
    );

    public Task<bool> TryConsumeClubGiftAsync(string productCode, CancellationToken ct);

    /// <summary>
    /// Turns a perk on for this account, persisted. Returns false when the player already had it,
    /// which is what makes it safe to call from a reward that may be claimed after a retry.
    /// </summary>
    /// <remarks>
    /// Perks are the hotel's entitlement mechanism — <see cref="PlayerPerkFlags.Trade"/> is the
    /// trading pass — so anything that unlocks a capability rather than handing over an object goes
    /// through here rather than growing a second notion of "this account may now do X".
    /// </remarks>
    public Task<bool> GrantPerkAsync(PlayerPerkFlags perk, CancellationToken ct);

    /// <summary>The perks this account holds.</summary>
    public Task<PlayerPerkFlags> GetPerksAsync(CancellationToken ct);
    public Task TrackCreditSpendAsync(int credits, CancellationToken ct);

    /// <summary>
    /// Suspends (or, with <paramref name="bannedUntil"/> null, lifts) the linked account's ability
    /// to log in. Returns false if this player has no linked account to ban (e.g. a system player).
    /// </summary>
    public Task<bool> ApplyAccountBanAsync(
        int actorPlayerId,
        DateTime? bannedUntil,
        string reason,
        CancellationToken ct
    );

    /// <summary>Locks (or, with <paramref name="lockedUntil"/> null, lifts) the linked account's
    /// ability to trade. Returns false if this player has no linked account.</summary>
    public Task<bool> ApplyTradingLockAsync(
        int actorPlayerId,
        DateTime? lockedUntil,
        CancellationToken ct
    );

    /// <summary>Null if not currently banned, else the account's active ban expiry (far-future = permanent).</summary>
    public Task<DateTime?> GetActiveBanExpiryAsync(CancellationToken ct);

    /// <summary>
    /// Applies (or, with <paramref name="mutedUntil"/> null, lifts) a hotel-wide chat mute. Unlike a
    /// room mute this follows the player between rooms, which is the whole point: the staff mod
    /// tool's mute is a sanction on the person, not a rule of one room.
    /// </summary>
    /// <returns>The expiry now in force, so the caller can push it to the room they are standing in.</returns>
    public Task<DateTime?> ApplyHotelMuteAsync(
        int actorPlayerId,
        DateTime? mutedUntil,
        CancellationToken ct
    );

    /// <summary>The account facts behind the staff mod tool's user card — including the email
    /// address and sanction counts, so callers must have checked a moderation capability first.</summary>
    public Task<PlayerModeratorInfoSnapshot> GetModeratorInfoAsync(CancellationToken ct);

    /// <summary>Stamps <c>last_login_at</c>. Called once per successful SSO handshake.</summary>
    /// <summary>Stamps this login; returns true when it is the first of the day.</summary>
    public Task<bool> MarkLoggedInAsync(CancellationToken ct);

    public Task<PlayerModToolPreferencesSnapshot> GetModToolPreferencesAsync(CancellationToken ct);

    public Task SetModToolPreferencesAsync(
        PlayerModToolPreferencesSnapshot preferences,
        CancellationToken ct
    );

    public Task<PlayerWiredPreferencesSnapshot> GetWiredPreferencesAsync(CancellationToken ct);

    public Task SetWiredPreferencesAsync(
        PlayerWiredPreferencesSnapshot preferences,
        CancellationToken ct
    );

    /// <summary>The player's persisted account preferences (volumes, chat/camera/invite toggles, UI
    /// flags) surfaced back to the client in the account-preferences packet on login so the settings
    /// dialog shows the saved selection.</summary>
    public Task<PlayerAccountPreferencesSnapshot> GetAccountPreferencesAsync(CancellationToken ct);

    /// <summary>Persists the three audio volumes (SetSoundSettings, header 3662). Values are clamped
    /// to 0..100.</summary>
    public Task SetSoundSettingsAsync(
        int uiVolume,
        int furniVolume,
        int traxVolume,
        CancellationToken ct
    );

    /// <summary>Persists whether free-flow (bubble) chat is disabled (SetChatPreferences, header 1149).</summary>
    public Task SetFreeFlowChatDisabledAsync(bool disabled, CancellationToken ct);

    /// <summary>Persists the chat dialog's three display settings, which ride the same message as
    /// the free-flow flag (SetChatPreferences, header 1149).</summary>
    public Task SetChatDisplayPreferencesAsync(
        int chatMode,
        int bubbleWidth,
        int scrollSpeed,
        CancellationToken ct
    );

    /// <summary>Persists the chat font size step (SetChatStylePreference, header 2634).</summary>
    public Task SetChatSizePreferenceAsync(int sizePreference, CancellationToken ct);

    /// <summary>The player's personal chat word filter, in insertion order (GetCustomFilter, header 801).</summary>
    public Task<ImmutableArray<string>> GetWordFilterAsync(CancellationToken ct);

    /// <summary>Adds a word and answers whether the filter now contains it (AddToCustomFilter, header 2656).</summary>
    public Task<bool> AddWordFilterAsync(string word, CancellationToken ct);

    /// <summary>Removes a word and answers whether it was there to remove (RemoveFromCustomFilter, header 2209).</summary>
    public Task<bool> RemoveWordFilterAsync(string word, CancellationToken ct);

    /// <summary>Persists whether incoming room invites are ignored (SetIgnoreRoomInvites, header 1332).</summary>
    public Task SetRoomInvitesIgnoredAsync(bool ignored, CancellationToken ct);

    /// <summary>Persists whether the room camera stops following the avatar (SetRoomCameraPreferences,
    /// header 3917).</summary>
    public Task SetRoomCameraFollowDisabledAsync(bool disabled, CancellationToken ct);

    /// <summary>Persists the client UI-flags bitmask, e.g. expanded friend bar / room tools
    /// (SetUIFlags, header 3653).</summary>
    public Task SetUiFlagsAsync(int flags, CancellationToken ct);

    /// <summary>Persists the four Discord Rich Presence toggles and the version of the consent dialog
    /// the player answered (SetDiscordPreferences, header 2304). The version comes from the client's
    /// <c>discord_activity.settings.version</c> and is stored verbatim: it is what decides whether the
    /// opt-in popup shows again.</summary>
    public Task SetDiscordPreferencesAsync(
        int version,
        bool showHabbo,
        bool shareActivity,
        bool hideInHiddenRooms,
        bool allowJoining,
        CancellationToken ct
    );

    /// <summary>All the player's saved avatar-editor wardrobe outfits, ordered by slot, echoed to the
    /// client on GetWardrobe (header 2210).</summary>
    public Task<List<PlayerWardrobeOutfitSnapshot>> GetWardrobeAsync(CancellationToken ct);

    /// <summary>Upserts one wardrobe slot's figure + gender (SaveWardrobeOutfit, header 116).
    /// Out-of-range slot ids are ignored.</summary>
    public Task SaveWardrobeOutfitAsync(
        int slotId,
        string figure,
        string gender,
        CancellationToken ct
    );
}
