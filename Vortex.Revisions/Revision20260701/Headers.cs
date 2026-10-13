namespace Vortex.Revisions.Revision20260701;

internal static class MessageEvent
{
    #region Incoming

    public const int OpenFlatConnectionMessageEvent = 3234;
    public const int QuitMessageEvent = 3061;

    public const int ClientHelloMessageEvent = 4000;
    public const int CompleteDiffieHandshakeMessageEvent = 2526;
    public const int DisconnectMessageEvent = 2864;
    public const int InfoRetrieveMessageEvent = 756;
    public const int InitDiffieHandshakeMessageEvent = 2022;
    public const int PongMessageEvent = 362;
    public const int SSOTicketMessageEvent = 882;
    public const int UniqueIDMessageEvent = 2309;
    public const int VersionCheckMessageEvent = 3584;
    public const int GetFurnitureAliasesMessageEvent = 1901;
    public const int MoveAvatarMessageEvent = 2364;

    // AS3-verified (direct read): RoomObjectEventHandler (_SafeCls_1821.as:2570) changeRoomObjectState()
    // branches on the object category -- category 10 (floor) sends UseFurniture(objectId, param)
    // @3353, category 20 (wall) sends _SafeCls_3754(objectId, param) @3590. Both were registered
    // before the core cut (72aecdde2) with these ids.
    public const int UseFurnitureMessageEvent = 3353;
    public const int UseWallItemMessageEvent = 3590;

    public const int LookToMessageEvent = 2508; // AS3-verified: the client registry maps _composers[2508] to the (x,y) composer RoomObjectEventHandler.setSelectedAvatar() sends when an avatar is selected without a click-user wired (WIN63 habbo/communication/_SafeCls_2046.as:819, LookToMessageComposer in win63_version). Was 9103, a placeholder whose own comment flagged it as unresolved; the parser here already read the right two ints, so only the header was wrong
    #endregion

    #region Vortex-specific (no AS3 backing)

    public const int RpgCharacterListRequestMessageEvent = 9000; // RPG (spec §3.7)
    public const int RpgCharacterCreateMessageEvent = 9001; // RPG (spec §3.7)
    public const int RpgCharacterChooseClassMessageEvent = 9002; // RPG (spec §3.7)
    public const int RpgEnterWorldMessageEvent = 9003; // RPG (spec §3.7)
    public const int RpgItemMoveMessageEvent = 9004; // RPG (spec §3.7)
    public const int RpgItemSplitMessageEvent = 9005; // RPG (spec §3.7)
    public const int RpgItemEquipMessageEvent = 9006; // RPG (spec §3.7)
    public const int RpgItemUnequipMessageEvent = 9007; // RPG (spec §3.7)
    public const int RpgItemDestroyMessageEvent = 9008; // RPG (spec §3.7)
    public const int RpgItemClaimParcelMessageEvent = 9009; // RPG (spec §3.7)
    public const int RpgTargetSelectMessageEvent = 9010; // RPG (spec §3.7)
    public const int RpgChatSendMessageEvent = 9011; // RPG (spec §3.7)
    public const int RpgEmoteMessageEvent = 9012; // RPG (spec §3.7)
    public const int RpgSkillCastMessageEvent = 9013; // RPG (spec §3.7)
    public const int RpgActionBarSetMessageEvent = 9014; // RPG (spec §3.7)
    public const int RpgCastCancelMessageEvent = 9015; // RPG (spec §3.7)
    public const int RpgAutoAttackMessageEvent = 9016; // RPG (M1d §2.2)
    public const int RpgDeathChoiceMessageEvent = 9017; // RPG (M1d §2.2)
    #endregion
}

internal static class MessageComposer
{
    #region Outgoing

    public const int CantConnectMessageComposer = 2430;
    public const int CloseConnectionMessageComposer = 3404;
    public const int HanditemConfigurationMessageComposer = 2942;
    public const int OpenConnectionMessageComposer = 611;
    public const int RoomForwardMessageComposer = 3339;
    public const int RoomReadyMessageComposer = 2349;
    public const int MaintenanceStatusMessageComposer = 1737;

    public const int RoomEntryTileMessageComposer = 2792;

