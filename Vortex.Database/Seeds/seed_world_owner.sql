-- The player the world's zones belong to. Nobody logs in as it: rooms need an owner, and a zone is
-- the server's, not a player's.
--
-- profile_visible is named because the model gives it no column default; MySQL's strict mode
-- refuses the insert without it.

INSERT INTO `players` (`name`, `profile_visible`)
VALUES ('__world__', FALSE);
