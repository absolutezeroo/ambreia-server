namespace Vortex.Revisions.Revision20260701;

internal static class MessageEvent
{
    // NEW since last revision
    public const int GetRecyclerPrizesMessageEvent = 3669; // AS3-verified 2026-08-22 (WIN63 registry + sender): _composers[3669] = _SafeCls_1777, HabboCatalog::getRecyclerPrizes(). 2516 was in no registry table.
    public const int GetRecyclerStatusMessageEvent = 1730; // AS3-verified 2026-08-22 (WIN63 registry + sender): _composers[1730] = _SafeCls_2061, HabboCatalog::getRecyclerStatus(). 1246 was in no registry table.
    public const int RecycleItemsMessageEvent = 1796; // AS3-verified 2026-08-22 (WIN63 registry + sender): _composers[1796] = _SafeCls_2312, HabboCatalog::sendRecycleItems(Array). 2956 was in no registry table.
    public const int AppealCfhMessageEvent = 3028; // AS3-verified 2026-08-22 (WIN63 registry + sender): _composers[3028] = _SafeCls_3470, MyReportStatus.as:250 sends it with the report id. 1077 was in no registry table.
    public const int AddToCustomFilterMessageEvent = 2656; // AS3-verified (direct read): WIN63 registry _SafeCls_2046.as _composers[2656] = _SafeCls_2763(String word); WordFilterSettingsView is the only consumer. The previous value was a placeholder with no handler behind it
    public const int GetCustomFilterMessageEvent = 801; // AS3-verified (direct read): WIN63 registry _SafeCls_2046.as _composers[801] = _SafeCls_2412 (no payload); WordFilterSettingsView is the only consumer. The previous value was a placeholder with no handler behind it
    public const int RemoveFromCustomFilterMessageEvent = 2209; // AS3-verified (direct read): WIN63 registry _SafeCls_2046.as _composers[2209] = _SafeCls_2506(String word); WordFilterSettingsView is the only consumer. The previous value was a placeholder with no handler behind it

    public const int BlockListInitEvent = 798; // AS3-verified (direct read): BlockedUsersManager.as:54 -> send(new _SafeCls_3511()) (no args) @798; was 2610, which has no entry in the WIN63 registry at all
    public const int BlockUserMessageEvent = 483; // AS3-verified (direct read): BlockedUsersManager.as:94 -> send(new _SafeCls_2856(userId)) @483; was 2371, which has no entry in the WIN63 registry at all
    public const int ReplenishRespectMessageEvent = 426; // AS3-verified (direct read): SessionDataManager.as:889 -> send(new _SafeCls_1865()) (no args) @426; was 3323, which has no entry in the WIN63 registry at all
    public const int RespectUserMessageEvent = 3770; // AS3-verified (direct read): SessionDataManager.as:882 -> send(new _SafeCls_1969(userId)) @3770. Its old value 3377 is Game2GetAccountGameStatus, whose name survives unobfuscated in the WIN63 registry (that message is not handled)
    public const int UnblockUserMessageEvent = 2512; // AS3-verified (direct read): BlockedUsersManager.as:99 -> send(new _SafeCls_2552(userId)) @2512; was 1231, which the WIN63 registry uses on the *incoming* side only (no composer at 1231)

    public const int ClaimDailyTaskEvent = 4101;
    public const int GetDailyTasksEvent = 4100;

    // 762 is in NEITHER table of WIN63's own registry (habbo/communication/_SafeCls_2046.as) — not
    // as a composer, not as an event — so this client cannot send it and never could. Nothing here
    // handles it either. Kept as -1 so the client's wire-coverage sweep stops reporting a gap that
    // is really a leftover header from an older revision. Verified 2026-09-01.
    public const int ProgressTreasureHuntMessageEvent = -1;
    public const int ClickCharacterEvent = 3244; // AS3-verified 2026-08-22 (WIN63 registry + sender): _composers[3244] = _SafeCls_2770(int), room/_SafeCls_1821.as:600. 785 was in no registry table.

    // RESOLVED 2026-09-01, and it is not a client→server message at all. The earlier note here
    // guessed "a moderation mute/freeze payload" from the shape (type:int, duration:int,
    // isPermanent:bool) and asked for verification before anyone built a handler. WIN63's registry
    // settles it: 3814 is in the *event* table (`_SafeStr_4546[3814] = _SafeCls_2624`), whose
    // parser `_SafeCls_2974` reads exactly those three fields — it is the avatar-effect activation
    // push, and this file already has it correctly as `AvatarEffectActivatedMessageComposer = 3814`
    // in the server→client region below. The client registers 3814 as
    // AvatarEffectActivatedMessageEvent. Nothing referenced this constant.
    public const int WiredUpdateRoomEvent = -1;

    #region Incoming

    public const int CustomizePetWithFurniEvent = 2099; // AS3-verified (old-revision trace): _SafeCls_3494 -> useProductForPet() still exists in current revision at 2099
    public const int GetPetInfoMessageEvent = 3899; // AS3-verified (old-revision trace): _SafeCls_3258 -> requestPetInfo() still exists in current revision at 3899
    public const int PetSelectedMessageEvent = 2757; // AS3-verified (old-revision trace): _SafeCls_2740 -> handleGetPetInfoMessage() still exists in current revision at 2757

    // AS3-verified: _composers[576] = _SafeCls_1909, and the client sends that one composer for
    // BOTH buttons -- SessionDataManager.givePetRespect() and InfoStandWidgetHandler's
    // RWUAM_TREAT_PET. One int, the pet id; the server tells the two apart by the pet's type,
    // because a monsterplant is watered where an animal is scratched.
    public const int RespectPetMessageEvent = 576;

    public const int GetCurrentTimingCodeMessageEvent = 1503;
    public const int GetNftCreditsMessageEvent = 2069;

    public const int GetSilverMessageEvent = 394;
    public const int GetBotInventoryEvent = 3148;
    public const int GetCreditsInfoEvent = 540;
    public const int CallForHelpFromForumMessageMessageEvent = 2991;
    public const int CallForHelpFromForumThreadMessageEvent = 380;
    public const int CallForHelpFromIMMessageEvent = 838;
    public const int CallForHelpFromPhotoMessageEvent = 1964;
    public const int CallForHelpFromSelfieMessageEvent = 201;
    public const int CallForHelpMessageEvent = 732;
    public const int ChatReviewGuideDecidesOnOfferMessageEvent = 2545; // AS3-verified (direct read, both revisions): GuideSessionController skip_link/accept_button -> _SafeCls_2508
    public const int ChatReviewGuideDetachedMessageEvent = 349; // AS3-verified (old-revision trace): _SafeCls_3740 -> onGuardianChatReviewWaitForOtherVotersEvent() still exists in current revision at 349
    public const int ChatReviewGuideVoteMessageEvent = 1801; // AS3-verified (ghost fix): GuideSessionController::onGuardianChatReviewVoteEvent()
    public const int ChatReviewSessionCreateMessageEvent = 3970; // AS3-verified (direct read, both revisions): GuideSessionController::onReportWindowEvent() submit_button -> sendMessage(new _SafeCls_2981(message)) registry@3970 (old _SafeCls_2760@296)
    public const int DeletePendingCallsForHelpMessageEvent = 3423; // AS3-verified (old-revision trace): _SafeCls_2943 -> deletePendingCallsForHelp() still exists in current revision at 3423
    public const int GetCfhStatusMessageEvent = 3458; // AS3-verified (old-revision trace): _SafeCls_1917 -> requestSanctionInfo() still exists in current revision at 3458
    public const int GetMyCfhReportStatusMessageEvent = 1834; // AS3-verified (direct read): _composers[1834] = _SafeCls_2121 in WIN63 habbo/communication/_SafeCls_2046.as, sent payload-free by HabboHelp::requestReportsStatus(). The reply (MyCfhReportStatusComposer) was already defined here; only the request header was missing, so the feature was half-built on this side
    public const int GetGuideReportingStatusMessageEvent = 2455; // AS3-verified (old-revision trace): _SafeCls_2092 -> queryForGuideReportingStatus() still exists in current revision at 2455

    public const int GetPendingCallsForHelpMessageEvent = 92; // AS3-verified: HabboHelp::queryForPendingCallsForHelp()

    public const int GetQuizQuestionsEvent = 1982; // AS3-verified (old-revision trace): _SafeCls_3778 -> showHabboWayQuiz() still exists in current revision at 1982
    public const int GuideSessionCreateMessageEvent = 2181; // AS3-verified (direct read, both revisions): GuideSessionController::createHelpRequest() -> _habboHelp.sendMessage(new _SafeCls_3461(...)) @2181 (old _SafeCls_3193@3662, stale - never updated for this revision)
    public const int GuideSessionFeedbackMessageEvent = 150; // AS3-verified (direct read, both revisions): GuideSessionController lines 812/969/982/1017/1022 -> _SafeCls_3165
    public const int GuideSessionGetRequesterRoomMessageEvent = 3914; // AS3-verified (direct read, both revisions): GuideSessionController::onGuideOngoingEvent() visit_button -> _SafeCls_3794 (earlier shape-match guess of the same header was actually right, just mislabeled)
    public const int GuideSessionGuideDecidesMessageEvent = 300; // AS3-verified (direct read, both revisions): GuideSessionController lines 710/715 -> _SafeCls_3277
    public const int GuideSessionInviteRequesterMessageEvent = 3336; // AS3-verified (direct read, both revisions): GuideSessionController::onGuideOngoingEvent() invite_button -> _SafeCls_3297
    public const int GuideSessionIsTypingMessageEvent = 296; // AS3-verified (old-revision trace): GuideSessionController::onTypingTimer() still exists in current revision at 296
    public const int GuideSessionMessageMessageEvent = 1561; // AS3-verified (direct read, both revisions): GuideSessionController::onInput() -> _SafeCls_2721
    public const int GuideSessionOnDutyUpdateMessageEvent = 958; // AS3-verified 2026-08-07: _composers[958] -> _SafeCls_3169, sent by GuideSessionController::showGuideTool() as (onDuty, tour_requests, help_requests, chat_reviews)

    // GuideSessionReportMessageEvent is gone, not renumbered. WIN63 has no such message: the guide
    // subsystem composes eighteen messages, every one of them already has a header here, and none
    // of them is a report. The id it carried (9106) is not in the client's registry either, whose
    // highest entry is 4101 -- so it was never a collision to resolve, it was a message that does
    // not exist. Re-derived 2026-08-13.
    public const int GuideSessionRequesterCancelsMessageEvent = 2847; // AS3-verified (old-revision trace): onUserPendingEvent() still exists in current revision at 2847
    public const int GuideSessionResolvedMessageEvent = 3831; // AS3-verified (direct read, both revisions): GuideSessionController::onGuideOngoingEvent() close_link -> _SafeCls_3979
    public const int PostQuizAnswersEvent = 1387;
    public const int OpenFlatConnectionMessageEvent = 3234;
    public const int QuitMessageEvent = 3061;
    public const int GetBonusRareInfoMessageEvent = 251;
    public const int GetBundleDiscountRulesetEvent = 317; // AS3-verified: HabboCatalog::sendGetBundleDiscountRuleset(). Note: GetBundleDiscountRulesetVortexEvent (1811) also resolves to this same AS3 message - likely a Vortex-specific duplicate/extension with no distinct AS3 backing, left untouched
    public const int GetCatalogIndexEvent = 2232;
    public const int GetCatalogPageEvent = 2093;
    public const int GetClubGiftMessageEvent = 472;
    public const int GetClubOffersMessageEvent = 667;
    public const int GetGiftWrappingConfigurationEvent = 940; // AS3-verified: HabboCatalog::getGiftWrappingConfiguration(). Note: GetGiftWrappingConfigurationVortexEvent also resolves to this same AS3 message - likely a Vortex-specific duplicate/extension with no distinct AS3 backing, left untouched

