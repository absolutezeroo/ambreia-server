# Revision index

> **Generated reference index.** This file inventories code symbols from a static scan of the
> repository at commit `e57f0be79a96` and should not be used as the sole source for runtime semantics.
> Regenerate with `/document-vortex update`. Explanatory pages live one directory up.


Artefacts of the embedded default revision `Revision20260701`, plus shared revision infrastructure. **Header ids are per-revision and are never global protocol truth** &mdash; see [`../02-network-protocol/revisions.md`](../02-network-protocol/revisions.md).

## Written vs mapped

A parser or serializer class existing is not the same as it being reachable. Only what a `Maps/*.cs` class registers into the revision tables can ever run; the remainder compiles, ships, and is dead. Registering a duplicate header or composer type is a startup crash rather than a silent overwrite (`Vortex.Revisions/RevisionMapBuilder.cs`).

| Artefact kind | Classes | Registered in `Maps/` | Unmapped |
|---|---|---|---|
| Parsers (incoming) | 539 | 534 | 5 |
| Serializers (outgoing) | 554 | 452 | 102 |

Counted as files declaring the base type (`IParser` / `AbstractSerializer<>`); a handful of base and helper classes inflate a raw file count of 540 and 597. The unmapped serializers are whole families &mdash; every `Game2*` (32 files, 0 mapped), all `Talent*` composers, the jukebox/playlist set. They compile and ship and can never run.

| Other artefact | Count |
|---|---|
| Header map classes | 43 |
| Header id constants in `Headers.cs` | 1074 |
| Infrastructure / other | 8 |

## Parsers by domain

File counts. See the written-vs-mapped table above before treating a count as coverage.

| Domain | Count |
|---|---|
| `Advertisement` | 2 |
| `Avatar` | 4 |
| `Camera` | 5 |
| `Campaign` | 2 |
| `Catalog` | 33 |
| `Collectibles` | 19 |
| `Competition` | 9 |
| `Crafting` | 5 |
| `FriendFurni` | 1 |
| `FriendList` | 15 |
| `Game` | 28 |
| `Gifts` | 5 |
| `GroupForums` | 12 |
| `Handshake` | 9 |
| `Help` | 27 |
| `Hotlooks` | 1 |
| `Inventory` | 27 |
| `LandingView` | 2 |
| `Marketplace` | 10 |
| `Moderator` | 21 |
| `MysteryBox` | 1 |
| `Navigator` | 36 |
| `NewNavigator` | 7 |
| `Nft` | 5 |
| `Notifications` | 2 |
| `Nux` | 3 |
| `Poll` | 3 |
| `Preferences` | 10 |
| `Quest` | 16 |
| `Register` | 1 |
| `Room` | 101 |
| `RoomDirectory` | 1 |
| `RoomSettings` | 8 |
| `Sound` | 9 |
| `Talent` | 3 |
| `Tracking` | 5 |
| `UserClassification` | 2 |
| `UserDefinedRoomEvents` | 43 |
| `Users` | 43 |
| `Vault` | 4 |

## Serializers by domain

| Domain | Count |
|---|---|
| `Advertisement` | 2 |
| `Availability` | 5 |
| `Avatar` | 4 |
| `CallForHelp` | 4 |
| `Camera` | 6 |
| `Campaign` | 2 |
| `Catalog` | 37 |
| `Collectibles` | 23 |
| `Competition` | 6 |
| `Crafting` | 4 |
| `Error` | 1 |
| `FriendFurni` | 3 |
| `FriendList` | 23 |
| `Game` | 38 |
| `Gifts` | 3 |
| `GroupForums` | 9 |
| `Handshake` | 13 |
| `Help` | 32 |
| `Hotlooks` | 1 |
| `Inventory` | 53 |
| `LandingView` | 2 |
| `Marketplace` | 9 |
| `Moderation` | 18 |
| `MysteryBox` | 4 |
| `Navigator` | 26 |
| `NewNavigator` | 8 |
| `Nft` | 3 |
| `Notifications` | 13 |
| `Nux` | 3 |
| `Perk` | 2 |
| `Poll` | 7 |
| `Preferences` | 3 |
| `Quest` | 14 |
| `Room` | 106 |
| `RoomSettings` | 12 |
| `Sound` | 8 |
| `Talent` | 3 |
| `Tracking` | 1 |
| `UserClassification` | 1 |
| `UserDefinedRoomEvents` | 44 |
| `Users` | 38 |
| `Vault` | 3 |

## Header maps

One map class per protocol domain; each contributes its ids to the revision registry.

- `Vortex.Revisions/Revision20260701/Maps/AdvertisementMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/AvailabilityMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/AvatarMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/CallForHelpMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/CameraMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/CampaignMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/CatalogMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/CollectiblesMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/CompetitionMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/CraftingMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/FriendFurniMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/FriendListMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/GameMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/GiftsMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/GroupForumsMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/HandshakeMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/HelpMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/HotlooksMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/InventoryMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/LandingViewMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/MarketplaceMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/ModeratorMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/MysteryBoxMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/NavigatorMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/NewNavigatorMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/NftMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/NotificationsMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/NuxMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/PerkMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/PollMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/PreferencesMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/QuestMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/RegisterMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/RoomDirectoryMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/RoomMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/RoomSettingsMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/SoundMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/TalentMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/TrackingMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/UserClassificationMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/UserDefinedRoomEventsMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/UsersMap.cs`
- `Vortex.Revisions/Revision20260701/Maps/VaultMap.cs`

## Revision infrastructure

- `Vortex.Revisions/Configuration/ProtocolLimitsConfig.cs`
- `Vortex.Revisions/Configuration/RevisionConfig.cs`
- `Vortex.Revisions/Extensions/ServiceCollectionExtensions.cs`
- `Vortex.Revisions/Revision20260701/Headers.cs`
- `Vortex.Revisions/Revision20260701/Revision20260701.cs`
- `Vortex.Revisions/RevisionBase.cs`
- `Vortex.Revisions/RevisionMapBuilder.cs`
- `Vortex.Revisions/RevisionRegistrationService.cs`
