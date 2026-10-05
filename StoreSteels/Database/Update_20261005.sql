-- =====================================================================================================
-- CIMS update 5 Oct 2026 - safe to run more than once (uses the new CIMS.* names)
--   1) CIMS.Stocks.ScanDisplayField : what PRODUCT CODE shows in Multi-Scanner / HISTORY per stock
--      (PartCode / PartA / PartNumber / Model / Description / FIELD:<FormatID>:<n>) - NULL = PartCode
--   2) History kept 3 months, rolling by day: on day X of month N the rows dated day X of month N-3 (and older)
--      are removed - a little at a time (500 rows per new row written), never the whole table at once.
--      Applies to ScanTransactions, SystemLogs and ScanAdjustments.
-- =====================================================================================================
SET NOCOUNT ON;
GO
IF COL_LENGTH('CIMS.Stocks', 'ScanDisplayField') IS NULL
    ALTER TABLE CIMS.Stocks ADD ScanDisplayField NVARCHAR(40) NULL;
GO

CREATE OR ALTER TRIGGER CIMS.trg_ScanTransactions_Rolling3Months ON CIMS.ScanTransactions AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    -- e.g. today 1 Apr -> cut 2 Jan -> rows dated 1 Jan and older are removed
    DECLARE @cut DATETIME = DATEADD(DAY, 1, CAST(DATEADD(MONTH, -3, CAST(GETDATE() AS DATE)) AS DATETIME));
    IF EXISTS (SELECT 1 FROM CIMS.ScanTransactions WHERE TransactionDate < @cut)
        DELETE TOP (500) FROM CIMS.ScanTransactions WHERE TransactionDate < @cut;
END
GO

CREATE OR ALTER TRIGGER CIMS.trg_SystemLogs_Rolling3Months ON CIMS.SystemLogs AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut DATETIME = DATEADD(DAY, 1, CAST(DATEADD(MONTH, -3, CAST(GETDATE() AS DATE)) AS DATETIME));
    IF EXISTS (SELECT 1 FROM CIMS.SystemLogs WHERE LogDate < @cut)
        DELETE TOP (500) FROM CIMS.SystemLogs WHERE LogDate < @cut;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScanAdjustments_Date' AND object_id = OBJECT_ID('CIMS.ScanAdjustments'))
    CREATE INDEX IX_ScanAdjustments_Date ON CIMS.ScanAdjustments (AdjustDate);
GO
CREATE OR ALTER TRIGGER CIMS.trg_ScanAdjustments_Rolling3Months ON CIMS.ScanAdjustments AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut DATETIME = DATEADD(DAY, 1, CAST(DATEADD(MONTH, -3, CAST(GETDATE() AS DATE)) AS DATETIME));
    IF EXISTS (SELECT 1 FROM CIMS.ScanAdjustments WHERE AdjustDate < @cut)
        DELETE TOP (500) FROM CIMS.ScanAdjustments WHERE AdjustDate < @cut;
END
GO

PRINT 'CIMS update 2026-10-05 done.';