    public const int GetHabboClubExtendOfferMessageEvent = 2931; // AS3-verified (direct read, both revisions): ClubDiscountPromoExtension::onClick() -> connection.send(new _SafeCls_2983()) registry@2931 (old _SafeCls_3628@1808 moved)
    public const int GetIsOfferGiftableEvent = 2564; // AS3-verified (old-revision trace): _SafeCls_2064 -> checkGiftable() still exists in current revision at 2564

    public const int GetProductOfferEvent = 1692;
    public const int GetSellablePetPalettesEvent = 3342; // shape+AS3-verified: HabboCatalog::getSellablePetPalettes() (_SafeCls_2003)
    public const int GetSnowWarGameTokensOfferEvent = 2447;
    public const int MarkCatalogNewAdditionsPageOpenedEvent = 3835; // AS3-verified (ghost fix): HabboCatalog::markNewAdditionPageOpened()
    public const int PurchaseBasicMembershipExtensionEvent = 3561;
    public const int PurchaseFromCatalogAsGiftEvent = 2909;
    public const int PurchaseFromCatalogEvent = 1706;
    public const int PurchaseSnowWarGameTokensOfferEvent = 3243;
    public const int PurchaseVipMembershipExtensionEvent = 2441;
    public const int SelectClubGiftEvent = 2087;
    public const int NewNavigatorInitEvent = 1590;
    public const int NewNavigatorSearchEvent = 81;

    // The floor-plan editor's two opening requests, re-derived 2026-08-13. Both were placeholder
    // values above the client's whole id range (its highest registry entry is 4101), so neither
    // request could ever arrive and both handlers were unreachable.
    //
    // BCFloorPlanEditor sends exactly two body-less messages when it opens -- 3426 and 880 -- and
    // listens for three answers: the height map, the entry tile and the occupied tiles. Which of
    // the two ids asks for which cannot be told apart from the AS3: both composers are empty, both
    // are sent back to back, and each answer is dispatched by its own header rather than by the
    // request that prompted it. The pairing below is therefore an assumption, not a reading -- and
    // a harmless one, because the editor receives both answers either way.
    public const int GetOccupiedTilesMessageEvent = 3426;
    public const int GetRoomEntryTileMessageEvent = 880;
    public const int UpdateFloorPropertiesMessageEvent = 2937; // AS3-verified 2026-08-13: _composers[2937] = _SafeCls_2580, sent by BCFloorPlanEditor on save. Variable length -- one, six or seven fields
    public const int SelectInitialRoomEvent = 3267; // AS3-verified (direct read, both revisions): RoomPicker::onButtonSelect() -> connection.send(new _SafeCls_3967(roomType)) registry@3267 (old _SafeCls_3549@1176 moved)
    public const int GetInterstitialMessageEvent = 1369; // AS3-verified (old-revision trace): AdManager::showInterstitial() still exists in current revision at 1369
    public const int CommandBotEvent = 3813; // AS3-verified (direct read): _composers[3813] = _SafeCls_2928(botId, commandId, data), sent by the bot-skill configuration views
    public const int GetBotCommandConfigurationDataEvent = 2311; // AS3-verified (direct read, both revisions): BotSkillConfigurationViewBase::open() -> _SafeCls_3415
    public const int GetBadgePointLimitsEvent = 2944; // AS3-verified 2026-08-22 (WIN63 registry + sender): _composers[2944] = _SafeCls_2019(), HabboInventory.as:207. The request trigger the old comment could not find is this one; 981 was in no registry table.
    public const int GetBadgesEvent = 770;
    public const int GetBadgeInfoEvent = 3159; // AS3-verified 2026-09-19 (WIN63 registry + sender): _composers[3159] = _SafePkg_2020/_SafeCls_3448(String), sent by ui/handler/FurnitureBadgeDisplayWidgetHandler.as:151. Outgoing 3159 is NftEmeraldConvertResult -- separate id spaces, not a collision
    public const int GetBadgeLeaderboardEvent = 1225; // AS3-verified 2026-09-19 (WIN63 registry + sender): _composers[1225] = _SafePkg_1789/_SafeCls_3493(type, rarity, chunkIndex, size), sent by groups/badge_leaderboard/server/BadgeLeaderboardDataServer.synchronizeChunk
    public const int GetIsBadgeRequestFulfilledEvent = 2236; // AS3-verified 2026-08-13: _composers[2236] = _SafeCls_3765, sent with the badge request code by the landing view's badge widget. 2236 is also an AvatarEffectExpired *composer* -- the two directions are separate id spaces, so that is not a collision. Was 9112, invented
    public const int RequestABadgeEvent = 3258;
    public const int SetActivatedBadgesEvent = 2764;
    public const int ResetUnseenItemIdsEvent = 3771; // AS3-verified (old-revision trace): _SafeCls_2563 -> sendResetItemsMessage() still exists in current revision at 3771
    public const int ResetUnseenItemsEvent = 699;
    public const int AddAdminRightsToMemberMessageEvent = 2152; // AS3-verified (direct read, both revisions): GuildMembersWindowCtrl::onActionLinkClick() member-branch -> _SafeCls_3129
    public const int ApproveAllMembershipRequestsMessageEvent = 1621; // AS3-verified (old-revision trace): GuildMembersWindowCtrl::onAcceptAll() still exists in current revision at 1621
    public const int ApproveMembershipRequestMessageEvent = 3505; // AS3-verified (direct read, both revisions): GuildMembersWindowCtrl::onActionLinkClick() else-branch -> _SafeCls_3772
    public const int ApproveNameMessageEvent = 1211;
    public const int CreateGuildMessageEvent = 207;
    public const int DeactivateGuildMessageEvent = 2725; // AS3-verified (old-revision trace): _SafeCls_3769 -> onDeleteGuildConfirmation() still exists in current revision at 2725
    public const int DeselectFavouriteHabboGroupMessageEvent = 306;
    public const int GetExtendedProfileByNameMessageEvent = 321;
    public const int GetExtendedProfileMessageEvent = 847;
    public const int GetGuildCreationInfoMessageEvent = 2989; // AS3-verified (direct read, both revisions): BuyGuildWidget::onButtonClicked() -> connection.send(new _SafeCls_3896()) registry@2989 (old _SafeCls_3443@831 moved; 831 now cancelAllMarketPlaceOffers)
    public const int GetGuildEditInfoMessageEvent = 874; // AS3-verified (old-revision trace): _SafeCls_2432 -> onManageGuild() still exists in current revision at 874
    public const int GetGuildEditorDataMessageEvent = 3398; // AS3-verified (ghost fix): HabboGroupsManager::requestGuildEditorData()
    public const int GetGuildMembershipsMessageEvent = 3918; // AS3-verified (old-revision trace): _SafeCls_3428 -> registerGuildSelectorWidget() still exists in current revision at 3918

    public const int GetGuildMembersMessageEvent = 1337; // AS3-verified (direct read, both revisions): GuildMembersWindowCtrl::onMembersClick() -> connection.send(new _SafeCls_2908(groupId,page,search,filter)) @1337 (old _SafeCls_3861@3234)

    public const int GetHabboGroupBadgesMessageEvent = 3346; // AS3-verified (old-revision trace): _SafeCls_3130 -> onRoomReady() still exists in current revision at 3346
    public const int GetHabboGroupDetailsMessageEvent = 1683;
    public const int GetIgnoredUsersMessageEvent = 1026;
    public const int GetMemberGuildItemCountMessageEvent = 781; // AS3-verified: HabboGroupsManager::handleUserKick()/handleUserBlock() -> send(GetMemberGuildItemCountMessageComposer(userId, groupId)) [deobfuscated flash]; registry _composers[781] = _SafeCls_1795(int groupId, int userId). This is step 1 of the kick flow (ask how much furni the target has in the HQ), NOT the kick itself
    public const int GetMOTDMessageEvent = 3163;
    public const int GetRelationshipStatusInfoMessageEvent = 3219; // reverted to client-verified value: GetRelationshipStatusInfoMessageComposer=3219 in HabboMessages.ts. 1773 legitimately belongs to SetRelationshipStatusMessageEvent (also client-verified: SetRelationshipStatusMessageComposer=1773)
    public const int GetSelectedBadgesMessageEvent = 3726;
    public const int GetUserNftChatStylesMessageEvent = 3642;
    public const int IgnoreUserMessageEvent = 2070;
    public const int JoinHabboGroupMessageEvent = 1469;

    public const int KickMemberMessageEvent = 3156; // AS3-verified: HabboGroupsManager::onKickConfirmationClose() -> send(KickMemberMessageComposer(guildId, targetId, blocked)) [deobfuscated flash]; registry _composers[3156] = _SafeCls_2084(int, int, Boolean). The previous value 781 was handleUserKick()'s *furni-count request*, not the kick — it collided with GetMemberGuildItemCountMessageEvent and made kicking fire on the wrong packet

    public const int RejectMembershipRequestMessageEvent = 3200; // AS3-verified (old-revision trace): _SafeCls_3781 -> onRemoveMouseClick() still exists in current revision at 3200
    public const int RemoveAdminRightsFromMemberMessageEvent = 3999; // AS3-verified (direct read, both revisions): GuildMembersWindowCtrl::onActionLinkClick() admin-branch -> _SafeCls_3648
    public const int ScrGetKickbackInfoMessageEvent = 1111;
    public const int ScrGetUserInfoMessageEvent = 1071;
    public const int SelectFavouriteHabboGroupMessageEvent = 1887;
    public const int UnblockGroupMemberMessageEvent = 2580; // AS3-verified (direct read, both revisions): GuildMembersWindowCtrl::onActionLinkClick() blocked-branch -> _SafeCls_2867
    public const int UnignoreUserMessageEvent = 3542;
    public const int UpdateGuildBadgeMessageEvent = 3882; // AS3-verified (direct read, both revisions): GuildManagementWindowCtrl::saveView() case 1 (badge) -> _SafeCls_3981
    public const int UpdateGuildColorsMessageEvent = 3421; // AS3-verified (direct read, both revisions): GuildManagementWindowCtrl::saveView() case 2 (colors) -> _SafeCls_2420
    public const int UpdateGuildIdentityMessageEvent = 2009; // AS3-verified (direct read, both revisions): GuildManagementWindowCtrl::saveView() case VIEW_IDENTITY -> connection.send(new _SafeCls_3413(groupId,name,desc)) registry@2009 (old _SafeCls_3634@1630 moved)
    public const int UpdateGuildSettingsMessageEvent = 3716; // AS3-verified: GuildManagementWindowCtrl -> _SafeCls_3380(groupId,guildType,rightsLevel), header 3716 (2793 belongs to UpdateForumSettingsMessageEvent - regression from automated re-pass, fixed again)
    public const int CloseIssueDefaultActionMessageEvent = 1631;
    public const int CloseIssuesMessageEvent = 3986;
    public const int DefaultSanctionMessageEvent = 2375; // AS3-verified (direct read, both revisions): ModActionCtrl::onDefaultSanctionButton() -> connection.send(new _SafeCls_2494(...)) @2375 (slot freed by correcting MakeOfferMessageEvent above)
    public const int GetCfhChatlogMessageEvent = 1580; // AS3-verified (direct read, both revisions): IssueHandler CFH ChatlogCtrl construction (type=3) -> connection.send(new _SafeCls_2471(issueId)) @1580 (old _SafeCls_3765@757)
    public const int GetModeratorRoomInfoMessageEvent = 1504; // AS3-verified (direct read, both revisions): RoomToolCtrl::show() -> connection.send(new _SafeCls_2915(flatId)) @1504 (old _SafeCls_3268@3513)

