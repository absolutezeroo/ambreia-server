-- ---------------------------------------------------------------------------
-- pet_levels seed
--
-- 20 levels (1-20) for all pet types (0-28), rebuilt from the official server.
--
-- experience_required is the total XP at which the level is reached. The table
-- is Habbo's, and a capture of the live hotel (2026-09-19, PetInfo/3930) pins
-- two of its entries exactly: a pet on 1404 XP reads level 7 with a goal of
-- 1800, one on 1830 reads level 8 with a goal of 2400.
--
-- energy_cap is 100 + 20 * level -- 240 at level 7 and 260 at level 8 in the
-- same capture. Two points fit a line; if a third ever contradicts it, this is
-- the formula to revisit. Arcturus says 100 * level, which the capture refutes.
--
-- nutrition_cap (the panel's happiness bar) is a flat 100 at both levels.
--
-- Level 0 never existed on Habbo and was our own off-by-one: a level 1 pet with
-- less than 100 XP resolved back to level 0 on its first scratch.
-- ---------------------------------------------------------------------------
DELETE FROM pet_levels WHERE level = 0;

INSERT INTO pet_levels (pet_type, level, experience_required, energy_cap, nutrition_cap)
SELECT pet_type, level, experience_required, energy_cap, nutrition_cap
FROM (
    SELECT
        t.pet_type,
        l.level,
        l.experience_required,
        100 + l.level * 20 AS energy_cap,
        100 AS nutrition_cap
    FROM (
        SELECT 1  AS level,     0 AS experience_required UNION ALL
        SELECT 2,     100 UNION ALL
        SELECT 3,     200 UNION ALL
        SELECT 4,     400 UNION ALL
        SELECT 5,     600 UNION ALL
        SELECT 6,     900 UNION ALL
        SELECT 7,    1300 UNION ALL
        SELECT 8,    1800 UNION ALL
        SELECT 9,    2400 UNION ALL
        SELECT 10,   3200 UNION ALL
        SELECT 11,   4300 UNION ALL
        SELECT 12,   5700 UNION ALL
        SELECT 13,   7600 UNION ALL
        SELECT 14,  10100 UNION ALL
        SELECT 15,  13300 UNION ALL
        SELECT 16,  17500 UNION ALL
        SELECT 17,  23000 UNION ALL
        SELECT 18,  30200 UNION ALL
        SELECT 19,  39600 UNION ALL
        SELECT 20,  51900
    ) l
    CROSS JOIN (
        SELECT 0  AS pet_type UNION ALL SELECT 1  UNION ALL SELECT 2  UNION ALL SELECT 3  UNION ALL
        SELECT 4              UNION ALL SELECT 5  UNION ALL SELECT 6  UNION ALL SELECT 7  UNION ALL
        SELECT 8              UNION ALL SELECT 9  UNION ALL SELECT 10 UNION ALL SELECT 11 UNION ALL
        SELECT 12             UNION ALL SELECT 13 UNION ALL SELECT 14 UNION ALL SELECT 15 UNION ALL
        SELECT 16             UNION ALL SELECT 17 UNION ALL SELECT 18 UNION ALL SELECT 19 UNION ALL
        SELECT 20             UNION ALL SELECT 21 UNION ALL SELECT 22 UNION ALL SELECT 23 UNION ALL
        SELECT 24             UNION ALL SELECT 25 UNION ALL SELECT 26 UNION ALL SELECT 27 UNION ALL
        SELECT 28
    ) t
) AS src
ON DUPLICATE KEY UPDATE
    experience_required = src.experience_required,
    energy_cap          = src.energy_cap,
    nutrition_cap       = src.nutrition_cap;
