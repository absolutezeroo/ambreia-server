using System;
using System.Collections.Immutable;
using Vortex.Primitives.RewardTracks;

namespace Vortex.Primitives.Signals;

/// <summary>
/// The vocabulary of things a player can be said to have done.
/// </summary>
/// <remarks>
/// <para>
/// Every constant here <em>forwards</em> to <see cref="RewardTrackActions"/> rather than repeating
/// its string. That is deliberate and it is the whole lesson of this subsystem: the last time this
/// vocabulary was written down in two places — the handlers and <c>RewardTrackActionFacts</c> — the
/// two drifted on nine actions and nobody noticed, because nothing compares two hand-kept lists. A
/// forwarding constant cannot drift; the compiler is the check.
/// </para>
/// <para>
/// The strings stay where their reason lives. They are the target client's artwork keys —
/// <c>RewardTrackTaskRowView</c> builds an icon name as
/// <c>"reward_track_tasks_" + actionType.toLowerCase()</c> — and <see cref="RewardTrackActions"/>
/// carries the full account of why inventing one leaves a blank square. This class exists so a
/// consumer that is not reward tracks does not have to name a reward-track type to say "a player
/// entered a room".
/// </para>
/// <para>
/// The list is <em>declared</em>, not derived from the translators that exist. Two of these actions
/// have no producer today (<see cref="Teleport"/>, <see cref="Wired"/>) and content may already name
/// them; deriving the list would make them vanish from the editor rather than be shown as inert,
/// and a task written on one would become invisible instead of being flagged.
/// </para>
/// </remarks>
public static class SignalActions
{
    /// <inheritdoc cref="RewardTrackActions.EnterOtherUsersRoom"/>
    public const string EnterOtherUsersRoom = RewardTrackActions.EnterOtherUsersRoom;

    /// <inheritdoc cref="RewardTrackActions.CreateRoom"/>
    public const string CreateRoom = RewardTrackActions.CreateRoom;

    /// <inheritdoc cref="RewardTrackActions.PlaceItem"/>
    public const string PlaceItem = RewardTrackActions.PlaceItem;

    /// <inheritdoc cref="RewardTrackActions.MoveItem"/>
    public const string MoveItem = RewardTrackActions.MoveItem;

    /// <inheritdoc cref="RewardTrackActions.RotateItem"/>
    public const string RotateItem = RewardTrackActions.RotateItem;

    /// <inheritdoc cref="RewardTrackActions.PickUpItem"/>
    public const string PickUpItem = RewardTrackActions.PickUpItem;

    /// <inheritdoc cref="RewardTrackActions.WalkOnFurni"/>
    public const string WalkOnFurni = RewardTrackActions.WalkOnFurni;

    /// <inheritdoc cref="RewardTrackActions.Teleport"/>
    public const string Teleport = RewardTrackActions.Teleport;

    /// <inheritdoc cref="RewardTrackActions.ChatWithSomeone"/>
    public const string ChatWithSomeone = RewardTrackActions.ChatWithSomeone;

    /// <inheritdoc cref="RewardTrackActions.RequestFriend"/>
    public const string RequestFriend = RewardTrackActions.RequestFriend;

    /// <inheritdoc cref="RewardTrackActions.GiveRespect"/>
    public const string GiveRespect = RewardTrackActions.GiveRespect;

    /// <inheritdoc cref="RewardTrackActions.SendMessengerMessage"/>
    public const string SendMessengerMessage = RewardTrackActions.SendMessengerMessage;

    /// <inheritdoc cref="RewardTrackActions.Dance"/>
    public const string Dance = RewardTrackActions.Dance;

    /// <inheritdoc cref="RewardTrackActions.Wave"/>
    public const string Wave = RewardTrackActions.Wave;

    /// <inheritdoc cref="RewardTrackActions.ChangeFigure"/>
    public const string ChangeFigure = RewardTrackActions.ChangeFigure;

    /// <inheritdoc cref="RewardTrackActions.ChangeMotto"/>
    public const string ChangeMotto = RewardTrackActions.ChangeMotto;

    /// <inheritdoc cref="RewardTrackActions.WearBadge"/>
    public const string WearBadge = RewardTrackActions.WearBadge;

    /// <inheritdoc cref="RewardTrackActions.BuyFromCatalogue"/>
    public const string BuyFromCatalogue = RewardTrackActions.BuyFromCatalogue;

