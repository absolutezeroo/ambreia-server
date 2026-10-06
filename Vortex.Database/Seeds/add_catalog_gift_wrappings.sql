-- The gift wrappings, box styles and ribbons the gift dialog offers. From the AddCatalogGiftWrappings
-- migration.

INSERT INTO catalog_gift_wrappings (kind, furniture_name, art_index, is_default, sort_order)
VALUES
    (0, 'present_gen',  NULL, 1, 0),
    (0, 'present_gen1', NULL, 0, 1),
    (0, 'present_gen2', NULL, 0, 2),
    (0, 'present_gen3', NULL, 0, 3),
    (0, 'present_gen4', NULL, 0, 4),
    (0, 'present_gen5', NULL, 0, 5),
    (0, 'present_gen6', NULL, 0, 6),
    (1, NULL, 0, 0, 0), (1, NULL, 1, 0, 1), (1, NULL, 2, 0, 2), (1, NULL, 3, 0, 3),
    (1, NULL, 4, 0, 4), (1, NULL, 5, 0, 5), (1, NULL, 6, 0, 6), (1, NULL, 8, 0, 7),
    (2, NULL, 0, 0, 0), (2, NULL, 1, 0, 1), (2, NULL, 2, 0, 2), (2, NULL, 3, 0, 3),
    (2, NULL, 4, 0, 4), (2, NULL, 5, 0, 5), (2, NULL, 6, 0, 6), (2, NULL, 7, 0, 7),
    (2, NULL, 8, 0, 8), (2, NULL, 9, 0, 9), (2, NULL, 10, 0, 10);