    // Re-derived from the WIN63-202607011411 composer registry (_composers[3230] = _SafeCls_2387):
    // the client has exactly one "load moderator user info" request, sent both by UserInfoCtrl::load()
    // and by ModerationMessageHandler::onModeratorActionResult(). The old 1948 was a phantom - that id
    // is absent from _composers entirely, so this handler could never fire.
    public const int GetModeratorUserInfoMessageEvent = 3230;
    public const int GetRoomChatlogMessageEvent = 1346; // AS3-verified (direct read): _composers[1346] = _SafeCls_2601(roomType, roomId), constructed by RoomToolCtrl/StartPanelCtrl - note roomType comes FIRST on the wire
    public const int GetRoomVisitsMessageEvent = 903; // AS3-verified (direct read): _composers[903] = _SafeCls_2383, sent by RoomVisitsCtrl with the USER id (not a room id) - the response is a per-user room-visit list
    public const int GetUserChatlogMessageEvent = 1686; // AS3-verified (direct read): _composers[1686] = _SafeCls_2670(userId), constructed by UserInfoCtrl
    public const int ModAlertMessageEvent = 2183; // AS3-verified (direct read, both revisions): ModActionCtrl::onCustomSanctionButton() case 0 (trackAction('sendCaution'), alertPermission) -> connection.send(new _SafeCls_3481(...)) @2183
    public const int ModBanMessageEvent = 2507; // AS3-verified (direct read): ModActionCtrl::onCustomSanctionButton() case 2 (trackAction('ban'), banPermission) -> _SafeCls_3959 @2507

    // AS3-verified (direct read): _composers[2735] = _SafeCls_3239(actionType, message, ""), sent by
    // RoomToolCtrl::act() for the "Send caution"/"Send message" buttons. actionType is one of the
    // class's own four constants (0/1/3/4) and carries no room id -- see ModToolRoomAlertMessage.
    public const int ModToolRoomAlertMessageEvent = 2735;

    public const int ModerateRoomMessageEvent = 2939; // AS3-verified (direct read): RoomToolCtrl::act() -> _SafeCls_2501(flatId, lockDoor, changeName, kickUsers) @2939

    public const int ModKickMessageEvent = 1401; // AS3-verified (direct read, both revisions): ModActionCtrl::onCustomSanctionButton() case 3 (trackAction('kick'), kickPermission) -> connection.send(new _SafeCls_3834(...)) @1401

    public const int ModMessageMessageEvent = 2579; // AS3-verified (old-revision trace): still exists in current revision at 2579
    public const int ModMuteMessageEvent = 2862; // AS3-verified (direct read, both revisions): ModActionCtrl::onCustomSanctionButton() case 1 (trackAction('mute')) -> connection.send(new _SafeCls_2569(...)) @2862

    public const int ModToolPreferencesEvent = 1415; // AS3-verified (direct read): _composers[1415] = _SafeCls_3647(windowX, windowY, windowWidth, windowHeight), sent by IssueHandler when a mod-tool window moves or resizes. Replaces the 9009 placeholder, which no client ever sent.

    public const int ModToolSanctionEvent = 2476; // AS3-verified (direct read): _composers[2476] = _SafeCls_3255(issueId, accountId, categoryId), sent by IssueManager::requestSanctionData/requestSanctionDataForAccount
    public const int ModTradingLockMessageEvent = 3495; // AS3-verified (direct read): ModActionCtrl::onCustomSanctionButton() case 4 (trackAction('trading_lock')) -> _SafeCls_3651(userId, message, durationMinutes, topicId, [issueId]) @3495
    public const int PickIssuesMessageEvent = 3400;
    public const int ReleaseIssuesMessageEvent = 3977; // AS3-verified (old-revision trace): _SafeCls_2791 -> sendRelease() still exists in current revision at 3977
    public const int ClientHelloMessageEvent = 4000;
    public const int CompleteDiffieHandshakeMessageEvent = 2526;
    public const int DisconnectMessageEvent = 2864;
    public const int InfoRetrieveMessageEvent = 756;
    public const int InitDiffieHandshakeMessageEvent = 2022;
    public const int PongMessageEvent = 362;
    public const int SSOTicketMessageEvent = 882;
    public const int UniqueIDMessageEvent = 2309;
    public const int VersionCheckMessageEvent = 3584;
    public const int AcceptQuestMessageEvent = 985; // AS3-verified (ghost fix): QuestDetails::onAcceptQuest()
    public const int ActivateQuestMessageEvent = 555;
    public const int CancelQuestMessageEvent = 1221;
    public const int FriendRequestQuestCompleteMessageEvent = 3074; // AS3-verified (direct read, both revisions): HabboFriendList::askForAFriend() quest side-effect -> _SafeCls_2290 (outgoing.quest package)
    public const int GetCommunityGoalHallOfFameMessageEvent = 2252;
    public const int GetCommunityGoalProgressMessageEvent = 1815;
    public const int GetConcurrentUsersGoalProgressMessageEvent = 2167;
    public const int GetConcurrentUsersRewardMessageEvent = 2451;
    public const int GetDailyQuestMessageEvent = 397;
    public const int GetQuestsMessageEvent = 2895; // AS3-verified (direct search, exact match): HabboQuestEngine::requestQuests() -> _SafeCls_1837
    public const int GetSeasonalQuestsOnlyMessageEvent = 1236; // shape+AS3-verified: HabboQuestEngine::requestSeasonalQuests() (_SafeCls_1847)
    public const int OpenQuestTrackerMessageEvent = 588; // AS3-verified (old-revision trace): _SafeCls_2973 -> onNextQuest() still exists in current revision at 588
    public const int RejectQuestMessageEvent = 20; // AS3-verified (direct read, both revisions): QuestDetails::onCancelQuest() -> _SafeCls_3635
    public const int StartCampaignMessageEvent = 3969; // AS3-verified 2026-08-12 (direct read): _composers[3969] = outgoing.quest._SafeCls_3041, a one-string composer sent by QuestTracker with getDefaultCampaign() and by CitizenshipVipQuestsPromoExtension with its own campaign name. The earlier "collided with MyFavouriteRoomsSearchMessageEvent" note was wrong: 3969 belongs to the quest package.
    public const int ClickFurniMessageEvent = 443;
    public const int GetFurnitureAliasesMessageEvent = 1901;
    public const int GetItemDataMessageEvent = 350;
    public const int GetPetCommandsMessageEvent = 2425;
    public const int MountPetMessageEvent = 1996;
    public const int MoveAvatarMessageEvent = 2364;
    public const int MoveObjectMessageEvent = 1482;
    public const int MovePetMessageEvent = 432; // AS3-verified (direct read): _composers[432] = _SafeCls_2560 in WIN63's registry _SafeCls_2046.as:1088, sent by habbo/room/_SafeCls_1821.as::sendMoveUserObjectMessage() on the "monsterplant" branch. The previous 2761 was win63_version's header — a different build, absent from this revision entirely
    public const int MoveWallItemMessageEvent = 2999; // AS3-verified (direct read, both revisions): modifyRoomObject() param3==20 (wall item move) -> _SafeCls_2682
    public const int PickupObjectMessageEvent = 1919;
    public const int PlaceBotMessageEvent = 2102; // AS3-verified (old-revision trace): _SafeCls_3867 -> placeItemToRoom() still exists in current revision at 2102
    public const int PlaceObjectMessageEvent = 1974; // AS3-verified (direct read, both revisions): room engine placeObject() generic/default branch -> _SafeCls_2135 @1974 (old _SafeCls_2136@3258; prior name-only fix wrongly landed on the stickie/post-it branch's value)
    public const int PlacePetMessageEvent = 1018; // AS3-verified (old-revision trace): _SafeCls_3396 -> placePetToRoom() still exists in current revision at 1018
    public const int RemoveBotFromFlatMessageEvent = 2743; // AS3-verified (old-revision trace): _SafeCls_2709 -> buttonEventProc() still exists in current revision at 2743
    public const int RemoveItemMessageEvent = 141; // AS3-verified (old-revision trace): _SafeCls_3756 -> deleteWallItem() still exists in current revision at 141
    public const int RemovePetFromFlatMessageEvent = 1640; // AS3-verified (old-revision trace): _SafeCls_2455 -> pickUpPet() still exists in current revision at 1640
    public const int RemoveSaddleFromPetMessageEvent = 2884;
    public const int SetClothingChangeDataMessageEvent = 1220;
    public const int SetItemDataMessageEvent = 3498;
    public const int SetObjectDataMessageEvent = 246; // AS3-verified (ghost fix): InfoStandWidgetHandler::setObjectData()
    public const int TogglePetRidingPermissionMessageEvent = 3713;
    public const int UseFurnitureMessageEvent = 3353;
    public const int UseWallItemMessageEvent = 3590; // AS3-verified (direct read): RoomObjectEventHandler (_SafeCls_1821.as:2570) branches on the object category - category 10 (floor) sends _SafeCls_3952/_SafeCls_3726, category 20 (wall) sends _SafeCls_3754(objectId, param) @3590. Supersedes the earlier "no distinct AS3 backing" note, which had confused it with modifyWallItemData()/SetItemData @3498
    public const int IncomeRewardClaimMessageEvent = 809;
    public const int IncomeRewardStatusMessageEvent = 3417;
    public const int AcceptTradingEvent = 490;
    public const int AddItemsToTradeEvent = 3370; // AS3-verified (direct read, both revisions): TradingModel::requestAddItemsToTrading() bulk branch -> connection.send(new _SafeCls_3302(vector)) @3370 (distinct from the single-item branch already correctly at AddItemToTradeEvent=2177 via _SafeCls_3237)
    public const int AddItemToTradeEvent = 2177;
    public const int CloseTradingEvent = 3639;
    public const int ConfirmAcceptTradingEvent = 2662;
    public const int ConfirmDeclineTradingEvent = 1217;
    public const int OpenTradingEvent = 1865;
    public const int RemoveItemFromTradeEvent = 573;
    public const int SilverFeeMessageEvent = 2717; // AS3-verified (ghost fix): TradingModel::addSilverFee()
    public const int UnacceptTradingEvent = 1030;
    public const int UpdateFigureDataMessageEvent = 3339; // AS3-verified (old-revision trace): still exists in current revision at 3339
    public const int RedeemPurchasableClothingMessageEvent = 3637; // AS3-verified (direct read): FurnitureContextMenuWidgetHandler::redeemPurchasableClothing() -> send(new _SafeCls_3394(roomObjectId)) @3637, one int. Answered by FigureSetIds@1231 -- there is no result message
    public const int AvatarExpressionMessageEvent = 2912;
    public const int ChangeMottoMessageEvent = 2659;
    public const int ChangePostureMessageEvent = 3181;

    public const int DanceMessageEvent = 48;
    public const int DropCarryItemMessageEvent = 1545; // AS3-verified (direct read, both revisions): InfoStandWidgetHandler RWUAM_DROP_CARRY_ITEM -> _SafeCls_2423
    public const int LookToMessageEvent = 2508; // AS3-verified: the client registry maps _composers[2508] to the (x,y) composer RoomObjectEventHandler.setSelectedAvatar() sends when an avatar is selected without a click-user wired (WIN63 habbo/communication/_SafeCls_2046.as:819, LookToMessageComposer in win63_version). Was 9103, a placeholder whose own comment flagged it as unresolved; the parser here already read the right two ints, so only the header was wrong
    public const int PassCarryItemMessageEvent = 1101; // AS3-verified (direct read, both revisions): InfoStandWidgetHandler RWUAM_PASS_CARRY_ITEM -> _SafeCls_3124
    public const int PassCarryItemToPetMessageEvent = 1429; // AS3-verified (direct read, both revisions): InfoStandWidgetHandler RWUAM_GIVE_CARRY_ITEM_TO_PET -> _SafeCls_2543
    public const int SignMessageEvent = 211;

