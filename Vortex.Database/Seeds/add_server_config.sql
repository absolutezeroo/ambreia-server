-- The message of the day. From the AddServerConfig migration.

INSERT INTO `server_config` (`config_key`, `config_value`, `description`)
VALUES ('motd.lines', '["Welcome to Vortex! Have fun and be nice to each other."]', 'Message-of-the-day lines (JSON array)');
