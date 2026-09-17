-- What a resolution statue offers. Keyed on the achievement so re-running is a no-op.
--
-- Deliberately not every achievement in the hotel. Two are left out on purpose:
-- AllTimeHotelPresence, because a challenge measured in hours online is not winnable inside a
-- week for anyone who has to sleep, and GamePlayed, because a hotel with the games off would show
-- a row nobody can ever advance.
--
-- target_level_offset is 1 everywhere: one level up from wherever the player stands. Raise it for
-- an achievement whose levels are cheap.

INSERT IGNORE INTO `achievement_resolutions`
    (`achievement_id`, `target_level_offset`, `sort_order`, `enabled`, `created_at`, `updated_at`)
SELECT `a`.`id`, `o`.`offset`, `o`.`sort_order`, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()
  FROM `achievements` AS `a`
  JOIN (
        SELECT 'RoomEntry'           AS `name`, 1 AS `offset`, 1 AS `sort_order`
  UNION SELECT 'Login',                   1, 2
  UNION SELECT 'Motto',                   1, 3
  UNION SELECT 'AvatarLooks',             1, 4
  UNION SELECT 'FriendListSize',          1, 5
  UNION SELECT 'RespectGiven',            1, 6
  UNION SELECT 'RespectEarned',           1, 7
  UNION SELECT 'RoomDecoFurniCount',      1, 8
       ) AS `o` ON `o`.`name` = `a`.`name`;
