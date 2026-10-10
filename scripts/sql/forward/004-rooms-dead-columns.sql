-- The rooms columns nothing reads any more (navigator occupancy, tags, rating, Habbo room settings:
-- pets, trade, moderation, chat, idle, door-tile exit) dropped from a database created before
-- InitialCreate 20261008163542, without regenerating it, then the history row moved from the
-- previous InitialCreate. Re-runnable: a column already gone is skipped.
--
-- Run it only while no emulator runs on the database: an emulator built before the change still
-- selects these columns, and every room it loads afterwards fails.

DROP PROCEDURE IF EXISTS `vortex_drop_room_column`;

DELIMITER //
CREATE PROCEDURE `vortex_drop_room_column`(IN p_column VARCHAR(64))
BEGIN
    -- Not named column_name: it would shadow information_schema's own column and always match.
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'rooms' AND `COLUMN_NAME` = p_column
    ) THEN
        SET @drop_sql = CONCAT('ALTER TABLE `rooms` DROP COLUMN `', p_column, '`');
        PREPARE drop_statement FROM @drop_sql;
        EXECUTE drop_statement;
        DEALLOCATE PREPARE drop_statement;
    END IF;
END //
DELIMITER ;

CALL `vortex_drop_room_column`('users_now');
CALL `vortex_drop_room_column`('allow_pets');
CALL `vortex_drop_room_column`('allow_pets_eat');
CALL `vortex_drop_room_column`('leave_on_door_tile');
CALL `vortex_drop_room_column`('idle_sleep_enabled');
CALL `vortex_drop_room_column`('idle_sleep_timeout_seconds');
CALL `vortex_drop_room_column`('idle_autokick_enabled');
CALL `vortex_drop_room_column`('idle_autokick_timeout_seconds');
CALL `vortex_drop_room_column`('mute_all_pets');
CALL `vortex_drop_room_column`('trade_type');
CALL `vortex_drop_room_column`('mute_type');
CALL `vortex_drop_room_column`('kick_type');
CALL `vortex_drop_room_column`('ban_type');
CALL `vortex_drop_room_column`('chat_mode_type');
CALL `vortex_drop_room_column`('chat_bubble_type');
CALL `vortex_drop_room_column`('chat_speed_type');
CALL `vortex_drop_room_column`('chat_flood_type');
CALL `vortex_drop_room_column`('chat_distance');
CALL `vortex_drop_room_column`('last_active');
CALL `vortex_drop_room_column`('tag_1');
CALL `vortex_drop_room_column`('tag_2');
CALL `vortex_drop_room_column`('score');
CALL `vortex_drop_room_column`('ranking');

DROP PROCEDURE `vortex_drop_room_column`;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261008163542_InitialCreate'
WHERE `MigrationId` = '20261008022654_InitialCreate';
