-- The item tables (spec Objets §5) on a database created before them, without regenerating it:
-- the tables as InitialCreate 20261008235710 builds them, then the history row moved from the
-- previous InitialCreate. Additive, re-runnable.

CREATE TABLE IF NOT EXISTS `rpg_items` (
  `id` int NOT NULL AUTO_INCREMENT,
  `player_id` int NOT NULL,
  `definition_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `quantity` int NOT NULL,
  `durability` int DEFAULT NULL,
  `bound` tinyint(1) NOT NULL,
  `container` int NOT NULL DEFAULT '0',
  `slot` int NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `IX_rpg_items_definition_id` (`definition_id`),
  KEY `IX_rpg_items_player_id` (`player_id`),
  CONSTRAINT `FK_rpg_items_players_player_id` FOREIGN KEY (`player_id`) REFERENCES `players` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `rpg_item_grants` (
  `id` int NOT NULL AUTO_INCREMENT,
  `grant_key` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `player_id` int NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_rpg_item_grants_grant_key` (`grant_key`),
  KEY `IX_rpg_item_grants_player_id` (`player_id`),
  CONSTRAINT `FK_rpg_item_grants_players_player_id` FOREIGN KEY (`player_id`) REFERENCES `players` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261008235710_InitialCreate'
WHERE `MigrationId` = '20261008163542_InitialCreate';
