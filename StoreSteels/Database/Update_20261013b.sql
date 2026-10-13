-- Update_20261013b.sql
-- Settings remembered per user (follow the user to any computer):
--   * CIMS.UserSettings: UserID + SettingKey -> SettingValue
--     e.g. ProductControl.Filter (stock shown in the Inventory Registration table)
--          ProductControl.Stocks (stocks ticked by default when registering a product)
-- Creates one table only - nothing else changes. Safe to run again.

IF OBJECT_ID('CIMS.UserSettings', 'U') IS NULL
BEGIN
    CREATE TABLE CIMS.UserSettings (
        UserID       VARCHAR(20)   NOT NULL,
        SettingKey   VARCHAR(60)   NOT NULL,
        SettingValue NVARCHAR(400) NULL,
        UpdatedDate  DATETIME      NOT NULL CONSTRAINT DF_UserSettings_UpdatedDate DEFAULT GETDATE(),
        CONSTRAINT PK_UserSettings PRIMARY KEY (UserID, SettingKey)
    );
END
GO
