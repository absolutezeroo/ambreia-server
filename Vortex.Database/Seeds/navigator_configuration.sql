-- Navigator reference data: the tabs of the left pane, the quick-link buttons under each tab, and
-- the flat categories a room can be filed under.
--
-- Nothing seeded these tables before, so a fresh hotel booted with zero top-level contexts: the
-- client's NavigatorMetaData arrived empty and the navigator rendered no tabs at all.
--
-- The search codes are the client's own, taken from the localization keys it looks a result block's
-- title up with (navigator.searchcode.title.<code> in gamedata/external_texts.json). A code that is
-- not in that list renders with the raw code as its header.
--
-- A tab is an overview: the server answers it with one block per quick link below, so the quick-link
-- rows are what decides which lists "My World" shows.
--
-- The RPG navigator (M0) serves my rooms, my friends' rooms, my groups' bases, every guild base and
-- a text search -- NavigatorSearchCodes, and the same two tabs NavigatorAdminService.DefaultTabs
-- recreates. Official, popular, room ads, event categories, history and favourites are gone.
--
-- query_type values are Vortex.Primitives.Navigator.Enums.NavigatorQueryType.
-- INSERT IGNORE throughout: safe to re-run, and an operator's own rows win.

-- ── Flat categories ──────────────────────────────────────────────────────────
-- global_category is a client localization key (navigator.flatcategory.global.<KEY>); when it is
-- set the client renders that instead of `name`, so `name` here is only the operator-facing label.
INSERT IGNORE INTO `navigator_flatcats`
    (`id`, `name`, `visible`, `automatic`, `automatic_category`, `global_category`, `staff_only`, `min_rank`, `order_num`)
VALUES
    (1,  'Personal Space',          1, 0, '', 'PERSONAL', 0, 1, 1),
    (2,  'Chat and discussion',     1, 0, '', 'CHAT',     0, 1, 2),
    (3,  'Party',                   1, 0, '', 'PARTY',    0, 1, 3),
    (4,  'Trading',                 1, 0, '', 'TRADING',  0, 1, 4),
    (5,  'Games',                   1, 0, '', 'GAMES',    0, 1, 5),
    (6,  'Building and decoration', 1, 0, '', 'BUILDING', 0, 1, 6),
    (7,  'Help Centers',            1, 0, '', 'HELP',     0, 1, 7),
    (8,  'Fansite Square',          1, 0, '', 'FANSITE',  0, 1, 8),
    -- 9 was Builders' Club, removed in M0.
    -- Staff-only: a player must not be able to file their own room as a public room.
    (10, 'Public Rooms',            1, 0, '', 'OFFICIAL', 1, 1, 10);

-- ── Top-level contexts (the navigator's tabs) ────────────────────────────────
INSERT IGNORE INTO `navigator_top_level_contexts`
    (`id`, `search_code`, `visible`, `query_type`, `order_num`)
VALUES
    (1, 'hotel_view',   1, 14, 1),  -- TextSearch: lists nothing until a text is typed
    (2, 'myworld_view', 1,  1, 2);  -- MyRooms

-- ── Quick links (the blocks inside a tab) ────────────────────────────────────
-- `filter` is appended to the button's caption by the client (QuickLinksView), so it stays empty
-- unless the extra text is wanted on screen. `localization` is unused by this client revision.
INSERT IGNORE INTO `navigator_quick_links`
    (`id`, `top_level_context_id`, `search_code`, `filter`, `localization`, `query_type`, `order_num`)
VALUES
    -- Hotel
    (1, 1, 'groups',        '', '', 16, 1),  -- GuildBases

    -- My world
    (2, 2, 'my',            '', '',  1, 1),  -- MyRooms
    (3, 2, 'my_groups',     '', '', 15, 2),  -- MyGroups
    (4, 2, 'friends_rooms', '', '',  3, 3);  -- FriendsRooms
