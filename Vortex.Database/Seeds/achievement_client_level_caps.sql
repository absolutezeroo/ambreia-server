-- An achievement has as many levels as the client can name, and not one more.
--
-- The caption in the achievement window comes from HabboLocalizationManager.getAchievementName,
-- which tries four keys in order: `badge_name_al_<code>`, `badge_name_al_<base>`,
-- `badge_name_<code>`, `badge_name_<base>` -- the per-level key, then the level-less one whose
-- value carries `%roman%` and therefore covers every level. A family that has the level-less key
-- can hold any number of levels. A family whose only key is `<base>1` holds exactly one, and its
-- level 2 renders as the literal string `badge_name_ACH_Motto2`.
--
-- The two seeds (AddAchievements, SeedMoreAchievementsFixCategories) gave all 35 achievements five
-- levels each, which is a number nothing supports. Six of them have a single key in
-- external_flash_texts and are one-level achievements in the real hotel:
--
--     Motto  AvatarLooks  Name  AvatarTags  EmailVerification  HappyHour
--
-- Every other seeded family either has per-level keys past 5 or has the level-less key, so five
-- levels render.
--
-- `FriendCount` is a third case: no client string under that name, and not the name the achievement
-- consumer credits either -- AchievementNames.FriendListSize is what `accept_friend` advances, so
-- accepting a friend moved nothing and logged "Progress requested for unknown achievement".
--
-- Re-runnable: every statement is scoped by what it has not already fixed.

-- 1. FriendCount -> FriendListSize, badge codes first so they still match their levels.
UPDATE `achievement_levels` `l`
  JOIN `achievements` `a` ON `a`.`id` = `l`.`achievement_id`
   SET `l`.`badge_code` = CONCAT('ACH_FriendListSize', `l`.`level`)
 WHERE `a`.`name` = 'FriendCount';

-- IGNORE: (player_id, badge_code) is unique, so a player holding both codes keeps the one they have.
UPDATE IGNORE `player_badges`
   SET `badge_code` = REPLACE(`badge_code`, 'ACH_FriendCount', 'ACH_FriendListSize')
 WHERE `badge_code` LIKE 'ACH\_FriendCount%';

UPDATE `achievements` SET `name` = 'FriendListSize' WHERE `name` = 'FriendCount';

-- 2. Take back the badges handed out for levels that are about to stop existing. Joined through
--    achievement_levels rather than matched by name, so this removes exactly what step 4 drops.
DELETE `b`
  FROM `player_badges` `b`
  JOIN `achievement_levels` `l` ON `l`.`badge_code` = `b`.`badge_code`
  JOIN `achievements` `a` ON `a`.`id` = `l`.`achievement_id`
 WHERE `a`.`name` IN (
           'Motto', 'AvatarLooks', 'Name', 'AvatarTags', 'EmailVerification', 'HappyHour'
       )
   AND `l`.`level` > 1;

-- 3. Clamp stored progress onto the surviving level. `level` is the count of completed levels, so
--    a player who had reached level 4 of Motto now holds level 1, completed.
UPDATE `player_achievements` `pa`
  JOIN `achievements` `a` ON `a`.`id` = `pa`.`achievement_id`
  JOIN `achievement_levels` `l` ON `l`.`achievement_id` = `a`.`id` AND `l`.`level` = 1
   SET `pa`.`level` = LEAST(`pa`.`level`, 1),
       `pa`.`progress` = LEAST(`pa`.`progress`, `l`.`progress_requirement`)
 WHERE `a`.`name` IN (
           'Motto', 'AvatarLooks', 'Name', 'AvatarTags', 'EmailVerification', 'HappyHour'
       );

-- 4. Drop the invented levels.
DELETE `l`
  FROM `achievement_levels` `l`
  JOIN `achievements` `a` ON `a`.`id` = `l`.`achievement_id`
 WHERE `a`.`name` IN (
           'Motto', 'AvatarLooks', 'Name', 'AvatarTags', 'EmailVerification', 'HappyHour'
       )
   AND `l`.`level` > 1;

-- 5. Hand back the badge of the level they now stand at. A level-up swaps the badge rather than
--    adding one, so a player who had reached level 3 of Motto held ACH_Motto3 and nothing else --
--    step 2 took that back, and without this they would end up having completed a level whose
--    badge they do not have. IGNORE covers the player who somehow holds it already.
INSERT IGNORE INTO `player_badges` (`player_id`, `badge_code`, `slot_id`)
SELECT `pa`.`player_id`, `l`.`badge_code`, 0
  FROM `player_achievements` `pa`
  JOIN `achievements` `a` ON `a`.`id` = `pa`.`achievement_id`
  JOIN `achievement_levels` `l`
    ON `l`.`achievement_id` = `a`.`id`
   AND `l`.`level` = `pa`.`level`
 WHERE `a`.`name` IN (
           'Motto', 'AvatarLooks', 'Name', 'AvatarTags', 'EmailVerification', 'HappyHour'
       );

-- 6. Recompute the achievement score from what is left. The grain only ever adds a delta per level
--    up (PlayerAchievementGrain -> AddAchievementScoreAsync is its single writer), so the levels
--    removed above are still counted in every affected player's total until this runs.
UPDATE `players` `p`
   SET `p`.`achievement_score` = COALESCE((
           SELECT SUM(`l`.`score_points`)
             FROM `player_achievements` `pa`
             JOIN `achievement_levels` `l`
               ON `l`.`achievement_id` = `pa`.`achievement_id`
              AND `l`.`level` <= `pa`.`level`
            WHERE `pa`.`player_id` = `p`.`id`
       ), 0);
