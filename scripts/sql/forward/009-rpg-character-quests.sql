-- The characters' quest logs (spec M2a §3.3) added to a database created before InitialCreate
-- 20261013205300, without regenerating it, then the history row moved from the previous
-- InitialCreate. Re-runnable: a table already there is kept.
--
-- Run it only while no emulator runs on the database: a quest log grain built after the change
-- writes this table on every quest change.
--
-- Same DDL as the migration: varchars in utf8mb4, timestamps in datetime(6), the player cascade.

CREATE TABLE IF NOT EXISTS `rpg_character_quests` (
    `id` int NOT NULL AUTO_INCREMENT,
    `player_id` int NOT NULL,
    `quest_id` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `status` varchar(16) CHARACTER SET utf8mb4 NOT NULL,
    `progress` varchar(256) CHARACTER SET utf8mb4 NOT NULL,
    `accepted_at` datetime(6) NOT NULL,
    `completed_at` datetime(6) NULL,
    `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    `deleted_at` datetime(6) NULL,
    CONSTRAINT `PK_rpg_character_quests` PRIMARY KEY (`id`),
    CONSTRAINT `FK_rpg_character_quests_players_player_id`
        FOREIGN KEY (`player_id`) REFERENCES `players` (`id`) ON DELETE CASCADE,
    UNIQUE INDEX `IX_rpg_character_quests_player_id_quest_id` (`player_id`, `quest_id`)
) CHARACTER SET = utf8mb4;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261013205300_InitialCreate'
WHERE `MigrationId` = '20261010130111_InitialCreate';
