-- Builders Club membership in the catalogue: the Builders Club tree's front page, the page that
-- sells membership time, and the page that sells furni-limit add-ons.
--
-- HOW THE CLIENT BUYS IT. Both selling pages go through the ordinary PurchaseFromCatalog: the
-- `builders_club_loyalty` and `builders_club_addons` layouts list the page's offers and call
-- showPurchaseConfirmation (BuilderLoyaltyCatalogWidget / BuilderAddonsCatalogWidget). The add-ons
-- page greys its buttons out without an active membership, and the server refuses them the same
-- way. The front page's own "subscribe" button opens the website shop
-- (`web.shop.subscription.relativeUrl`) and sells nothing here.
--
-- WHAT A PRODUCT IS. product_type 8 = Builders Club days, 9 = Builders Club furni
-- (Vortex.Primitives.Furniture.Enums.ProductType); `extra_param` is the amount. `localization_id`
-- is the official product code from productdata (builders_club_14_days, builders_club_addon_45furnis,
-- ...), which is what the client names the offer with.
--
-- CREDITS OR DIAMONDS. The hotel sells it for either, so each product gets one offer per currency:
-- a credit offer and a diamond offer side by side, both named by the same product code. A price of
-- 0 leaves that variant out. A diamond offer is `cost_currency` with `currency_type_id` = 5 -- the
-- column carries the activity-point type, not a currency_types row id, as the other 5000 diamond
-- offers do (tools/catalog_converter/convert.py) and as CatalogPurchaseGrain debits it.
--
-- PRICES ARE OURS. Nothing records an official figure; these are a starting point, not evidence.
-- Change them here before running, or afterwards in the dashboard's catalogue page.
--
-- RE-RUNNABLE. Every statement is guarded by NOT EXISTS; pages are keyed by layout inside the
-- Builders Club tree (catalog_type 1), offers by page, localization id and currency.

SET @diamonds = 5;

--                              credits  diamonds
SET @credits_7_days = 25;       SET @diamonds_7_days = 15;
SET @credits_14_days = 45;      SET @diamonds_14_days = 25;
SET @credits_31_days = 85;      SET @diamonds_31_days = 45;
SET @credits_3_months = 230;    SET @diamonds_3_months = 120;

SET @credits_addon_45 = 20;     SET @diamonds_addon_45 = 10;
SET @credits_addon_250 = 90;    SET @diamonds_addon_250 = 45;
SET @credits_addon_600 = 190;   SET @diamonds_addon_600 = 95;
SET @credits_addon_1000 = 290;  SET @diamonds_addon_1000 = 145;

-- ---------------------------------------------------------------------------------------------
-- 1. The pages
-- ---------------------------------------------------------------------------------------------

INSERT INTO `catalog_pages`
  (`catalog_type`, `parent_id`, `localization`, `name`, `icon`, `layout`, `image_data`, `text_data`, `sort_order`, `visible`)
SELECT 1, NULL, 'Builders Club', 'builders_club', 1, 'builders_club_frontpage', NULL, NULL, 0, 1
  FROM DUAL
 WHERE NOT EXISTS (
   SELECT 1 FROM `catalog_pages`
    WHERE `layout` = 'builders_club_frontpage' AND `catalog_type` = 1 AND `deleted_at` IS NULL);

SET @page_front := (
  SELECT `id` FROM `catalog_pages`
   WHERE `layout` = 'builders_club_frontpage' AND `catalog_type` = 1 AND `deleted_at` IS NULL
   ORDER BY `id` LIMIT 1);

INSERT INTO `catalog_pages`
  (`catalog_type`, `parent_id`, `localization`, `name`, `icon`, `layout`, `image_data`, `text_data`, `sort_order`, `visible`)
