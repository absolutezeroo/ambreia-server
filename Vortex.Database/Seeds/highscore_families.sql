-- High-score boards: the stuff-data format their logic reads.
--
-- The twelve scoreboards (highscore_classic, highscore_mostwin, highscore_perteam, four variants
-- each) carry `wf_highscore`, the Arcturus interaction_type, which is the key FurnitureHighScoreLogic
-- is registered under. The logic reads HighScoreData (format 6), and StuffDataSnapshotSerializer
-- writes that shape in the order the client parses it; a board left on the legacy format has
-- nowhere to keep its rows.
--
-- Extracted from crackable_and_highscore_families.sql when the crackables were removed (M0). That
-- file also renamed the logic to `furniture_high_score`, the client's name, which nothing on the
-- server registers -- the boards would have resolved to the plain floor default. The rename is not
-- carried over; the format is.
--
-- Re-runnable: keyed on the outcome.
UPDATE `furniture_definitions`
   SET `stuff_data_type` = 6
 WHERE `logic` = 'wf_highscore'
   AND `stuff_data_type` <> 6
   AND `deleted_at` IS NULL;
