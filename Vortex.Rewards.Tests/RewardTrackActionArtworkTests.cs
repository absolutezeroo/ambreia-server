using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Vortex.Primitives.RewardTracks;
using Xunit;

namespace Vortex.Rewards.Tests;

/// <summary>
/// An action code is the client's artwork key, so it cannot be invented.
/// <c>RewardTrackTaskRowView.as</c> builds a task's icon name as
/// <c>"reward_track_tasks_" + actionType.toLowerCase()</c> and does nothing else with the string —
/// a code outside the client's vocabulary draws a blank square and logs
/// <c>ResourceManager: Asset not found</c>, silently, on every task that uses it. Twelve of them
/// did, because the Introduction Track's seed copied each task's localization id into its action
/// code and the two lists are not the same list.
/// </summary>
public class RewardTrackActionArtworkTests
{
    /// <summary>
    /// The thirty <c>reward_track_tasks_*</c> embeds declared in the client's
    /// <c>HabboWindowManagerCom.as</c>, which is the whole set of icons that exists. Duplicated here
    /// on purpose: this project cannot read the client's asset library, and a list that has to be
    /// edited by hand is exactly what makes adding a code a deliberate act.
    /// </summary>
    private static readonly HashSet<string> ClientArtwork = new(StringComparer.Ordinal)
    {
        "buy_from_catalogue",
        "change_figure",
        "change_motto",
        "chat_with_someone",
        "create_room",
        "dance",
        "enter_other_users_room",
        "find_hand_item",
        "follow_friend",
        "friend_furni_locked",
        "give_respect",
        "move_item",
        "pet_eat",
        "pet_level",
        "pet_respect",
        "place_builders_club_furni",
        "place_item",
        "publish_picture",
        "replenish_respect",
        "request_friend",
        "rotate_item",
        "send_messenger_invite",
        "send_messenger_message",
        "set_relationship_status",
        "swim",
        "switch_item_state",
        "teleport",
        "use_habbicon",
        "wave",
        "wear_badge",
    };

    /// <summary>
    /// This hotel's own signals, which Habbo never drew an icon for. A task on one of these renders
    /// an empty square; that is a known, accepted cost of having the signal at all. Adding to this
    /// set is how you say "there is deliberately no artwork" — and the test above stops you from
    /// saying it by accident.
    /// </summary>
    private static readonly HashSet<string> DeliberatelyWithoutArtwork = new(StringComparer.Ordinal)
    {
        RewardTrackActions.CompleteTrade,
        RewardTrackActions.SpendCredits,
        RewardTrackActions.CompleteHabbiconCollection,
        RewardTrackActions.CompleteQuest,
        RewardTrackActions.AchievementLevel,
        RewardTrackActions.Wired,
        // Added for sequences, where nothing after step 0 is ever drawn. Both make poor opening
        // steps for exactly the reason this list exists, and good later ones: "place it, walk on
        // it, then pick it up" shows the place_item icon and needs no picture for the rest.
        RewardTrackActions.PickUpItem,
        RewardTrackActions.WalkOnFurni,
        // The coverage sweep: everything a player does that Habbo's own reward tracks never had a
        // task for, so there is no embed to borrow and inventing one would only move the blank
        // square. Each is a signal this hotel raises because its event already existed and carried
        // what a task would need -- the icon is the one thing that does not come free.
        RewardTrackActions.RateRoom,
        RewardTrackActions.LeaveRoom,
        RewardTrackActions.UpdateRoomSettings,
        RewardTrackActions.AnswerDoorbell,
        RewardTrackActions.AcceptFriend,
        RewardTrackActions.ReceiveRespect,
        RewardTrackActions.Login,
        RewardTrackActions.ChangeName,
        RewardTrackActions.EarnBadge,
        RewardTrackActions.AcceptQuest,
        RewardTrackActions.ActivateEffect,
        RewardTrackActions.CompletePoll,
        RewardTrackActions.SubmitQuiz,
        RewardTrackActions.ClaimDailyTask,
        RewardTrackActions.SaveOutfit,
        RewardTrackActions.RedeemClothing,
        RewardTrackActions.ChangePreference,
        RewardTrackActions.CreateGroup,
        RewardTrackActions.JoinGroup,
        RewardTrackActions.FavouriteGroup,
        RewardTrackActions.CreateForumThread,
        RewardTrackActions.CreateForumPost,
        RewardTrackActions.AdoptPet,
        RewardTrackActions.PlacePet,
        RewardTrackActions.PickUpPet,
        RewardTrackActions.ListOnMarketplace,
        RewardTrackActions.BuyOnMarketplace,
        RewardTrackActions.RedeemMarketplaceCredits,
        RewardTrackActions.BuyClub,
        RewardTrackActions.ClaimClubGift,
        RewardTrackActions.OpenPresent,
        RewardTrackActions.OpenMysteryBox,
        RewardTrackActions.OpenMysteryTrophy,
        RewardTrackActions.BuyGift,
        RewardTrackActions.BuyTargetedOffer,
        RewardTrackActions.EnterRaffle,
        RewardTrackActions.WinRaffle,
        RewardTrackActions.RedeemVoucher,
        RewardTrackActions.MintRelic,
        RewardTrackActions.BuyMintTokens,
        RewardTrackActions.BuyFromNftStore,
        RewardTrackActions.CollectNftClaims,
        RewardTrackActions.ClaimVaultIncome,
        RewardTrackActions.WearNftAvatar,
        RewardTrackActions.EarnHabbicon,
        RewardTrackActions.ClaimHabbiconReward,
        // The room games' scoring. Habbo's reward tracks never had a task on a game's points —
        // the achievements did, which is what these were raised for — so there is no embed to
        // borrow here either.
        RewardTrackActions.FreezePlayerFrozen,
        RewardTrackActions.FreezePowerUpCollected,
        RewardTrackActions.FreezePoints,
        RewardTrackActions.BanzaiPoints,
        RewardTrackActions.FootballGoalScored,
    };

    private static IEnumerable<string> AllActionCodes() =>
        typeof(RewardTrackActions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f =>
                f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string)
            )
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void Every_action_code_either_has_client_artwork_or_is_declared_to_have_none()
    {
        string[] orphans = AllActionCodes()
            .Where(code => !ClientArtwork.Contains(code))
            .Where(code => !DeliberatelyWithoutArtwork.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        orphans
            .Should()
            .BeEmpty(
                "every reward-track action code is the client's artwork key: "
                    + "reward_track_tasks_<code> must be one of the embeds in HabboWindowManagerCom.as, "
                    + "or the code must be listed as deliberately artwork-less"
            );
    }

    [Fact]
    public void The_codes_declared_artwork_less_really_have_none()
    {
        // Guards the other direction: once Habbo artwork exists for one of these — or someone
        // renames a code onto an existing icon — the exemption is stale and should go.
        DeliberatelyWithoutArtwork
            .Where(ClientArtwork.Contains)
            .Should()
            .BeEmpty("a code with real artwork must not be listed as having none");
    }

    [Fact]
    public void Action_codes_are_lower_snake_case()
    {
        // The client lowercases the code before pasting it into the asset name, so an upper-case
        // constant would resolve to a *different* string than the one written here and be
        // impossible to grep for.
        AllActionCodes()
            .Where(code => code != code.ToLowerInvariant())
            .Should()
            .BeEmpty("the client lowercases actionType before building the asset name");
    }
}
