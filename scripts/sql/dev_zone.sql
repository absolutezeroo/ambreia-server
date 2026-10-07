-- The development zone: one room on model_a owned by __world__, and world.start_room_id pointing at
-- it so every login lands there. Applied by scripts/import-reference-data.ps1 after the import,
-- because the room models come from the source database and InitialCreate has none.
--
-- Re-runnable: the room is created once (matched on owner and name), and the config key is
-- rewritten to whatever id it has. Without model_a or __world__ nothing is created and the key is
-- set to 0, which the SSO handler reads as "no start zone".

SET @world = (SELECT `id` FROM `players` WHERE `name` = '__world__' ORDER BY `id` LIMIT 1);
SET @model = (SELECT `id` FROM `room_models` WHERE `name` = 'model_a' ORDER BY `id` LIMIT 1);

INSERT INTO `rooms` (`name`, `description`, `player_id`, `model_id`)
SELECT 'Zone de développement', 'La zone de départ du noyau', @world, @model
FROM DUAL
WHERE @world IS NOT NULL
  AND @model IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM `rooms`
       WHERE `player_id` = @world AND `name` = 'Zone de développement' AND `deleted_at` IS NULL
  );

SET @zone = (
    SELECT `id` FROM `rooms`
     WHERE `player_id` = @world AND `name` = 'Zone de développement' AND `deleted_at` IS NULL
     ORDER BY `id` LIMIT 1
);

INSERT INTO `server_config` (`config_key`, `config_value`, `description`)
VALUES ('world.start_room_id', CAST(COALESCE(@zone, 0) AS CHAR), 'Room every login is forwarded into (0 = none)')
ON DUPLICATE KEY UPDATE `config_value` = VALUES(`config_value`);

SELECT 'world.start_room_id', COALESCE(@zone, 0);