    public const int GetForumsListMessageEvent = 488; // AS3-verified (ghost fix): GroupForumController::openForumsList()
    public const int GetForumStatsMessageEvent = 3592; // AS3-verified (old-revision trace): GroupForumController::initForum() still exists in current revision at 3592

    public const int GetMessagesMessageEvent = 225; // AS3-verified (direct read): GroupForumController::requestThreadMessageList(groupId,threadId,startIndex) -> connection.send(new _SafeCls_2438(...)) @225 (registry: _SafeCls_2046.as:787)

    public const int GetThreadMessageEvent = 3218; // AS3-verified (direct read, both revisions): GroupForumController::linkReceived() -> _SafeCls_2586
    public const int GetThreadsMessageEvent = 3668; // AS3-verified (old-revision trace): _SafeCls_2466 -> requestThreadList() still exists in current revision at 3668
    public const int GetUnreadForumsCountMessageEvent = 1076; // AS3-verified (old-revision trace): _SafeCls_2504 -> onUnreadForumsCountUpdateTimerEvent() still exists in current revision at 1076
    public const int ModerateMessageMessageEvent = 3373; // AS3-verified (old-revision trace): _SafeCls_3239 -> deleteMessage() still exists in current revision at 3373
    public const int ModerateThreadMessageEvent = 3320; // AS3-verified (old-revision trace): _SafeCls_3768 -> deleteThread() still exists in current revision at 3320
    public const int PostMessageMessageEvent = 2811;
    public const int UpdateForumReadMarkerMessageEvent = 429; // AS3-verified (direct search, exact semantic match): GroupForumController::markForumAsRead() -> _SafeCls_3696
    public const int UpdateForumSettingsMessageEvent = 2793; // shape+AS3-verified: GroupForumController::updateForumSettings() (_SafeCls_2662)
    public const int UpdateThreadMessageEvent = 3206;
    public const int GetOfficialSongIdMessageEvent = 1723; // AS3-verified (old-revision trace): _SafeCls_2539 -> onSelectProduct() still exists in current revision at 1723
    public const int GetSongInfoMessageEvent = 3130; // AS3-verified (old-revision trace): _SafeCls_3422 -> sendNextSongRequestMessage() still exists in current revision at 3130
    public const int GetSoundMachinePlayListMessageEvent = 3633; // AS3-verified (direct read, both revisions): SoundMachinePlayListController::requestPlayList() -> _SafeCls_3689
    public const int GetSoundSettingsEvent = 541; // AS3-verified (direct read, both revisions): HabboSoundManagerFlash10::init() -> _SafeCls_1901
    public const int GetUserSongDisksMessageEvent = 1685; // AS3-verified (ghost fix): HabboMusicController::requestUserSongDisks()
    public const int ChangeUserNameMessageEvent = 1703; // AS3-verified (ghost fix): NameChangeController::changeName()

    // The onboarding name dialog claims a name with a DIFFERENT composer than the paid rename:
    // NameChangeDialog::claimName() sends _SafeCls_3401, registry@879, where
    // NameChangeController::changeName() sends _SafeCls_3913, registry@1703 above. Same payload
    // (one string), two ids -- so both have to be mapped or the onboarding name is never claimed.
    public const int ClaimNewUserNameMessageEvent = 879; // AS3-verified (direct read): registry _SafeCls_2046.as `_composers[879] = _SafeCls_3401`
    public const int CheckUserNameMessageEvent = 413; // AS3-verified (ghost fix): AvatarEditorMessageHandler::checkName()
    public const int GetWardrobeMessageEvent = 2210; // AS3-verified (ghost fix): AvatarEditorMessageHandler::getWardrobe()
    public const int SaveWardrobeOutfitMessageEvent = 116; // AS3-verified (name-only): AvatarEditorMessageHandler::saveWardrobeOutfit()
    public const int BuyMarketplaceOfferMessageEvent = 252;
    public const int CancelMarketplaceOfferMessageEvent = 2096;
    public const int GetMarketplaceCanMakeOfferMessageEvent = 1493; // AS3-verified (old-revision trace): _SafeCls_3045 -> startOfferMaking() still exists in current revision at 1493
    public const int GetMarketplaceConfigurationMessageEvent = 780;
    public const int GetMarketplaceItemStatsEvent = 1552;
    public const int GetMarketplaceOffersMessageEvent = 2731;
    public const int GetMarketplaceOwnOffersMessageEvent = 2086;
    public const int MakeOfferMessageEvent = 3695; // AS3-verified (direct read): MarketplaceModel::makeOffer() -> connection.send(new _SafeCls_2606(price,category,refs)) @3695; corrects a never-verified baseline value that actually belonged to DefaultSanctionMessageEvent
    public const int RedeemMarketplaceOfferCreditsMessageEvent = 3546;
    public const int GetAchievementsEvent = 2435; // UNRESOLVED: collided with the AS3-verified WiredSetRoomSettingsMessageEvent (2553); this constant's own value had zero backing (only weak event-side candidates found, wrong side for this incoming message) - flagged for a future retrace
    public const int AddSpamWallPostItMessageEvent = 2684; // AS3-verified (direct read, both revisions): SpamWallPostItWidgetHandler::processWidgetMessage() RWSWPFEE_SEND_POSTIT_DATA -> connection.send(new _SafeCls_3639(...)) @2684; corrects a self-inflicted regression - the prior fix mistook the incoming _SafeCls_3830 handler-param class for the outgoing composer
    public const int CreditFurniRedeemMessageEvent = 434;
    public const int DiceOffMessageEvent = 259; // AS3-verified (direct read, both revisions): useObject() ROFCAE_DICE_OFF -> _SafeCls_3356
    public const int EnterOneWayDoorMessageEvent = 1753; // AS3-verified (direct read, both revisions): useObject() ROFCAE_ENTER_ONEWAYDOOR -> _SafeCls_3749

    public const int GetGuildFurniContextMenuInfoMessageEvent = 826; // AS3-verified: RoomObjectEventHandler case "ROWRE_GUILD_FURNI_CONTEXT_MENU" -> send(objectId, guildId); registry _composers[826] = _SafeCls_3874(int objectId, int guildId)

    public const int PlacePostItMessageEvent = 1122; // AS3-verified (direct read, both revisions): room engine placeObject() furniture_is_stickie branch -> _SafeCls_3815 @1122 (old _SafeCls_3355@174; slot freed by correcting PlaceObjectMessageEvent above)
    public const int PresentOpenMessageEvent = 2485;
    public const int RentableSpaceCancelRentMessageEvent = 61; // AS3-verified (old-revision trace): _SafeCls_2575 -> cancelRent() still exists in current revision at 61
    public const int RentableSpaceConfigureMessageEvent = 4601; // Vortex-custom (not in official AS3 dumps): confirmed via vortex-client's own registry (_composers[4601] = ConfigureRentableSpaceMessageComposer) and commit f3bba54 "feat(rentablespace): add config message, compositors and updated display widget"
    public const int RentableSpaceGetConfigMessageEvent = 4600; // Vortex-custom (not in official AS3 dumps): confirmed via vortex-client's own registry (_composers[4600] = GetRentableSpaceConfigMessageComposer) and commit e8dc43d "chore(protocol): register rentable space (4600/4601) and pet (3072/3073) message IDs"
    public const int RentableSpaceRentMessageEvent = 3165; // AS3-verified (old-revision trace): _SafeCls_2674 -> rentSpace() still exists in current revision at 3165
    public const int RentableSpaceStatusMessageEvent = 2626; // AS3-verified: getRentableSpaceStatus() (exact name match). Note: RentableSpaceGetConfigMessageEvent (4600) also candidate-matched this same AS3 message but with a weaker name fit - left untouched, likely a distinct Vortex-specific config feature paired with RentableSpaceConfigureMessageEvent (4601)
    public const int RoomDimmerChangeStateMessageEvent = 3894;
    public const int RoomDimmerGetPresetsMessageEvent = 3145;
    public const int RoomDimmerSavePresetMessageEvent = 130;
    public const int SetAdjacentCustomStackingHeightEvent = 3315; // AS3-verified (direct read): _composers[3315] = _SafeCls_3040(objectId:int, down:Boolean), sent by CustomStackHeightWidget.sendAdjacentHeightRequest for its move_up/move_down buttons
    public const int SetCustomStackingHeightEvent = 3045; // AS3-verified (old-revision trace): _SafeCls_3545 -> onMultiWalkChange() still exists in current revision at 3045
    public const int SetMannequinFigureEvent = 2301; // AS3-verified (old-revision trace): _SafeCls_3486 -> saveOutfit() still exists in current revision at 2301
    public const int SetMannequinNameEvent = 606; // AS3-verified (old-revision trace): MannequinWidget::saveOutfitName() still exists in current revision at 606
    public const int SetRandomStateMessageEvent = 1942; // AS3-verified (direct read, both revisions): room engine changeRoomObjectState() param5==true branch (floor item, param3==10) -> connection.send(new _SafeCls_3726(objectId,state)) @1942 (old _SafeCls_3310@938)
    public const int SetRoomBackgroundColorDataEvent = 1647; // AS3-verified 2026-08-07: _composers[1647] in the WIN63-202607011411 registry, sent by BackgroundColorFurniWidget's apply button as (objectId, hue, saturation, lightness). The earlier search missed it because the widget sends it directly rather than through RoomSession, so it sits with no send* method near the other room-engine composers
    public const int SpinWheelOfFortuneMessageEvent = 3625; // AS3-verified (direct read, both revisions): useObject() ROFCAE_USE_HABBOWHEEL -> _SafeCls_3425
    public const int ThrowDiceMessageEvent = 1673; // AS3-verified (direct read, both revisions): useObject() ROFCAE_DICE_ACTIVATE -> _SafeCls_3154
    public const int GetResolutionAchievementsMessageEvent = 1760; // AS3-verified 2026-08-13 (re-derived): _composers[1760] = _SafeCls_2562, sent by AchievementsResolutionController and by the room engine on ROWRE_ACHIEVEMENT_RESOLUTION_OPEN. Two ints (stuffId, achievementId), not an empty body
    public const int ResetResolutionAchievementMessageEvent = 916; // AS3-verified 2026-08-13: _composers[916] = _SafeCls_3330, sent by AchievementsResolutionController::resetResolution. One int (stuffId). Had no header at all
    public const int GetPetInventoryEvent = 3891;
    public const int EventLogMessageEvent = 3809; // AS3-verified (shape-only): ClubExtendController::onOffer()
    public const int LagWarningReportMessageEvent = 481;
    public const int LatencyPingReportMessageEvent = 1744;
    public const int LatencyPingRequestMessageEvent = 544;
    public const int PerformanceLogMessageEvent = 3983; // AS3-verified (shape-only): PerformanceTracker::sendReport()
    public const int AvatarEffectActivatedEvent = 3022;
    public const int AvatarEffectSelectedEvent = 2362;
    public const int CanCreateRoomMessageEvent = 2617;
    public const int ConvertGlobalRoomIdMessageEvent = 584;
    public const int CreateFlatMessageEvent = 354;
    public const int ForwardToSomeRoomMessageEvent = 3427;
    public const int GetGuestRoomMessageEvent = 2603;
    public const int GetUserFlatCatsMessageEvent = 235;
    public const int GuildBaseSearchMessageEvent = 3744;
    public const int MyFriendsRoomsSearchMessageEvent = 1903;
    public const int MyGuildBasesSearchMessageEvent = 2224;
    public const int MyRoomsSearchMessageEvent = 361;
    public const int RemoveOwnRoomRightsRoomMessageEvent = 260;
    public const int SetRoomSessionTagsMessageEvent = 3101;
    public const int UpdateHomeRoomMessageEvent = 1817;
    public const int AssignRightsMessageEvent = 373;
    public const int BanUserWithDurationMessageEvent = 120;
    public const int KickUserMessageEvent = 2748;
    public const int LetUserInMessageEvent = 963;
    public const int MuteAllInRoomEvent = 32;
    public const int MuteUserMessageEvent = 2339;
    public const int RemoveAllRightsMessageEvent = 159;
    public const int RemoveRightsMessageEvent = 3444;
    public const int UnbanUserFromRoomMessageEvent = 2804;
    public const int UnmuteUserMessageEvent = 498;
    public const int DeleteRoomMessageEvent = 701;
    public const int GetBannedUsersFromRoomMessageEvent = 2702;
    public const int GetCustomRoomFilterMessageEvent = 790;
    public const int GetFlatControllersMessageEvent = 342;
    public const int GetRoomSettingsMessageEvent = 256;
    public const int SaveRoomSettingsMessageEvent = 725;
    public const int UpdateRoomCategoryAndTradeSettingsEvent = 3946;
    public const int UpdateRoomFilterMessageEvent = 1622; // AS3-verified (shape-only): RoomFilterCtrl::addBadWord()
    public const int RequestFurniInventoryEvent = 41;
    public const int RequestFurniInventoryWhenNotInRoomEvent = 3862; // AS3-verified (direct read, both revisions): FurniModel::requestInitialization() !_isInRoom branch -> connection.send(new _SafeCls_3731()) @3862 (old _SafeCls_3534@3674)
    public const int RequestRoomPropertySet = 2292; // AS3-verified 2026-08-22 (WIN63 registry + sender): _composers[2292] = _SafeCls_2323(int), HabboCatalog.as:3236. 3300 was in no registry table.
    public const int CancelTypingMessageEvent = 2718;
    public const int ChatMessageEvent = 3034;
    public const int ShoutMessageEvent = 1763;
    public const int StartTypingMessageEvent = 2106;
    public const int WhisperMessageEvent = 1697;
    public const int AcceptFriendMessageEvent = 1772;
    public const int DeclineFriendMessageEvent = 2778;
    public const int FindNewFriendsMessageEvent = 546;
    public const int FollowFriendMessageEvent = 886;
    public const int FriendListUpdateMessageEvent = 3679;
    public const int GetFriendRequestsMessageEvent = 3797;
    public const int GetMessengerHistoryEvent = 727;
    public const int HabboSearchMessageEvent = 3686;
    public const int MessengerInitMessageEvent = 3278;
    public const int RemoveFriendMessageEvent = 3005;
    public const int RequestFriendMessageEvent = 1;
    public const int SendMsgMessageEvent = 3357;
    public const int SendRoomInviteMessageEvent = 617;
    public const int SetRelationshipStatusMessageEvent = 1773;
    public const int VisitUserMessageEvent = 1515;
    public const int SetChatPreferencesMessageEvent = 1149; // AS3-verified (ghost fix): HabboFreeFlowChat::sendChatPreferences()
    public const int SetChatStylePreferenceEvent = 2634; // AS3-verified (direct read, both revisions): HabboFreeFlowChat::set preferedChatStyle() -> connection.send(new _SafeCls_2257(chatStyle,fontSizeMode)) @2634 (old _SafeCls_1812(chatStyle)@3964; new revision added a 2nd param for font size)
    public const int SetIgnoreRoomInvitesMessageEvent = 1332; // AS3-verified (direct read, both revisions): OtherSettingsView ignore_room_invites_checkbox -> connection.send(new _SafeCls_2539(ignored)) @1332 (old _SafeCls_3525@3617)
    public const int SetNewNavigatorWindowPreferencesMessageEvent = 1276;
    public const int SetRoomCameraPreferencesMessageEvent = 3917; // AS3-verified (direct read, both revisions): OtherSettingsView disable_room_camera_follow_checkbox -> connection.send(new _SafeCls_3911(disabled)) @3917 (old _SafeCls_3094@3914)
    public const int SetSoundSettingsEvent = 3662; // AS3-verified (direct read, both revisions): HabboSoundManagerFlash10::storeVolumeSetting() -> connection.send(new _SafeCls_2171(trax,furni,generic)) @3662 (old _SafeCls_2070@2608; slot freed by correcting GuideSessionCreateMessageEvent above). NOTE: those are the CONSTRUCTOR args; the composer pushes them back to front, so the wire order is generic, furni, trax - see SetSoundSettingsMessageParser.
    public const int SetUIFlagsMessageEvent = 3653;
    public const int GetDiscordPreferencesMessageEvent = 2883; // AS3-verified (direct read): _composers[2883] = _SafeCls_2988 (empty, no-arg) @ communication/_SafeCls_2046.as:880, sent by DiscordSettingsController::initComponent()
    public const int SetDiscordPreferencesMessageEvent = 2304; // AS3-verified (direct read): _composers[2304] = _SafeCls_3638(version, showHabbo, shareActivity, hideInHiddenRooms, allowJoining) @ communication/_SafeCls_2046.as:792, sent by DiscordSettingsController::updatePreferences()
    #endregion

