-- Recovery when the stored RPG content no longer reads with the current records (a field removed
-- or made required in code): the registry refuses the start (spec Contenu §6). This retires every
-- stored definition so the host starts with an empty registry; the next push
-- (`node .claude/skills/run-vortex-emulator/driver.mjs content`) revives each row from content/.
-- Run with the emulator stopped. Re-runnable. Characters keep their class ids meanwhile.

UPDATE `rpg_content` SET `deleted_at` = CURRENT_TIMESTAMP(6) WHERE `deleted_at` IS NULL;
