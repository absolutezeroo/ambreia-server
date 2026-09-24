
-- Les quatre boîtes wired dont le serveur a reçu la logique en 2026-09 et qu'aucun mobi ne portait :
-- projectile (21), courbe de mouvement (22), placeholder global (2000), activateur de succès (2001).
-- Additif et idempotent : réexécutable, ne modifie aucune ligne existante.
--
-- Le projectile et la courbe sont des mobis Habbo : wf_xtra_rotate_to_dir et wf_xtra_mov_curve
-- (furnidata habbo.com, révision 74952), convertis de leur .swf officiel en .nitro sur l'hôte
-- d'assets. Le placeholder global et l'activateur n'existent dans aucun furnidata public : leurs
-- entrées (mêmes ids, dans gamedata/furnidata_json.json) reprennent le graphisme de
-- wf_xtra_mov_no_animation ; c'est le couple (id, logic) qui fait la boîte, pas la classname.
-- L'effet « faire progresser un succès » n'a besoin de rien :
-- wf_act_give_achievement (504300) porte déjà la bonne logique.

INSERT IGNORE INTO `furniture_definitions` (`id`, `sprite_id`, `name`, `type`, `category`, `logic`, `total_states`, `width`, `length`, `stack_height`, `can_stack`, `can_walk`, `can_sit`, `can_lay`, `can_recycle`, `can_trade`, `can_group`, `can_sell`, `usage_policy`, `extra_data`, `created_at`, `updated_at`, `deleted_at`, `stuff_data_type`) VALUES
  (90000021, 90000021, 'wf_xtra_rotate_to_dir', 0, 1, 'wf_xtra_rotate_to_dir', 1, 1, 1, 0.35, 1, 1, 0, 0, 0, 1, 1, 0, 1, NULL, NOW(), NOW(), NULL, 0),
  (90000022, 90000022, 'wf_xtra_mov_curve', 0, 1, 'wf_xtra_mov_curve', 1, 1, 1, 0.35, 1, 1, 0, 0, 0, 1, 1, 0, 1, NULL, NOW(), NOW(), NULL, 0),
  (90002000, 90002000, 'wf_xtra_global_placeholder', 0, 1, 'wf_xtra_global_placeholder', 1, 1, 1, 0.35, 1, 1, 0, 0, 0, 1, 1, 0, 1, NULL, NOW(), NOW(), NULL, 0),
  (90002001, 90002001, 'wf_xtra_achievement_enabler', 0, 1, 'wf_xtra_achievement_enabler', 1, 1, 1, 0.35, 1, 1, 0, 0, 0, 1, 1, 0, 1, NULL, NOW(), NOW(), NULL, 0);

-- En vente sur la page des addons, comme wired_catalog_fill.sql : trois, pas quatre. L'activateur de
-- succès reste hors catalogue — le client dit lui-même que c'est au staff de le configurer.
CREATE TEMPORARY TABLE wired_nouveaux AS
SELECT f.id AS definition_id, f.name, f.type AS product_type,
       (SELECT c.id FROM catalog_pages c JOIN catalog_pages r ON r.id = c.parent_id
         WHERE r.localization = 'wired_furniture' AND c.localization = 'wired_addons'
           AND c.deleted_at IS NULL AND r.deleted_at IS NULL
         LIMIT 1) AS page_id
FROM furniture_definitions f
WHERE f.id IN (90000021, 90000022, 90002000)
  AND NOT EXISTS (SELECT 1 FROM catalog_products p JOIN catalog_offers o ON o.id = p.offer_id
                  WHERE p.definition_id = f.id AND p.deleted_at IS NULL AND o.deleted_at IS NULL);

INSERT INTO catalog_offers (page_id, localization_id, cost_credits, cost_currency, can_gift, can_bundle, club_level, visible)
SELECT page_id, name, 3, 0, 1, 1, 0, 1 FROM wired_nouveaux WHERE page_id IS NOT NULL;

INSERT INTO catalog_products (offer_id, product_type, definition_id, quantity, unique_size, unique_remaining, builders_club_eligible)
SELECT o.id, n.product_type, n.definition_id, 1, 0, 0, 0
FROM wired_nouveaux n
JOIN catalog_offers o ON o.localization_id = n.name AND o.page_id = n.page_id
WHERE n.page_id IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM catalog_products p WHERE p.offer_id = o.id);

DROP TEMPORARY TABLE wired_nouveaux;