    /// <inheritdoc cref="RewardTrackActions.PetLevel"/>
    public const string PetLevel = RewardTrackActions.PetLevel;

    /// <inheritdoc cref="RewardTrackActions.UseHabbicon"/>
    public const string UseHabbicon = RewardTrackActions.UseHabbicon;

    /// <inheritdoc cref="RewardTrackActions.CompleteTrade"/>
    public const string CompleteTrade = RewardTrackActions.CompleteTrade;

    /// <inheritdoc cref="RewardTrackActions.SpendCredits"/>
    public const string SpendCredits = RewardTrackActions.SpendCredits;

    /// <inheritdoc cref="RewardTrackActions.CompleteHabbiconCollection"/>
    public const string CompleteHabbiconCollection = RewardTrackActions.CompleteHabbiconCollection;

    /// <inheritdoc cref="RewardTrackActions.CompleteQuest"/>
    public const string CompleteQuest = RewardTrackActions.CompleteQuest;

    /// <inheritdoc cref="RewardTrackActions.AchievementLevel"/>
    public const string AchievementLevel = RewardTrackActions.AchievementLevel;

    /// <inheritdoc cref="RewardTrackActions.Wired"/>
    public const string Wired = RewardTrackActions.Wired;

    /// <inheritdoc cref="RewardTrackActions.RateRoom"/>
    public const string RateRoom = RewardTrackActions.RateRoom;

    /// <inheritdoc cref="RewardTrackActions.LeaveRoom"/>
    public const string LeaveRoom = RewardTrackActions.LeaveRoom;

    /// <inheritdoc cref="RewardTrackActions.UpdateRoomSettings"/>
    public const string UpdateRoomSettings = RewardTrackActions.UpdateRoomSettings;

    /// <inheritdoc cref="RewardTrackActions.AnswerDoorbell"/>
    public const string AnswerDoorbell = RewardTrackActions.AnswerDoorbell;

    /// <inheritdoc cref="RewardTrackActions.AcceptFriend"/>
    public const string AcceptFriend = RewardTrackActions.AcceptFriend;

    /// <inheritdoc cref="RewardTrackActions.ReceiveRespect"/>
    public const string ReceiveRespect = RewardTrackActions.ReceiveRespect;

    /// <inheritdoc cref="RewardTrackActions.Login"/>
    public const string Login = RewardTrackActions.Login;

    /// <inheritdoc cref="RewardTrackActions.ChangeName"/>
    public const string ChangeName = RewardTrackActions.ChangeName;

    /// <inheritdoc cref="RewardTrackActions.EarnBadge"/>
    public const string EarnBadge = RewardTrackActions.EarnBadge;

    /// <inheritdoc cref="RewardTrackActions.AcceptQuest"/>
    public const string AcceptQuest = RewardTrackActions.AcceptQuest;

    /// <inheritdoc cref="RewardTrackActions.ActivateEffect"/>
    public const string ActivateEffect = RewardTrackActions.ActivateEffect;

    /// <inheritdoc cref="RewardTrackActions.CompletePoll"/>
    public const string CompletePoll = RewardTrackActions.CompletePoll;

    /// <inheritdoc cref="RewardTrackActions.SubmitQuiz"/>
    public const string SubmitQuiz = RewardTrackActions.SubmitQuiz;

    /// <inheritdoc cref="RewardTrackActions.ClaimDailyTask"/>
    public const string ClaimDailyTask = RewardTrackActions.ClaimDailyTask;

    /// <inheritdoc cref="RewardTrackActions.SaveOutfit"/>
    public const string SaveOutfit = RewardTrackActions.SaveOutfit;

    /// <inheritdoc cref="RewardTrackActions.RedeemClothing"/>
    public const string RedeemClothing = RewardTrackActions.RedeemClothing;

    /// <inheritdoc cref="RewardTrackActions.ChangePreference"/>
    public const string ChangePreference = RewardTrackActions.ChangePreference;

    /// <inheritdoc cref="RewardTrackActions.CreateGroup"/>
    public const string CreateGroup = RewardTrackActions.CreateGroup;

    /// <inheritdoc cref="RewardTrackActions.JoinGroup"/>
    public const string JoinGroup = RewardTrackActions.JoinGroup;

