-- Achievements 11 to 35 and the category/name fixes of the SeedMoreAchievementsFixCategories
-- migration (InsertData/UpdateData, moved here when the history was squashed into InitialCreate).
--
-- M0 dropped the rows of removed features, ids kept unchanged: 25 GamePlayerExperience and
-- 26 GameAuthorExperience (wired games), 28 PetFeeding (pet food), 32 MonsterPlantBreeder (monster
-- plants), 35 CameraPhotoCount (camera), and their levels.

UPDATE `achievements` SET `category` = 'identity'
WHERE `id` = 4;

UPDATE `achievements` SET `category` = 'identity'
WHERE `id` = 5;

UPDATE `achievements` SET `category` = 'room_builder'
WHERE `id` = 9;

UPDATE `achievements` SET `name` = 'FriendListSize'
WHERE `id` = 6;

UPDATE `achievement_levels` SET `badge_code` = 'ACH_FriendListSize1'
WHERE `id` = 26;

UPDATE `achievement_levels` SET `badge_code` = 'ACH_FriendListSize2'
WHERE `id` = 27;

UPDATE `achievement_levels` SET `badge_code` = 'ACH_FriendListSize3'
WHERE `id` = 28;

UPDATE `achievement_levels` SET `badge_code` = 'ACH_FriendListSize4'
WHERE `id` = 29;

UPDATE `achievement_levels` SET `badge_code` = 'ACH_FriendListSize5'
WHERE `id` = 30;

INSERT INTO `achievements` (`id`, `name`, `category`, `display_method`)
VALUES
    (11, 'RegistrationDuration', 'explore', 0),
    (12, 'HappyHour', 'explore', 0),
    (13, 'Name', 'identity', 0),
    (14, 'AvatarTags', 'identity', 0),
    (15, 'EmailVerification', 'identity', 0),
    (16, 'GiftGiver', 'social', 0),
    (17, 'GiftReceiver', 'social', 0),
    (18, 'NotesLeft', 'social', 0),
    (19, 'NotesReceived', 'social', 0),
    (20, 'RoomDecoWallpaper', 'room_builder', 0),
    (21, 'RoomDecoFloor', 'room_builder', 0),
    (22, 'RoomDecoLandscape', 'room_builder', 0),
    (23, 'RoomDecoHosting', 'room_builder', 0),
    (24, 'RoomRank', 'room_builder', 0),
    (27, 'PetLover', 'pets', 0),
    (29, 'PetLevelUp', 'pets', 0),
    (30, 'PetRespectReceiver', 'pets', 0),
    (31, 'HorseRent', 'pets', 0),
    (33, 'MusicPlayer', 'music', 0),
    (34, 'MusicCollector', 'music', 0);

