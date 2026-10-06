-- The web shop's default products. From the AddShop migration.

INSERT INTO `shop_products` (`code`, `kind`, `amount`, `price_minor`, `currency`, `section`, `sort_order`, `icon`, `featured`, `is_active`)
VALUES ('c-25', 0, 25, 150, 'EUR', 'credits', 1, 1, FALSE, TRUE),
('c-50', 0, 50, 250, 'EUR', 'credits', 2, 2, FALSE, TRUE),
('c-100', 0, 100, 450, 'EUR', 'credits', 3, 3, TRUE, TRUE),
('c-250', 0, 250, 950, 'EUR', 'credits', 4, 4, FALSE, TRUE),
('c-500', 0, 500, 1750, 'EUR', 'credits', 5, 5, FALSE, TRUE),
('c-1000', 0, 1000, 2950, 'EUR', 'credits', 6, 6, FALSE, TRUE),
('hc-1', 4, 1, 550, 'EUR', 'club', 1, 3, FALSE, TRUE),
('hc-3', 4, 3, 1450, 'EUR', 'club', 2, 4, TRUE, TRUE),
('hc-12', 4, 12, 4950, 'EUR', 'club', 3, 6, FALSE, TRUE);
