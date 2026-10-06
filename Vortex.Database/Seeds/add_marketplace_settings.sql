-- The singleton marketplace settings row MarketplaceSettingsProvider reads, admin-editable. From the
-- AddMarketplaceSettings migration.

INSERT INTO `marketplace_settings` (`commission_percent`, `offer_duration_seconds`)
VALUES (1, 259200);
