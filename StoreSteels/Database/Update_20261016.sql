-- Update_20261016.sql
-- Multi-Scanner: the ✔ DEDUCTED / ⚠ NOT DEDUCTED buttons are set by the Admin per UserID + stock
--   * CIMS.UserScanStocks.ShowDeduct = 1 -> the user sees the two buttons for that stock (only stocks that cut stock
--     across stocks: a stock that receives a barcode format with "deduct from stock", and that source stock - today
--     STOCK-MAT receives Panta labels and cuts STOCK-PANTA).
--   * First run: rows of those stocks get ShowDeduct = 1, so users who see the buttons today keep seeing them.
-- Adds one column only - nothing else changes. Safe to run again.

IF COL_LENGTH('CIMS.UserScanStocks', 'ShowDeduct') IS NULL
BEGIN
    ALTER TABLE CIMS.UserScanStocks ADD ShowDeduct BIT NOT NULL CONSTRAINT DF_UserScanStocks_ShowDeduct DEFAULT 0;

    EXEC (N'
        UPDATE x SET ShowDeduct = 1
        FROM CIMS.UserScanStocks x
        WHERE x.StockID IN (SELECT f.SourceStockID FROM CIMS.BarcodeFormats f WHERE f.SourceStockID IS NOT NULL AND f.IsActive = 1)
           OR x.StockID IN (SELECT sf.StockID FROM CIMS.StockBarcodeFormats sf
                            JOIN CIMS.BarcodeFormats f ON f.FormatID = sf.FormatID
                            WHERE f.SourceStockID IS NOT NULL AND f.IsActive = 1 AND sf.StockID <> f.SourceStockID);');
END
GO
