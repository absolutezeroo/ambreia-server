-- The character table (spec Personnage §7) on a database created before it, without regenerating
-- it: the table as InitialCreate 20261008002411 builds it, then the history row moved from the
-- previous InitialCreate. Additive, re-runnable.

CREATE TABLE IF NOT EXISTS `player_characters` (
  `id` int NOT NULL AUTO_INCREMENT,
  `player_id` int NOT NULL,
  `class_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `specialization_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `level` int NOT NULL DEFAULT '1',
  `xp` int NOT NULL DEFAULT '0',
  `last_room_id` int DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_player_characters_player_id` (`player_id`),
  CONSTRAINT `FK_player_characters_players_player_id` FOREIGN KEY (`player_id`) REFERENCES `players` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261008002411_InitialCreate'
WHERE `MigrationId` = '20261007225517_InitialCreate';
