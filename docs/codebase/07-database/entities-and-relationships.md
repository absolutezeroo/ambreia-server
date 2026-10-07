# Entities and relationships

## Purpose

The schema by bounded context. **145 entities**, grouped by what they are for rather than listed
alphabetically. Constraints appear here only when they encode a rule; the exhaustive list is
[Entity index](../generated/entity-index.md).

Every entity derives `VortexEntity` (`id`, `created_at`, `updated_at`, `deleted_at`) and inherits the
global soft-delete query filter.

## Identity and authorization

`player_accounts` · `players` · `account_bans` · `security_tickets` · `roles` · `role_permissions` ·
`player_account_roles` · `sanction_presets` · `account_levels`

**`players.account_id` is nullable** — an avatar can exist without a login.

| Constraint | Encodes |
|---|---|
| `player_accounts.email` unique | one account per address |
| `players.name` unique | the hotel-wide handle |
| `security_tickets` unique on **both** `player_id` and `ticket` | one live SSO handoff per player, no ticket collision |
| `account_bans (player_account_id)` unique | at most one **live** ban row — which is why unbanning soft-deletes and re-banning revives with `IgnoreQueryFilters()` |
| `roles.key`, `role_permissions (role_id, capability_key)`, `player_account_roles (account_id, role_id)` unique | the capability model |
| `sanction_presets (kind, preset_index)` unique | the ban dialog indexes presets **positionally** — a wire-shape constraint |
| `account_levels.level_number` unique | the client addresses it by number |

## Player profile and preferences

`player_badges` · `player_effects` · `player_chat_styles` · `player_chat_styles_owned` ·
`player_clothing` · `player_wardrobe_outfits` · `player_word_filters` ·
`player_account_preferences` · `player_mod_tool_preferences` · `player_wired_preferences` ·
`player_navigator_preferences` · `player_navigator_saved_searches` ·
`player_navigator_collapsed_categories` · `player_navigator_view_modes` · `player_favorite_rooms` ·
`player_subscriptions` · `builders_club_tiers` · `player_kickback` ·
`player_vault_income_rewards`

Two recurring shapes:

- **one row per player** — unique on `player_id` alone: `player_account_preferences`,
  `player_mod_tool_preferences`, `player_wired_preferences`, `player_navigator_preferences`,
  `player_kickback`, `player_nft_outfit`
- **one row per (player, thing)** — `player_badges (player_id, badge_code)`,
  `player_chat_styles_owned`, `player_word_filters (player_id, word)`,
  `player_wardrobe_outfits (player_id, slot_id)`, `player_favorite_rooms (player_id, room_id)`,
  `player_navigator_view_modes (player_id, search_code)`, `player_navigator_collapsed_categories`

`player_subscriptions (player_id, subscription_type)` unique is what makes the club upsert in
`PlayerGrain` correct. `player_chat_styles.client_style_id` and `builders_club_tiers.level` are unique
because the client addresses them by that number.

## Economy

`currency_types` · `player_currencies` · `economy_ledger`

`player_currencies (player_id, currency_type_id)` unique is the wallet invariant.
**`currency_types` is the gate**: a grant to a currency with no enabled row is a logged no-op.
→ [Economy overview](../06-economy/overview.md)

`economy_ledger` is indexed `(player_id, occurred_at)` and `(correlation_id)`.

## Catalog and commerce

`catalog_pages` · `catalog_offers` · `catalog_products` · `catalog_frontpage_items` ·
`catalog_club_offers` · `catalog_club_gifts` · `catalog_vouchers` ·
`catalog_voucher_redemptions` · `catalog_ltd_series` · `catalog_ltd_raffle_entries` ·
`targeted_offers` · `targeted_offer_products` · `player_targeted_offers` ·
`commerce_operations` · `commerce_receipts`

| Constraint | Encodes |
|---|---|
| `catalog_vouchers.code` unique | |
| `catalog_voucher_redemptions (voucher_id, player_id)` unique | one redemption per player per code, as a DB guarantee not a read-then-write |
| `player_targeted_offers (player_id, targeted_offer_id)` unique | |
| **`commerce_receipts (operation_id, step_key)` unique** | **the replay guard** — `TryRecordStepAsync` lets the insert fail and reads the failure as "already ran" |
| `commerce_operations (state, pivoted_at)`, `(relayed_at)`, `(player_id)` | recovery asks "what is stuck past its pivot" and "what still owes an event" |

