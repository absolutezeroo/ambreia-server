-- Definitions the generated binding pass rebinds away from their own behaviour.
--
-- `furni_logic_bindings.sql` is generated from the shipped assets and its statements are
-- name-scoped: `UPDATE furniture_definitions SET logic = 'furniture_multistate' WHERE name IN (...)`.
-- The generator does protect a definition already bound to a registered Vortex logic, but it decides
-- that per row, from the dump it read at generation time, and then writes per name. Two ways out of
-- that gap, and this hotel has hit both:
--
--   1. A behaviour Vortex gained AFTER the pass was generated. The scan saw `none` on those rows,
--      so the asset's own value -- `furniture_basic` or `furniture_multistate`, because the client
--      has no logic for something that is purely server-side -- won.
--   2. A namesake. A classname is not a key here (3 533 duplicates), so a protected row is
--      overwritten anyway the moment a sibling row with the same classname was not protected.
--
-- The result is silent: the furni places, resolves cleanly to the family default, and does nothing.
-- Nothing logs, because `furniture_basic` is a perfectly real logic -- it is just not this furni's.
--
-- Every name below is a `[RoomObjectLogic("...")]` key that is also its own classname, so the
-- correct value is the classname itself. `FurnitureLogicBindingTests` used to fail when a newly
-- registered logic needed to join the list.
--
-- Runs after the generated pass, and re-runnable: `logic <> name` touches only the rows still
-- carrying the generated value.
--
-- M0 removed the wired boxes, which were every other name on this list; the one key left is the
-- invisible click tile.

UPDATE `furniture_definitions`
   SET `logic` = `name`
 WHERE `name` IN (
    'room_invisible_click_tile'
)
   AND `logic` <> `name`;