    // AS3-verified (client fix 5316023): the in-room figure_effect broadcast is 2624, not 3629.
    // WIN63 registry _SafeCls_2046.as maps _SafeStr_4546[2624] = _SafeCls_2589 (parser _SafeCls_3361:
    // userId, effectId, delayMilliSeconds) to RoomMessageHandler.onAvatarEffect. Header 3629 there is a
    // different message (AvatarEditorMessageHandler.onAvatarEffectSelected). Serializer field order
    // already matches the client's 2624 parser, so only the header number was wrong — this is why
    // worn effects never appeared on avatars in-room.
    public const int AvatarEffectMessageComposer = 2624;
    public const int CarryObjectMessageComposer = 2850;

    public const int FigureUpdateComposer = 132;

    public const int AuthenticationOKMessageComposer = 230;
    public const int CompleteDiffieHandshakeComposer = 3401;
    public const int DisconnectReasonComposer = 4000;
    public const int InitDiffieHandshakeComposer = 3309;
    public const int PingMessageComposer = 1407;
    public const int UserObjectComposer = 3985;
    public const int UserRightsMessageComposer = 3599;
    public const int NotificationDialogMessageComposer = 2243;

    public const int YouAreControllerMessageComposer = 934;
    public const int YouAreNotControllerMessageComposer = 456;
    public const int YouAreOwnerMessageComposer = 1986;

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
    public const int UserChangeMessageComposer = 3798;
    public const int UserRemoveMessageComposer = 3693;
    public const int UsersMessageComposer = 996;
    public const int UserUpdateMessageComposer = 2613;

    #endregion

    #region Vortex-specific (no AS3 backing)

    public const int RpgCharacterListMessageComposer = 9000; // RPG (spec §3.7)
    public const int RpgClassListMessageComposer = 9001; // RPG (spec §3.7)
    public const int RpgCharacterResultMessageComposer = 9002; // RPG (spec §3.7)
    public const int RpgCharacterStateMessageComposer = 9003; // RPG (spec §3.7)
    public const int RpgCharacterProgressMessageComposer = 9004; // RPG (spec §3.7)
    public const int RpgCharacterStatsMessageComposer = 9005; // RPG (spec §3.7)
    public const int RpgInventoryMessageComposer = 9006; // RPG (spec §3.7)
    public const int RpgItemResultMessageComposer = 9007; // RPG (spec §3.7)
    public const int RpgZoneInteractivesMessageComposer = 9008; // RPG (spec §3.7)
    public const int RpgTargetMessageComposer = 9009; // RPG (spec §3.7)
    public const int RpgChatMessageComposer = 9010; // RPG (spec §3.7)
    public const int RpgChatResultMessageComposer = 9011; // RPG (spec §3.7)
    public const int RpgCombatEventsMessageComposer = 9012; // RPG (spec §3.7)
    public const int RpgVitalsMessageComposer = 9013; // RPG (spec §3.7)
    public const int RpgSelfVitalsMessageComposer = 9014; // RPG (spec §3.7)
    public const int RpgDeathMessageComposer = 9015; // RPG (spec §3.7)
    public const int RpgCombatResultMessageComposer = 9016; // RPG (spec §3.7)
    public const int RpgEmoteMessageComposer = 9017; // RPG (spec §3.7)
    public const int RpgSkillsMessageComposer = 9018; // RPG (spec §3.7)
    public const int RpgSkillResultMessageComposer = 9019; // RPG (spec §3.7)
    public const int RpgCastStartMessageComposer = 9020; // RPG (spec §3.7)
    public const int RpgCastEndMessageComposer = 9021; // RPG (spec §3.7)
    public const int RpgCooldownsMessageComposer = 9022; // RPG (spec §3.7)
    public const int RpgStatusesMessageComposer = 9023; // RPG (spec §3.7)
    public const int RpgSkillEventsMessageComposer = 9024; // RPG (spec §3.7)
    public const int RpgTelegraphsMessageComposer = 9025; // RPG (spec §3.7)
    public const int RpgMonsterYellMessageComposer = 9026; // RPG (spec §3.7)
    public const int RpgTargetsMessageComposer = 9027; // RPG (M1d §2.1)
    public const int RpgThreatMessageComposer = 9028; // RPG (M1d §2.1)
    public const int RpgResurrectOfferMessageComposer = 9029; // RPG (M1d §2.1)
    public const int RpgLevelUpMessageComposer = 9030; // RPG (M1d §2.1)
    public const int RpgAutoAttackStateMessageComposer = 9031; // RPG (M1d §2.1)
    #endregion
}