`targeted_offer_products` cascades from `targeted_offers` and is `Restrict` against
`furniture_definitions`. `commerce_operations`' PK is the caller-minted GUID
(`[DatabaseGenerated(None)]`).

## Marketplace

`marketplace_offers` (indexed `(state, expires_at)` and `(seller_id, state)`) · `marketplace_settings`

The expiry index backs a sweep that **does not exist** — [Marketplace](../06-economy/marketplace.md).

## Furniture and items

`furniture_definitions` · `furniture` · `furniture_teleport_links` ·
`furniture_purchasable_clothing` · `figure_sellable_sets` · `hand_items`

`furniture_definitions (sprite_id, product_type, furni_category)` unique — **`name` deliberately is
not**, and 3533 classnames are duplicated. → [Furniture](../04-rooms/furniture.md)

`furniture.rentable_space_furniture_id` is self-referencing with `OnDelete(SetNull)`, so deleting a
space furni does not cascade-delete the items standing in it. `furniture_teleport_links` is unique on
**each side independently**, so a portal cannot be double-linked.

## Rooms

`rooms` · `room_models` · `room_bans` · `room_mutes` · `room_rights` · `room_ratings` ·
`room_advertisements` · `room_entry_logs` · `room_chatlogs` · `room_rentable_space_terms` ·
`room_rentable_spaces`

**`rooms (group_id)` unique** carries a full explanation in `RoomEntity.cs` and in `OnModelCreating`:
it enforces "at most one guild per room" on the **nullable** side, because MySQL permits repeated
NULLs — so dissolving a guild frees the room. A unique index on `groups.room_id` would be permanently
blocked by the soft-deleted guild row, which is why `VortexDbContext` explicitly re-declares that one
as `IsUnique(false)` to undo EF's reference-navigation convention.

`room_bans`, `room_mutes`, `room_rights`, `room_ratings` are all unique on `(room_id, player_id)`.
`room_chatlogs` is indexed `(room_id, created_at)` and `(player_id, created_at)` — the two moderation
queries.

Rentable space: `room_rentable_space_terms (furniture_id)` and `room_rentable_spaces (furniture_id)`
both unique (one state row per instance), plus a non-unique `renter_player_id` index backing the "one
space per player" rule that is enforced in application logic.

## Wired

`wired_permanent_variables` (unique on `(target_type, target_id, variable_id)` — set/create/delete
semantics) · `wired_chests` and `wired_contracts` (both unique on `furniture_id` — one logical object
per placed furni) · `wired_chest_transactions` · `room_wired_logs` (indexed `(room_id, created_at)`)

**Wired *configuration* is not in these tables** — it lives in `furniture.extra_data`'s `wired`
section. → Wireds (retirés au M0)

## Pets and bots

`pets` · `pet_levels` · `pet_commands` · `pet_command_names` · `pet_food` · `pet_palettes` ·
`pet_vocals` · `bots`

**`pets.room_id` nullable means inventory** — asserted as an invariant in
`Vortex.Database.Tests/Pets/PetModelTests.cs`. Same shape for `bots`.

Reference-data uniqueness: `pet_levels (pet_type, level)`, `pet_commands (pet_type, command_id)`,
`pet_command_names (command_id)`, `pet_food (definition_id, pet_type)`,
`pet_palettes (pet_type, breed_index, rare)`.

## Messenger

`messenger_categories` · `messenger_friends` · `messenger_requests` · `messenger_blocked` ·
`messenger_ignored` · `messenger_messages`

Every relation table is unique on the **ordered pair** `(player_id, other_id)` — friendships,
requests, blocks and ignores are all directional rows, and that uniqueness is what makes "add if
absent" safe.

`messenger_messages` carries **both** `(receiver, sender, timestamp)` and
`(sender, receiver, timestamp)` indexes, so a conversation reads efficiently in either direction.

## Groups and guilds

`groups` · `group_members` · `group_membership_requests` · `group_blocked_members` ·
`group_forum_settings` · `group_forum_threads` · `group_forum_posts` · `group_forum_read_markers` ·
`group_badge_parts` · `group_colors`

The circular `groups.room_id ↔ rooms.group_id` link is modelled as **two independent one-directional
relationships, both `Restrict`**, so MySQL never builds a cascade cycle; dissolving a guild detaches
then soft-deletes.

`group_members`, `group_membership_requests`, `group_blocked_members` and `group_forum_read_markers`
are all unique on `(group_id, player_id)`. `group_forum_threads (last_post_at)` backs the thread list
ordering.

