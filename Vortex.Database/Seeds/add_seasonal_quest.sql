-- One seasonal quest, open for a week from the moment the database is created. From the
-- AddSeasonalQuest migration.

INSERT INTO `quests` (`id`,`campaign_code`,`chain_code`,`localization_code`,`quest_type`,`total_steps`,`reward_type`,`reward_amount`,`sort_order`,`easy`,`seasonal`,`ends_at`) VALUES (10,'globe','globe','expljungle','RoomEntry',3,0,30,1,1,1,DATE_ADD(NOW(), INTERVAL 7 DAY));