    #region Vortex-specific (no AS3 backing)
    // These headers do NOT exist in any Habbo client. They belong to the in-client furni editor, a
    // Vortex-only staff tool, and are matched by hand against vortex-modern-client's
    // HabboMessages.ts. The 8000-8999 band was picked because it is empty in BOTH registries (0
    // occurrences in this file and in HabboMessages.ts as of 2026-07-25) and is clear of the 9xxx
    // placeholders above, which are unresolved *real* messages awaiting a retrace — do not allocate
    // Vortex-specific headers there. Never renumber one side alone.
    public const int VortexGetFurniEditorDataMessageEvent = 8001;
    public const int VortexApplyFurniEditMessageEvent = 8003;
    public const int VortexGetFurniDefinitionMessageEvent = 8005;
    public const int VortexApplyFurniDefinitionMessageEvent = 8007;

    // Fishing takes 8100-8199 of the same band, leaving the furni editor room to grow. Odd for
    // events (client->server), even for composers, so a request and its reply sit next to each
    // other. The client sends three things and decides none of them: it names a sighting the server
    // issued, a record the server wrote, or a derby the server scheduled.
    // See the client's docs/vortex-original/fishing.md, sections 3 to 5.
    // Origins fishing is a SESSION, not a cast per fish: the client starts one and then only
    // listens until the spot runs dry. 8101 was VortexFishingCastMessageEvent and modelled the wrong
    // interaction.
    public const int VortexStartFishingMessageEvent = 8101;
    public const int VortexFishingMountCatchMessageEvent = 8103;
    public const int VortexFishingJoinDerbyMessageEvent = 8105;
    public const int VortexStopFishingMessageEvent = 8107;

    // The player's whole Hook Havoc attempt, replayed server-side against the seed it was given.
    public const int VortexHookHavocInputMessageEvent = 8109;

    // Raid protection. These two ARE the AIR 1.0.31 ids -- _composers[206] and _composers[2687] --
    // and they are safe to take because neither appears in the WIN63-202607011411 `_composers`
    // table: this revision's target client sends nothing on either. (206 is live in WIN63 in the
    // OTHER direction, as UserTypingMessageComposer below; the two tables are independent.)
    //
    // The three composers could not follow suit: AIR binds them at 734 and 3620, which WIN63
    // already spends on ChatReviewSessionOfferedToGuide and SecondsUntil, and 3553. Taking those
    // would have silenced two working messages, so the two that collide were reassigned to 3554
    // and 3555 -- see RaidProtection*MessageComposer below, which is where the real numbers are.
    // When this revision moves to an AIR-era client, those constants change and nothing else does.
    public const int GetRaidProtectionSettingsMessageEvent = 206;
    public const int SaveRaidProtectionSettingsMessageEvent = 2687;
    #endregion
}

internal static class MessageComposer
{
    // NEW since last revision
    public const int IncomeRewardNotificationMessageComposer = 1914;
    public const int GetCustomFilterResultMessageComposer = 2231; // AS3-verified (direct read): WIN63 registry _SafeCls_2046.as _SafeStr_4546[2231] = _SafeCls_3579; WordFilterSettingsView is the only consumer. The previous value was a placeholder with no handler behind it
    public const int ModifyCustomFilterResultMessageComposer = 3622; // AS3-verified (direct read): WIN63 registry _SafeCls_2046.as _SafeStr_4546[3622] = _SafeCls_3793; WordFilterSettingsView is the only consumer. The previous value was a placeholder with no handler behind it
    public const int FurniListRemoveMultipleComposer = 1268; // AS3-verified (direct read): _SafeStr_4546[1268] = _SafeCls_3469 (parser _SafeCls_3854: `stripIds:Vector.<int>`), consumed by the inventory controller; was 149, which has no entry in the WIN63 registry at all
    public const int BlockListMessageComposer = 505; // AS3-verified (direct read): _SafeStr_4546[505] = _SafeCls_2982, whose only consumer is BlockedUsersManager.as; was 214, which has no entry in the WIN63 registry at all
    public const int BlockUserUpdateMessageComposer = 1825; // AS3-verified (direct read): _SafeStr_4546[1825] = _SafeCls_2460, consumed by BlockedUsersManager.as / RoomUsersHandler.as; was 219, which has no entry in the WIN63 registry at all

    // AS3-verified (direct read): the three events DailyTasksController.as registers in its
    // constructor, resolved in WIN63's own registry _SafeCls_2046.as — 1824 = _SafeCls_3179
    // (onActiveDailyTasks, line 1295), 2506 = _SafeCls_2859 (onTasksAdded, line 1414), 1065 =
    // _SafeCls_3449 (onTaskUpdated, line 1177). The previous 2507/1762 were inherited from the
    // pre-Vortex header table: 1762 is really CfhTopicsInit, which this file already declares
    // correctly further down, so the two constants collided on the same wire id. 2506 (tasks
    // added) has no constant: nothing ever sent it, so its composer was removed in M0 bis.
    public const int DailyTasksActiveListMessageComposer = 1824;
    public const int DailyTasksTaskUpdateMessageComposer = 1065;
    public const int UserPurchasableChatStyleChangedMessageComposer = 3971; // AS3-verified (direct read): _SafeStr_4546[3971] = _SafeCls_2210, consumed by SessionDataManager.as; was 2894, which has no entry in the WIN63 registry at all
    public const int UserPurchasableChatStylesMessageComposer = 3774; // AS3-verified (direct read): _SafeStr_4546[3774] = _SafeCls_2023, consumed by SessionDataManager.as; was 2255, which has no entry in the WIN63 registry at all
    public const int BanInfoMessageComposer = 2557;
    public const int MyCfhReportStatusMessageComposer = 3809; // AS3-verified 2026-08-22 (WIN63 registry + sender): _events[3809] = _SafeCls_1752, HabboHelp::onMyCfhReportStatusMessageEvent. 2474 was in no registry table.

