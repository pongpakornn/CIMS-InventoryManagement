-- Update_20261013.sql
-- Multi-Scanner DISPLAY per user:
--   * CIMS.UserScanDisplays: what each user shows in the PRODUCT CODE column of each stock
--     (PRODUCT CODE / PART A / PART NO / MODEL / PRODUCT NAME / a barcode field).
--   * A user without a row for a stock sees the stock's own setting (CIMS.Stocks.ScanDisplayField) as before.
-- Creates one table only - nothing else changes. Safe to run again.

IF OBJECT_ID('CIMS.UserScanDisplays', 'U') IS NULL
BEGIN
    CREATE TABLE CIMS.UserScanDisplays (
        UserID       VARCHAR(20)   NOT NULL,
        StockID      INT           NOT NULL,
        DisplayField NVARCHAR(100) NOT NULL,
        UpdatedDate  DATETIME      NOT NULL CONSTRAINT DF_UserScanDisplays_UpdatedDate DEFAULT GETDATE(),
        CONSTRAINT PK_UserScanDisplays PRIMARY KEY (UserID, StockID)
    );
END
GO