INSERT INTO `achievement_levels` (`id`, `achievement_id`, `level`, `badge_code`, `progress_requirement`, `reward_amount`, `reward_type`, `score_points`)
VALUES
    (51, 11, 1, 'ACH_RegistrationDuration1', 1, 10, 0, 1),
    (52, 11, 2, 'ACH_RegistrationDuration2', 10, 20, 0, 2),
    (53, 11, 3, 'ACH_RegistrationDuration3', 25, 30, 0, 3),
    (54, 11, 4, 'ACH_RegistrationDuration4', 50, 40, 0, 4),
    (55, 11, 5, 'ACH_RegistrationDuration5', 100, 50, 0, 5),
    (56, 12, 1, 'ACH_HappyHour1', 1, 10, 0, 1),
    (57, 12, 2, 'ACH_HappyHour2', 10, 20, 0, 2),
    (58, 12, 3, 'ACH_HappyHour3', 25, 30, 0, 3),
    (59, 12, 4, 'ACH_HappyHour4', 50, 40, 0, 4),
    (60, 12, 5, 'ACH_HappyHour5', 100, 50, 0, 5),
    (61, 13, 1, 'ACH_Name1', 1, 10, 0, 1),
    (62, 13, 2, 'ACH_Name2', 10, 20, 0, 2),
    (63, 13, 3, 'ACH_Name3', 25, 30, 0, 3),
    (64, 13, 4, 'ACH_Name4', 50, 40, 0, 4),
    (65, 13, 5, 'ACH_Name5', 100, 50, 0, 5),
    (66, 14, 1, 'ACH_AvatarTags1', 1, 10, 0, 1),
    (67, 14, 2, 'ACH_AvatarTags2', 10, 20, 0, 2),
    (68, 14, 3, 'ACH_AvatarTags3', 25, 30, 0, 3),
    (69, 14, 4, 'ACH_AvatarTags4', 50, 40, 0, 4),
    (70, 14, 5, 'ACH_AvatarTags5', 100, 50, 0, 5),
    (71, 15, 1, 'ACH_EmailVerification1', 1, 10, 0, 1),
    (72, 15, 2, 'ACH_EmailVerification2', 10, 20, 0, 2),
    (73, 15, 3, 'ACH_EmailVerification3', 25, 30, 0, 3),
    (74, 15, 4, 'ACH_EmailVerification4', 50, 40, 0, 4),
    (75, 15, 5, 'ACH_EmailVerification5', 100, 50, 0, 5),
    (76, 16, 1, 'ACH_GiftGiver1', 1, 10, 0, 1),
    (77, 16, 2, 'ACH_GiftGiver2', 10, 20, 0, 2),
    (78, 16, 3, 'ACH_GiftGiver3', 25, 30, 0, 3),
    (79, 16, 4, 'ACH_GiftGiver4', 50, 40, 0, 4),
    (80, 16, 5, 'ACH_GiftGiver5', 100, 50, 0, 5),
    (81, 17, 1, 'ACH_GiftReceiver1', 1, 10, 0, 1),
    (82, 17, 2, 'ACH_GiftReceiver2', 10, 20, 0, 2),
    (83, 17, 3, 'ACH_GiftReceiver3', 25, 30, 0, 3),
    (84, 17, 4, 'ACH_GiftReceiver4', 50, 40, 0, 4),
    (85, 17, 5, 'ACH_GiftReceiver5', 100, 50, 0, 5),
    (86, 18, 1, 'ACH_NotesLeft1', 1, 10, 0, 1),
    (87, 18, 2, 'ACH_NotesLeft2', 10, 20, 0, 2),
    (88, 18, 3, 'ACH_NotesLeft3', 25, 30, 0, 3),
    (89, 18, 4, 'ACH_NotesLeft4', 50, 40, 0, 4),
    (90, 18, 5, 'ACH_NotesLeft5', 100, 50, 0, 5),
    (91, 19, 1, 'ACH_NotesReceived1', 1, 10, 0, 1),
    (92, 19, 2, 'ACH_NotesReceived2', 10, 20, 0, 2);
INSERT INTO `achievement_levels` (`id`, `achievement_id`, `level`, `badge_code`, `progress_requirement`, `reward_amount`, `reward_type`, `score_points`)
VALUES
    (93, 19, 3, 'ACH_NotesReceived3', 25, 30, 0, 3),
    (94, 19, 4, 'ACH_NotesReceived4', 50, 40, 0, 4),
    (95, 19, 5, 'ACH_NotesReceived5', 100, 50, 0, 5),
    (96, 20, 1, 'ACH_RoomDecoWallpaper1', 1, 10, 0, 1),
    (97, 20, 2, 'ACH_RoomDecoWallpaper2', 10, 20, 0, 2),
    (98, 20, 3, 'ACH_RoomDecoWallpaper3', 25, 30, 0, 3),
    (99, 20, 4, 'ACH_RoomDecoWallpaper4', 50, 40, 0, 4),
    (100, 20, 5, 'ACH_RoomDecoWallpaper5', 100, 50, 0, 5),
    (101, 21, 1, 'ACH_RoomDecoFloor1', 1, 10, 0, 1),
    (102, 21, 2, 'ACH_RoomDecoFloor2', 10, 20, 0, 2),
    (103, 21, 3, 'ACH_RoomDecoFloor3', 25, 30, 0, 3),
    (104, 21, 4, 'ACH_RoomDecoFloor4', 50, 40, 0, 4),
    (105, 21, 5, 'ACH_RoomDecoFloor5', 100, 50, 0, 5),
    (106, 22, 1, 'ACH_RoomDecoLandscape1', 1, 10, 0, 1),
    (107, 22, 2, 'ACH_RoomDecoLandscape2', 10, 20, 0, 2),
    (108, 22, 3, 'ACH_RoomDecoLandscape3', 25, 30, 0, 3),
    (109, 22, 4, 'ACH_RoomDecoLandscape4', 50, 40, 0, 4),
    (110, 22, 5, 'ACH_RoomDecoLandscape5', 100, 50, 0, 5),
    (111, 23, 1, 'ACH_RoomDecoHosting1', 1, 10, 0, 1),
    (112, 23, 2, 'ACH_RoomDecoHosting2', 10, 20, 0, 2),
    (113, 23, 3, 'ACH_RoomDecoHosting3', 25, 30, 0, 3),
    (114, 23, 4, 'ACH_RoomDecoHosting4', 50, 40, 0, 4),
    (115, 23, 5, 'ACH_RoomDecoHosting5', 100, 50, 0, 5),
    (116, 24, 1, 'ACH_RoomRank1', 1, 10, 0, 1),
    (117, 24, 2, 'ACH_RoomRank2', 10, 20, 0, 2),
    (118, 24, 3, 'ACH_RoomRank3', 25, 30, 0, 3),
    (119, 24, 4, 'ACH_RoomRank4', 50, 40, 0, 4),
    (120, 24, 5, 'ACH_RoomRank5', 100, 50, 0, 5),
    (131, 27, 1, 'ACH_PetLover1', 1, 10, 0, 1),
    (132, 27, 2, 'ACH_PetLover2', 10, 20, 0, 2),
    (133, 27, 3, 'ACH_PetLover3', 25, 30, 0, 3),
    (134, 27, 4, 'ACH_PetLover4', 50, 40, 0, 4);
