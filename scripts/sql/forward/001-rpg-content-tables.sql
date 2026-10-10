-- The RPG content tables (spec Contenu §5) on a database created before them, without
-- regenerating it: the four tables as InitialCreate 20261007225517 builds them, then the history
-- row moved to that migration. Additive, re-runnable, touches no existing row but the history.

CREATE TABLE IF NOT EXISTS `rpg_content_packs` (
  `id` int NOT NULL AUTO_INCREMENT,
  `account_id` int NOT NULL,
  `added` int NOT NULL,
  `changed` int NOT NULL,
  `removed` int NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `rpg_content_texts` (
  `id` int NOT NULL AUTO_INCREMENT,
  `language` varchar(8) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `body` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_rpg_content_texts_language` (`language`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `rpg_content` (
  `id` int NOT NULL AUTO_INCREMENT,
  `content_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `kind` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `document` longtext CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `hash` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `pack_id` int NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_rpg_content_content_id` (`content_id`),
  KEY `IX_rpg_content_pack_id` (`pack_id`),
  CONSTRAINT `FK_rpg_content_rpg_content_packs_pack_id` FOREIGN KEY (`pack_id`) REFERENCES `rpg_content_packs` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `rpg_content_migrations` (
  `id` int NOT NULL AUTO_INCREMENT,
  `from_content_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `action` int NOT NULL DEFAULT '0',
  `to_content_id` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `pack_id` int NOT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `IX_rpg_content_migrations_pack_id` (`pack_id`),
  CONSTRAINT `FK_rpg_content_migrations_rpg_content_packs_pack_id` FOREIGN KEY (`pack_id`) REFERENCES `rpg_content_packs` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261007225517_InitialCreate'
WHERE `MigrationId` = '20261007154949_InitialCreate';
