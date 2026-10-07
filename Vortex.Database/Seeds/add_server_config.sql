-- The message of the day. From the AddServerConfig migration.

INSERT INTO `server_config` (`config_key`, `config_value`, `description`)
VALUES ('motd.lines', '["Welcome to Vortex! Have fun and be nice to each other."]', 'Message-of-the-day lines (JSON array)');

-- The room every login is forwarded into; 0 = none. scripts/import-reference-data.ps1 rewrites it
-- through dev_zone.sql once the room models exist.
INSERT INTO `server_config` (`config_key`, `config_value`, `description`)
VALUES ('world.start_room_id', '0', 'Room every login is forwarded into (0 = none)');
