-- Silver and emeralds, the two collectibles currencies.
--
-- Neither had a currency_types row, which is not the harmless omission it looks like: the wallet
-- resolves a currency by looking one up, so with no row a grant found nothing to credit and
-- returned without doing anything. Reading and debiting still worked, so both currencies behaved
-- like a balance permanently stuck at zero -- and a catalogue offer priced in silver was unbuyable
-- by construction, because nothing could ever put silver in a wallet.
--
-- starting_amount is 0 on purpose, unlike credits. These are earned, not handed out: a new account
-- opening the Collectors Guild should see nothing until it has done something to deserve it. The
-- column is admin-editable, so a hotel that wants to seed newcomers can raise it without a
-- migration.
--
-- activity_point_type stays NULL. It only distinguishes the several currencies that share the
-- ActivityPoints type (duckets, diamonds); these two have a type of their own, and a non-null value
-- here would key them under a kind nothing asks for.

INSERT INTO `currency_types` (`name`, `type`, `activity_point_type`, `enabled`, `starting_amount`,
                              `created_at`, `updated_at`)
SELECT * FROM (
    SELECT 'Silver'   AS `name`, 2 AS `type`, NULL AS `activity_point_type`, 1 AS `enabled`,
           0 AS `starting_amount`, UTC_TIMESTAMP() AS `created_at`, UTC_TIMESTAMP() AS `updated_at`
    UNION ALL
    SELECT 'Emeralds', 3, NULL, 1, 0, UTC_TIMESTAMP(), UTC_TIMESTAMP()
) AS `seed`
WHERE NOT EXISTS (
    SELECT 1 FROM `currency_types` AS `existing` WHERE `existing`.`type` = `seed`.`type`
);