## Navigator

`navigator_top_level_contexts` (`search_code` unique — the client addresses tabs by code) ·
`navigator_flatcats` · `navigator_eventcats` · `navigator_quick_links`

These tables are authoritative for **repointing**, not for the code vocabulary itself.
→ [Navigator](../05-gameplay/navigator.md)

## Moderation and help

`cfh_categories` · `cfh_topics` · `cfh_tickets` · `quizzes` · `quiz_questions` · `player_quizzes`

`cfh_tickets` has three FKs to `players` (reporter, reported, picker), **all `Restrict`** — a ticket
outlives whichever party it names. `reported_player_id` was made nullable by
`20260819042238_AllowCfhTicketWithoutReportedPlayer`. Indexed on `(state)` for the queue.

`quizzes.code`, `quiz_questions (quiz_id, question_number)` and `player_quizzes (player_id, quiz_id)`
are unique.

## Progression

Achievements: `achievements` · `achievement_levels` · `player_achievements` ·
`achievement_resolutions` · `player_achievement_resolutions`
Quests: `quests` · `player_quests` · `daily_tasks` · `daily_task_rewards` · `player_daily_tasks` ·
`community_goals` · `community_goal_levels` · `player_community_goal_contributions`
Polls: `polls` · `poll_questions` · `poll_question_choices` · `player_polls` · `player_poll_answers`
Prizes: `prize_pools` · `prize_pool_entries` · `prize_pool_bindings` · `player_prize_claims`
Plus `player_mystery_box_keys`

One delete policy runs through all of it → [Ownership boundaries](ownership-boundaries.md).

`player_achievements (player_id, achievement_id)`, `player_quests (player_id, quest_id)`,
`player_polls (player_id, poll_id)`, `player_community_goal_contributions (player_id, goal_id)` and
`player_prize_claims (player_id, pool_id)` are all unique.

**`player_daily_tasks (player_id, task_id, assigned_on)` unique** — the date is part of the key, which
is what makes a daily task re-assignable.

## Collectibles / NFT

`nft_collections` · `nft_collection_items` · `nft_store_offers` · `nft_claims` ·
`nft_mintable_item_types` · `nft_mint_token_offers` · `nft_assets` · `nft_asset_ledger` ·
`player_collector_stats` · `player_mint_tokens` · `nft_avatars` · `player_nft_avatars` ·
`player_nft_outfit`

**`nft_assets (product_code, serial_number)` unique is the provenance guarantee** — it is what replaces
a chain's uniqueness. `nft_asset_ledger` is the append-only history, and `RoomTradingSystem.MoveAssets`
writes the ledger row **inside the same transaction** as the ownership change, with the comment:
*"a chain gets that from its blocks, and a table gets it from a transaction."*

Also unique: `nft_collections.collection_code`, `nft_collection_items (collection_id, product_code)`,
`nft_store_offers.product_code`, `nft_mintable_item_types.product_code`,
`nft_claims (player_id, claim_code)`, `nft_avatars.avatar_code`,
`player_nft_avatars (player_id, avatar_id)`; `player_collector_stats` and `player_mint_tokens` are
one-per-player.

## Observability and audit

`audit_events` · `economy_ledger` · `item_events` · `error_groups` · `error_occurrences`

`audit_events` carries **five composite indexes, all `(dimension, occurred_at)`** — actor, target,
room, category — plus standalone `correlation_id` and `ip_hash`. Those are the six axes the forensics
page queries by.

`error_groups.fingerprint` unique is the dedup key.

## Server config

`server_config` (`key` unique). Owned live by `ServerConfigGrain`, write-through.

## Sources

- `Vortex.Database/Entities/**` — the `[Index]`, `[ForeignKey]` and `[Column]` attributes cited
- `Vortex.Database/Context/VortexDbContext.cs` — `OnModelCreating`, the relationship decisions
- `Vortex.Database/Entities/Room/RoomEntity.cs` — the `group_id` uniqueness rationale
- `Vortex.Database/Entities/Commerce/CommerceReceiptEntity.cs`
- `Vortex.Database/Entities/Collectibles/NftAssetEntity.cs`
- `Vortex.Rooms/Grains/Systems/RoomTradingSystem.cs` — `MoveAssets`
- `Vortex.Database.Tests/Pets/PetModelTests.cs`
- [Entity index](../generated/entity-index.md)
