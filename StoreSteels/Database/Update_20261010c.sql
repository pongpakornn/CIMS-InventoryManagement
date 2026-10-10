-- =====================================================================================================
-- CIMS update 2026-10-10 (c): DEDUCTED list (what SCAN IN cut from the source stock, e.g. STOCK-PANTA)
--   * CIMS.StockTransfers.ScanTransactionID: the SCAN IN that made this transfer (TransferMode = SCAN)
--     New scans fill it. Older SCAN transfers are linked to their scan by product, stock and time (+/- 5 s).
--   Adds one column + one index. Safe to run more than once.
-- =====================================================================================================
IF COL_LENGTH('CIMS.StockTransfers', 'ScanTransactionID') IS NULL
    ALTER TABLE CIMS.StockTransfers ADD ScanTransactionID INT NULL;
GO

UPDATE st
   SET st.ScanTransactionID = x.TransactionID
  FROM CIMS.StockTransfers st
 CROSS APPLY (SELECT TOP 1 t.TransactionID
                FROM CIMS.ScanTransactions t
               WHERE t.PartID = st.PartID AND t.StockID = st.ToStockID AND t.TransactionType = 'IN'
                 AND t.TransactionDate BETWEEN DATEADD(SECOND, -5, st.TransferDate) AND DATEADD(SECOND, 5, st.TransferDate)
                 AND NOT EXISTS (SELECT 1 FROM CIMS.StockTransfers o WHERE o.ScanTransactionID = t.TransactionID)
               ORDER BY ABS(DATEDIFF(MILLISECOND, t.TransactionDate, st.TransferDate))) x
 WHERE st.TransferMode = 'SCAN' AND st.ScanTransactionID IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StockTransfers_ScanTx' AND object_id = OBJECT_ID('CIMS.StockTransfers'))
    CREATE INDEX IX_StockTransfers_ScanTx ON CIMS.StockTransfers (ScanTransactionID) INCLUDE (Quantity, FromStockID, TransferDate);
GO
