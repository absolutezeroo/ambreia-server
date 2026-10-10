-- The operator XP grants (audit P0-6: one grant per key, a cap per day) added to a database created
-- before InitialCreate 20261010130111, without regenerating it, then the history row moved from the
-- previous InitialCreate. Re-runnable: a table already there is kept.
--
-- Run it only while no emulator runs on the database: a grain built after the change writes this
-- table on every admin grant.
--
-- Same DDL as the migration: a varchar in utf8mb4, timestamps in datetime(6), the player cascade.

CREATE TABLE IF NOT EXISTS `rpg_xp_grants` (
    `id` int NOT NULL AUTO_INCREMENT,
    `grant_key` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
    `player_id` int NOT NULL,
    `amount` int NOT NULL,
    `source` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    `deleted_at` datetime(6) NULL,
    CONSTRAINT `PK_rpg_xp_grants` PRIMARY KEY (`id`),
    CONSTRAINT `FK_rpg_xp_grants_players_player_id`
        FOREIGN KEY (`player_id`) REFERENCES `players` (`id`) ON DELETE CASCADE,
    UNIQUE INDEX `IX_rpg_xp_grants_grant_key` (`grant_key`),
    INDEX `IX_rpg_xp_grants_player_id_created_at` (`player_id`, `created_at`)
) CHARACTER SET = utf8mb4;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261010130111_InitialCreate'
WHERE `MigrationId` = '20261009214347_InitialCreate';
