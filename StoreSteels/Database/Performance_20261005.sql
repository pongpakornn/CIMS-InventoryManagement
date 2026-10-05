-- =====================================================================================================
-- CIMS performance indexes (October 2026) - safe to run more than once
--   * Multi-Scanner "today" list per stock      -> ScanTransactions (StockID, TransactionDate)
--   * ADJUST SCAN / scan -> PR link             -> PRHeaders (ScanTransactionID)
--   * PR list by status / date                  -> PRHeaders (Status, PRDate)
--   * Activity Log filter by user / action      -> SystemLogs (UserID, LogDate), (ActionType, LogDate)
--   * PR detail lookup by PR number             -> PRDetails (PRNumber)
-- =====================================================================================================
SET NOCOUNT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScanTransactions_Stock_Date' AND object_id = OBJECT_ID('CIMS.ScanTransactions'))
    CREATE INDEX IX_ScanTransactions_Stock_Date ON CIMS.ScanTransactions (StockID, TransactionDate)
        INCLUDE (PartID, PartACode, Quantity, TransactionType, IsCancelled);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PRHeaders_ScanTransactionID' AND object_id = OBJECT_ID('CIMS.PRHeaders'))
    CREATE INDEX IX_PRHeaders_ScanTransactionID ON CIMS.PRHeaders (ScanTransactionID) WHERE ScanTransactionID IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PRHeaders_Status_Date' AND object_id = OBJECT_ID('CIMS.PRHeaders'))
    CREATE INDEX IX_PRHeaders_Status_Date ON CIMS.PRHeaders (Status, PRDate);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PRDetails_PRNumber' AND object_id = OBJECT_ID('CIMS.PRDetails'))
    CREATE INDEX IX_PRDetails_PRNumber ON CIMS.PRDetails (PRNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SystemLogs_User_Date' AND object_id = OBJECT_ID('CIMS.SystemLogs'))
    CREATE INDEX IX_SystemLogs_User_Date ON CIMS.SystemLogs (UserID, LogDate);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SystemLogs_Action_Date' AND object_id = OBJECT_ID('CIMS.SystemLogs'))
    CREATE INDEX IX_SystemLogs_Action_Date ON CIMS.SystemLogs (ActionType, LogDate);
GO
PRINT 'CIMS performance indexes ready.';
