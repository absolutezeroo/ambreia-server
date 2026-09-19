-- Pet commands, rebuilt 2026-08-03 on the client's own command ids.
--
-- The previous seed numbered its rows 0=Sit, 1=Stand, 2=Lay Down ... while pet_command_names is
-- lifted verbatim from the client's text bundle, where 0=Free, 1=Sit, 2=Down, 8=Stand. Both tables
-- are read with the same id -- PetCommandProvider resolves the spoken word to a name id and then
-- looks up the config by that id -- so the two schemes collided: telling a pet to Sit made it
-- stand, Free made it sit, and Nest matched no row at all and was silently ignored.
--
-- Postures are restricted to the ids a pet asset actually declares. Decoding dog.nitro gives
-- std, beg, bnd, ded, eat, jmp, lay, pla, rdy, scr, sit, snf, spk, mv -- the previous seed used
-- `rll` for Roll Over and `flp` for Back Flip, which resolve to nothing, so the client fell back to
-- standing and the trick was invisible.
--
-- Only commands whose behaviour the room can actually carry out are seeded. An unseeded command
-- resolves to nothing and the pet answers UNKNOWN_COMMAND, which is honest; a seeded command that
-- does the wrong thing is not. Follow, Stay, Silent and the rest wait for the behaviour to exist.
--
-- Re-running is harmless: the delete scopes to the rows this file owns.

DELETE FROM `pet_commands`;

INSERT INTO `pet_commands`
    (`pet_type`, `command`, `level_required`, `posture`, `energy_cost`, `xp_reward`, `created_at`, `updated_at`)
SELECT t.pet_type, c.command, c.level_required, c.posture, c.energy_cost, c.xp_reward, NOW(), NOW()
FROM (
    -- command, level, posture, energy, xp
    -- Levels, energy and experience rebuilt 2026-09-19 against a capture of the live hotel
    -- (room-pet-commands-official-001). Three tiers, each anchored on a measured command:
    --   errand   0 energy,  5 xp   -- the pet walks somewhere; nothing was measured, kept cheap
    --   easy    10 energy, 10 xp   -- MEASURED on Sit, twice: 258 -> 248 -> 238 energy, +10 xp each
    --   hard    28 energy, 20 xp   -- MEASURED on Play dead: +20 xp, 238 -> 210 energy
    -- The old numbers (3/5 xp, 5/10 energy) were roughly a third of what Habbo pays and half what
    -- it charges.
    --
    -- The capture also pins the LEVELS, by bounding them rather than naming them. A level 8 dog was
    -- sent 23 commands of which the first 13 were enabled, in this order:
    --   46 0 1 14 43 2 3 4 17 5 6 7 8 | 9 10 11 12 13 15 16 24 25 26
    -- so everything left of the bar is level <= 8 and everything right of it is level > 8. Jump(9),
    -- Speak(10), Play(11) and Nest(13) were seeded here at 3, 2, 3 and 2 -- a level 2 pet could
    -- nest and speak, which Habbo does not allow until past level 8. They move to the smallest
    -- levels that satisfy the bound while keeping the hotel's own order. The rest keep Arcturus's
    -- values, which the bound and the ordering both agree with.
    SELECT  0 AS command, 1 AS level_required, ''    AS posture,  0 AS energy_cost,  5 AS xp_reward UNION ALL -- Free
    SELECT  1, 1, 'sit', 10, 10 UNION ALL -- Sit
    SELECT 43, 1, 'eat',  0,  5 UNION ALL -- Eat: the room walks it to a bowl
    SELECT  2, 2, 'lay', 10, 10 UNION ALL -- Down
    SELECT  4, 2, 'beg', 10, 10 UNION ALL -- Beg
    SELECT  5, 3, 'ded', 28, 20 UNION ALL -- Play dead
    SELECT  8, 6, 'std', 10, 10 UNION ALL -- Stand
    SELECT  9, 9, 'jmp', 28, 20 UNION ALL -- Jump
    SELECT 10, 10, 'spk', 10, 10 UNION ALL -- Speak
    SELECT 11, 11, 'pla', 28, 20 UNION ALL -- Play
    SELECT 13, 13, 'lay',  0,  5           -- Nest: the room walks it to its nest
) c
CROSS JOIN (
    -- Every pet type the catalogue sells, minus the monsterplant, which is rooted and obeys nothing.
    SELECT  0 AS pet_type UNION ALL SELECT  1 UNION ALL SELECT  2 UNION ALL SELECT  3 UNION ALL
    SELECT  4 UNION ALL SELECT  5 UNION ALL SELECT  6 UNION ALL SELECT  7 UNION ALL
    SELECT  8 UNION ALL SELECT  9 UNION ALL SELECT 10 UNION ALL SELECT 11 UNION ALL
    SELECT 12 UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15 UNION ALL
    SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19 UNION ALL SELECT 20 UNION ALL
    SELECT 21 UNION ALL SELECT 22 UNION ALL SELECT 23 UNION ALL SELECT 24 UNION ALL
    SELECT 25 UNION ALL SELECT 28 UNION ALL SELECT 29 UNION ALL SELECT 30 UNION ALL
    SELECT 31 UNION ALL SELECT 32 UNION ALL SELECT 35 UNION ALL SELECT 36
) t;

-- ─── Pet toys the catalogue left on `default` ────────────────────────────────
-- Habbo's guides name toys as what cheers a pet up. None of these carried a logic, so no pet has
-- ever been able to play with one. Arcturus calls them pet_toy and pet_trampoline.
UPDATE `furniture_definitions`
SET `logic` = 'pet_toy'
WHERE `name` IN ('pet_toy_ball', 'pet_ufo_toy', 'pet_puppy_toy');

UPDATE `furniture_definitions`
SET `logic` = 'pet_trampoline'
WHERE `name` IN ('pet_toy_trampoline');
