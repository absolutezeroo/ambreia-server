-- Stuff-data formats, derived from the client's own logic classes.
--
-- stuff_data_type tells StuffDataFactory which shape a furni's state travels in. It shipped as 0
-- (LegacyKey) for all ~55 000 definitions. Where a logic actually needs a richer shape, the state
-- silently never round-trips: the grant/stamp path checks the type and skips, the reader hands back
-- a freshly defaulted instance, and the furni renders with the client's defaults. That is the bug
-- that made every guild furni white.
--
-- Derived, not guessed: each client logic class was read for the StuffData class it casts to, and
-- the ids come from the client's own factory (0 legacy, 1 map, 2 string array, 3 vote, 4 empty,
-- 5 number, 6 high score, 7 crackable) -- which matches Vortex's StuffDataType one for one.
--
-- LIMIT OF THE METHOD, worth knowing before trusting this list: a logic that reads its state through
-- the generic model controller names no StuffData class, so it cannot be derived this way.
-- furniture_crackable is exactly that case, and it is deliberately absent here for a second reason:
-- a crackable also needs a loot_table_bindings row for its hit count (furnidata does not carry it),
-- so flipping the format alone would leave 317 definitions as half-configured crackables. The
-- crackables were removed in M0; those definitions stay on the legacy format and the default logic.
--
-- Keyed on logic, so this has to run after furni_logic_bindings.sql.

-- format 1
UPDATE `furniture_definitions`
   SET `stuff_data_type` = 1
 WHERE `logic` IN ('furniture_bb', 'furniture_bg', 'furniture_coinschest', 'furniture_furnichest', 'furniture_mannequin', 'furniture_present');

-- format 2
UPDATE `furniture_definitions`
   SET `stuff_data_type` = 2
 WHERE `logic` IN ('furniture_achievement_resolution', 'furniture_badge_display', 'furniture_group_forum_terminal', 'furniture_guild_customized', 'furniture_guild_gate', 'furniture_hween_lovelock', 'furniture_lovelock', 'furniture_wildwest_wanted');

-- format 3
UPDATE `furniture_definitions`
   SET `stuff_data_type` = 3
 WHERE `logic` IN ('furniture_vote_counter', 'furniture_vote_majority');

-- format 5
UPDATE `furniture_definitions`
   SET `stuff_data_type` = 5
 WHERE `logic` IN ('furniture_area_hide', 'furniture_background_color');
