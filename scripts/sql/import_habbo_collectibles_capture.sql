-- Reprend le contenu du Collectors Guild d'Habbo, releve sur le vrai serveur.
--
-- Source : une capture de l'ouverture de l'onglet Collectibles, client AIR-1.0.31 (confirme :
-- _composers[3191] et [1285] ne correspondent qu'a ce build). Les ids d'en-tete de cette capture
-- ne sont donc PAS les notres et ne sont copies nulle part ici -- seules les structures et les
-- valeurs le sont, et chaque paquet a ete relu octet par octet jusqu'a consommer son corps
-- exactement.
--
-- Ce que la capture contient, et ce qu'elle ne contient pas :
--   * NftStoreOffers        26 offres   -> section 2
--   * CollectibleMintTokenOffers 10 bundles -> section 3
--   * CollectableMintableItemTypes 60 lignes -> section 4
--   * NftCollections        ABSENT. La requete part bien (GetNftCollections avec le wallet), mais
--     la reponse n'est pas dans le releve. Les collections du Collectors Guild -- les series a
--     completer pour un prix -- demandent une seconde capture, onglet Collections ouvert.
--
-- Deux pieges releves en preparant ce script :
--   1. Les sprite ids d'Habbo ne sont pas ceux de cette furnidata. nft_h24_showcase vaut 14559
--      chez eux et 15870329 ET 24444295 ici. D'ou l'import par classname.
--   2. Habbo vend ses furnis emeraude a leur valeur PLUS UN : nft_emerald_1000 coute 1001
--      emeraudes et en rend 1000. Maintenant que l'echange fonctionne, ces cinq offres sont un
--      puits d'une emeraude, pas une affaire. C'est le choix d'Habbo, pas une coquille -- mais
--      c'est un choix, et il se decide ici.
--
-- Idempotent : relançable sans doublon (product_code est unique sur les trois tables).

-- 1) Verification a blanc -- qu'y a-t-il deja ?
SELECT 'nft_store_offers' AS `table`, COUNT(*) AS `rows` FROM `nft_store_offers`
UNION ALL SELECT 'nft_mint_token_offers', COUNT(*) FROM `nft_mint_token_offers`
UNION ALL SELECT 'nft_mintable_item_types', COUNT(*) FROM `nft_mintable_item_types`;

-- Et les classnames de la section 2 existent-ils dans furniture_definitions ? Une offre qui nomme
-- un meuble absent s'affiche, se paie et ne livre rien.
SELECT o.`code` AS `product_code`,
       COUNT(fd.`id`) AS `definitions`
FROM (
    SELECT 'clothing_nftwesternvest' AS `code`
    UNION ALL SELECT 'clothing_nftwesternshirt'
    UNION ALL SELECT 'nft_h24_showcase'
    UNION ALL SELECT 'nft_h24_largeshowcase'
    UNION ALL SELECT 'nft_h23_paygate'
    UNION ALL SELECT 'nft_h23_teleport'
    UNION ALL SELECT 'nft_emerald_10'
    UNION ALL SELECT 'nft_emerald_50'
    UNION ALL SELECT 'nft_emerald_100'
    UNION ALL SELECT 'nft_emerald_500'
    UNION ALL SELECT 'nft_emerald_1000'
) AS o
LEFT JOIN `furniture_definitions` fd ON fd.`name` = o.`code`
GROUP BY o.`code`
ORDER BY `definitions`, o.`code`;

-- Rien de ce qui suit ne s'execute tant que les deux requetes ci-dessus n'ont pas ete lues.

-- 2) Boutique NFT : les 26 offres de la capture.
--
--    product_code est le CLASSNAME. item_type_id et product_type_id restent vides : ils sont
--    derives a l'execution par CollectibleProductIdentity depuis la definition de meuble, et les
--    ids d'Habbo ne sont pas ceux de cette furnidata (nft_h24_showcase = 14559 chez eux,
--    15870329 et 24444295 ici -- deux lignes pour un meme classname).
INSERT INTO `nft_store_offers`
    (`product_code`, `emerald_price`, `is_featured`, `is_limited`, `mint_limit`, `sold_count`,
     `item_type_id`, `product_type_id`, `score`, `rarity`, `enabled`, `sort_order`,
     `created_at`, `updated_at`)
VALUES
    ('clothing_nftwesternvest', 550, 0, 1, 100, 0, '', 0, 0, 'Rare', 1, 0, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('clothing_nftwesternshirt', 550, 0, 1, 100, 0, '', 0, 0, 'Rare', 1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h24_showcase', 50, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 2, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h24_largeshowcase', 75, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 3, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_paygate', 300, 0, 1, 300, 0, '', 0, 0, 'Uncommon', 1, 4, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_nightstand', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 5, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_chair_r', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 6, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_fence_p', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 7, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_chair_p', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 8, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_teleport', 300, 0, 1, 300, 0, '', 0, 0, 'Uncommon', 1, 9, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_fence', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 10, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_desk', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 11, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_fence_r', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 12, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_chair', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 13, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_table', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 14, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_sofa_r', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 15, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_sofa_p', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 16, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_sofa', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 17, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_stool_p', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 18, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_stool', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 19, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_h23_vintaque_stool_r', 40, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 20, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_emerald_100', 101, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 21, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_emerald_1000', 1001, 0, 1, 15, 0, '', 0, 0, 'Uncommon', 1, 22, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_emerald_500', 501, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 23, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_emerald_50', 51, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 24, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_emerald_10', 11, 0, 1, 10, 0, '', 0, 0, 'Common', 1, 25, UTC_TIMESTAMP(), UTC_TIMESTAMP())
ON DUPLICATE KEY UPDATE `updated_at` = `updated_at`;

-- 3) Bundles de tampons : les 10 offres de la capture, prix en silver.
INSERT INTO `nft_mint_token_offers`
    (`product_code`, `silver_price`, `amount_tokens`, `enabled`, `sort_order`,
     `created_at`, `updated_at`)
