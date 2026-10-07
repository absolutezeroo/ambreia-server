-- The player the world's zones belong to. Nobody logs in as it: rooms need an owner, and a zone is
-- the server's, not a player's.

INSERT INTO `players` (`name`)
VALUES ('__world__');