    // _events[2910] = _SafeCls_3439 answers both log requests. It says which list it is: type 0 is
    // a chest and the id is the chest, type 1 is a room and the id is the room.
    public const int TreasureHuntFailMessageComposer = 3790; // AS3-verified 2026-08-22 (WIN63 registry + sender): _events[3790] = _SafeCls_3474, notifications::onTreasureHuntFail. 3822 was in no registry table.
    public const int TreasureHuntFirstWinnerMessageComposer = 3257; // AS3-verified 2026-08-22 (WIN63 registry + sender): _events[3257] = _SafeCls_3856, notifications::onTreasureHuntFirstWinner. 1232 was in no registry table.
    public const int TreasureHuntUpdateMessageComposer = 377;
    public const int RecyclerFinishedMessageComposer = 3617; // AS3-verified (registry): _SafeCls_2046[3617] = _SafeCls_2078 (parser _SafeCls_2299 -> recyclerFinishedStatus:int, prizeId:int). Was 281, an id with no registry entry at all.
    public const int RecyclerPrizesMessageComposer = 3367; // AS3-verified (registry): _SafeCls_2046[3367] = _SafeCls_2027 (parser _SafeCls_2243 -> prizeLevels:Vector.<PrizeLevelMessageData>). Was 3783, an id with no registry entry at all.
    public const int RecyclerStatusMessageComposer = 2166; // AS3-verified (registry): _SafeCls_2046[2166] = _SafeCls_2004 (parser _SafeCls_2165 -> recyclerStatus:int, recyclerTimeoutSeconds:int). Was 1919, an id with no registry entry at all.
    #region Outgoing

    public const int CantConnectMessageComposer = 2430;
    public const int CloseConnectionMessageComposer = 3404;
    public const int FlatAccessibleMessageComposer = 2051;
    public const int GamePlayerValueMessageComposer = 1052;
    public const int HanditemConfigurationMessageComposer = 2942;
    public const int OpenConnectionMessageComposer = 611;
    public const int RoomForwardMessageComposer = 3339;
    public const int RoomReadyMessageComposer = 2349;
    public const int YouArePlayingGameMessageComposer = 1600;
    public const int BotCommandConfigurationComposer = 2463; // AS3-verified (ghost fix): onBotCommandConfigurationEvent @ BotSkillConfigurationViewBase
    public const int BotSkillListUpdateComposer = 1293; // AS3-verified (ghost fix): onBotSkillListUpdateEvent @ RoomDesktop
    public const int AvailabilityStatusMessageComposer = 1350;
    public const int MaintenanceStatusMessageComposer = 1737;
    public const int SelectInitialRoomComposer = 3624; // AS3-verified (ghost fix): onSelectInitialRoomResponse @ RoomPicker

    // AS3-verified (direct read of the 2026 registry): _events[3050] = _SafeCls_3346, the event
    // SongDiskProductViewCatalogWidget::onOfficialSongIdMessageEvent() subscribes to. NOT 2264:
    // that was win63_version's id, and the 2026 registry reassigned 2264 to
    // WeeklyCompetitiveFriendsLeaderboardEvent (_SafeCls_2046.as:1373 vs :1502), so the old value
    // made this answer arrive at the client as a leaderboard. The client has been registered at
    // 3050 all along (HabboMessages.ts, "=== SOUND (Trax) ===").
    public const int OfficialSongIdMessageComposer = 3050;
    public const int PlayListMessageComposer = 1242;
    public const int TraxSongInfoMessageComposer = 2278;

    // AS3-verified (direct read of the 2026 registry): _SafeStr_4546[1930] = _SafeCls_3625, the
    // event HabboMusicController::onSongDiskInventoryMessage() subscribes to. Its parser reads a
    // count then that many (diskId, songId) pairs.
    public const int UserSongDisksInventoryMessageComposer = 1930;
    public const int AchievementComposer = 3981; // AS3-verified (ghost fix): onAchievement
    public const int AchievementsComposer = 1969; // AS3-verified (ghost fix): onAchievements
    public const int AchievementsScoreComposer = 3070;
    public const int BonusRareInfoMessageComposer = 3573;
    public const int BundleDiscountRulesetMessageComposer = 1073; // AS3-verified (ghost fix): onBundleDiscountRulesetMessageEvent @ HabboCatalog
    public const int CatalogIndexMessageComposer = 3666;
    public const int CatalogPageMessageComposer = 1660;
    public const int ClubGiftInfoComposer = 3422;
    public const int ClubGiftSelectedComposer = 1842;

    public const int GiftReceiverNotFoundComposer = 2735; // AS3-verified: onGiftReceiverNotFound @ HabboCatalog

    public const int GiftWrappingConfigurationComposer = 1369;
    public const int HabboClubExtendOfferMessageComposer = 3689;
    public const int HabboClubOffersMessageComposer = 419;
    public const int IsOfferGiftableEventMessageComposer = 1750; // UNRESOLVED: not found - no matching (OfferId, IsGiftable) response near the targeted-offers or club-gift flows located in either official AS3 revision
    public const int NotEnoughBalanceMessageComposer = 1038;
    public const int ProductOfferComposer = 1911; // AS3-verified (ghost fix): onProductOffer @ HabboCatalog
    public const int PurchaseErrorMessageComposer = 1029;
    public const int PurchaseNotAllowedMessageComposer = 2493;
    public const int PurchaseOKMessageComposer = 1570;
    public const int SellablePetPalettesMessageComposer = 3350; // AS3-verified (ghost fix): onSellablePalettes @ HabboCatalog

    public const int CallForHelpPendingCallsMessageComposer = 2987; // AS3-verified 2026-08-07: _SafeStr_4546[2987] -> the event class HabboHelp binds to onPendingCallsForHelp. Was 678, which the client maps to a class referenced nowhere else in the SWF -- so the packet was going out on a header nothing listens to
    public const int CallForHelpPendingCallsDeletedMessageComposer = 2440; // AS3-verified 2026-08-07: _SafeStr_4546[2440] -> onPendingCallsForHelpDeleted @ HabboHelp. Had no header at all; the composer existed and could never be registered
    public const int CallForHelpReplyMessageComposer = 2807; // AS3-verified (ghost fix): onCallForHelpReply @ CallForHelpManager
    public const int ChatReviewSessionOfferedToGuideMessageComposer = 734; // AS3-verified (ghost fix): onChatReviewSessionOfferedToGuide @ GuideSessionController
    public const int ChatReviewSessionResultsMessageComposer = 508; // AS3-verified (ghost fix): onChatReviewSessionResults @ GuideSessionController
    public const int ChatReviewSessionStartedMessageComposer = 286; // AS3-verified (ghost fix): onChatReviewSessionStarted @ GuideSessionController
    public const int ChatReviewSessionVotingStatusMessageComposer = 1881; // AS3-verified (ghost fix): onChatReviewSessionVotingStatus @ GuideSessionController
    public const int GuideOnDutyStatusMessageComposer = 1923;
    public const int GuideReportingStatusMessageComposer = 3725; // AS3-verified (ghost fix): onGuideReportingStatus @ HabboHelp
    public const int GuideSessionAttachedMessageComposer = 3274; // AS3-verified 2026-08-07: _SafeStr_4546[3274] -> _SafeCls_2443, bound to onGuideSessionAttached @ GuideSessionController. Payload is asGuide:bool, helpRequestType:int, helpRequestDescription:string, roleSpecificWaitTime:int
    public const int GuideSessionEndedMessageComposer = 2126; // AS3-verified (ghost fix): onGuideSessionEnded @ GuideSessionController
    public const int GuideSessionErrorMessageComposer = 2377; // AS3-verified (ghost fix): onGuideSessionError @ GuideSessionController
    public const int GuideSessionInvitedToGuideRoomMessageComposer = 776; // AS3-verified (ghost fix): onGuideSessionInvitedToGuideRoom @ GuideSessionController
    public const int GuideSessionMessageMessageComposer = 485; // AS3-verified (ghost fix): onGuideSessionMessage @ GuideSessionController
    public const int GuideSessionPartnerIsTypingMessageComposer = 3656; // AS3-verified (ghost fix): onGuideSessionPartnerIsTyping @ GuideSessionController
    public const int GuideSessionRequesterRoomMessageComposer = 2022; // AS3-verified (ghost fix): onGuideSessionRequesterRoom @ GuideSessionController
    public const int GuideSessionStartedMessageComposer = 3649; // AS3-verified 2026-08-07: _SafeStr_4546[3649] -> _SafeCls_3061, bound to onGuideSessionStarted @ GuideSessionController
    public const int IssueCloseNotificationMessageComposer = 3943; // AS3-verified (ghost fix): onIssueClose @ CallForHelpManager
    public const int QuizDataMessageComposer = 3999; // AS3-verified (ghost fix): onQuizData @ HabboWayQuizController
    public const int QuizResultsMessageComposer = 548; // AS3-verified (ghost fix): onQuizResults @ HabboWayQuizController

    public const int GuildFurniContextMenuInfoMessageComposer = 3220;
    public const int OneWayDoorStatusMessageComposer = 1778; // AS3-verified (ghost fix): onOneWayDoorStatus
    public const int PresentOpenedMessageComposer = 914;
    public const int RentableSpaceConfigMessageComposer = 4600; // Vortex-custom (not in official AS3 dumps): confirmed via vortex-client's own registry (_events[4600] = RentableSpaceConfigMessageEvent) and commit f3bba54 "feat(rentablespace): add config message, compositors and updated display widget"
    public const int RentableSpaceRentFailedMessageComposer = 3117; // AS3-verified (name-only): onRentableSpaceRentFailedMessage
    public const int RentableSpaceStatusMessageComposer = 2800;
    public const int RoomDimmerPresetsMessageComposer = 1093;
    public const int TradeOpenFailedComposer = 2855; // AS3-verified (direct read, both revisions): inventory handler::onTradingOpenFailed() -> _SafeCls_3581 @2855 (old _SafeCls_3311@2675)
    public const int TradingAcceptComposer = 560;
    public const int TradingCloseComposer = 699;
    public const int TradingCompletedComposer = 1070;
    public const int TradingConfirmationComposer = 3138;
    public const int TradingItemListComposer = 2275;
    public const int TradingNotOpenComposer = 3556;
    public const int TradingOpenComposer = 953;
    public const int MarketplaceBuyOfferResultComposer = 2249;
    public const int MarketplaceCancelOfferResultComposer = 2448;
    public const int MarketplaceCanMakeOfferResult = 789; // AS3-verified (old-revision trace): _SafeCls_3644 -> onMarketplaceCanMakeOfferResult() still exists in current revision at 789
    public const int MarketplaceConfigurationComposer = 1397;
    public const int MarketplaceItemStatsComposer = 2821;
    public const int MarketplaceMakeOfferResult = 2954; // AS3-verified (old-revision trace): _SafeCls_2273 -> onMarketplaceMakeOfferResult() still exists in current revision at 2954
    public const int MarketPlaceOffersComposer = 2442;
    public const int MarketPlaceOwnOffersComposer = 88;
    public const int PerkAllowancesMessageComposer = 1535;
    public const int RoomEntryTileMessageComposer = 2792;
    public const int RoomOccupiedTilesMessageComposer = 1235; // AS3-verified (name-only): onOccupiedTiles @ BCFloorPlanEditor
    public const int CreditBalanceComposer = 3642;

    // AS3-verified (client fix 5316023): the in-room figure_effect broadcast is 2624, not 3629.
    // WIN63 registry _SafeCls_2046.as maps _SafeStr_4546[2624] = _SafeCls_2589 (parser _SafeCls_3361:
    // userId, effectId, delayMilliSeconds) to RoomMessageHandler.onAvatarEffect. Header 3629 there is a
    // different message (AvatarEditorMessageHandler.onAvatarEffectSelected). Serializer field order
    // already matches the client's 2624 parser, so only the header number was wrong — this is why
    // worn effects never appeared on avatars in-room.
    public const int AvatarEffectMessageComposer = 2624;
    public const int CarryObjectMessageComposer = 2850;
    public const int DanceMessageComposer = 2217;
    public const int ExpressionMessageComposer = 1036;
    public const int SleepMessageComposer = 3517;
    public const int ChangeUserNameResultMessageComposer = 1621;

