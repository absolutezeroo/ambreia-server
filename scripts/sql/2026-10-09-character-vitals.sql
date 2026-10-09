-- The character's current health and mana (spec M1a §7) added to a database created before
-- InitialCreate 20261009175218, without regenerating it, then the history row moved from the
-- previous InitialCreate. Re-runnable: a column already there is skipped.
--
-- Run it only while no emulator runs on the database: a grain built after the change selects
-- these columns, and a character loaded before the script fails.

DROP PROCEDURE IF EXISTS `vortex_add_character_column`;

DELIMITER //
CREATE PROCEDURE `vortex_add_character_column`(IN p_column VARCHAR(64), IN p_definition VARCHAR(128))
BEGIN
    -- Not named column_name: it would shadow information_schema's own column and always match.
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'player_characters' AND `COLUMN_NAME` = p_column
    ) THEN
        SET @add_sql = CONCAT('ALTER TABLE `player_characters` ADD COLUMN `', p_column, '` ', p_definition);
        PREPARE add_statement FROM @add_sql;
        EXECUTE add_statement;
        DEALLOCATE PREPARE add_statement;
    END IF;
END //
DELIMITER ;

CALL `vortex_add_character_column`('current_health', 'int NULL');
CALL `vortex_add_character_column`('current_mana', 'int NULL');
CALL `vortex_add_character_column`('vitals_at', 'datetime(6) NULL');

DROP PROCEDURE `vortex_add_character_column`;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261009175218_InitialCreate'
WHERE `MigrationId` = '20261008235710_InitialCreate';
