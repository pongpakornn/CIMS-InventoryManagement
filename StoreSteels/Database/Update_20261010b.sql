-- =====================================================================================================
-- CIMS update 2026-10-10 (b): NOT DEDUCTED list
--   * CIMS.DeductMisses: every label received with SCAN IN whose source stock could not be cut
--     (mother coil not in STOCK-PANTA = NOT_FOUND / mother coil had less weight than the label = SHORT)
--     Saved automatically by the scanner in the same transaction as the scan.
--     The Multi-Scanner NOT DEDUCTED button lists them (cancelled scans are hidden).
--   Adds one table only. Safe to run more than once.
-- =====================================================================================================
IF OBJECT_ID('CIMS.DeductMisses', 'U') IS NULL
BEGIN
    CREATE TABLE CIMS.DeductMisses (
        MissID         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeductMisses PRIMARY KEY,
        TransactionID  INT            NULL,                 -- CIMS.ScanTransactions (the SCAN IN)
        StockID        INT            NOT NULL,             -- stock received into (STOCK-MAT)
        SourceStockID  INT            NOT NULL,             -- stock that should have been cut (STOCK-PANTA)
        PartID         INT            NOT NULL,
        CoilNo         NVARCHAR(60)   NULL,                 -- child coil on the label
        MotherCoil     NVARCHAR(60)   NULL,
        LabelQty       DECIMAL(18,3)  NOT NULL,
        Deducted       DECIMAL(18,3)  NOT NULL CONSTRAINT DF_DeductMisses_Deducted DEFAULT (0),
        Reason         VARCHAR(20)    NOT NULL,             -- NOT_FOUND / SHORT
        Barcode        NVARCHAR(300)  NULL,
        UserID         VARCHAR(20)    NULL,
        ScanDate       DATETIME       NOT NULL CONSTRAINT DF_DeductMisses_Date DEFAULT (GETDATE())
    );
    CREATE INDEX IX_DeductMisses_Date ON CIMS.DeductMisses (ScanDate) INCLUDE (TransactionID, MotherCoil);
END
GO
