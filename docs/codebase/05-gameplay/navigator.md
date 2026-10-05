# Navigator

## Purpose

Room search and discovery — and one structural rule about how a tab is answered that is easy to get
wrong.

## The rule

> **A tab is answered with one block per quick link, not one flat list of rooms.**

`Vortex.Navigator/NavigatorService.cs` — `GetSearchBlocksAsync(searchCode, filterType, filterValue,
playerId, ct)`:

```
non-empty filterValue        → collapse any view to ONE flat block  (NavigatorActionAllowedType.Back)
searchCode == "categories"   → GetCategoryBlocksAsync              (many blocks)
otherwise                    → ResolveQuickLinksAsync(searchCode)
                                 └─ if the code names a top-level context with quick links:
                                      BuildOverviewBlocksAsync emits ONE BLOCK PER QUICK LINK,
                                      each carrying its own search code, so the client localizes its
                                      own header and "show more" drills into that search
                                      (NavigatorActionAllowedType.Expanded)
                                    a quick link that is itself "categories" expands in place
```

This is implemented and merged — commits `4793aa61` *"make each tab run its own search instead of
listing the hotel"* and `8108dadf` *"answer a tab with one block per quick link, not one list"*.

## Where search codes come from

`Vortex.Primitives/Navigator/NavigatorSearchCodes.cs`. Its class doc states the authority chain
precisely, and it matters:

| Layer | Role |
|---|---|
| the **client** | the codes are hardcoded client-side |
| `external_texts.json` | the spellings are the `navigator.searchcode.title.<code>` keys — the naming source |
| `navigator_top_level_contexts` / `navigator_quick_links` | authoritative for **repointing**, not for the vocabulary |
| `QueryTypeBySearchCode` | the built-in fallback map |

> **A missing DB row must not fall back to "every room."** That is stated in the code, and it is the
> failure mode the one-block-per-quick-link design exists to prevent.

`navigator_top_level_contexts.search_code` is unique — the client addresses tabs by code.

## Room ads are room events

`RoomAdsView`, `RoomAds` and `TopPromotions` all map to `NavigatorQueryType.RoomAds`, served by
`NavigatorProvider` against:

```csharp
RoomAdvertisements.Any(a => a.RoomEntityId == x.Id && a.ExpiresAt > now)
```

There is no separate "events" concept — they are the same rows.

## Favourites and preferences

`Vortex.Players/Grains/PlayerNavigatorGrain.cs`, keyed by player id, hydrate-on-activate plus
write-through. Owns favourites, saved searches, collapsed categories, view modes and the home room.

`GetFavouriteRoomIdsAsync` is read at login — the comment notes *"the client tracks both from this one
message and never asks again"*, which is why a favourite change has to push, not wait to be polled.

`player_favourite_rooms (player_id, room_id)` and `player_navigator_view_modes (player_id, search_code)`
are unique.

## Population is not what the column says

> **`rooms.users_now` is a dead column.** Written as `0` at room creation and never updated.

`NavigatorProvider`'s `OrderByPopularity` sorts on it — so rooms sort by a constant, and `score` then
`id` is the effective order. `NavigatorService.ToSearchResults` **overlays the real population** from
`RoomDirectoryGrain`, so the client sees the truth; the sort does not.

Whether that ordering is acceptable is a product call.
→ [State ownership](../03-orleans/persistence.md)

## Implementation status

Handlers exist for all 38 `Navigator/` and 7 `NewNavigator/` messages.

A stub scan — a `HandleAsync` body that is only `await ValueTask.CompletedTask` with no grain or
service reference — flags **2 of 38 Navigator** and **0 of 7 NewNavigator**.

> Treat that as approximate. The heuristic cannot see a handler that resolves a grain and then does
> nothing useful with it.

For context, the same scan across neighbouring domains: Catalog 3/36, Marketplace 1/10, Vault 2/4,
FriendList 1/15, Talent 3/3, Collectibles 0/19, Nft 0/5, Quest 0/16.


## Admin

`INavigatorAdminService` — 12 write sites, **12 reloads**. Class (c): every write reloads the provider
it feeds.

Correct, but it means a half-finished navigator edit is live to every player the moment it is saved —
there is no draft state. → [Dashboard operations](../08-dashboard/operations.md)

## Persistence

`navigator_top_level_contexts` · `navigator_flatcats` · `navigator_eventcats` ·
`navigator_quick_links` · `player_navigator_*` · `player_favourite_rooms` · `room_advertisements`

## Known unknowns

- **Unverified:** the per-`NavigatorQueryType` query bodies in `NavigatorProvider.cs` (817 lines) —
  read only where the room-ads and quick-link routing required it — and `NavigatorAdminService.cs`.

## Sources

- `Vortex.Navigator/NavigatorService.cs` — `GetSearchBlocksAsync`, `BuildOverviewBlocksAsync`, `ToSearchResults`
- `Vortex.Navigator/NavigatorProvider.cs` — `OrderByPopularity`, the room-ads query
- `Vortex.Primitives/Navigator/NavigatorSearchCodes.cs`
- `Vortex.Players/Grains/PlayerNavigatorGrain.cs`
- `Vortex.PacketHandlers/Navigator/**`, `Vortex.PacketHandlers/NewNavigator/**`
- `Vortex.Rooms/Grains/RoomDirectoryGrain.cs`
