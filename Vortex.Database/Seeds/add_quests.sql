-- The social quest chain. From the AddQuests migration.

INSERT INTO `quests` (`id`, `campaign_code`, `chain_code`, `localization_code`, `quest_type`, `total_steps`, `reward_type`, `reward_amount`, `sort_order`, `easy`)
VALUES (1, 'social', 'social', 'ENTEROTHERSROOM', 'RoomEntry', 1, 0, 10, 1, TRUE),
(2, 'social', 'social', 'REQUESTFRIEND', 'FriendListSize', 1, 0, 10, 2, TRUE),
(3, 'social', 'social', 'CHATWITHSOMEONE', 'Chat', 1, 0, 10, 3, TRUE),
(4, 'social', 'social', 'WAVE', 'Wave', 1, 0, 10, 4, TRUE),
(5, 'social', 'social', 'DANCE', 'Dance', 1, 0, 10, 5, TRUE),
(6, 'social', 'social', 'GIVERESPECT', 'RespectGiven', 1, 0, 10, 6, TRUE);