    /// <inheritdoc cref="RewardTrackActions.FavouriteGroup"/>
    public const string FavouriteGroup = RewardTrackActions.FavouriteGroup;

    /// <inheritdoc cref="RewardTrackActions.CreateForumThread"/>
    public const string CreateForumThread = RewardTrackActions.CreateForumThread;

    /// <inheritdoc cref="RewardTrackActions.CreateForumPost"/>
    public const string CreateForumPost = RewardTrackActions.CreateForumPost;

    /// <inheritdoc cref="RewardTrackActions.AdoptPet"/>
    public const string AdoptPet = RewardTrackActions.AdoptPet;

    /// <inheritdoc cref="RewardTrackActions.PlacePet"/>
    public const string PlacePet = RewardTrackActions.PlacePet;

    /// <inheritdoc cref="RewardTrackActions.PickUpPet"/>
    public const string PickUpPet = RewardTrackActions.PickUpPet;

    /// <inheritdoc cref="RewardTrackActions.ListOnMarketplace"/>
    public const string ListOnMarketplace = RewardTrackActions.ListOnMarketplace;

    /// <inheritdoc cref="RewardTrackActions.BuyOnMarketplace"/>
    public const string BuyOnMarketplace = RewardTrackActions.BuyOnMarketplace;

    /// <inheritdoc cref="RewardTrackActions.RedeemMarketplaceCredits"/>
    public const string RedeemMarketplaceCredits = RewardTrackActions.RedeemMarketplaceCredits;

    /// <inheritdoc cref="RewardTrackActions.BuyClub"/>
    public const string BuyClub = RewardTrackActions.BuyClub;

    /// <inheritdoc cref="RewardTrackActions.ClaimClubGift"/>
    public const string ClaimClubGift = RewardTrackActions.ClaimClubGift;

    /// <inheritdoc cref="RewardTrackActions.OpenPresent"/>
    public const string OpenPresent = RewardTrackActions.OpenPresent;

    /// <inheritdoc cref="RewardTrackActions.OpenMysteryBox"/>
    public const string OpenMysteryBox = RewardTrackActions.OpenMysteryBox;

    /// <inheritdoc cref="RewardTrackActions.OpenMysteryTrophy"/>
    public const string OpenMysteryTrophy = RewardTrackActions.OpenMysteryTrophy;

    /// <inheritdoc cref="RewardTrackActions.BuyGift"/>
    public const string BuyGift = RewardTrackActions.BuyGift;

    /// <inheritdoc cref="RewardTrackActions.BuyTargetedOffer"/>
    public const string BuyTargetedOffer = RewardTrackActions.BuyTargetedOffer;

    /// <inheritdoc cref="RewardTrackActions.EnterRaffle"/>
    public const string EnterRaffle = RewardTrackActions.EnterRaffle;

    /// <inheritdoc cref="RewardTrackActions.WinRaffle"/>
    public const string WinRaffle = RewardTrackActions.WinRaffle;

    /// <inheritdoc cref="RewardTrackActions.RedeemVoucher"/>
    public const string RedeemVoucher = RewardTrackActions.RedeemVoucher;

    /// <inheritdoc cref="RewardTrackActions.MintRelic"/>
    public const string MintRelic = RewardTrackActions.MintRelic;

    /// <inheritdoc cref="RewardTrackActions.BuyMintTokens"/>
    public const string BuyMintTokens = RewardTrackActions.BuyMintTokens;

    /// <inheritdoc cref="RewardTrackActions.BuyFromNftStore"/>
    public const string BuyFromNftStore = RewardTrackActions.BuyFromNftStore;

    /// <inheritdoc cref="RewardTrackActions.CollectNftClaims"/>
    public const string CollectNftClaims = RewardTrackActions.CollectNftClaims;

    /// <inheritdoc cref="RewardTrackActions.ClaimVaultIncome"/>
    public const string ClaimVaultIncome = RewardTrackActions.ClaimVaultIncome;

    /// <inheritdoc cref="RewardTrackActions.WearNftAvatar"/>
    public const string WearNftAvatar = RewardTrackActions.WearNftAvatar;

    /// <inheritdoc cref="RewardTrackActions.EarnHabbicon"/>
    public const string EarnHabbicon = RewardTrackActions.EarnHabbicon;

