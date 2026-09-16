-- The stamp bundles the Collectors Guild sells, priced in silver.
--
-- Seeded because the minting half of the tab is unusable without them, and unusable in the worst
-- way: nft_mintable_clothing.sql fills the list with items whose price is a currency the player has
-- no way to hold, so the tab looks like something that should work and never does. An empty list at
-- least reads as "nothing yet".
--
-- The silver itself is earned, not seeded. It arrives through the shop (Vortex.Shop maps
-- ShopProductKind.Duckets onto CurrencyType.Silver) or through an admin grant from the dashboard's
-- currency operations. currency_types.starting_amount stays 0, which is collectibles_currencies.sql's
-- own deliberate choice: a new account should see nothing in the Guild until it has done something.
--
-- THE PRICES ARE PLACEHOLDERS. Habbo's own silver prices are in none of the shipped assets, so
-- there is nothing here to port -- these are round numbers with a discount that grows with the
-- bundle, and every one of them is editable from the dashboard, which writes this same table.
--
-- product_code is a free label here and is NOT joined to furniture_definitions, unlike the one on
-- nft_mintable_item_types: the offer is sent to the client as a plain string
-- (MintTokenOfferSnapshot.ProductCode), so it names the bundle rather than a piece of furniture.

INSERT IGNORE INTO `nft_mint_token_offers`
  (`product_code`, `silver_price`, `amount_tokens`, `enabled`, `sort_order`,
   `created_at`, `updated_at`)
VALUES
  ('collectibles_stamp_1',  50, 1,  1, 0, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  ('collectibles_stamp_5',  225, 5,  1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  ('collectibles_stamp_10', 400, 10, 1, 2, UTC_TIMESTAMP(), UTC_TIMESTAMP());
