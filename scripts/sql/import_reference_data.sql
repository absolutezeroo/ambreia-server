-- Reference data for a fresh fork database, copied from an older Vortex/Turbo database.
--
-- Run it through scripts/import-reference-data.ps1, which sets @src (the source schema), runs this
-- file with the TARGET as the default database, then re-applies the furniture-dependent seeds and
-- the development zone. Running it by hand: `SET @src = 'turbo';` first, in a session whose default
-- database is the target.
--
-- InitialCreate seeds every table whose content the fork owns, but it cannot ship the hotel's
-- content: no room model, no furniture definition. Without them no zone can be drawn and no
-- furniture loaded. This copies exactly that content and nothing a player made:
--
--   room_models, furniture_definitions   -- truncated, then reloaded
--
-- Only the columns both schemas share are copied, computed from information_schema at run time:
-- the fork dropped columns and may drop more.
--
-- Re-runnable. Both tables are TRUNCATEd and reloaded with the source's ids, so a re-run converges
-- on the source. Rows that point at them -- furniture, rooms.model_id -- keep their ids, which stay
-- valid as long as every import comes from the same source. Foreign-key checks are off for the
-- reload only.

SET SESSION group_concat_max_len = 1048576;

-- Builds, into @sql, `INSERT INTO <@t> (<shared columns>) SELECT <shared columns> FROM <@src>.<@t>`.
SET @gen = '
SELECT CONCAT(''INSERT INTO `'', @t, ''` ('', GROUP_CONCAT(CONCAT(''`'', d.column_name, ''`'') ORDER BY d.ordinal_position),
              '') SELECT '', GROUP_CONCAT(CONCAT(''`'', d.column_name, ''`'') ORDER BY d.ordinal_position),
              '' FROM `'', @src, ''`.`'', @t, ''`'')
  INTO @sql
  FROM information_schema.columns d
  JOIN information_schema.columns s
    ON s.table_schema = @src AND s.table_name = @t AND s.column_name = d.column_name
 WHERE d.table_schema = DATABASE() AND d.table_name = @t';
PREPARE gen FROM @gen;

SET FOREIGN_KEY_CHECKS = 0;

TRUNCATE TABLE `furniture_definitions`;
TRUNCATE TABLE `room_models`;

SET @t = 'room_models';           EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'furniture_definitions'; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;

SET FOREIGN_KEY_CHECKS = 1;

-- Wired is not in the RPG, and neither are its furni: triggers, effects, conditions and add-ons
-- (wf_*), the test conditions (test_cnd_*), the room-settings furni (conf_*) and the score boards
-- (highscore_*). Removed after the copy, with the checks back on, so the cascade on
-- furniture.definition_id takes any copy a player still holds with them. The served furnidata and
-- the asset pack drop the same four families.
DELETE FROM `furniture_definitions`
 WHERE `name` LIKE 'wf\_%' OR `name` LIKE 'test\_cnd\_%' OR `name` LIKE 'conf\_%' OR `name` LIKE 'highscore\_%';

DEALLOCATE PREPARE cp;
DEALLOCATE PREPARE gen;
