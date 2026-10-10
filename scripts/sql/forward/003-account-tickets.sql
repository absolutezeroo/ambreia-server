-- Account sign-in tickets (spec F3a §5.1) on a database created before them, without regenerating
-- it: the table as InitialCreate 20261008022654 builds it, then the history row moved from the
-- previous InitialCreate. Run after 2026-10-08-player-characters.sql. Additive, re-runnable.

CREATE TABLE IF NOT EXISTS `account_tickets` (
  `id` int NOT NULL AUTO_INCREMENT,
  `account_id` int NOT NULL,
  `ticket` varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ip_address` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `expires_at` datetime(6) DEFAULT NULL,
  `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
  `deleted_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_account_tickets_account_id` (`account_id`),
  UNIQUE KEY `IX_account_tickets_ticket` (`ticket`),
  CONSTRAINT `FK_account_tickets_player_accounts_account_id` FOREIGN KEY (`account_id`) REFERENCES `player_accounts` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

UPDATE `__EFMigrationsHistory`
SET `MigrationId` = '20261008022654_InitialCreate'
WHERE `MigrationId` = '20261008002411_InitialCreate';