INSERT INTO `achievement_levels` (`id`, `achievement_id`, `level`, `badge_code`, `progress_requirement`, `reward_amount`, `reward_type`, `score_points`)
VALUES
    (135, 27, 5, 'ACH_PetLover5', 100, 50, 0, 5),
    (141, 29, 1, 'ACH_PetLevelUp1', 1, 10, 0, 1),
    (142, 29, 2, 'ACH_PetLevelUp2', 10, 20, 0, 2),
    (143, 29, 3, 'ACH_PetLevelUp3', 25, 30, 0, 3),
    (144, 29, 4, 'ACH_PetLevelUp4', 50, 40, 0, 4),
    (145, 29, 5, 'ACH_PetLevelUp5', 100, 50, 0, 5),
    (146, 30, 1, 'ACH_PetRespectReceiver1', 1, 10, 0, 1),
    (147, 30, 2, 'ACH_PetRespectReceiver2', 10, 20, 0, 2),
    (148, 30, 3, 'ACH_PetRespectReceiver3', 25, 30, 0, 3),
    (149, 30, 4, 'ACH_PetRespectReceiver4', 50, 40, 0, 4),
    (150, 30, 5, 'ACH_PetRespectReceiver5', 100, 50, 0, 5),
    (151, 31, 1, 'ACH_HorseRent1', 1, 10, 0, 1),
    (152, 31, 2, 'ACH_HorseRent2', 10, 20, 0, 2),
    (153, 31, 3, 'ACH_HorseRent3', 25, 30, 0, 3),
    (154, 31, 4, 'ACH_HorseRent4', 50, 40, 0, 4),
    (155, 31, 5, 'ACH_HorseRent5', 100, 50, 0, 5),
    (161, 33, 1, 'ACH_MusicPlayer1', 1, 10, 0, 1),
    (162, 33, 2, 'ACH_MusicPlayer2', 10, 20, 0, 2),
    (163, 33, 3, 'ACH_MusicPlayer3', 25, 30, 0, 3),
    (164, 33, 4, 'ACH_MusicPlayer4', 50, 40, 0, 4),
    (165, 33, 5, 'ACH_MusicPlayer5', 100, 50, 0, 5),
    (166, 34, 1, 'ACH_MusicCollector1', 1, 10, 0, 1),
    (167, 34, 2, 'ACH_MusicCollector2', 10, 20, 0, 2),
    (168, 34, 3, 'ACH_MusicCollector3', 25, 30, 0, 3),
    (169, 34, 4, 'ACH_MusicCollector4', 50, 40, 0, 4),
    (170, 34, 5, 'ACH_MusicCollector5', 100, 50, 0, 5);