SELECT 1, @page_front, 'Membership', 'builders_club_loyalty', 1, 'builders_club_loyalty', NULL, NULL, 1, 1
  FROM DUAL
 WHERE @page_front IS NOT NULL
   AND NOT EXISTS (
   SELECT 1 FROM `catalog_pages`
    WHERE `layout` = 'builders_club_loyalty' AND `catalog_type` = 1 AND `deleted_at` IS NULL);

INSERT INTO `catalog_pages`
  (`catalog_type`, `parent_id`, `localization`, `name`, `icon`, `layout`, `image_data`, `text_data`, `sort_order`, `visible`)
SELECT 1, @page_front, 'Add-ons', 'builders_club_addons', 1, 'builders_club_addons', NULL, NULL, 2, 1
  FROM DUAL
 WHERE @page_front IS NOT NULL
   AND NOT EXISTS (
   SELECT 1 FROM `catalog_pages`
    WHERE `layout` = 'builders_club_addons' AND `catalog_type` = 1 AND `deleted_at` IS NULL);

SET @page_loyalty := (
  SELECT `id` FROM `catalog_pages`
   WHERE `layout` = 'builders_club_loyalty' AND `catalog_type` = 1 AND `deleted_at` IS NULL
   ORDER BY `id` LIMIT 1);

SET @page_addons := (
  SELECT `id` FROM `catalog_pages`
   WHERE `layout` = 'builders_club_addons' AND `catalog_type` = 1 AND `deleted_at` IS NULL
   ORDER BY `id` LIMIT 1);

-- ---------------------------------------------------------------------------------------------
-- 2. The offers and their products
-- ---------------------------------------------------------------------------------------------

DROP TEMPORARY TABLE IF EXISTS `tmp_bc_offers`;
CREATE TEMPORARY TABLE `tmp_bc_offers` (
  `code`         VARCHAR(64) NOT NULL,
  `page_id`      INT NULL,
  `product_type` INT NOT NULL,
  `amount`       INT NOT NULL,
  `in_diamonds`  TINYINT NOT NULL,
  `price`        INT NOT NULL,
  PRIMARY KEY (`code`, `in_diamonds`)
);

INSERT INTO `tmp_bc_offers` (`code`, `page_id`, `product_type`, `amount`, `in_diamonds`, `price`) VALUES
  ('builders_club_7_days',          @page_loyalty, 8,    7, 0, @credits_7_days),
  ('builders_club_7_days',          @page_loyalty, 8,    7, 1, @diamonds_7_days),
  ('builders_club_14_days',         @page_loyalty, 8,   14, 0, @credits_14_days),
  ('builders_club_14_days',         @page_loyalty, 8,   14, 1, @diamonds_14_days),
  ('builders_club_31_days',         @page_loyalty, 8,   31, 0, @credits_31_days),
  ('builders_club_31_days',         @page_loyalty, 8,   31, 1, @diamonds_31_days),
  ('builders_club_3_months',        @page_loyalty, 8,   93, 0, @credits_3_months),
  ('builders_club_3_months',        @page_loyalty, 8,   93, 1, @diamonds_3_months),
  ('builders_club_addon_45furnis',   @page_addons, 9,   45, 0, @credits_addon_45),
  ('builders_club_addon_45furnis',   @page_addons, 9,   45, 1, @diamonds_addon_45),
  ('builders_club_addon_250furnis',  @page_addons, 9,  250, 0, @credits_addon_250),
  ('builders_club_addon_250furnis',  @page_addons, 9,  250, 1, @diamonds_addon_250),
  ('builders_club_addon_600furnis',  @page_addons, 9,  600, 0, @credits_addon_600),
  ('builders_club_addon_600furnis',  @page_addons, 9,  600, 1, @diamonds_addon_600),
  ('builders_club_addon_1000furnis', @page_addons, 9, 1000, 0, @credits_addon_1000),
  ('builders_club_addon_1000furnis', @page_addons, 9, 1000, 1, @diamonds_addon_1000);

