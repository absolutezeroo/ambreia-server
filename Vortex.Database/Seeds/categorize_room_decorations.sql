-- The three room decorations (wallpaper, floor, landscape) get their own categories, which is what
-- the client's room-decoration handling keys on. From the CategorizeRoomDecorations migration.
-- Matches nothing until furniture definitions are imported.

UPDATE furniture_definitions SET category = 2 WHERE name = 'wallpaper' AND type = 1;
UPDATE furniture_definitions SET category = 3 WHERE name = 'floor' AND type = 1;
UPDATE furniture_definitions SET category = 4 WHERE name = 'landscape' AND type = 1;
