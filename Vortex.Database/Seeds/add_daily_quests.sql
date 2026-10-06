-- The daily quest pool. From the AddDailyQuests migration.

INSERT INTO `quests` (`id`, `campaign_code`, `chain_code`, `localization_code`, `quest_type`, `total_steps`, `reward_type`, `reward_amount`, `sort_order`, `easy`)
VALUES (7, 'daily', 'daily', 'EXPLORE', 'RoomEntry', 10, 0, 20, 1, TRUE),
(8, 'daily', 'daily', 'CHANGELOOK1', 'AvatarLooks', 3, 0, 20, 2, TRUE),
(9, 'daily', 'daily', 'DANCE', 'Dance', 1, 0, 20, 3, TRUE);