    /// <inheritdoc cref="RewardTrackActions.ClaimHabbiconReward"/>
    public const string ClaimHabbiconReward = RewardTrackActions.ClaimHabbiconReward;

    /// <inheritdoc cref="RewardTrackActions.FreezePlayerFrozen"/>
    public const string FreezePlayerFrozen = RewardTrackActions.FreezePlayerFrozen;

    /// <inheritdoc cref="RewardTrackActions.FreezePowerUpCollected"/>
    public const string FreezePowerUpCollected = RewardTrackActions.FreezePowerUpCollected;

    /// <inheritdoc cref="RewardTrackActions.FreezePoints"/>
    public const string FreezePoints = RewardTrackActions.FreezePoints;

    /// <inheritdoc cref="RewardTrackActions.BanzaiPoints"/>
    public const string BanzaiPoints = RewardTrackActions.BanzaiPoints;

    /// <inheritdoc cref="RewardTrackActions.FootballGoalScored"/>
    public const string FootballGoalScored = RewardTrackActions.FootballGoalScored;

    /// <inheritdoc cref="RewardTrackActions.RespectPet"/>
    public const string RespectPet = RewardTrackActions.RespectPet;

    /// <inheritdoc cref="RewardTrackActions.PetRespected"/>
    public const string PetRespected = RewardTrackActions.PetRespected;

    /// <inheritdoc cref="RewardTrackActions.FeedPet"/>
    public const string FeedPet = RewardTrackActions.FeedPet;

    /// <inheritdoc cref="RewardTrackActions.CommandPet"/>
    public const string CommandPet = RewardTrackActions.CommandPet;

    /// <inheritdoc cref="RewardTrackActions.StandNearFurni"/>
    public const string StandNearFurni = RewardTrackActions.StandNearFurni;

    /// <inheritdoc cref="RewardTrackActions.ReceiveGift"/>
    public const string ReceiveGift = RewardTrackActions.ReceiveGift;

    /// <summary>
    /// Every core action. The governance test freezes this list, and the translator processor uses
    /// it to tell a core action from one a plugin must qualify with its own key.
    /// </summary>
    public static readonly ImmutableHashSet<string> All = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        EnterOtherUsersRoom,
        CreateRoom,
        PlaceItem,
        MoveItem,
        RotateItem,
        PickUpItem,
        WalkOnFurni,
        Teleport,
        ChatWithSomeone,
        RequestFriend,
        GiveRespect,
        SendMessengerMessage,
        Dance,
        Wave,
        ChangeFigure,
        ChangeMotto,
        WearBadge,
        BuyFromCatalogue,
        PetLevel,
        UseHabbicon,
        CompleteTrade,
        SpendCredits,
        CompleteHabbiconCollection,
        CompleteQuest,
        AchievementLevel,
        Wired,
        RateRoom,
        LeaveRoom,
        UpdateRoomSettings,
        AnswerDoorbell,
        AcceptFriend,
        ReceiveRespect,
        Login,
        ChangeName,
        EarnBadge,
        AcceptQuest,
        ActivateEffect,
        CompletePoll,
        SubmitQuiz,
        ClaimDailyTask,
        SaveOutfit,
        RedeemClothing,
        ChangePreference,
        CreateGroup,
        JoinGroup,
        FavouriteGroup,
        CreateForumThread,
        CreateForumPost,
        AdoptPet,
        PlacePet,
        PickUpPet,
        ListOnMarketplace,
        BuyOnMarketplace,
        RedeemMarketplaceCredits,
        BuyClub,
        ClaimClubGift,
        OpenPresent,
        OpenMysteryBox,
        OpenMysteryTrophy,
        BuyGift,
        BuyTargetedOffer,
        EnterRaffle,
        WinRaffle,
        RedeemVoucher,
        MintRelic,
        BuyMintTokens,
        BuyFromNftStore,
        CollectNftClaims,
        ClaimVaultIncome,
        WearNftAvatar,
        EarnHabbicon,
        ClaimHabbiconReward,
        FreezePlayerFrozen,
        FreezePowerUpCollected,
        FreezePoints,
        BanzaiPoints,
        FootballGoalScored,
        RespectPet,
        PetRespected,
        FeedPet,
        CommandPet,
        StandNearFurni,
        ReceiveGift
    );
}