VALUES
    ('nft_mint_token_purchase', 100, 1, 1, 0, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_2', 200, 2, 1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_3', 300, 3, 1, 2, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_4', 400, 4, 1, 3, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_5', 500, 5, 1, 4, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_6', 600, 6, 1, 5, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_7', 700, 7, 1, 6, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_8', 800, 8, 1, 7, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_9', 900, 9, 1, 8, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('nft_mint_token_purchase_10', 1000, 10, 1, 9, UTC_TIMESTAMP(), UTC_TIMESTAMP())
ON DUPLICATE KEY UPDATE `updated_at` = `updated_at`;

-- 4) Types mintables : 57 des 60 lignes capturees.
--
--    Trois fenetres de vingt meubles, ouvertes a un mois d'intervalle -- les drops mensuels
--    d'Habbo. Ce sont des meubles ORDINAIRES (wooden_screen, prize1, bonusbag20_3) : minter
--    convertit ce que le joueur possede deja, il ne vend pas du NFT.
--
--    stamp_price est a 1 parce que le paquet ne le porte pas -- a ajuster, ce n'est pas une
--    valeur d'Habbo. limited_edition reste a 0 : le seul champ qui varie dans la capture est
--    vrai pour exactement quatre entrees (celestial_c19_constellations et sa paire, presentes
--    dans deux fenetres), et rien n'atteste ce qu'il signifie.
INSERT INTO `nft_mintable_item_types`
    (`product_code`, `stamp_price`, `starts_at`, `ends_at`, `region_locked`, `limited_edition`,
     `edition_size`, `enabled`, `sort_order`, `created_at`, `updated_at`)
VALUES
    ('wooden_screen*0', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 0, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('bonusbag20_3', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('gothiccafe_c20_sofa', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 2, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('neonpunk_c20_stardeco', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 3, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('hween_c18_olddocs1', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 4, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('val_c19_till', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 5, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('rare_prize19*7', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 6, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('rare_prize19*8', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 7, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('icedblk_c16_sofachair', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 8, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('icedblk_c16_shelves*8', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 9, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('bc_alpha1_c*2', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 10, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('bc_alpha1_b*6', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 11, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('steampunk_lamp', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 12, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('army_c15_teledoor', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 14, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('army_c15_locker', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 15, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('army_c15_watchtwr', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 16, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('room_hcl15_bar', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 17, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('clothing_longmohawk', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 18, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('clothing_layeredlongskirt', 1, '2026-09-02 06:00:00', '2026-12-05 10:00:00', 0, 0, 0, 1, 19, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('fireworks_10', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 20, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('snowb_slope', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 21, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('snowb_divider', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 22, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('waasa_wall_shelf1', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 23, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('room_wl15_sink', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 24, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('classic7_gate', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 25, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('diner_tray_2', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 26, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('sound_set_69', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 27, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('diner_sofa_2*4', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 28, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('CFC_10_coin_bronze', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 29, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('classic9_light', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 30, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('xmas_c18_curtaintopper', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 31, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('xmas_c19_stackedicedrinks', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 32, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('clothing_r19_frosted', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 33, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('matic_tree_silver', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 34, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('matic_probe', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 35, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('room_gh15_ball', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 36, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('clothing_medal3', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 37, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('clothing_medal1', 1, '2026-08-06 06:00:00', '2026-11-10 10:00:00', 0, 0, 0, 1, 38, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('xmas11_luxchair2', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 40, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('wl_sofa', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 41, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('xmas11_luxsofa3', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 42, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('snowb_jump', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 43, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('room_wlof15_bed', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 44, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('classic8_disco', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 45, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('diner_sofa_2*1', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 46, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('diner_cashreg*5', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 47, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('svnr_de', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 48, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('diner_cashreg*3', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 49, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('bonusrare20_2*5', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 50, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('clothing_pompomhat', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 51, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('xmas_c19_pugplushie', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 52, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('matic_walkway_green', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 53, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('matic_tree_yellow', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 54, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('prize1', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 55, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('prize2', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 56, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('prize3', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 57, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    ('bc_alpha1_k*8', 1, '2026-07-02 06:00:00', '2026-10-05 10:00:00', 0, 0, 0, 1, 59, UTC_TIMESTAMP(), UTC_TIMESTAMP())
ON DUPLICATE KEY UPDATE `updated_at` = `updated_at`;

-- Omis : 8051, 14542, 14540 -- aucun meuble ne porte ce sprite id dans cette furnidata.
