-- Each content document's source path and text, and each pack's migrations.json (content authoring
-- §3), added to a database created before InitialCreate 20261014092904, without regenerating it,
-- then the history row moved from the previous InitialCreate. Re-runnable: a column already there is
-- skipped.
--
-- Run it only while no emulator runs on the database: the content service reads these columns. A
-- push of the repository's pack afterwards fills them (no new content version).

DROP PROCEDURE IF EXISTS `vortex_add_content_column`;

DELIMITER //
CREATE PROCEDURE `vortex_add_content_column`(IN p_table VARCHAR(64), IN p_column VARCHAR(64), IN p_definition VARCHAR(128))
BEGIN
    -- Not named column_name: it would shadow information_schema's own column and always match.
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = p_table AND `COLUMN_NAME` = p_column
    ) THEN
        SET @add_sql = CONCAT('ALTER TABLE `', p_table, '` ADD COLUMN `', p_column, '` ', p_definition);
        PREPARE add_statement FROM @add_sql;
        EXECUTE add_statement;
        DEALLOCATE PREPARE add_statement;
    END IF;
END //
DELIMITER ;

CALL `vortex_add_content_column`('rpg_content', 'source_path', 'varchar(256) CHARACTER SET utf8mb4 NULL');
CALL `vortex_add_content_column`('rpg_content', 'source', 'longtext CHARACTER SET utf8mb4 NULL');
CALL `vortex_add_content_column`('rpg_content_packs', 'migrations', 'longtext CHARACTER SET utf8mb4 NULL');

DROP PROCEDURE `vortex_add_content_column`;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261014092904_InitialCreate'
WHERE `MigrationId` = '20261013205300_InitialCreate';
