-- =====================================================================================================
-- CIMS - clear operational data before go-live (keeps the setup)
--
--   KEEP   : Users, Permissions, Stocks (settings), BarcodeFormats, StockBarcodeFormats,
--            MaxMinFormulas, MaxMinStockSettings, CustomerWorkdays, PRLookups
--   CLEAR  : Parts (products) + PartStocks + MaxMinPartConfigs, ScanTransactions, ScanAdjustments,
--            StockImports, StockTransfers, StockDeleteLogs, PRHeaders / PRDetails, PickListPrintLogs,
--            ForecastOrderImports, SystemLogs
--   Identity columns restart at 1. Product images on the file server are not touched.
--   BACK UP THE DATABASE FIRST - this cannot be undone.
-- =====================================================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE FROM CIMS.ScanAdjustments;
DELETE FROM CIMS.ScanTransactions;
DELETE FROM CIMS.StockImports;
DELETE FROM CIMS.StockTransfers;
DELETE FROM CIMS.StockDeleteLogs;
DELETE FROM CIMS.PRDetails;
DELETE FROM CIMS.PRHeaders;
DELETE FROM CIMS.PickListPrintLogs;
DELETE FROM CIMS.ForecastOrderImports;
DELETE FROM CIMS.MaxMinPartConfigs;
DELETE FROM CIMS.PartStocks;
DELETE FROM CIMS.Parts;
DELETE FROM CIMS.SystemLogs;

UPDATE CIMS.MaxMinStockSettings SET LastCalc = NULL;
UPDATE CIMS.Users SET IsOnline = 0;

COMMIT TRANSACTION;
GO

-- identities restart at 1 (tables are empty now)
DECLARE @t NVARCHAR(200);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name)
    FROM sys.tables t
    WHERE SCHEMA_NAME(t.schema_id) = 'CIMS'
      AND t.name IN ('ScanAdjustments','ScanTransactions','StockImports','StockTransfers','StockDeleteLogs','PRDetails',
                     'PickListPrintLogs','ForecastOrderImports','MaxMinPartConfigs','Parts','SystemLogs')
      AND EXISTS (SELECT 1 FROM sys.identity_columns ic WHERE ic.object_id = t.object_id);
OPEN c; FETCH NEXT FROM c INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC('IF NOT EXISTS (SELECT 1 FROM ' + @t + ') DBCC CHECKIDENT (''' + @t + ''', RESEED, 0) WITH NO_INFOMSGS;');
    FETCH NEXT FROM c INTO @t;
END
CLOSE c; DEALLOCATE c;
GO

SELECT 'Parts' AS tbl, COUNT(*) AS rows_left FROM CIMS.Parts
UNION ALL SELECT 'ScanTransactions', COUNT(*) FROM CIMS.ScanTransactions
UNION ALL SELECT 'SystemLogs', COUNT(*) FROM CIMS.SystemLogs
UNION ALL SELECT 'PRHeaders', COUNT(*) FROM CIMS.PRHeaders
UNION ALL SELECT 'Users (kept)', COUNT(*) FROM CIMS.Users
UNION ALL SELECT 'Permissions (kept)', COUNT(*) FROM CIMS.Permissions
UNION ALL SELECT 'Stocks (kept)', COUNT(*) FROM CIMS.Stocks
UNION ALL SELECT 'BarcodeFormats (kept)', COUNT(*) FROM CIMS.BarcodeFormats;
