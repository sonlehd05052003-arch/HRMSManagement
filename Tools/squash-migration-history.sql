-- Moves an existing database onto the squashed migrations (InitialSchema + SeedTemplateData).
--
-- The database must already have every old migration applied, up to 20261013084419_AddHolidays: its schema
-- then matches InitialSchema and its data is kept as it is. Only __EFMigrationsHistory changes; the old rows
-- are kept in __EFMigrationsHistory_BeforeSquash, so the change can be undone.
--
-- Run once per existing database, before starting the new version of the API:
--   sqlcmd -S localhost -E -d HRMS_DB -i Tools/squash-migration-history.sql
-- Running it again does nothing.

SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261014025454_InitialSchema')
BEGIN
    PRINT N'Already on the squashed migrations; nothing to do.';
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261013084419_AddHolidays')
BEGIN
    RAISERROR(N'Apply every old migration (up to 20261013084419_AddHolidays) with the previous version first.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

SELECT * INTO [__EFMigrationsHistory_BeforeSquash] FROM [__EFMigrationsHistory];
DELETE FROM [__EFMigrationsHistory];
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES
    (N'20261014025454_InitialSchema', N'8.0.23'),
    (N'20261014025500_SeedTemplateData', N'8.0.23');

COMMIT;
PRINT N'Migration history moved to the squashed migrations; the old rows are in __EFMigrationsHistory_BeforeSquash.';