-- A diamond price with no diamond currency row would be a price nobody can pay: the wallet resolves
-- the debit through currency_types, and a missing row refuses it. Say so rather than sell it.
SELECT 'diamonds currency row' AS `check`,
       IF(EXISTS (SELECT 1 FROM `currency_types`
                   WHERE `activity_point_type` = @diamonds AND `enabled` = 1 AND `deleted_at` IS NULL),
          'ok', 'MISSING -- the diamond offers below cannot be paid') AS `status`;

-- can_bundle 0: the quantity picker would multiply membership time, which the loyalty layout
-- never offers. can_gift 0: a gift delivers furni, and this is not furni.
INSERT INTO `catalog_offers`
  (`page_id`, `localization_id`, `cost_credits`, `cost_currency`, `currency_type_id`,
   `can_gift`, `can_bundle`, `club_level`, `discount_percent`, `visible`)
SELECT t.`page_id`, t.`code`,
       IF(t.`in_diamonds` = 1, 0, t.`price`),
       IF(t.`in_diamonds` = 1, t.`price`, 0),
       IF(t.`in_diamonds` = 1, @diamonds, NULL),
       0, 0, 0, 0, 1
  FROM `tmp_bc_offers` t
 WHERE t.`page_id` IS NOT NULL
   AND t.`price` > 0
   AND NOT EXISTS (
   SELECT 1 FROM `catalog_offers` o
    WHERE o.`page_id` = t.`page_id`
      AND o.`localization_id` = t.`code`
      AND (o.`cost_currency` > 0) = (t.`in_diamonds` = 1)
      AND o.`deleted_at` IS NULL);

INSERT INTO `catalog_products`
  (`offer_id`, `product_type`, `definition_id`, `extra_param`, `quantity`,
   `unique_size`, `unique_remaining`, `builders_club_eligible`)
SELECT o.`id`, t.`product_type`, NULL, CAST(t.`amount` AS CHAR), 1, 0, 0, 0
  FROM `tmp_bc_offers` t
  JOIN `catalog_offers` o
    ON o.`page_id` = t.`page_id`
   AND o.`localization_id` = t.`code`
   AND (o.`cost_currency` > 0) = (t.`in_diamonds` = 1)
   AND o.`deleted_at` IS NULL
 WHERE NOT EXISTS (
   SELECT 1 FROM `catalog_products` p
    WHERE p.`offer_id` = o.`id` AND p.`deleted_at` IS NULL);

-- ---------------------------------------------------------------------------------------------
-- 3. Report
-- ---------------------------------------------------------------------------------------------
--
-- The catalogue is cached in memory: the pages appear after a restart, or after saving any
-- catalogue row from the dashboard.

SELECT 'pages' AS `check`,
       p.`id`, p.`name`, p.`layout`, p.`parent_id`,
       (SELECT COUNT(*) FROM `catalog_offers` o WHERE o.`page_id` = p.`id` AND o.`deleted_at` IS NULL) AS `offers`
  FROM `catalog_pages` p
 WHERE p.`catalog_type` = 1
   AND p.`layout` IN ('builders_club_frontpage', 'builders_club_loyalty', 'builders_club_addons')
   AND p.`deleted_at` IS NULL;

SELECT 'offers' AS `check`, o.`id`, o.`localization_id`, o.`cost_credits`, o.`cost_currency`,
       o.`currency_type_id`, x.`product_type`, x.`extra_param`
  FROM `catalog_offers` o
  JOIN `tmp_bc_offers` t
    ON t.`code` = o.`localization_id` AND t.`page_id` = o.`page_id`
   AND (o.`cost_currency` > 0) = (t.`in_diamonds` = 1)
  LEFT JOIN `catalog_products` x ON x.`offer_id` = o.`id` AND x.`deleted_at` IS NULL
 WHERE o.`deleted_at` IS NULL;

DROP TEMPORARY TABLE IF EXISTS `tmp_bc_offers`;
