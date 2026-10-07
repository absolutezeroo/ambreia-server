-- Reference data for a fresh fork database, copied from an older Vortex/Turbo database.
--
-- Run it through scripts/import-reference-data.ps1, which sets @src (the source schema), runs this
-- file with the TARGET as the default database, then re-applies the furniture-dependent seeds.
-- Running it by hand: `SET @src = 'turbo';` first, in a session whose default database is the target.
--
-- InitialCreate seeds every table whose content the fork owns, but it cannot ship the hotel's
-- content: no room model, no furniture definition, no catalogue. Without them nothing can be built,
-- placed or bought. This copies exactly that content and nothing a player made:
--
--   room_models, furniture_definitions, furniture_purchasable_clothing,
--   catalog_pages, catalog_offers, catalog_products,
--   membership_offers, membership_gifts, catalog_frontpage_items   -- truncated, then reloaded
--   currency_types                                                    -- merged, see below
--
-- Only the columns both schemas share are copied, computed from information_schema at run time:
-- the fork dropped columns (catalog_products.builders_club_eligible) and may drop more.
--
-- Re-runnable. The nine tables above are TRUNCATEd and reloaded with the source's ids, so a re-run
-- converges on the source and discards edits made to those tables in the target (the dashboard's
-- catalogue and furniture editors write there). Player rows that point at them -- furniture,
-- rooms.model_id, marketplace offers -- keep their ids, which stay valid as long as every import
-- comes from the same source. Foreign-key checks are off for the reload only.
--
-- currency_types is not reloaded: player_currencies points at its ids, and InitialCreate seeds two
-- rows (Silver, Emeralds) of its own. The source's rows are added only for a kind (type +
-- activity_point_type, the key CurrencyTypeProvider resolves by) the target does not have yet --
-- on a fresh fork that is Credits, Duckets and Diamonds, which no migration seeds.

SET SESSION group_concat_max_len = 1048576;

-- Builds, into @sql, `INSERT INTO <@t> (<shared columns>) SELECT <shared columns> FROM <@src>.<@st>`.
-- @st is the source's name for the table: the same as @t except where the fork renamed it.
SET @gen = '
SELECT CONCAT(''INSERT INTO `'', @t, ''` ('', GROUP_CONCAT(CONCAT(''`'', d.column_name, ''`'') ORDER BY d.ordinal_position),
              '') SELECT '', GROUP_CONCAT(CONCAT(''`'', d.column_name, ''`'') ORDER BY d.ordinal_position),
              '' FROM `'', @src, ''`.`'', @st, ''`'')
  INTO @sql
  FROM information_schema.columns d
  JOIN information_schema.columns s
    ON s.table_schema = @src AND s.table_name = @st AND s.column_name = d.column_name
 WHERE d.table_schema = DATABASE() AND d.table_name = @t';
PREPARE gen FROM @gen;

SET FOREIGN_KEY_CHECKS = 0;

TRUNCATE TABLE `catalog_frontpage_items`;
TRUNCATE TABLE `membership_gifts`;
TRUNCATE TABLE `membership_offers`;
TRUNCATE TABLE `catalog_products`;
TRUNCATE TABLE `catalog_offers`;
TRUNCATE TABLE `catalog_pages`;
TRUNCATE TABLE `furniture_purchasable_clothing`;
TRUNCATE TABLE `furniture_definitions`;
TRUNCATE TABLE `room_models`;

SET @t = 'room_models';                    SET @st = @t; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'furniture_definitions';          SET @st = @t; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'furniture_purchasable_clothing'; SET @st = @t; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'catalog_pages';                  SET @st = @t; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'catalog_offers';                 SET @st = @t; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'catalog_products';               SET @st = @t; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'membership_offers';            SET @st = 'catalog_club_offers'; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'membership_gifts';             SET @st = 'catalog_club_gifts'; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;
SET @t = 'catalog_frontpage_items';        SET @st = @t; EXECUTE gen; PREPARE cp FROM @sql; EXECUTE cp;

-- Columns the fork renamed, which the shared-column copy above skips.
SET @sql = CONCAT('UPDATE `catalog_offers` d JOIN `', @src, '`.`catalog_offers` s ON s.`id` = d.`id` SET d.`membership_level` = s.`club_level`');
PREPARE cp FROM @sql; EXECUTE cp;

SET FOREIGN_KEY_CHECKS = 1;

SET @sql = CONCAT('
INSERT INTO `currency_types` (`name`, `type`, `activity_point_type`, `enabled`, `starting_amount`, `created_at`, `updated_at`)
SELECT s.`name`, s.`type`, s.`activity_point_type`, s.`enabled`, s.`starting_amount`, s.`created_at`, s.`updated_at`
  FROM `', @src, '`.`currency_types` s
 WHERE s.`deleted_at` IS NULL
   AND NOT EXISTS (SELECT 1 FROM `currency_types` d
                    WHERE d.`type` = s.`type` AND d.`activity_point_type` <=> s.`activity_point_type`)');
PREPARE cp FROM @sql; EXECUTE cp;

DEALLOCATE PREPARE cp;
DEALLOCATE PREPARE gen;
