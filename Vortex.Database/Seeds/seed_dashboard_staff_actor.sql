-- The player row dashboard staff actions are attributed to (StaffActorAccount). From the
-- SeedDashboardStaffActor migration.
--
-- profile_visible is named because the model gives it no column default; the migration history
-- added it with a DEFAULT FALSE that a schema built from the model does not have, and MySQL's strict
-- mode refuses the insert without it.

INSERT INTO `players` (`name`, `profile_visible`)
VALUES ('__dashboard_staff__', FALSE);
