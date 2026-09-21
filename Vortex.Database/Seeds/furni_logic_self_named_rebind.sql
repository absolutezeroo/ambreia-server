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
-- correct value is the classname itself. That is the same repair `scripts/sql/wired_logic_binding_fix.sql`
-- and `scripts/sql/wired_contract_logic_binding.sql` already made by hand for the wired boxes; this
-- is the third round of it, so it lives in the migration path instead of waiting for someone to run
-- a script, and `FurnitureLogicBindingTests` fails when a newly registered logic needs to join the
-- list.
--
-- Runs after the generated pass, and re-runnable: `logic <> name` touches only the rows still
-- carrying the generated value. Adding a name here therefore needs a new migration to apply the
-- file again; the rows the earlier round already repaired are skipped by that same guard.
--
-- One name deliberately absent: `wf_act_forward_user`. It is a registered key here, so it looks
-- like it belongs -- but the furni carrying that name is the forward-to-another-room box (its
-- `logic` is `wf_act_forward_user_to_room`, which is also what `wf_act_teleport_to_room` carries),
-- and the key was claimed by `WiredActionMoveUser`, which nudges a user one tile inside the room.
-- Rebinding it would point a room forward at the wrong box. The membership test for this list is
-- that the name and the behaviour agree, not that the name is a key.

UPDATE `furniture_definitions`
   SET `logic` = `name`
 WHERE `name` IN (
    'room_invisible_click_tile',
    'wf_act_cancel_transaction',
    'wf_act_change_var_val',
    'wf_act_freeze_habbo',
    'wf_act_give_currency',
    'wf_act_give_furni',
    'wf_act_init_transaction',
    'wf_act_neg_send_signal',
    'wf_act_rel_mov',
    'wf_act_remove_furni_inventory',
    'wf_act_send_signal',
    'wf_act_toggle_to_rnd',
    'wf_act_unfreeze_habbo',
    'wf_cnd_chest_has_item_type',
    'wf_cnd_chest_has_items',
    'wf_cnd_has_var',
    'wf_cnd_neg_has_var',
    'wf_cnd_not_triggerer_match',
    'wf_cnd_triggerer_match',
    'wf_cnd_var_age_match',
    'wf_cnd_var_val_match',
    'wf_contract_payment',
    'wf_contract_reward',
    'wf_contract_trade',
    'wf_trg_transaction_complete',
    'wf_trg_transaction_fail',
    'wf_var_echo',
    'wf_xtra_custom_contract',
    'wf_xtra_filter_furni_by_var',
    'wf_xtra_filter_users_by_var',
    'wf_xtra_scan_chest_furni_by_type',
    'wf_xtra_text_output_furni_name',
    'wf_xtra_text_output_username',
    'wf_xtra_text_output_variable'
)
   AND `logic` <> `name`;