    // Was 2802, which the WIN63 registry assigns to Game2WeeklyFriendsLeaderboardEvent -- the check
    // result went out under a leaderboard header and no client ever routed it. The real id is 382:
    // registry _SafeCls_2046.as `_events[382] = _SafeCls_3600`, the parser NameChangeDialog and
    // NameChangeController both listen on.
    public const int CheckUserNameResultMessageComposer = 382;
    public const int FigureUpdateComposer = 132;
    public const int WardrobeMessageComposer = 1484; // AS3-verified (ghost fix): onWardrobe @ AvatarEditorMessageHandler

    public const int EmeraldBalanceMessageComposer = 583; // AS3-verified (ghost fix): onEmeraldBalance @ HabboCatalog
    public const int SilverBalanceMessageComposer = 3727; // AS3-verified (ghost fix): onSilverBalance @ HabboCatalog

    // AS3-verified this session, by reading the registry rather than a name: WIN63 binds
    // server->client 3407 to SelfDonationResultMessageEvent -- one of the 25 entries whose class
    // name survived obfuscation, which is how it was caught. The accept-friend result is 3707:
    // _SafeStr_4546[3707] = _SafeCls_2256, whose parser _SafeCls_1878 reads a count and then
    // _SafeCls_1782(wrapper) per entry -- two ints, senderId and errorCode -- which is exactly
    // what AcceptFriendResultMessageSerializer writes. HabboFriendList.onAcceptFriendResult is
    // the listener. 3707 is also the jukebox playlist request client->server; the two directions are
    // separate tables and do not collide.
    public const int AcceptFriendResultComposer = 3707;
    public const int ConsoleMessageHistoryComposer = 933;
    public const int FindFriendsProcessResultComposer = 2642;
    public const int FollowFriendFailedComposer = 240;
    public const int FriendListFragmentMessageComposer = 2641;
    public const int FriendListUpdateComposer = 3611;
    public const int FriendRequestsComposer = 1120;
    public const int HabboSearchResultComposer = 2637;
    public const int InstantMessageErrorComposer = 3501;
    public const int MessengerErrorComposer = 358;
    public const int MessengerInitComposer = 1590;
    public const int NewConsoleMessageComposer = 468;
    public const int NewFriendRequestComposer = 1860;
    public const int RoomInviteComposer = 3194;
    public const int InterstitialMessageComposer = 3898;
    public const int ForumDataMessageComposer = 3965; // AS3-verified (ghost fix): onForumData @ GroupForumController
    public const int ForumsListMessageComposer = 1373; // AS3-verified (ghost fix): onForumsList @ GroupForumController
    public const int ForumThreadsMessageComposer = 1222; // AS3-verified (old-revision trace): _SafeCls_3128 -> onThreadList() still exists in current revision at 1222
    public const int PostMessageMessageComposer = 2475; // AS3-verified (ghost fix): onPostMessageMessage @ GroupForumController

    public const int PostThreadMessageComposer = 956; // AS3-verified: onPostThreadMessage @ GroupForumController

    public const int ThreadMessagesMessageComposer = 3603; // AS3-verified (old-revision trace): _SafeCls_2696 -> onThreadMessageList() still exists in current revision at 3603
    public const int UnreadForumsCountMessageComposer = 866; // AS3-verified (name-only): onUnreadForumsCountMessage @ GroupForumController
    public const int UpdateMessageMessageComposer = 2988; // AS3-verified (ghost fix): onUpdateMessage @ GroupForumController
    public const int UpdateThreadMessageComposer = 1146; // AS3-verified (ghost fix): onUpdateThread @ GroupForumController
    public const int IncomeRewardClaimResponseMessageComposer = 2984;
    public const int IncomeRewardStatusMessageComposer = 3976;
    public const int BotAddedToInventoryComposer = 3570; // AS3-verified (old-revision trace): _SafeCls_3152 -> onBotAdded() still exists in current revision at 3570
    public const int BotInventoryComposer = 682;
    public const int BotRemovedFromInventoryComposer = 2032; // AS3-verified (old-revision trace): still exists in current revision at 2032
    public const int BadgePointLimitsComposer = 3510; // AS3-verified (ghost fix): onBadgePointLimits
    public const int BadgeReceivedComposer = 2840; // AS3-verified (ghost fix): onBadgeReceived
    public const int BadgeInfoComposer = 1153; // AS3-verified 2026-09-19 (WIN63 registry + consumer): _SafeStr_4546[1153] = _SafeCls_3875, parser unknowns/_SafePkg_3206/_SafeCls_4369 (badgeId, badgeCode, ownerCount, badgeRarityId), read by FurnitureBadgeDisplayWidgetHandler.onBadgeInfo
    public const int BadgeLeaderboardResultComposer = 2503; // AS3-verified 2026-09-19 (WIN63 registry + consumer): _SafeStr_4546[2503] = _SafeCls_3446, parser unknowns/_SafePkg_1891/_SafeCls_3883, read by groups/badge_leaderboard/BadgeLeaderboardController.onBadgeLeaderboardResult
    public const int BadgesComposer = 2748;
    public const int IsBadgeRequestFulfilledComposer = 2121; // AS3-verified (direct read, both revisions): moderation handler::onBadgeRequestFulfilledEvent() -> _SafeCls_2930 @2121 (old _SafeCls_3282@2295)
    public const int NavigatorCollapsedCategoriesMessageComposer = 1754;
    public const int NavigatorLiftedRoomsComposer = 1761;
    public const int NavigatorMetaDataComposer = 24;
    public const int NavigatorSavedSearchesComposer = 432; // AS3-verified (name-only): onSavedSearches
    public const int NavigatorSearchResultBlocksComposer = 3708;
    public const int NewNavigatorPreferencesComposer = 3937;
    public const int BannedUsersFromRoomComposer = 845;
    public const int FlatControllersComposer = 726;

    // AS3-verified: the client's registry binds 1172 to _SafeCls_3486 (_SafeCls_2046.as:1202),
    // whose parser _SafeCls_2723 reads a single boolean and exposes it as `allMuted`; the navigator
    // stores it on the room data as `allInRoomMuted` (navigator/_SafeCls_1951.as:178).
    public const int MuteAllInRoomComposer = 1172;
    public const int RoomSettingsDataComposer = 791;
    public const int RoomSettingsSavedComposer = 1783;
    public const int UserUnbannedFromRoomComposer = 2089;
    public const int CurrentTimingCodeMessageComposer = 3076;
    public const int CfhChatlogComposer = 3880; // AS3-verified (ghost fix): onCfhChatlog @ ModerationMessageHandler
    public const int IssueDeletedMessageComposer = 122; // AS3-verified (ghost fix): onIssueDeleted @ ModerationMessageHandler

    public const int IssueInfoMessageComposer = 3585; // AS3-verified: onIssueInfo @ ModerationMessageHandler (== its ORIGINAL pre-fix value - was never actually wrong)

    public const int IssuePickFailedMessageComposer = 940;
    public const int ModeratorActionResultMessageComposer = 2960; // AS3-verified (ghost fix): onModeratorActionResult @ ModerationMessageHandler
    public const int ModeratorCautionComposer = 2619;
    public const int ModeratorInitMessageComposer = 757; // AS3-verified (ghost fix): onModeratorInit @ ModerationMessageHandler
    public const int ModeratorMessageComposer = 3885; // AS3-verified (direct read): _SafeStr_4546[3885] = _SafeCls_2629, parser reads (message, url)
    public const int ModeratorRoomInfoComposer = 251; // AS3-verified (direct read): _SafeStr_4546[251] = _SafeCls_2831 -> onRoomInfo @ ModerationMessageHandler
    public const int ModeratorToolPreferencesComposer = 1898; // AS3-verified (ghost fix): onModeratorToolPreferences @ ModerationMessageHandler
    public const int ModeratorUserInfoComposer = 2589; // AS3-verified (ghost fix): onUserInfo @ ModerationMessageHandler
    public const int RoomChatlogComposer = 2886; // AS3-verified (ghost fix): onRoomChatlog @ ModerationMessageHandler
    public const int RoomVisitsComposer = 497; // AS3-verified (ghost fix): onRoomVisits @ ModerationMessageHandler
    public const int SanctionInfoMessageComposer = 1634; // AS3-verified (direct read): _SafeStr_4546[1634] = _SafeCls_2679 -> onSanctions @ ModerationMessageHandler
    public const int UserBannedMessageComposer = 3621;
    public const int UserChatlogComposer = 3732;

    public const int ChatMessageComposer = 311;
    public const int FloodControlMessageComposer = 3614; // AS3-verified (ghost fix): onFloodControl @ RoomChatHandler
    public const int RemainingMutePeriodComposer = 2129; // AS3-verified (ghost fix): onRemainingMutePeriod @ RoomChatHandler
    public const int RoomChatSettingsMessageComposer = 594; // AS3-verified (ghost fix): onRoomChatSettings @ HabboFreeFlowChat
    public const int RoomFilterSettingsMessageComposer = 3208; // AS3-verified (ghost fix): onRoomFilterSettings
    public const int ShoutMessageComposer = 1776;
    public const int UserTypingMessageComposer = 206;
    public const int WhisperMessageComposer = 3072;

    public const int PetAddedToInventoryComposer = 3653; // AS3-verified (old-revision trace): still exists in current revision at 3653
    public const int PetInventoryComposer = 1200;
    public const int PetRemovedFromInventoryComposer = 3013; // AS3-verified (old-revision trace): _SafeCls_3331 -> onPetRemoved() still exists in current revision at 3013
    public const int AuthenticationOKMessageComposer = 230;
    public const int CompleteDiffieHandshakeComposer = 3401;
    public const int DisconnectReasonComposer = 4000;
    public const int InitDiffieHandshakeComposer = 3309;
    public const int IsFirstLoginOfDayComposer = 2313;
    public const int NoobnessLevelMessageComposer = 70; // shape+AS3-verified: onNoobnessLevelEvent @ SessionDataManager (_SafeCls_2080)
    public const int PingMessageComposer = 1407;
    public const int UserObjectComposer = 3985;
    public const int UserRightsMessageComposer = 3599;
    public const int UserNftChatStylesMessageComposer = 2996;
    public const int LatencyPingResponseMessageComposer = 188;
    public const int CommunityGoalHallOfFameMessageComposer = 363;
    public const int CommunityGoalProgressMessageComposer = 283;
    public const int ConcurrentUsersGoalProgressMessageComposer = 1003;
    public const int QuestCancelledMessageComposer = 1425; // AS3-verified (ghost fix): onQuestCancelled
    public const int QuestCompletedMessageComposer = 1272; // AS3-verified (name-only): onQuestCompleted
    public const int QuestDailyMessageComposer = 1417;
    public const int QuestMessageComposer = 54; // AS3-verified (ghost fix): onQuest
    public const int QuestsMessageComposer = 3398; // AS3-verified (ghost fix): onQuests
    public const int SeasonalQuestsMessageComposer = 1390; // AS3-verified (ghost fix): onSeasonalQuests
    public const int ActivityPointsMessageComposer = 509;
    public const int ClubGiftNotificationComposer = 1023;
    public const int HabboAchievementNotificationMessageComposer = 639;
    public const int HabboActivityPointNotificationMessageComposer = 2046; // AS3-verified (ghost fix): onActivityPointNotification @ HabboCatalog
    public const int InfoFeedEnableMessageComposer = 1936;
    public const int MOTDNotificationComposer = 1330;
    public const int NotificationDialogMessageComposer = 2243;
    public const int PetLevelNotificationComposer = 1702;
    public const int UnseenItemsComposer = 3059;
    public const int PetCommandsMessageComposer = 332; // AS3-verified (old-revision trace): _SafeCls_2541 -> onEnabledPetCommands() still exists in current revision at 332
    public const int PetExperienceComposer = 946; // AS3-verified (ghost fix): onPetExperience
    public const int PetFigureUpdateComposer = 3796;
    public const int PetInfoMessageComposer = 3192;
    public const int PetLevelUpdateComposer = 3104; // AS3-verified (ghost fix): onPetLevelUpdate @ RoomUsersHandler
    public const int PetPlacingErrorComposer = 3195; // AS3-verified (ghost fix): onPetPlacingError @ RoomUsersHandler
    public const int PetRespectFailedComposer = 31;
    public const int PetStatusUpdateComposer = 2753;
    public const int AchievementResolutionCompletedMessageComposer = 1166; // AS3-verified (ghost fix): onAchievementResolutionCompleted
    public const int AchievementResolutionProgressMessageComposer = 1844;
    public const int AchievementResolutionsMessageComposer = 3143; // AS3-verified (ghost fix): onAchievementResolutions

    public const int FigureSetIdsComposer = 1231;
    public const int AccountPreferencesComposer = 724;
    public const int DiscordPreferencesComposer = 2767; // AS3-verified (direct read): _SafeStr_4546[2767] = _SafeCls_2938 @ communication/_SafeCls_2046.as:1453 -> parser _SafeCls_4070 -> DiscordPreferences.readFromData(): readInteger, then four readBoolean (one byte each)

    public const int CfhTopicsInitMessageComposer = 1762; // AS3-verified (ghost fix): onCfhTopics @ HabboHelp
    public const int SanctionStatusComposer = 1746; // AS3-verified (direct read, both revisions): HabboHelp::onSanctionStatusEvent()/onMySanctionStatusMessageEvent() -> _SafeCls_1807 @1746 (old _SafeCls_2129@3113)
    public const int YouAreControllerMessageComposer = 934;
    public const int YouAreNotControllerMessageComposer = 456;
    public const int YouAreOwnerMessageComposer = 1986;

    // Avatar-effect inventory acks, all five confirmed against the WIN63-202607011411 registry
    // (_SafeCls_2046.as).
    //
    // Selected was 1333 on the reasoning that "the client registers no handler for it, so it is
    // harmless either way". It does register one: AvatarEditorMessageHandler binds _SafeCls_3136 to
    // onAvatarEffectSelected, and the registry puts _SafeCls_3136 at 3629. The four others are bound
    // in the inventory handler, which is where that search stopped — the odd one out lives with the
    // avatar editor because selecting an effect is an editor action, not an inventory one. 1333
    // appears nowhere in the registry, so the ack PlayerEffectGrain sends on every effect change was
    // going out on a header nothing listens to.
    public const int AvatarEffectActivatedMessageComposer = 3814;
    public const int AvatarEffectAddedMessageComposer = 1577;
    public const int AvatarEffectExpiredMessageComposer = 2236;
    public const int AvatarEffectSelectedMessageComposer = 3629; // AS3-verified 2026-08-08: _SafeStr_4546[3629] -> _SafeCls_3136, payload one int (the effect type), which is what the composer already writes
    public const int AvatarEffectsMessageComposer = 2405;

    public const int AccountSafetyLockStatusChangeMessageComposer = 3913; // AS3-verified (name-only): onAccountSafetyLockStatusChanged @ SessionDataManager
    public const int ApproveNameMessageComposer = 1879;
    public const int ExtendedProfileMessageComposer = 1918;
    public const int GroupDetailsChangedMessageComposer = 12;
    public const int GuildCreatedMessageComposer = 2138; // AS3-verified (ghost fix): onGuildCreated @ HabboGroupsManager
    public const int GuildCreationInfoMessageComposer = 973; // AS3-verified (ghost fix): onGuildCreationInfo @ HabboGroupsManager
    public const int GuildEditFailedMessageComposer = 496; // AS3-verified (ghost fix): onGuildEditFailed @ HabboGroupsManager
    public const int GuildEditInfoMessageComposer = 1288; // AS3-verified (ghost fix): onGuildEditInfo @ HabboGroupsManager
    public const int GuildEditorDataMessageComposer = 1132; // AS3-verified (ghost fix): onGuildEditorData @ HabboGroupsManager
    public const int GuildMemberFurniCountInHQMessageComposer = 1402; // AS3-verified: HabboGroupsManager registers GuildMemberFurniCountInHQMessageEvent(onKickConfirmation), parser reads (int userId, int furniCount); registry _SafeStr_4546[1402] = _SafeCls_2145
    public const int GuildMemberMgmtFailedMessageComposer = 1735; // AS3-verified (direct read, both revisions): HabboGroupsManager onGuildMemberMgmtFailed -> _SafeCls_1768
    public const int GuildMembershipRejectedMessageComposer = 595; // AS3-verified (direct read, both revisions): HabboGroupsManager onGuildMembershipRejected -> _SafeCls_1930
    public const int GuildMembershipsMessageComposer = 3994; // AS3-verified (ghost fix): onGuildMemberships @ HabboCatalog
    public const int GuildMembershipUpdatedMessageComposer = 3477; // AS3-verified (direct read, both revisions): HabboGroupsManager onGuildMembershipUpdated -> _SafeCls_1730
    public const int GuildMembersMessageComposer = 403; // AS3-verified (direct read, both revisions): HabboGroupsManager onGuildMembers -> _SafeCls_2233
    public const int HabboGroupBadgesMessageComposer = 1400;
    public const int HabboGroupDeactivatedMessageComposer = 1948; // AS3-verified: HabboGroupsManager registers _SafeCls_2104(onGroupDeactivated) and its parser reads a single int groupId; registry _SafeStr_4546[1948]. The previous 2087 is GroupMembershipRequested, whose parser reads int + a 5-field MemberData block — the client was over-reading the buffer on every guild dissolution
    public const int GroupMembershipRequestedMessageComposer = 2087; // AS3-verified: HabboGroupsManager registers _SafeCls_2076(GuildMembersWindowCtrl.onMembershipRequested); parser reads (int groupId, MemberData requester); registry _SafeStr_4546[2087]
    public const int HabboGroupDetailsMessageComposer = 2847;
    public const int HabboGroupJoinFailedMessageComposer = 3356;
    public const int HabboUserBadgesMessageComposer = 1292;
    public const int IgnoredUsersMessageComposer = 191;
    public const int IgnoreResultMessageComposer = 253;
    public const int PetRespectNotificationComposer = 1784;
    public const int RelationshipStatusInfoComposer = 3360;
    public const int RespectNotificationMessageComposer = 2686;
    public const int ScrSendKickbackInfoMessageComposer = 3887;
    public const int ScrSendUserInfoComposer = 1097;
    public const int FavoriteMembershipUpdateMessageComposer = 1259; // AS3-verified (name-only): onFavoriteMembershipUpdate @ RoomUsersHandler
    public const int FloorHeightMapMessageComposer = 2885;
    public const int FurnitureAliasesMessageComposer = 154;
    public const int HeightMapMessageComposer = 2260;
    public const int HeightMapUpdateMessageComposer = 3279;
    public const int ItemAddMessageComposer = 3733;
    public const int ItemDataUpdateMessageComposer = 540; // AS3-verified (ghost fix): onItemDataUpdate
    public const int ItemRemoveMessageComposer = 2859;
    public const int ItemsMessageComposer = 3379;
    public const int ItemStateUpdateMessageComposer = 834; // AS3-verified (ghost fix): onItemStateUpdate
    public const int ItemUpdateMessageComposer = 1198;
    public const int ObjectAddMessageComposer = 368;
    public const int ObjectDataUpdateMessageComposer = 2329;
    public const int ObjectRemoveMessageComposer = 1916;

    public const int ObjectsMessageComposer = 2104;
    public const int ObjectUpdateMessageComposer = 114;
    public const int RoomEntryInfoMessageComposer = 2914;

    public const int RoomPropertyMessageComposer = 1956; // AS3-verified: onRoomProperty (best of a weak field, reasonably confident)

    public const int RoomVisualizationSettingsComposer = 2986; // AS3-verified (ghost fix): onRoomVisualizationSettings @ RoomSessionManager
    public const int SlideObjectBundleMessageComposer = 2794;
    public const int UserChangeMessageComposer = 3798;
    public const int UserRemoveMessageComposer = 3693;
    public const int UsersMessageComposer = 996;
    public const int UserUpdateMessageComposer = 2613;
    public const int CanCreateRoomComposer = 2831; // AS3-verified (old-revision trace): _SafeCls_3693 -> onCanCreateRoom() still exists in current revision at 2831
    public const int DoorbellMessageComposer = 466;
    public const int FavouritesComposer = 1055;
    public const int FlatAccessDeniedMessageComposer = 1086;
    public const int FlatCreatedComposer = 1712;
    public const int GetGuestRoomResultComposer = 3042;
    public const int NavigatorSettingsComposer = 3586;
    public const int RoomRatingComposer = 2502;
    public const int UserFlatCatsComposer = 837;
    public const int FurniListAddOrUpdateComposer = 3151;
    public const int FurniListComposer = 2694;
    public const int FurniListInvalidateComposer = 1856;
    public const int FurniListRemoveComposer = 1156;

    #endregion

    #region Vortex-specific (no AS3 backing)
    // Outgoing half of the in-client furni editor. See the matching note in MessageEvent above for
    // why the 8000-8999 band was chosen.
    public const int VortexFurniEditorDataMessageComposer = 8002;
    public const int VortexFurniEditorRightsMessageComposer = 8004;
    public const int VortexFurniDefinitionMessageComposer = 8006;

    // Fishing takes 8100-8199 of the same band, leaving the furni editor room to grow. Even for
    // composers (server->client), odd for events, so a request and its reply sit next to each other.
    // 8102 is re-sendable: it is broadcast again on every definitions reload, not only at login.
    // See the client's docs/vortex-original/fishing.md, sections 3 and 6.
    public const int VortexFishingDefinitionsMessageComposer = 8102;
    public const int VortexFishingPlayerStateMessageComposer = 8104;

    // Deliberately species-agnostic: naming the species here would let a client ignore the common
    // ones and only ever cast on the rare.
    public const int VortexFishSightedMessageComposer = 8106;
    public const int VortexFishingCatchResultMessageComposer = 8108;

    // 8110 was CatchFailed. Origins has no per-cast miss: catches keep coming until the spot runs
    // dry, so the id carries the end of the session instead.
    public const int VortexFishingSpotDepletedMessageComposer = 8110;
    public const int VortexFishingDerbyStandingMessageComposer = 8112;
    public const int VortexFishingErrorMessageComposer = 8114;

    // Only the species actually caught; the client greys out the rest from the definitions.
    public const int VortexFishingRecordsMessageComposer = 8116;
    public const int VortexHookHavocStartedMessageComposer = 8118;
    public const int VortexHookHavocResultMessageComposer = 8120;

    // Raid protection. The only part of the feature NOT on its official id: AIR 1.0.31 binds these
    // three at 734, 3553 and 3620, and two of those are spent in this revision on messages the
    // target client really reads (ChatReviewSessionOfferedToGuide, SecondsUntil). The bodies are
    // the client's own, field for field -- only two of the numbers moved.
    //
    // These are NOT free choices: vortex-modern-client already ported the feature and assigned
    // them, and its HabboMessages.ts says so out loud ("must be assigned identically in the
    // emulator's Headers.cs"). 3553 is kept because it is free on both sides; 3554 and 3555 are the
    // first ids after it that are unused as an event AND as a composer in the WIN63 registry, in
    // that file, and here. Changing one side alone silently unplugs the panel.
    // See packages/vortex-engine/src/habbo/communication/HabboMessages.ts, "=== RAID PROTECTION ===".
    public const int RaidProtectionCapabilityMessageComposer = 3554; // AIR 1.0.31: 734
    public const int RaidProtectionSettingsMessageComposer = 3553; // AIR 1.0.31: 3553, kept
    public const int RaidProtectionSettingsResultMessageComposer = 3555; // AIR 1.0.31: 3620
    #endregion
}
