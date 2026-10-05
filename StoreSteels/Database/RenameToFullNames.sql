-- =====================================================================================================
-- CIMS - rename every table / column / view / procedure / trigger to full names in schema CIMS
--   e.g. dbo.MST_PART.PT_CODE -> CIMS.Parts.PartCode,  dbo.SYS_LOGS -> CIMS.SystemLogs
--   The full mapping is in Database\NameMap.tsv.
--   Safe to run more than once: every step checks the old name first.
--   Run AFTER the legacy scripts (Database\Legacy\MultiStock.sql, MaxMinCalc.sql, PRView.sql) on a database
--   that still has the old names. The CIMS program from this version on uses only the new names.
-- =====================================================================================================
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF SCHEMA_ID('CIMS') IS NULL EXEC('CREATE SCHEMA CIMS AUTHORIZATION dbo');
GO

-- 1) old programmable objects (re-created with the new names at the end) ------------------------------
IF OBJECT_ID('dbo.trg_ScanLog_Rolling3Months', 'TR') IS NOT NULL DROP TRIGGER dbo.trg_ScanLog_Rolling3Months;
IF OBJECT_ID('dbo.trg_TRN_SCAN_AutoPR', 'TR') IS NOT NULL DROP TRIGGER dbo.trg_TRN_SCAN_AutoPR;
IF OBJECT_ID('dbo.trg_MST_USER_GrantStockView', 'TR') IS NOT NULL DROP TRIGGER dbo.trg_MST_USER_GrantStockView;
IF OBJECT_ID('dbo.trg_SysLogs_Rolling3Months', 'TR') IS NOT NULL DROP TRIGGER dbo.trg_SysLogs_Rolling3Months;
IF OBJECT_ID('dbo.trg_MST_PART_STOCK_Box', 'TR') IS NOT NULL DROP TRIGGER dbo.trg_MST_PART_STOCK_Box;
IF OBJECT_ID('dbo.VW_StockMonitoring', 'V') IS NOT NULL DROP VIEW dbo.VW_StockMonitoring;
IF OBJECT_ID('dbo.VW_StoreMonitoring', 'V') IS NOT NULL DROP VIEW dbo.VW_StoreMonitoring;
IF OBJECT_ID('dbo.VW_StockSummary', 'V') IS NOT NULL DROP VIEW dbo.VW_StockSummary;
IF OBJECT_ID('dbo.VW_Dashboard_BarChart', 'V') IS NOT NULL DROP VIEW dbo.VW_Dashboard_BarChart;
IF OBJECT_ID('dbo.VW_Dashboard_PieChart', 'V') IS NOT NULL DROP VIEW dbo.VW_Dashboard_PieChart;
IF OBJECT_ID('dbo.v_PartQRReady', 'V') IS NOT NULL DROP VIEW dbo.v_PartQRReady;
IF OBJECT_ID('dbo.v_PRReady', 'V') IS NOT NULL DROP VIEW dbo.v_PRReady;
IF OBJECT_ID('dbo.sp_GetPartByScan', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_GetPartByScan;
IF OBJECT_ID('dbo.sp_GetPartForQR', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_GetPartForQR;
IF OBJECT_ID('dbo.sp_GetPRList', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_GetPRList;
IF OBJECT_ID('dbo.sp_MaxMin_Calculate', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_MaxMin_Calculate;
IF OBJECT_ID('dbo.sp_PR_SaveLookup', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PR_SaveLookup;
IF OBJECT_ID('dbo.sp_Stock_AddQty', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Stock_AddQty;
IF OBJECT_ID('dbo.sp_Stock_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Stock_Delete;
IF OBJECT_ID('dbo.sp_Stock_GrantViewAll', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Stock_GrantViewAll;
IF OBJECT_ID('dbo.sp_Stock_Transfer', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Stock_Transfer;
IF OBJECT_ID('dbo.fn_MaxMin_Round') IS NOT NULL DROP FUNCTION dbo.fn_MaxMin_Round;
GO

-- 2) computed column (re-added after the rename)
IF COL_LENGTH('dbo.MST_USER', 'USR_STATUS_DESC') IS NOT NULL ALTER TABLE dbo.MST_USER DROP COLUMN USR_STATUS_DESC;
-- check constraint on the stock balance blocks the column rename (re-added as CK_PartStocks_Quantity)
IF OBJECT_ID('dbo.CK_PSTK_QTY', 'C') IS NOT NULL ALTER TABLE dbo.MST_PART_STOCK DROP CONSTRAINT CK_PSTK_QTY;
GO

-- 3) tables: dbo.<old> -> CIMS.<new>
IF OBJECT_ID('dbo.MST_BARCODE_FMT', 'U') IS NOT NULL AND OBJECT_ID('CIMS.BarcodeFormats', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_BARCODE_FMT; EXEC sp_rename 'CIMS.MST_BARCODE_FMT', 'BarcodeFormats'; END;
IF OBJECT_ID('dbo.MST_CALC_CONFIG', 'U') IS NOT NULL AND OBJECT_ID('CIMS.MaxMinPartConfigs', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_CALC_CONFIG; EXEC sp_rename 'CIMS.MST_CALC_CONFIG', 'MaxMinPartConfigs'; END;
IF OBJECT_ID('dbo.MST_CALC_FORMULA', 'U') IS NOT NULL AND OBJECT_ID('CIMS.MaxMinFormulas', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_CALC_FORMULA; EXEC sp_rename 'CIMS.MST_CALC_FORMULA', 'MaxMinFormulas'; END;
IF OBJECT_ID('dbo.MST_CALC_STOCK', 'U') IS NOT NULL AND OBJECT_ID('CIMS.MaxMinStockSettings', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_CALC_STOCK; EXEC sp_rename 'CIMS.MST_CALC_STOCK', 'MaxMinStockSettings'; END;
IF OBJECT_ID('dbo.MST_CUST_WORKDAY', 'U') IS NOT NULL AND OBJECT_ID('CIMS.CustomerWorkdays', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_CUST_WORKDAY; EXEC sp_rename 'CIMS.MST_CUST_WORKDAY', 'CustomerWorkdays'; END;
IF OBJECT_ID('dbo.MST_PART', 'U') IS NOT NULL AND OBJECT_ID('CIMS.Parts', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_PART; EXEC sp_rename 'CIMS.MST_PART', 'Parts'; END;
IF OBJECT_ID('dbo.MST_PART_STOCK', 'U') IS NOT NULL AND OBJECT_ID('CIMS.PartStocks', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_PART_STOCK; EXEC sp_rename 'CIMS.MST_PART_STOCK', 'PartStocks'; END;
IF OBJECT_ID('dbo.MST_PERM', 'U') IS NOT NULL AND OBJECT_ID('CIMS.Permissions', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_PERM; EXEC sp_rename 'CIMS.MST_PERM', 'Permissions'; END;
IF OBJECT_ID('dbo.MST_PR_LOOKUP', 'U') IS NOT NULL AND OBJECT_ID('CIMS.PRLookups', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_PR_LOOKUP; EXEC sp_rename 'CIMS.MST_PR_LOOKUP', 'PRLookups'; END;
IF OBJECT_ID('dbo.MST_STOCK', 'U') IS NOT NULL AND OBJECT_ID('CIMS.Stocks', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_STOCK; EXEC sp_rename 'CIMS.MST_STOCK', 'Stocks'; END;
IF OBJECT_ID('dbo.MST_STOCK_DEL_LOG', 'U') IS NOT NULL AND OBJECT_ID('CIMS.StockDeleteLogs', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_STOCK_DEL_LOG; EXEC sp_rename 'CIMS.MST_STOCK_DEL_LOG', 'StockDeleteLogs'; END;
IF OBJECT_ID('dbo.MST_STOCK_FMT', 'U') IS NOT NULL AND OBJECT_ID('CIMS.StockBarcodeFormats', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_STOCK_FMT; EXEC sp_rename 'CIMS.MST_STOCK_FMT', 'StockBarcodeFormats'; END;
IF OBJECT_ID('dbo.MST_USER', 'U') IS NOT NULL AND OBJECT_ID('CIMS.Users', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.MST_USER; EXEC sp_rename 'CIMS.MST_USER', 'Users'; END;
IF OBJECT_ID('dbo.PackingPrintLog', 'U') IS NOT NULL AND OBJECT_ID('CIMS.PickListPrintLogs', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.PackingPrintLog; EXEC sp_rename 'CIMS.PackingPrintLog', 'PickListPrintLogs'; END;
IF OBJECT_ID('dbo.SYS_LOGS', 'U') IS NOT NULL AND OBJECT_ID('CIMS.SystemLogs', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.SYS_LOGS; EXEC sp_rename 'CIMS.SYS_LOGS', 'SystemLogs'; END;
IF OBJECT_ID('dbo.TRN_IMPORT_STAGE', 'U') IS NOT NULL AND OBJECT_ID('CIMS.ForecastOrderImports', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.TRN_IMPORT_STAGE; EXEC sp_rename 'CIMS.TRN_IMPORT_STAGE', 'ForecastOrderImports'; END;
IF OBJECT_ID('dbo.TRN_PR_D', 'U') IS NOT NULL AND OBJECT_ID('CIMS.PRDetails', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.TRN_PR_D; EXEC sp_rename 'CIMS.TRN_PR_D', 'PRDetails'; END;
IF OBJECT_ID('dbo.TRN_PR_H', 'U') IS NOT NULL AND OBJECT_ID('CIMS.PRHeaders', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.TRN_PR_H; EXEC sp_rename 'CIMS.TRN_PR_H', 'PRHeaders'; END;
IF OBJECT_ID('dbo.TRN_SCAN', 'U') IS NOT NULL AND OBJECT_ID('CIMS.ScanTransactions', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.TRN_SCAN; EXEC sp_rename 'CIMS.TRN_SCAN', 'ScanTransactions'; END;
IF OBJECT_ID('dbo.TRN_SCAN_ADJ', 'U') IS NOT NULL AND OBJECT_ID('CIMS.ScanAdjustments', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.TRN_SCAN_ADJ; EXEC sp_rename 'CIMS.TRN_SCAN_ADJ', 'ScanAdjustments'; END;
IF OBJECT_ID('dbo.TRN_STOCK_IMPORT', 'U') IS NOT NULL AND OBJECT_ID('CIMS.StockImports', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.TRN_STOCK_IMPORT; EXEC sp_rename 'CIMS.TRN_STOCK_IMPORT', 'StockImports'; END;
IF OBJECT_ID('dbo.TRN_TRANSFER', 'U') IS NOT NULL AND OBJECT_ID('CIMS.StockTransfers', 'U') IS NULL
BEGIN ALTER SCHEMA CIMS TRANSFER dbo.TRN_TRANSFER; EXEC sp_rename 'CIMS.TRN_TRANSFER', 'StockTransfers'; END;
GO

-- 4) columns
-- CIMS.BarcodeFormats
IF COL_LENGTH('CIMS.BarcodeFormats', 'FMT_ID') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'FormatID') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.FMT_ID', 'FormatID', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'FMT_NAME') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'FormatName') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.FMT_NAME', 'FormatName', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'DELIMITER') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'Delimiter') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.DELIMITER', 'Delimiter', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'CODE_POS') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'CodePosition') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.CODE_POS', 'CodePosition', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'ALT_CODE_POS') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'AltCodePosition') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.ALT_CODE_POS', 'AltCodePosition', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'QTY_POS') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'QuantityPosition') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.QTY_POS', 'QuantityPosition', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'MIN_FIELDS') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'MinFields') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.MIN_FIELDS', 'MinFields', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'SAMPLE_TEXT') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'SampleText') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.SAMPLE_TEXT', 'SampleText', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'IS_ACTIVE') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'IsActive') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.IS_ACTIVE', 'IsActive', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'CREATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'CreatedBy') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.CREATED_BY', 'CreatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'CREATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'CreatedDate') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.CREATED_DATE', 'CreatedDate', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'UPDATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'UpdatedBy') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.UPDATED_BY', 'UpdatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'UPDATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'UpdatedDate') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.UPDATED_DATE', 'UpdatedDate', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'SOURCE_STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'SourceStockID') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.SOURCE_STK_ID', 'SourceStockID', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'DELIM_MODE') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'DelimiterMode') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.DELIM_MODE', 'DelimiterMode', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'TRIM_CHARS') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'TrimChars') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.TRIM_CHARS', 'TrimChars', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'CODE_PREFIX') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'CodePrefix') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.CODE_PREFIX', 'CodePrefix', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'CODE_CUT') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'CodeCut') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.CODE_CUT', 'CodeCut', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'MATCH_START') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'MatchStart') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.MATCH_START', 'MatchStart', 'COLUMN';
IF COL_LENGTH('CIMS.BarcodeFormats', 'MATCH_END') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'MatchEnd') IS NULL EXEC sp_rename 'CIMS.BarcodeFormats.MATCH_END', 'MatchEnd', 'COLUMN';
-- CIMS.MaxMinPartConfigs
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'CONFIG_ID') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'ConfigID') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.CONFIG_ID', 'ConfigID', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'CUST_CODE') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'CustomerCode') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.CUST_CODE', 'CustomerCode', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'PT_ID') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'PartID') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.PT_ID', 'PartID', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'PT_ACODE') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'PartACode') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.PT_ACODE', 'PartACode', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'DAY_MIN') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'MinDays') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.DAY_MIN', 'MinDays', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'DAY_MAX') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'MaxDays') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.DAY_MAX', 'MaxDays', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'UPDATE_BY') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'UpdatedBy') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.UPDATE_BY', 'UpdatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'UPDATE_DATE') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'UpdatedDate') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.UPDATE_DATE', 'UpdatedDate', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinPartConfigs', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinPartConfigs', 'StockID') IS NULL EXEC sp_rename 'CIMS.MaxMinPartConfigs.STK_ID', 'StockID', 'COLUMN';
-- CIMS.MaxMinFormulas
IF COL_LENGTH('CIMS.MaxMinFormulas', 'FORMULA_ID') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'FormulaID') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.FORMULA_ID', 'FormulaID', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'FORMULA_NAME') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'FormulaName') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.FORMULA_NAME', 'FormulaName', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'QTY_SOURCE') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'QuantitySource') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.QTY_SOURCE', 'QuantitySource', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'ROUND_MODE') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'RoundMode') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.ROUND_MODE', 'RoundMode', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'DEF_DAY_MAX') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'DefaultMaxDays') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.DEF_DAY_MAX', 'DefaultMaxDays', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'DEF_DAY_MIN') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'DefaultMinDays') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.DEF_DAY_MIN', 'DefaultMinDays', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'DEF_WORKDAYS') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'DefaultWorkdays') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.DEF_WORKDAYS', 'DefaultWorkdays', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'IS_DEFAULT') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'IsDefault') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.IS_DEFAULT', 'IsDefault', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'UPDATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'UpdatedBy') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.UPDATED_BY', 'UpdatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinFormulas', 'UPDATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinFormulas', 'UpdatedDate') IS NULL EXEC sp_rename 'CIMS.MaxMinFormulas.UPDATED_DATE', 'UpdatedDate', 'COLUMN';
-- CIMS.MaxMinStockSettings
IF COL_LENGTH('CIMS.MaxMinStockSettings', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinStockSettings', 'StockID') IS NULL EXEC sp_rename 'CIMS.MaxMinStockSettings.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinStockSettings', 'FORMULA_ID') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinStockSettings', 'FormulaID') IS NULL EXEC sp_rename 'CIMS.MaxMinStockSettings.FORMULA_ID', 'FormulaID', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinStockSettings', 'AUTO_CALC') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinStockSettings', 'AutoCalc') IS NULL EXEC sp_rename 'CIMS.MaxMinStockSettings.AUTO_CALC', 'AutoCalc', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinStockSettings', 'LAST_CALC') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinStockSettings', 'LastCalc') IS NULL EXEC sp_rename 'CIMS.MaxMinStockSettings.LAST_CALC', 'LastCalc', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinStockSettings', 'UPDATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinStockSettings', 'UpdatedBy') IS NULL EXEC sp_rename 'CIMS.MaxMinStockSettings.UPDATED_BY', 'UpdatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.MaxMinStockSettings', 'UPDATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.MaxMinStockSettings', 'UpdatedDate') IS NULL EXEC sp_rename 'CIMS.MaxMinStockSettings.UPDATED_DATE', 'UpdatedDate', 'COLUMN';
-- CIMS.CustomerWorkdays
IF COL_LENGTH('CIMS.CustomerWorkdays', 'CUST_CODE') IS NOT NULL AND COL_LENGTH('CIMS.CustomerWorkdays', 'CustomerCode') IS NULL EXEC sp_rename 'CIMS.CustomerWorkdays.CUST_CODE', 'CustomerCode', 'COLUMN';
IF COL_LENGTH('CIMS.CustomerWorkdays', 'WORK_DATE') IS NOT NULL AND COL_LENGTH('CIMS.CustomerWorkdays', 'WorkDate') IS NULL EXEC sp_rename 'CIMS.CustomerWorkdays.WORK_DATE', 'WorkDate', 'COLUMN';
IF COL_LENGTH('CIMS.CustomerWorkdays', 'UPDATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.CustomerWorkdays', 'UpdatedBy') IS NULL EXEC sp_rename 'CIMS.CustomerWorkdays.UPDATED_BY', 'UpdatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.CustomerWorkdays', 'UPDATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.CustomerWorkdays', 'UpdatedDate') IS NULL EXEC sp_rename 'CIMS.CustomerWorkdays.UPDATED_DATE', 'UpdatedDate', 'COLUMN';
-- CIMS.Parts
IF COL_LENGTH('CIMS.Parts', 'PT_ID') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'PartID') IS NULL EXEC sp_rename 'CIMS.Parts.PT_ID', 'PartID', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_CODE') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'PartCode') IS NULL EXEC sp_rename 'CIMS.Parts.PT_CODE', 'PartCode', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_SUPPLIER') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Supplier') IS NULL EXEC sp_rename 'CIMS.Parts.PT_SUPPLIER', 'Supplier', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_CAT') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Category') IS NULL EXEC sp_rename 'CIMS.Parts.PT_CAT', 'Category', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_DESC') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Description') IS NULL EXEC sp_rename 'CIMS.Parts.PT_DESC', 'Description', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_BIN') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Bin') IS NULL EXEC sp_rename 'CIMS.Parts.PT_BIN', 'Bin', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_PSZ') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'PackSize') IS NULL EXEC sp_rename 'CIMS.Parts.PT_PSZ', 'PackSize', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'QTY_MAX') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'MaxQuantity') IS NULL EXEC sp_rename 'CIMS.Parts.QTY_MAX', 'MaxQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'QTY_MIN') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'MinQuantity') IS NULL EXEC sp_rename 'CIMS.Parts.QTY_MIN', 'MinQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'QTY_STKB') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'StockQuantity') IS NULL EXEC sp_rename 'CIMS.Parts.QTY_STKB', 'StockQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_REMARK') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Remark') IS NULL EXEC sp_rename 'CIMS.Parts.PT_REMARK', 'Remark', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'LIT_STAT') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Status') IS NULL EXEC sp_rename 'CIMS.Parts.LIT_STAT', 'Status', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_QR') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'QRCode') IS NULL EXEC sp_rename 'CIMS.Parts.PT_QR', 'QRCode', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'IS_ACTIVE') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'IsActive') IS NULL EXEC sp_rename 'CIMS.Parts.IS_ACTIVE', 'IsActive', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'IS_SHOW_MST') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'IsShowInMaster') IS NULL EXEC sp_rename 'CIMS.Parts.IS_SHOW_MST', 'IsShowInMaster', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_IMG') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'ImageFileName') IS NULL EXEC sp_rename 'CIMS.Parts.PT_IMG', 'ImageFileName', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_CUST') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Customer') IS NULL EXEC sp_rename 'CIMS.Parts.PT_CUST', 'Customer', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_PARTA') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'PartA') IS NULL EXEC sp_rename 'CIMS.Parts.PT_PARTA', 'PartA', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_PARTNO') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'PartNumber') IS NULL EXEC sp_rename 'CIMS.Parts.PT_PARTNO', 'PartNumber', 'COLUMN';
IF COL_LENGTH('CIMS.Parts', 'PT_MODEL') IS NOT NULL AND COL_LENGTH('CIMS.Parts', 'Model') IS NULL EXEC sp_rename 'CIMS.Parts.PT_MODEL', 'Model', 'COLUMN';
-- CIMS.PartStocks
IF COL_LENGTH('CIMS.PartStocks', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'StockID') IS NULL EXEC sp_rename 'CIMS.PartStocks.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'PT_ID') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'PartID') IS NULL EXEC sp_rename 'CIMS.PartStocks.PT_ID', 'PartID', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'QTY') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'Quantity') IS NULL EXEC sp_rename 'CIMS.PartStocks.QTY', 'Quantity', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'QTY_MAX') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'MaxQuantity') IS NULL EXEC sp_rename 'CIMS.PartStocks.QTY_MAX', 'MaxQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'QTY_MIN') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'MinQuantity') IS NULL EXEC sp_rename 'CIMS.PartStocks.QTY_MIN', 'MinQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'REMARK') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'Remark') IS NULL EXEC sp_rename 'CIMS.PartStocks.REMARK', 'Remark', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'UPDATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'UpdatedDate') IS NULL EXEC sp_rename 'CIMS.PartStocks.UPDATED_DATE', 'UpdatedDate', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'QTY_BOX') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'BoxQuantity') IS NULL EXEC sp_rename 'CIMS.PartStocks.QTY_BOX', 'BoxQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.PartStocks', 'IS_SHOW') IS NOT NULL AND COL_LENGTH('CIMS.PartStocks', 'IsShow') IS NULL EXEC sp_rename 'CIMS.PartStocks.IS_SHOW', 'IsShow', 'COLUMN';
-- CIMS.Permissions
IF COL_LENGTH('CIMS.Permissions', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.Permissions', 'UserID') IS NULL EXEC sp_rename 'CIMS.Permissions.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.Permissions', 'SYS_ID') IS NOT NULL AND COL_LENGTH('CIMS.Permissions', 'SystemID') IS NULL EXEC sp_rename 'CIMS.Permissions.SYS_ID', 'SystemID', 'COLUMN';
IF COL_LENGTH('CIMS.Permissions', 'PERM_VIEW') IS NOT NULL AND COL_LENGTH('CIMS.Permissions', 'CanView') IS NULL EXEC sp_rename 'CIMS.Permissions.PERM_VIEW', 'CanView', 'COLUMN';
IF COL_LENGTH('CIMS.Permissions', 'PERM_ADD') IS NOT NULL AND COL_LENGTH('CIMS.Permissions', 'CanAdd') IS NULL EXEC sp_rename 'CIMS.Permissions.PERM_ADD', 'CanAdd', 'COLUMN';
IF COL_LENGTH('CIMS.Permissions', 'PERM_EDIT') IS NOT NULL AND COL_LENGTH('CIMS.Permissions', 'CanEdit') IS NULL EXEC sp_rename 'CIMS.Permissions.PERM_EDIT', 'CanEdit', 'COLUMN';
IF COL_LENGTH('CIMS.Permissions', 'PERM_DEL') IS NOT NULL AND COL_LENGTH('CIMS.Permissions', 'CanDelete') IS NULL EXEC sp_rename 'CIMS.Permissions.PERM_DEL', 'CanDelete', 'COLUMN';
IF COL_LENGTH('CIMS.Permissions', 'PERM_APP') IS NOT NULL AND COL_LENGTH('CIMS.Permissions', 'CanApprove') IS NULL EXEC sp_rename 'CIMS.Permissions.PERM_APP', 'CanApprove', 'COLUMN';
-- CIMS.PRLookups
IF COL_LENGTH('CIMS.PRLookups', 'FIELD') IS NOT NULL AND COL_LENGTH('CIMS.PRLookups', 'FieldName') IS NULL EXEC sp_rename 'CIMS.PRLookups.FIELD', 'FieldName', 'COLUMN';
IF COL_LENGTH('CIMS.PRLookups', 'LOOKUP_VALUE') IS NOT NULL AND COL_LENGTH('CIMS.PRLookups', 'LookupValue') IS NULL EXEC sp_rename 'CIMS.PRLookups.LOOKUP_VALUE', 'LookupValue', 'COLUMN';
IF COL_LENGTH('CIMS.PRLookups', 'USE_COUNT') IS NOT NULL AND COL_LENGTH('CIMS.PRLookups', 'UseCount') IS NULL EXEC sp_rename 'CIMS.PRLookups.USE_COUNT', 'UseCount', 'COLUMN';
IF COL_LENGTH('CIMS.PRLookups', 'LAST_USED') IS NOT NULL AND COL_LENGTH('CIMS.PRLookups', 'LastUsed') IS NULL EXEC sp_rename 'CIMS.PRLookups.LAST_USED', 'LastUsed', 'COLUMN';
-- CIMS.Stocks
IF COL_LENGTH('CIMS.Stocks', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'StockID') IS NULL EXEC sp_rename 'CIMS.Stocks.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'STK_CODE') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'StockCode') IS NULL EXEC sp_rename 'CIMS.Stocks.STK_CODE', 'StockCode', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'STK_NAME') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'StockName') IS NULL EXEC sp_rename 'CIMS.Stocks.STK_NAME', 'StockName', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'STK_UNIT') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'Unit') IS NULL EXEC sp_rename 'CIMS.Stocks.STK_UNIT', 'Unit', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'IS_MAIN') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'IsMain') IS NULL EXEC sp_rename 'CIMS.Stocks.IS_MAIN', 'IsMain', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'USE_MAXMIN') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'UseMaxMin') IS NULL EXEC sp_rename 'CIMS.Stocks.USE_MAXMIN', 'UseMaxMin', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_IMAGE') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColImage') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_IMAGE', 'ShowColImage', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_CODE') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColCode') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_CODE', 'ShowColCode', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_NAME') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColName') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_NAME', 'ShowColName', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_QTY') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColQuantity') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_QTY', 'ShowColQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_REMARK') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColRemark') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_REMARK', 'ShowColRemark', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'IN_PICKLIST') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'InPickList') IS NULL EXEC sp_rename 'CIMS.Stocks.IN_PICKLIST', 'InPickList', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'IN_SUPPLIER') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'InSupplier') IS NULL EXEC sp_rename 'CIMS.Stocks.IN_SUPPLIER', 'InSupplier', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'IN_SYSQR') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'InSystemQR') IS NULL EXEC sp_rename 'CIMS.Stocks.IN_SYSQR', 'InSystemQR', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'IN_EXCEL') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'InExcel') IS NULL EXEC sp_rename 'CIMS.Stocks.IN_EXCEL', 'InExcel', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'OUT_PICKLIST') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'OutPickList') IS NULL EXEC sp_rename 'CIMS.Stocks.OUT_PICKLIST', 'OutPickList', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'OUT_SUPPLIER') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'OutSupplier') IS NULL EXEC sp_rename 'CIMS.Stocks.OUT_SUPPLIER', 'OutSupplier', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'OUT_SYSQR') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'OutSystemQR') IS NULL EXEC sp_rename 'CIMS.Stocks.OUT_SYSQR', 'OutSystemQR', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'SORT_NO') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'SortNo') IS NULL EXEC sp_rename 'CIMS.Stocks.SORT_NO', 'SortNo', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'CREATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'CreatedBy') IS NULL EXEC sp_rename 'CIMS.Stocks.CREATED_BY', 'CreatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'CREATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'CreatedDate') IS NULL EXEC sp_rename 'CIMS.Stocks.CREATED_DATE', 'CreatedDate', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'UPDATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'UpdatedBy') IS NULL EXEC sp_rename 'CIMS.Stocks.UPDATED_BY', 'UpdatedBy', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'UPDATED_DATE') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'UpdatedDate') IS NULL EXEC sp_rename 'CIMS.Stocks.UPDATED_DATE', 'UpdatedDate', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'GROUP_BY') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'GroupBy') IS NULL EXEC sp_rename 'CIMS.Stocks.GROUP_BY', 'GroupBy', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'MAXMIN_BASIS') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'MaxMinBasis') IS NULL EXEC sp_rename 'CIMS.Stocks.MAXMIN_BASIS', 'MaxMinBasis', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_CUSTOMER') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColCustomer') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_CUSTOMER', 'ShowColCustomer', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_PARTA') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColPartA') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_PARTA', 'ShowColPartA', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_PARTNO') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColPartNumber') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_PARTNO', 'ShowColPartNumber', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_STK_BOX') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColStockBox') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_STK_BOX', 'ShowColStockBox', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_STK_PCS') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColStockPcs') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_STK_PCS', 'ShowColStockPcs', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'PR_AUTO') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'AutoPR') IS NULL EXEC sp_rename 'CIMS.Stocks.PR_AUTO', 'AutoPR', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_NO') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColNo') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_NO', 'ShowColNo', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'COL_MODEL') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColModel') IS NULL EXEC sp_rename 'CIMS.Stocks.COL_MODEL', 'ShowColModel', 'COLUMN';
IF COL_LENGTH('CIMS.Stocks', 'OUT_EXCEL') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'OutExcel') IS NULL EXEC sp_rename 'CIMS.Stocks.OUT_EXCEL', 'OutExcel', 'COLUMN';
-- CIMS.StockDeleteLogs
IF COL_LENGTH('CIMS.StockDeleteLogs', 'DEL_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'DeleteID') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.DEL_ID', 'DeleteID', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'StockID') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'STK_CODE') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'StockCode') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.STK_CODE', 'StockCode', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'STK_NAME') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'StockName') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.STK_NAME', 'StockName', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'ITEM_COUNT') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'ItemCount') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.ITEM_COUNT', 'ItemCount', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'TOTAL_QTY') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'TotalQuantity') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.TOTAL_QTY', 'TotalQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'REASON') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'Reason') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.REASON', 'Reason', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'UserID') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.StockDeleteLogs', 'DEL_DATE') IS NOT NULL AND COL_LENGTH('CIMS.StockDeleteLogs', 'DeletedDate') IS NULL EXEC sp_rename 'CIMS.StockDeleteLogs.DEL_DATE', 'DeletedDate', 'COLUMN';
-- CIMS.StockBarcodeFormats
IF COL_LENGTH('CIMS.StockBarcodeFormats', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockBarcodeFormats', 'StockID') IS NULL EXEC sp_rename 'CIMS.StockBarcodeFormats.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.StockBarcodeFormats', 'FMT_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockBarcodeFormats', 'FormatID') IS NULL EXEC sp_rename 'CIMS.StockBarcodeFormats.FMT_ID', 'FormatID', 'COLUMN';
-- CIMS.Users
IF COL_LENGTH('CIMS.Users', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'UserID') IS NULL EXEC sp_rename 'CIMS.Users.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'USR_PWD') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'Password') IS NULL EXEC sp_rename 'CIMS.Users.USR_PWD', 'Password', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'USR_NAME') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'FullName') IS NULL EXEC sp_rename 'CIMS.Users.USR_NAME', 'FullName', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'USR_POS') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'Position') IS NULL EXEC sp_rename 'CIMS.Users.USR_POS', 'Position', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'USR_DEPT') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'Department') IS NULL EXEC sp_rename 'CIMS.Users.USR_DEPT', 'Department', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'USR_LVL') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'UserLevel') IS NULL EXEC sp_rename 'CIMS.Users.USR_LVL', 'UserLevel', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'IS_ONLINE') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'IsOnline') IS NULL EXEC sp_rename 'CIMS.Users.IS_ONLINE', 'IsOnline', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'IS_LOCKED') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'IsLocked') IS NULL EXEC sp_rename 'CIMS.Users.IS_LOCKED', 'IsLocked', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'LAST_SESSION') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'LastSession') IS NULL EXEC sp_rename 'CIMS.Users.LAST_SESSION', 'LastSession', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'LAST_LOGIN') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'LastLogin') IS NULL EXEC sp_rename 'CIMS.Users.LAST_LOGIN', 'LastLogin', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'IS_MASTER_ADMIN') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'IsMasterAdmin') IS NULL EXEC sp_rename 'CIMS.Users.IS_MASTER_ADMIN', 'IsMasterAdmin', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'USR_SECTION') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'Section') IS NULL EXEC sp_rename 'CIMS.Users.USR_SECTION', 'Section', 'COLUMN';
IF COL_LENGTH('CIMS.Users', 'USR_DIVISION') IS NOT NULL AND COL_LENGTH('CIMS.Users', 'Division') IS NULL EXEC sp_rename 'CIMS.Users.USR_DIVISION', 'Division', 'COLUMN';
-- CIMS.PickListPrintLogs
IF COL_LENGTH('CIMS.PickListPrintLogs', 'Qty') IS NOT NULL AND COL_LENGTH('CIMS.PickListPrintLogs', 'Quantity') IS NULL EXEC sp_rename 'CIMS.PickListPrintLogs.Qty', 'Quantity', 'COLUMN';
-- CIMS.SystemLogs
IF COL_LENGTH('CIMS.SystemLogs', 'LOG_ID') IS NOT NULL AND COL_LENGTH('CIMS.SystemLogs', 'LogID') IS NULL EXEC sp_rename 'CIMS.SystemLogs.LOG_ID', 'LogID', 'COLUMN';
IF COL_LENGTH('CIMS.SystemLogs', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.SystemLogs', 'UserID') IS NULL EXEC sp_rename 'CIMS.SystemLogs.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.SystemLogs', 'ACT_TYPE') IS NOT NULL AND COL_LENGTH('CIMS.SystemLogs', 'ActionType') IS NULL EXEC sp_rename 'CIMS.SystemLogs.ACT_TYPE', 'ActionType', 'COLUMN';
IF COL_LENGTH('CIMS.SystemLogs', 'LOG_DESC') IS NOT NULL AND COL_LENGTH('CIMS.SystemLogs', 'Description') IS NULL EXEC sp_rename 'CIMS.SystemLogs.LOG_DESC', 'Description', 'COLUMN';
IF COL_LENGTH('CIMS.SystemLogs', 'LOG_REF') IS NOT NULL AND COL_LENGTH('CIMS.SystemLogs', 'Reference') IS NULL EXEC sp_rename 'CIMS.SystemLogs.LOG_REF', 'Reference', 'COLUMN';
IF COL_LENGTH('CIMS.SystemLogs', 'LOG_DATE') IS NOT NULL AND COL_LENGTH('CIMS.SystemLogs', 'LogDate') IS NULL EXEC sp_rename 'CIMS.SystemLogs.LOG_DATE', 'LogDate', 'COLUMN';
IF COL_LENGTH('CIMS.SystemLogs', 'LOG_PC') IS NOT NULL AND COL_LENGTH('CIMS.SystemLogs', 'ComputerName') IS NULL EXEC sp_rename 'CIMS.SystemLogs.LOG_PC', 'ComputerName', 'COLUMN';
-- CIMS.ForecastOrderImports
IF COL_LENGTH('CIMS.ForecastOrderImports', 'IMPORT_ID') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'ImportID') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.IMPORT_ID', 'ImportID', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'CUST_CODE') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'CustomerCode') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.CUST_CODE', 'CustomerCode', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'PT_ACODE') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'PartACode') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.PT_ACODE', 'PartACode', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'FORECAST_QTY') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'ForecastQuantity') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.FORECAST_QTY', 'ForecastQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'ORDER_QTY') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'OrderQuantity') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.ORDER_QTY', 'OrderQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'WORK_DAYS') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'WorkDays') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.WORK_DAYS', 'WorkDays', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'TARGET_DATE') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'TargetDate') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.TARGET_DATE', 'TargetDate', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'GUID_RUN') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'RunGuid') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.GUID_RUN', 'RunGuid', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'CREATED_AT') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'CreatedAt') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.CREATED_AT', 'CreatedAt', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'DELIVERY_QTY') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'DeliveryQuantity') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.DELIVERY_QTY', 'DeliveryQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.ForecastOrderImports', 'CREATED_BY') IS NOT NULL AND COL_LENGTH('CIMS.ForecastOrderImports', 'CreatedBy') IS NULL EXEC sp_rename 'CIMS.ForecastOrderImports.CREATED_BY', 'CreatedBy', 'COLUMN';
-- CIMS.PRDetails
IF COL_LENGTH('CIMS.PRDetails', 'PR_D_ID') IS NOT NULL AND COL_LENGTH('CIMS.PRDetails', 'PRDetailID') IS NULL EXEC sp_rename 'CIMS.PRDetails.PR_D_ID', 'PRDetailID', 'COLUMN';
IF COL_LENGTH('CIMS.PRDetails', 'PR_NO') IS NOT NULL AND COL_LENGTH('CIMS.PRDetails', 'PRNumber') IS NULL EXEC sp_rename 'CIMS.PRDetails.PR_NO', 'PRNumber', 'COLUMN';
IF COL_LENGTH('CIMS.PRDetails', 'PT_DESC') IS NOT NULL AND COL_LENGTH('CIMS.PRDetails', 'Description') IS NULL EXEC sp_rename 'CIMS.PRDetails.PT_DESC', 'Description', 'COLUMN';
IF COL_LENGTH('CIMS.PRDetails', 'QTY_REQ') IS NOT NULL AND COL_LENGTH('CIMS.PRDetails', 'RequestQuantity') IS NULL EXEC sp_rename 'CIMS.PRDetails.QTY_REQ', 'RequestQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.PRDetails', 'QTY_UNIT') IS NOT NULL AND COL_LENGTH('CIMS.PRDetails', 'Unit') IS NULL EXEC sp_rename 'CIMS.PRDetails.QTY_UNIT', 'Unit', 'COLUMN';
-- CIMS.PRHeaders
IF COL_LENGTH('CIMS.PRHeaders', 'PR_NO') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'PRNumber') IS NULL EXEC sp_rename 'CIMS.PRHeaders.PR_NO', 'PRNumber', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'PR_DATE') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'PRDate') IS NULL EXEC sp_rename 'CIMS.PRHeaders.PR_DATE', 'PRDate', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'UserID') IS NULL EXEC sp_rename 'CIMS.PRHeaders.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'REQ_DEPT') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'RequestDepartment') IS NULL EXEC sp_rename 'CIMS.PRHeaders.REQ_DEPT', 'RequestDepartment', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'TRG_DEPT') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'TargetDepartment') IS NULL EXEC sp_rename 'CIMS.PRHeaders.TRG_DEPT', 'TargetDepartment', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'PR_REM') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'Remark') IS NULL EXEC sp_rename 'CIMS.PRHeaders.PR_REM', 'Remark', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'PR_STAT') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'Status') IS NULL EXEC sp_rename 'CIMS.PRHeaders.PR_STAT', 'Status', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'APP_USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'ApprovedBy') IS NULL EXEC sp_rename 'CIMS.PRHeaders.APP_USR_ID', 'ApprovedBy', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'APP_DATE') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'ApprovedDate') IS NULL EXEC sp_rename 'CIMS.PRHeaders.APP_DATE', 'ApprovedDate', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'IS_EXPORT') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'IsExported') IS NULL EXEC sp_rename 'CIMS.PRHeaders.IS_EXPORT', 'IsExported', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'EXPORT_REMARK') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'ExportRemark') IS NULL EXEC sp_rename 'CIMS.PRHeaders.EXPORT_REMARK', 'ExportRemark', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'EXPORT_DATE') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'ExportDate') IS NULL EXEC sp_rename 'CIMS.PRHeaders.EXPORT_DATE', 'ExportDate', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'CREATED_AT') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'CreatedAt') IS NULL EXEC sp_rename 'CIMS.PRHeaders.CREATED_AT', 'CreatedAt', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'REQ_NAME') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'RequesterName') IS NULL EXEC sp_rename 'CIMS.PRHeaders.REQ_NAME', 'RequesterName', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'PR_SOURCE') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'Source') IS NULL EXEC sp_rename 'CIMS.PRHeaders.PR_SOURCE', 'Source', 'COLUMN';
IF COL_LENGTH('CIMS.PRHeaders', 'SCAN_TX_ID') IS NOT NULL AND COL_LENGTH('CIMS.PRHeaders', 'ScanTransactionID') IS NULL EXEC sp_rename 'CIMS.PRHeaders.SCAN_TX_ID', 'ScanTransactionID', 'COLUMN';
-- CIMS.ScanTransactions
IF COL_LENGTH('CIMS.ScanTransactions', 'TX_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'TransactionID') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.TX_ID', 'TransactionID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'UserID') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'PT_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'PartID') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.PT_ID', 'PartID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'PT_ACODE') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'PartACode') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.PT_ACODE', 'PartACode', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'REF_NO') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'ReferenceNo') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.REF_NO', 'ReferenceNo', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'TX_QTY') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'Quantity') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.TX_QTY', 'Quantity', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'TX_TYPE') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'TransactionType') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.TX_TYPE', 'TransactionType', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'TX_BAL') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'BalanceAfter') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.TX_BAL', 'BalanceAfter', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'TX_DATE') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'TransactionDate') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.TX_DATE', 'TransactionDate', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'StockID') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'TX_BOX') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'BoxChange') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.TX_BOX', 'BoxChange', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'IS_CANCEL') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'IsCancelled') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.IS_CANCEL', 'IsCancelled', 'COLUMN';
IF COL_LENGTH('CIMS.ScanTransactions', 'ADJ_NOTE') IS NOT NULL AND COL_LENGTH('CIMS.ScanTransactions', 'AdjustNote') IS NULL EXEC sp_rename 'CIMS.ScanTransactions.ADJ_NOTE', 'AdjustNote', 'COLUMN';
-- CIMS.ScanAdjustments
IF COL_LENGTH('CIMS.ScanAdjustments', 'ADJ_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'AdjustID') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.ADJ_ID', 'AdjustID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'TX_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'TransactionID') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.TX_ID', 'TransactionID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'ADJ_ACTION') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'Action') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.ADJ_ACTION', 'Action', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'TX_TYPE') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'TransactionType') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.TX_TYPE', 'TransactionType', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'StockID') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'PT_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'PartID') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.PT_ID', 'PartID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'OLD_QTY') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'OldQuantity') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.OLD_QTY', 'OldQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'NEW_QTY') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'NewQuantity') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.NEW_QTY', 'NewQuantity', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'STOCK_DELTA') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'StockChange') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.STOCK_DELTA', 'StockChange', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'BAL_AFTER') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'BalanceAfter') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.BAL_AFTER', 'BalanceAfter', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'REASON') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'Reason') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.REASON', 'Reason', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'UserID') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.ScanAdjustments', 'ADJ_DATE') IS NOT NULL AND COL_LENGTH('CIMS.ScanAdjustments', 'AdjustDate') IS NULL EXEC sp_rename 'CIMS.ScanAdjustments.ADJ_DATE', 'AdjustDate', 'COLUMN';
-- CIMS.StockImports
IF COL_LENGTH('CIMS.StockImports', 'IMP_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'ImportID') IS NULL EXEC sp_rename 'CIMS.StockImports.IMP_ID', 'ImportID', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'BATCH_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'BatchID') IS NULL EXEC sp_rename 'CIMS.StockImports.BATCH_ID', 'BatchID', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'StockID') IS NULL EXEC sp_rename 'CIMS.StockImports.STK_ID', 'StockID', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'PT_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'PartID') IS NULL EXEC sp_rename 'CIMS.StockImports.PT_ID', 'PartID', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'PT_CODE') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'PartCode') IS NULL EXEC sp_rename 'CIMS.StockImports.PT_CODE', 'PartCode', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'QTY') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'Quantity') IS NULL EXEC sp_rename 'CIMS.StockImports.QTY', 'Quantity', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'BAL_AFTER') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'BalanceAfter') IS NULL EXEC sp_rename 'CIMS.StockImports.BAL_AFTER', 'BalanceAfter', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'FILE_NAME') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'FileName') IS NULL EXEC sp_rename 'CIMS.StockImports.FILE_NAME', 'FileName', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'UserID') IS NULL EXEC sp_rename 'CIMS.StockImports.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.StockImports', 'IMP_DATE') IS NOT NULL AND COL_LENGTH('CIMS.StockImports', 'ImportDate') IS NULL EXEC sp_rename 'CIMS.StockImports.IMP_DATE', 'ImportDate', 'COLUMN';
-- CIMS.StockTransfers
IF COL_LENGTH('CIMS.StockTransfers', 'TRF_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'TransferID') IS NULL EXEC sp_rename 'CIMS.StockTransfers.TRF_ID', 'TransferID', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'FROM_STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'FromStockID') IS NULL EXEC sp_rename 'CIMS.StockTransfers.FROM_STK_ID', 'FromStockID', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'FROM_STK_CODE') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'FromStockCode') IS NULL EXEC sp_rename 'CIMS.StockTransfers.FROM_STK_CODE', 'FromStockCode', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'TO_STK_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'ToStockID') IS NULL EXEC sp_rename 'CIMS.StockTransfers.TO_STK_ID', 'ToStockID', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'TO_STK_CODE') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'ToStockCode') IS NULL EXEC sp_rename 'CIMS.StockTransfers.TO_STK_CODE', 'ToStockCode', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'PT_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'PartID') IS NULL EXEC sp_rename 'CIMS.StockTransfers.PT_ID', 'PartID', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'PT_CODE') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'PartCode') IS NULL EXEC sp_rename 'CIMS.StockTransfers.PT_CODE', 'PartCode', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'QTY') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'Quantity') IS NULL EXEC sp_rename 'CIMS.StockTransfers.QTY', 'Quantity', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'TRF_MODE') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'TransferMode') IS NULL EXEC sp_rename 'CIMS.StockTransfers.TRF_MODE', 'TransferMode', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'FROM_BAL_AFTER') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'FromBalanceAfter') IS NULL EXEC sp_rename 'CIMS.StockTransfers.FROM_BAL_AFTER', 'FromBalanceAfter', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'TO_BAL_AFTER') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'ToBalanceAfter') IS NULL EXEC sp_rename 'CIMS.StockTransfers.TO_BAL_AFTER', 'ToBalanceAfter', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'USR_ID') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'UserID') IS NULL EXEC sp_rename 'CIMS.StockTransfers.USR_ID', 'UserID', 'COLUMN';
IF COL_LENGTH('CIMS.StockTransfers', 'TRF_DATE') IS NOT NULL AND COL_LENGTH('CIMS.StockTransfers', 'TransferDate') IS NULL EXEC sp_rename 'CIMS.StockTransfers.TRF_DATE', 'TransferDate', 'COLUMN';
GO

-- 5) computed column back with its new name
IF COL_LENGTH('CIMS.Users', 'StatusText') IS NULL
    ALTER TABLE CIMS.Users ADD StatusText AS (CASE WHEN IsOnline = 1 THEN 'Online' ELSE 'Offline' END);
IF OBJECT_ID('CIMS.CK_PartStocks_Quantity', 'C') IS NULL
    ALTER TABLE CIMS.PartStocks ADD CONSTRAINT CK_PartStocks_Quantity CHECK (Quantity >= 0);
GO

-- 6) permission codes: SCANNER -> MultiScanner, STORE -> StoreMaxMin, PDControl -> ProductControl, USERMGMT -> UserManagement
UPDATE CIMS.Permissions SET SystemID = 'MultiScanner'   WHERE SystemID = 'SCANNER';
UPDATE CIMS.Permissions SET SystemID = 'StoreMaxMin'    WHERE SystemID = 'STORE';
UPDATE CIMS.Permissions SET SystemID = 'ProductControl' WHERE SystemID = 'PDControl';
UPDATE CIMS.Permissions SET SystemID = 'UserManagement' WHERE SystemID = 'USERMGMT';
GO

-- 7) views / procedures / function / triggers with the new names ----------------------------------------
-- rounding used by CIMS.sp_MaxMin_Calculate (UP / NEAREST / DOWN)
CREATE OR ALTER FUNCTION CIMS.fn_MaxMin_Round (@v DECIMAL(18,6), @mode VARCHAR(10))
RETURNS DECIMAL(18,0)
AS
BEGIN
    RETURN CASE @mode WHEN 'DOWN' THEN FLOOR(@v) WHEN 'NEAREST' THEN ROUND(@v, 0) ELSE CEILING(@v) END;
END
GO

CREATE OR ALTER VIEW CIMS.vw_StoreMonitoring AS
SELECT
    PartID,
    Category AS Category,        -- เนเธเนเธเธฑเธ”เธเธฅเธธเนเธกเนเธ—เธ CustomerCode เน€เธ”เธดเธก
    Supplier AS Supplier,
    ImageFileName AS ImageFileName,
    PartCode AS PartCode,
    Description AS PartName,
    PackSize AS PackSize,
    MaxQuantity AS [Max],
    MinQuantity AS [Min],
    StockQuantity AS QtyStkb,       -- เธ•เธญเธเธเธตเนเน€เธเนเธเธเนเธฒเธเนเธณเธซเธเธฑเธ (เธเธ.) เธ•เธฑเธงเน€เธ”เธตเธขเธง เนเธกเนเธกเธต QTY_STK เนเธขเธเธญเธตเธเธ•เนเธญเนเธ
    Remark AS Remark,
    Status AS Priority,
    Bin AS Bin,
    CASE
        WHEN ISNULL(MaxQuantity, 0) = 0 AND ISNULL(MinQuantity, 0) = 0 THEN 'NO_CONFIG'
        WHEN ISNULL(StockQuantity, 0) < ISNULL(MinQuantity, 0) THEN 'UNDER_MIN'
        WHEN ISNULL(StockQuantity, 0) > ISNULL(MaxQuantity, 0) AND ISNULL(MaxQuantity, 0) > 0 THEN 'OVER_MAX'
        WHEN ISNULL(StockQuantity, 0) >= ISNULL(MinQuantity, 0) AND ISNULL(StockQuantity, 0) <= ISNULL(MaxQuantity, 0) THEN 'NORMAL_GOOD'
        ELSE 'NORMAL'
    END AS StockStatus
FROM CIMS.Parts
WHERE IsActive = 1
  AND IsShowInMaster = 1;
GO

-- -----------------------------------------------------------------------------------------------------
-- 7) CIMS.vw_StockMonitoring - CIMS.vw_StoreMonitoring columns + StkId + Customer/PartA/PartNo/StockBox/StockPcs/GroupKey
--    (CIMS.vw_StoreMonitoring itself is left untouched for the Dashboard)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER VIEW CIMS.vw_StockMonitoring AS
WITH base AS (
    -- main stock: balance / max / min / remark live in CIMS.Parts
    SELECT s.StockID, s.UseMaxMin, s.MaxMinBasis, s.GroupBy, p.PartID,
           p.MaxQuantity AS QMAX, p.MinQuantity AS QMIN, ISNULL(p.StockQuantity, 0) AS Quantity, p.Remark AS RMK, CAST(NULL AS INT) AS BOXQ
    FROM CIMS.Stocks s
    JOIN CIMS.Parts p ON s.IsMain = 1
    WHERE p.IsActive = 1 AND p.IsShowInMaster = 1

    UNION ALL

    -- other stocks: balance per part in CIMS.PartStocks
    SELECT s.StockID, s.UseMaxMin, s.MaxMinBasis, s.GroupBy, p.PartID,
           ps.MaxQuantity, ps.MinQuantity, ps.Quantity, ps.Remark, ps.BoxQuantity
    FROM CIMS.PartStocks ps
    JOIN CIMS.Stocks s ON s.StockID = ps.StockID AND s.IsMain = 0
    JOIN CIMS.Parts p  ON p.PartID  = ps.PartID
    WHERE p.IsActive = 1 AND ps.IsShow = 1
),
calc AS (
    SELECT b.*,
           -- STOCK (BOX): other stocks keep their own box count (scan = +1 / -1 box), main stock = balance / pack size
           CASE WHEN b.BOXQ IS NOT NULL THEN b.BOXQ WHEN ISNULL(p.PackSize, 0) > 0 THEN b.Quantity / p.PackSize ELSE 0 END AS BoxQuantity
    FROM base b JOIN CIMS.Parts p ON p.PartID = b.PartID
)
SELECT
    c.StockID        AS StkId,
    p.PartID,
    p.Category        AS Category,
    p.Supplier   AS Supplier,
    p.ImageFileName        AS ImageFileName,
    p.PartCode       AS PartCode,
    p.Description       AS PartName,
    p.PackSize        AS PackSize,
    c.QMAX          AS [Max],
    c.QMIN          AS [Min],
    c.Quantity           AS QtyStkb,
    c.RMK           AS Remark,
    p.Status      AS Priority,
    p.Bin        AS Bin,
    p.Customer       AS Customer,
    p.PartA      AS PartA,
    p.PartNumber     AS PartNo,
    p.Model      AS Model,
    c.BoxQuantity       AS StockBox,
    c.Quantity           AS StockPcs,
    -- group header of the Store table: Category (default) or Customer
    CASE WHEN c.GroupBy = 'CUSTOMER' THEN ISNULL(NULLIF(p.Customer, ''), '-') ELSE ISNULL(NULLIF(p.Category, ''), '-') END AS GroupKey,
    CASE
        WHEN c.UseMaxMin = 0 THEN 'NORMAL'      -- stock without Max/Min: plain rows (not the grey NO_CONFIG look)
        WHEN ISNULL(c.QMAX, 0) = 0 AND ISNULL(c.QMIN, 0) = 0 THEN 'NO_CONFIG'
        -- MAX / MIN compared with Quantity (stock unit) or with STOCK (BOX)
        WHEN (CASE WHEN c.MaxMinBasis = 'BOX' THEN c.BoxQuantity ELSE c.Quantity END) < ISNULL(c.QMIN, 0) THEN 'UNDER_MIN'
        WHEN (CASE WHEN c.MaxMinBasis = 'BOX' THEN c.BoxQuantity ELSE c.Quantity END) > ISNULL(c.QMAX, 0) AND ISNULL(c.QMAX, 0) > 0 THEN 'OVER_MAX'
        WHEN (CASE WHEN c.MaxMinBasis = 'BOX' THEN c.BoxQuantity ELSE c.Quantity END) BETWEEN ISNULL(c.QMIN, 0) AND ISNULL(c.QMAX, 0) THEN 'NORMAL_GOOD'
        ELSE 'NORMAL'
    END AS StockStatus
FROM calc c
JOIN CIMS.Parts p ON p.PartID = c.PartID;
GO

-- Card summary: number of items (not quantity) per stock
CREATE OR ALTER VIEW CIMS.vw_StockSummary AS
SELECT
    s.StockID, s.StockCode, s.StockName, s.Unit, s.IsMain, s.SortNo,
    ISNULL(c.ItemCount, 0) AS ItemCount
FROM CIMS.Stocks s
LEFT JOIN (SELECT StkId, COUNT(*) AS ItemCount FROM CIMS.vw_StockMonitoring GROUP BY StkId) c
       ON c.StkId = s.StockID;
GO

-- bar: Forecast / Order / Delivery per month of the current year
CREATE OR ALTER VIEW CIMS.vw_DashboardBarChart AS
SELECT LEFT(DATENAME(MONTH, DATEFROMPARTS(YEAR(GETDATE()), MONTH(TargetDate), 1)), 3) AS MonthName,
       MONTH(TargetDate) AS MonthOrder,
       SUM(ISNULL(ForecastQuantity, 0)) AS ForecastValue,
       SUM(ISNULL(OrderQuantity, 0))    AS OrderValue,
       SUM(ISNULL(DeliveryQuantity, 0)) AS DeliveryValue
FROM CIMS.ForecastOrderImports
WHERE YEAR(TargetDate) = YEAR(GETDATE())
GROUP BY MONTH(TargetDate);
GO

-- 6) Dashboard charts -----------------------------------------------------------------------------------
-- pie: orders per customer of the current month; on the 1st day of a month that has not been imported yet,
--      the previous month is still shown
CREATE OR ALTER VIEW CIMS.vw_DashboardPieChart AS
WITH mon AS (
    SELECT CASE
             WHEN EXISTS (SELECT 1 FROM CIMS.ForecastOrderImports WHERE TargetDate >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
                                                               AND TargetDate <  DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)))
                  OR DAY(GETDATE()) > 1
             THEN DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
             ELSE DATEADD(MONTH, -1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
           END AS M
),
rows_ AS (
    SELECT s.CustomerCode, ISNULL(s.OrderQuantity, 0) AS OrderQuantity
    FROM CIMS.ForecastOrderImports s CROSS JOIN mon
    WHERE s.TargetDate >= mon.M AND s.TargetDate < DATEADD(MONTH, 1, mon.M)
)
SELECT CustomerCode AS CustomerName,
       SUM(OrderQuantity) AS OrderAmount,
       SUM(OrderQuantity) * 100.0 / NULLIF((SELECT SUM(OrderQuantity) FROM rows_), 0) AS Percentage
FROM rows_
GROUP BY CustomerCode
HAVING SUM(OrderQuantity) > 0;
GO

-- -----------------------------------------------------------------------------------------------------
-- 10) Inventory Registration list: + Customer / Part A / Part No (also searchable)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER VIEW CIMS.vw_PartQRReady AS
SELECT
    PartID,
    Category,
    Supplier,
    PartCode,
    Description,
    PackSize,
    ISNULL(QRCode, '-') AS PT_QR_DISPLAY,
    'CAT:'   + ISNULL(Category, '-') +
    ' | CD:' + ISNULL(PartCode, '-') +
    ' | NM:' + ISNULL(Description, '-') +
    ' | Q:'  + CAST(ISNULL(PackSize, 0) AS VARCHAR) +
    ' | BC:' + ISNULL(QRCode, '-') AS PT_QR_FULL,
    StockQuantity,
    MaxQuantity,
    MinQuantity,
    Bin,
    IsActive,
    IsShowInMaster,
    ImageFileName,
    Customer,
    PartA,
    PartNumber,
    Model
FROM CIMS.Parts
WHERE IsActive = 1;
GO

-- 5) PR list view --------------------------------------------------------------------------------------
CREATE OR ALTER VIEW CIMS.vw_PRReady AS
SELECT
    h.PRNumber,
    h.PRDate,
    h.UserID,
    COALESCE(NULLIF(LTRIM(RTRIM(h.RequesterName)), N''), u.FullName, N'N/A') AS REQUESTER,
    ISNULL(p.PartCode, 'N/A')     AS PartCode,
    d.Description,
    d.RequestQuantity,
    d.Unit,
    h.RequestDepartment,
    h.TargetDepartment,
    h.Status,
    h.Remark,
    h.ApprovedBy,
    h.ApprovedDate,
    ISNULL(h.IsExported, 'N')     AS IsExported,
    h.ExportRemark,
    h.ExportDate,
    ISNULL(h.Source, 'MANUAL') AS Source,
    h.ScanTransactionID
FROM CIMS.PRHeaders h
JOIN CIMS.PRDetails d      ON d.PRNumber = h.PRNumber
LEFT JOIN CIMS.Users u ON u.UserID = h.UserID
-- PR stores the product by name (Description); pick one matching code for display
OUTER APPLY (SELECT TOP 1 mp.PartCode FROM CIMS.Parts mp WHERE mp.Description = d.Description ORDER BY mp.IsActive DESC, mp.PartID) p;
GO

-- -----------------------------------------------------------------------------------------------------
-- 11) Scan lookup: also by PART A / PART NO (like StorePC, so supplier labels that carry the Part A work)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE CIMS.sp_GetPartByScan
    @BarcodeInput NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CleanInput NVARCHAR(200) = LTRIM(RTRIM(@BarcodeInput));

    SELECT TOP 1 PartID, PartCode, Description, StockQuantity, PackSize, Category, IsActive, ImageFileName
    FROM CIMS.Parts WITH (NOLOCK)
    WHERE IsActive = 1
      AND (   LTRIM(RTRIM(PartCode)) = @CleanInput
           OR LTRIM(RTRIM(QRCode)) = @CleanInput
           OR LTRIM(RTRIM(ISNULL(PartA, ''))) = @CleanInput
           OR LTRIM(RTRIM(ISNULL(PartNumber, ''))) = @CleanInput
           OR LTRIM(RTRIM(Description)) = @CleanInput)
    -- PD CODE first, then QR CODE, PART A, PART NO, name
    ORDER BY CASE WHEN LTRIM(RTRIM(PartCode)) = @CleanInput THEN 0
                  WHEN LTRIM(RTRIM(QRCode)) = @CleanInput THEN 1
                  WHEN LTRIM(RTRIM(ISNULL(PartA, ''))) = @CleanInput THEN 2
                  WHEN LTRIM(RTRIM(ISNULL(PartNumber, ''))) = @CleanInput THEN 3 ELSE 4 END, PartID;
END
GO

-- @StkId: show only the parts in that stock (main = IsShowInMaster / other = row in CIMS.PartStocks), NULL = all
CREATE OR ALTER PROCEDURE CIMS.sp_GetPartForQR
    @SearchText NVARCHAR(100) = '',
    @StkId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @SearchText = ISNULL(TRIM(@SearchText), '');
    DECLARE @isMain BIT = ISNULL((SELECT IsMain FROM CIMS.Stocks WHERE StockID = @StkId), 0);

    -- IS_SHOW_VIEW: SHOW/HIDE of the filtered stock (other stock = CIMS.PartStocks.IsShow / main or all = IsShowInMaster)
    SELECT v.*,
           CASE WHEN @StkId IS NOT NULL AND @isMain = 0
                THEN ISNULL((SELECT ps.IsShow FROM CIMS.PartStocks ps WHERE ps.StockID = @StkId AND ps.PartID = v.PartID), 1)
                ELSE v.IsShowInMaster END AS IS_SHOW_VIEW
    FROM CIMS.vw_PartQRReady v
    WHERE
        (@StkId IS NULL
         OR (@isMain = 1 AND v.IsShowInMaster = 1)
         OR (@isMain = 0 AND EXISTS (SELECT 1 FROM CIMS.PartStocks ps WHERE ps.StockID = @StkId AND ps.PartID = v.PartID)))
    AND (
        @SearchText = ''
        OR (
            ISNULL(PartCode, '')        LIKE '%' + @SearchText + '%'
            OR ISNULL(Description, '')     LIKE '%' + @SearchText + '%'
            OR ISNULL(Supplier, '') LIKE '%' + @SearchText + '%'
            OR ISNULL(Bin, '')      LIKE '%' + @SearchText + '%'
            OR ISNULL(Category, '')      LIKE '%' + @SearchText + '%'
            OR ISNULL(Customer, '')     LIKE '%' + @SearchText + '%'
            OR ISNULL(PartA, '')    LIKE '%' + @SearchText + '%'
            OR ISNULL(PartNumber, '')   LIKE '%' + @SearchText + '%'
            OR ISNULL(Model, '')    LIKE '%' + @SearchText + '%'
        )
    )
    ORDER BY Category ASC, PartCode ASC;
END
GO

CREATE OR ALTER PROCEDURE CIMS.sp_GetPRList    
    @SearchText NVARCHAR(100) = ''    
AS    
BEGIN    
    SELECT * FROM CIMS.vw_PRReady    
    WHERE PRNumber LIKE '%' + @SearchText + '%'    
       OR Description LIKE '%' + @SearchText + '%'    
       OR REQUESTER LIKE '%' + @SearchText + '%'    
       OR RequestDepartment LIKE '%' + @SearchText + '%'    
       OR Status LIKE '%' + @SearchText + '%'    
    ORDER BY PRDate DESC, PRNumber DESC; -- เอาใบงานล่าสุดขึ้นก่อน    
END
GO

-- 5) Calculation -----------------------------------------------------------------------------------------
--    @StkId NULL = every stock with AutoCalc = 1 (after import) / a stock id = that stock (CALCULATE NOW)
--    @PtId  NULL = every part shown in the Store (Max-Min) of the stock / a part = only that part (after SAVE)
--    Formula of the stock (CIMS.MaxMinStockSettings) or the default formula when the stock has not chosen one.
--    Month used: the latest month <= this month that has an Order (or Forecast) for the customer + part.
--    Parts without an Order / Forecast are left unchanged (reported as skipped).
--    MAX = Round(Qty / Workdays [/ PackSize when the stock MAX / MIN is in BOX]) x MAX DAYS  (MIN the same)
CREATE OR ALTER PROCEDURE CIMS.sp_MaxMin_Calculate
    @StkId  INT = NULL,
    @PtId   INT = NULL,
    @UserId VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @thisMonth DATE = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @defFormula INT = (SELECT TOP 1 FormulaID FROM CIMS.MaxMinFormulas WHERE IsDefault = 1 ORDER BY FormulaID);

    SELECT s.StockID, s.IsMain, s.MaxMinBasis, f.QuantitySource, f.RoundMode, f.DefaultMaxDays, f.DefaultMinDays, f.DefaultWorkdays
    INTO #stk
    FROM CIMS.Stocks s
    LEFT JOIN CIMS.MaxMinStockSettings cs ON cs.StockID = s.StockID
    JOIN CIMS.MaxMinFormulas f ON f.FormulaID = ISNULL(cs.FormulaID, @defFormula)
    WHERE (@StkId IS NULL AND cs.AutoCalc = 1) OR s.StockID = @StkId;

    -- parts shown in the Store (Max-Min) of those stocks
    SELECT v.StkId AS StockID, v.PartID, LTRIM(RTRIM(ISNULL(p.Customer, ''))) AS CUST,
           LTRIM(RTRIM(ISNULL(p.PartA, ''))) AS PARTA, LTRIM(RTRIM(ISNULL(p.PartNumber, ''))) AS PARTNO, LTRIM(RTRIM(p.PartCode)) AS CODE,
           CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN p.PackSize ELSE 1 END AS PSZ,
           ISNULL(c.MaxDays, k.DefaultMaxDays) AS DMAX, ISNULL(c.MinDays, k.DefaultMinDays) AS DMIN,
           k.QuantitySource, k.RoundMode, k.DefaultWorkdays, k.IsMain,
           CASE WHEN k.MaxMinBasis = 'BOX' THEN 1 ELSE 0 END AS IN_BOX
    INTO #parts
    FROM CIMS.vw_StockMonitoring v
    JOIN #stk k ON k.StockID = v.StkId
    JOIN CIMS.Parts p ON p.PartID = v.PartID
    LEFT JOIN CIMS.MaxMinPartConfigs c ON c.StockID = v.StkId AND c.PartID = v.PartID
    WHERE @PtId IS NULL OR v.PartID = @PtId;

    -- monthly quantity per part (latest month <= this month)
    SELECT x.StockID, x.PartID, m.MON, m.Quantity, m.WD_IMPORT
    INTO #qty
    FROM #parts x
    CROSS APPLY (
        SELECT TOP 1 DATEFROMPARTS(YEAR(st.TargetDate), MONTH(st.TargetDate), 1) AS MON,
               CASE WHEN x.QuantitySource = 'FORECAST' THEN SUM(ISNULL(st.ForecastQuantity, 0)) ELSE SUM(ISNULL(st.OrderQuantity, 0)) END AS Quantity,
               MAX(st.WorkDays) AS WD_IMPORT
        FROM CIMS.ForecastOrderImports st
        WHERE LTRIM(RTRIM(ISNULL(st.CustomerCode, ''))) = x.CUST
          AND LTRIM(RTRIM(ISNULL(st.PartACode, ''))) <> ''
          AND LTRIM(RTRIM(st.PartACode)) IN (x.PARTA, x.PARTNO, x.CODE)
          AND st.TargetDate < DATEADD(MONTH, 1, @thisMonth)
        GROUP BY DATEFROMPARTS(YEAR(st.TargetDate), MONTH(st.TargetDate), 1)
        HAVING CASE WHEN x.QuantitySource = 'FORECAST' THEN SUM(ISNULL(st.ForecastQuantity, 0)) ELSE SUM(ISNULL(st.OrderQuantity, 0)) END > 0
        ORDER BY DATEFROMPARTS(YEAR(st.TargetDate), MONTH(st.TargetDate), 1) DESC
    ) m;

    -- workdays: customer calendar > WORK DAYS in the import > formula default
    SELECT x.StockID, x.PartID, x.IsMain, x.DMAX, x.DMIN,
           -- per day (/ pack size when the stock MAX / MIN is in BOX), rounded as set in the formula
           CIMS.fn_MaxMin_Round(q.Quantity / CAST(COALESCE(NULLIF(wd.CNT, 0), NULLIF(q.WD_IMPORT, 0), NULLIF(x.DefaultWorkdays, 0), 1) AS DECIMAL(18,6))
                               / CASE WHEN x.IN_BOX = 1 THEN x.PSZ ELSE 1 END, x.RoundMode) AS PER_DAY
    INTO #per
    FROM #parts x
    JOIN #qty q ON q.StockID = x.StockID AND q.PartID = x.PartID
    OUTER APPLY (SELECT COUNT(*) AS CNT FROM CIMS.CustomerWorkdays w
                 WHERE w.CustomerCode = x.CUST AND w.WorkDate >= q.MON AND w.WorkDate < DATEADD(MONTH, 1, q.MON)) wd;

    SELECT StockID, PartID, IsMain, CAST(PER_DAY * DMAX AS INT) AS NEW_MAX, CAST(PER_DAY * DMIN AS INT) AS NEW_MIN
    INTO #result FROM #per;

    BEGIN TRAN;
        UPDATE mp SET mp.MaxQuantity = r.NEW_MAX, mp.MinQuantity = r.NEW_MIN
        FROM CIMS.Parts mp JOIN #result r ON r.PartID = mp.PartID AND r.IsMain = 1;

        UPDATE ps SET ps.MaxQuantity = r.NEW_MAX, ps.MinQuantity = r.NEW_MIN, ps.UpdatedDate = GETDATE()
        FROM CIMS.PartStocks ps JOIN #result r ON r.StockID = ps.StockID AND r.PartID = ps.PartID AND r.IsMain = 0;

        IF @PtId IS NULL
        BEGIN
            UPDATE cs SET cs.LastCalc = GETDATE() FROM CIMS.MaxMinStockSettings cs JOIN #stk k ON k.StockID = cs.StockID;
            INSERT INTO CIMS.MaxMinStockSettings (StockID, FormulaID, AutoCalc, LastCalc, UpdatedBy, UpdatedDate)
            SELECT k.StockID, NULL, 0, GETDATE(), @UserId, GETDATE() FROM #stk k
            WHERE NOT EXISTS (SELECT 1 FROM CIMS.MaxMinStockSettings cs WHERE cs.StockID = k.StockID);
        END
    COMMIT;

    SELECT (SELECT COUNT(*) FROM #result) AS Updated,
           (SELECT COUNT(*) FROM #parts) - (SELECT COUNT(*) FROM #result) AS Skipped,
           (SELECT COUNT(*) FROM #stk) AS Stocks;
END
GO

-- 3) Save / bump a typed value ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE CIMS.sp_PR_SaveLookup
    @Field VARCHAR(20),
    @Value NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET @Value = LTRIM(RTRIM(ISNULL(@Value, N'')));
    IF @Value = N'' RETURN;

    UPDATE CIMS.PRLookups SET UseCount = UseCount + 1, LastUsed = GETDATE()
    WHERE FieldName = @Field AND LookupValue = @Value;

    IF @@ROWCOUNT = 0
        INSERT INTO CIMS.PRLookups (FieldName, LookupValue) VALUES (@Field, @Value);
END
GO

-- Add quantity to a non-main stock (creates the part row on first receive). Returns the new balance.
-- Used by Import Excel and by Multi-Scanner IN for non-main stocks. Runs inside the caller's transaction.
CREATE OR ALTER PROCEDURE CIMS.sp_Stock_AddQty
    @StkId  INT,
    @PtId   INT,
    @Qty    INT,
    @NewBal INT OUTPUT,
    @Box    INT = NULL      -- scan: +1 box per label (NULL = box count follows Quantity / PACK SIZE)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM CIMS.Stocks WHERE StockID = @StkId AND IsMain = 1)
    BEGIN
        UPDATE CIMS.Parts SET StockQuantity = ISNULL(StockQuantity, 0) + @Qty WHERE PartID = @PtId;
        SELECT @NewBal = ISNULL(StockQuantity, 0) FROM CIMS.Parts WHERE PartID = @PtId;
        RETURN;
    END

    IF @Box IS NULL
        UPDATE CIMS.PartStocks WITH (UPDLOCK, HOLDLOCK)
           SET Quantity = Quantity + @Qty, UpdatedDate = GETDATE()
         WHERE StockID = @StkId AND PartID = @PtId;
    ELSE
        UPDATE CIMS.PartStocks WITH (UPDLOCK, HOLDLOCK)
           SET Quantity = Quantity + @Qty, BoxQuantity = CASE WHEN BoxQuantity + @Box < 0 THEN 0 ELSE BoxQuantity + @Box END, UpdatedDate = GETDATE()
         WHERE StockID = @StkId AND PartID = @PtId;

    IF @@ROWCOUNT = 0
        INSERT INTO CIMS.PartStocks (StockID, PartID, Quantity, BoxQuantity) VALUES (@StkId, @PtId, @Qty, CASE WHEN ISNULL(@Box, 0) > 0 THEN @Box ELSE 0 END);

    SELECT @NewBal = Quantity FROM CIMS.PartStocks WHERE StockID = @StkId AND PartID = @PtId;
END
GO

-- Delete a stock and everything that belongs to it. Reason is mandatory and kept in CIMS.StockDeleteLogs.
-- Kept: CIMS.StockTransfers history (it also explains Stock-CHR increases) and CIMS.SystemLogs.
CREATE OR ALTER PROCEDURE CIMS.sp_Stock_Delete
    @StkId  INT,
    @Reason NVARCHAR(500),
    @UserId VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Code VARCHAR(30), @Name NVARCHAR(100), @IsMain BIT, @Items INT, @Total INT;
    SELECT @Code = StockCode, @Name = StockName, @IsMain = IsMain FROM CIMS.Stocks WHERE StockID = @StkId;

    IF @Code IS NULL THROW 50011, N'ไม่พบคลังที่ต้องการลบ', 1;
    IF @IsMain = 1   THROW 50012, N'ไม่สามารถลบคลังหลัก (Stock-CHR) ได้', 1;
    IF LEN(LTRIM(RTRIM(ISNULL(@Reason, N'')))) = 0 THROW 50013, N'กรุณาระบุเหตุผลในการลบคลัง', 1;

    SELECT @Items = COUNT(*), @Total = ISNULL(SUM(Quantity), 0) FROM CIMS.PartStocks WHERE StockID = @StkId;

    BEGIN TRAN;

    INSERT INTO CIMS.StockDeleteLogs (StockID, StockCode, StockName, ItemCount, TotalQuantity, Reason, UserID)
    VALUES (@StkId, @Code, @Name, @Items, @Total, LTRIM(RTRIM(@Reason)), @UserId);

    DELETE FROM CIMS.Permissions WHERE SystemID = @Code;
    DELETE FROM CIMS.ScanTransactions WHERE StockID = @StkId;
    DELETE FROM CIMS.Stocks WHERE StockID = @StkId;   -- cascades CIMS.PartStocks, CIMS.StockBarcodeFormats, CIMS.StockImports

    INSERT INTO CIMS.SystemLogs (UserID, ActionType, Description, Reference, LogDate)
    VALUES (@UserId, 'STOCK_DELETE',
            CONCAT(N'Deleted stock ', @Code, N' (', @Name, N') | Items: ', @Items, N' | Total qty: ', @Total,
                   N' | Reason: ', LTRIM(RTRIM(@Reason))),
            @Code, GETDATE());

    COMMIT;
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 8) Stored procedures
-- -----------------------------------------------------------------------------------------------------

-- Give every user VIEW on one stock (called right after a stock is created)
CREATE OR ALTER PROCEDURE CIMS.sp_Stock_GrantViewAll
    @StkId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SysId VARCHAR(30) = (SELECT StockCode FROM CIMS.Stocks WHERE StockID = @StkId AND IsMain = 0);
    IF @SysId IS NULL RETURN;

    INSERT INTO CIMS.Permissions (UserID, SystemID, CanView, CanAdd, CanEdit, CanDelete, CanApprove)
    SELECT u.UserID, @SysId, 'Y', 'N', 'N', 'N', 'N'
    FROM CIMS.Users u
    WHERE NOT EXISTS (SELECT 1 FROM CIMS.Permissions p WHERE p.UserID = u.UserID AND p.SystemID = @SysId);

    -- user already had a row (e.g. re-created stock id) -> make sure VIEW is on
    UPDATE CIMS.Permissions SET CanView = 'Y' WHERE SystemID = @SysId AND ISNULL(CanView, 'N') <> 'Y';
    -- ADD / EDIT in a new stock are granted by an admin per person (section 16 permission model)
END
GO

-- Transfer from a non-main stock into Stock-CHR.  @Qty NULL = transfer everything.
-- Result set: TransferID, Quantity, FromBalanceAfter, ToBalanceAfter.  Errors are raised with Thai messages.
CREATE OR ALTER PROCEDURE CIMS.sp_Stock_Transfer
    @FromStkId  INT,
    @PtId       INT,
    @Qty        INT = NULL,
    @UserId     VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @FromCode VARCHAR(30), @ToId INT, @ToCode VARCHAR(30), @PtCode VARCHAR(50);
    DECLARE @Bal INT, @Mode VARCHAR(10), @FromAfter INT, @ToAfter INT;

    SELECT @FromCode = StockCode FROM CIMS.Stocks WHERE StockID = @FromStkId AND IsMain = 0;
    SELECT @ToId = StockID, @ToCode = StockCode FROM CIMS.Stocks WHERE IsMain = 1;
    SELECT @PtCode = PartCode FROM CIMS.Parts WHERE PartID = @PtId;

    IF @FromCode IS NULL THROW 50001, N'ไม่พบคลังต้นทาง หรือคลังต้นทางเป็นคลังหลัก', 1;
    IF @ToId IS NULL     THROW 50002, N'ไม่พบคลังหลัก (Stock-CHR)', 1;
    IF @PtCode IS NULL   THROW 50003, N'ไม่พบข้อมูลสินค้าในระบบ', 1;

    BEGIN TRAN;

    SELECT @Bal = Quantity FROM CIMS.PartStocks WITH (UPDLOCK, HOLDLOCK)
    WHERE StockID = @FromStkId AND PartID = @PtId;

    IF ISNULL(@Bal, 0) <= 0
    BEGIN
        ROLLBACK;
        THROW 50004, N'สินค้ารายการนี้ไม่มียอดคงเหลือในคลังต้นทาง', 1;
    END

    IF @Qty IS NULL SET @Qty = @Bal;
    SET @Mode = CASE WHEN @Qty = @Bal THEN 'ALL' ELSE 'PARTIAL' END;

    IF @Qty <= 0
    BEGIN
        ROLLBACK;
        THROW 50005, N'จำนวนที่โอนต้องมากกว่า 0', 1;
    END

    IF @Qty > @Bal
    BEGIN
        ROLLBACK;
        THROW 50006, N'จำนวนที่โอนมากกว่ายอดคงเหลือในคลังต้นทาง', 1;
    END

    UPDATE CIMS.PartStocks SET Quantity = Quantity - @Qty, UpdatedDate = GETDATE()
    WHERE StockID = @FromStkId AND PartID = @PtId;

    UPDATE CIMS.Parts SET StockQuantity = ISNULL(StockQuantity, 0) + @Qty WHERE PartID = @PtId;

    SELECT @FromAfter = Quantity FROM CIMS.PartStocks WHERE StockID = @FromStkId AND PartID = @PtId;
    SELECT @ToAfter = ISNULL(StockQuantity, 0) FROM CIMS.Parts WHERE PartID = @PtId;

    INSERT INTO CIMS.StockTransfers (FromStockID, FromStockCode, ToStockID, ToStockCode, PartID, PartCode,
                                  Quantity, TransferMode, FromBalanceAfter, ToBalanceAfter, UserID)
    VALUES (@FromStkId, @FromCode, @ToId, @ToCode, @PtId, @PtCode, @Qty, @Mode, @FromAfter, @ToAfter, @UserId);

    DECLARE @TrfId INT = SCOPE_IDENTITY();

    INSERT INTO CIMS.SystemLogs (UserID, ActionType, Description, Reference, LogDate)
    VALUES (@UserId, 'STOCK_TRANSFER',
            CONCAT('[', @Mode, '] ', @FromCode, ' -> ', @ToCode, ' | PD: ', @PtCode, ' | Quantity: ', @Qty,
                   ' | BAL ', @FromCode, ': ', @FromAfter, ' | BAL ', @ToCode, ': ', @ToAfter),
            @PtCode, GETDATE());

    COMMIT;

    SELECT @TrfId AS TransferID, @Qty AS Quantity, @Mode AS TransferMode, @FromAfter AS FromBalanceAfter, @ToAfter AS ToBalanceAfter;
END
GO

CREATE OR ALTER TRIGGER CIMS.trg_ScanTransactions_Rolling3Months ON CIMS.ScanTransactions AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut DATETIME = DATEADD(MONTH, -3, GETDATE());
    IF EXISTS (SELECT 1 FROM CIMS.ScanTransactions WHERE TransactionDate < @cut)
        DELETE TOP (500) FROM CIMS.ScanTransactions WHERE TransactionDate < @cut;
END
GO

-- 4) SCAN OUT -> "Waiting" PR -----------------------------------------------------------------------------
-- Department / Target Dept = the values used most recently on the PR page (fallback: the old defaults).
-- Wrapped in TRY/CATCH so a PR problem can never block the scan itself.
CREATE OR ALTER TRIGGER CIMS.trg_ScanTransactions_AutoPR
ON CIMS.ScanTransactions
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;

    IF NOT EXISTS (SELECT 1 FROM inserted WHERE TransactionType = 'OUT') RETURN;

    BEGIN TRY
        DECLARE @prefix VARCHAR(4) = RIGHT(CAST(YEAR(GETDATE()) AS VARCHAR(4)), 2) + 'PR';

        DECLARE @dept NVARCHAR(200) = ISNULL((SELECT TOP 1 LookupValue FROM CIMS.PRLookups WHERE FieldName = 'DEPT' ORDER BY LastUsed DESC), N'41304134-บำรุงรักษาแม่พิมพ์');
        DECLARE @target NVARCHAR(200) = ISNULL((SELECT TOP 1 LookupValue FROM CIMS.PRLookups WHERE FieldName = 'TARGET' ORDER BY LastUsed DESC), N'ชนนิกานต์');

        DECLARE @last INT = ISNULL((
            SELECT MAX(TRY_CAST(RIGHT(PRNumber, 8) AS INT))
            FROM CIMS.PRHeaders WITH (UPDLOCK, HOLDLOCK)
            WHERE PRNumber LIKE @prefix + '%' AND LEN(PRNumber) = 12), 0);

        DECLARE @rows TABLE (RN INT, TransactionID INT, UserID VARCHAR(20), Description NVARCHAR(200), Quantity INT, UNIT NVARCHAR(20), StockCode VARCHAR(30));

        INSERT INTO @rows (RN, TransactionID, UserID, Description, Quantity, UNIT, StockCode)
        SELECT ROW_NUMBER() OVER (ORDER BY i.TransactionID), i.TransactionID, i.UserID, p.Description, i.Quantity,
               ISNULL(s.Unit, 'KG'), ISNULL(s.StockCode, 'Stock-CHR')
        FROM inserted i
        JOIN CIMS.Parts p       ON p.PartID = i.PartID
        -- old rows without StockID belong to the main stock
        LEFT JOIN CIMS.Stocks s ON s.StockID = ISNULL(i.StockID, (SELECT TOP 1 m.StockID FROM CIMS.Stocks m WHERE m.IsMain = 1))
        WHERE i.TransactionType = 'OUT'
          AND ISNULL(s.AutoPR, 1) = 1;      -- only stocks switched ON in PR SETTINGS

        IF NOT EXISTS (SELECT 1 FROM @rows) RETURN;

        INSERT INTO CIMS.PRHeaders (PRNumber, PRDate, UserID, RequesterName, RequestDepartment, TargetDepartment, Status, Remark,
                                  IsExported, Source, ScanTransactionID, CreatedAt)
        SELECT @prefix + RIGHT('00000000' + CAST(@last + r.RN AS VARCHAR(8)), 8),
               CAST(GETDATE() AS DATE), r.UserID, u.FullName, @dept, @target, N'Waiting',
               N'AUTO: SCAN OUT (' + r.StockCode + N')', 'N', 'SCAN_OUT', r.TransactionID, GETDATE()
        FROM @rows r
        LEFT JOIN CIMS.Users u ON u.UserID = r.UserID;

        INSERT INTO CIMS.PRDetails (PRNumber, Description, RequestQuantity, Unit)
        SELECT @prefix + RIGHT('00000000' + CAST(@last + r.RN AS VARCHAR(8)), 8), r.Description, r.Quantity, r.UNIT
        FROM @rows r;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> -1
            INSERT INTO CIMS.SystemLogs (UserID, ActionType, Description, Reference, LogDate)
            VALUES (NULL, 'AUTO_PR_ERROR', N'Scan out -> PR failed: ' + ERROR_MESSAGE(), '', GETDATE());
    END CATCH
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 9) Trigger: a newly created user automatically gets VIEW on the Store (Max-Min) page (SystemID STORE)
--    and VIEW on every stock, main stock included (SystemID = stock code)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER TRIGGER CIMS.trg_Users_GrantStockView
ON CIMS.Users
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO CIMS.Permissions (UserID, SystemID, CanView, CanAdd, CanEdit, CanDelete, CanApprove)
    SELECT i.UserID, s.StockCode, 'Y', 'N', 'N', 'N', 'N'
    FROM inserted i
    CROSS JOIN CIMS.Stocks s
    WHERE NOT EXISTS (SELECT 1 FROM CIMS.Permissions p
                      WHERE p.UserID = i.UserID AND p.SystemID = s.StockCode);

    INSERT INTO CIMS.Permissions (UserID, SystemID, CanView, CanAdd, CanEdit, CanDelete, CanApprove)
    SELECT i.UserID, 'StoreMaxMin', 'Y', 'N', 'N', 'N', 'N'
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM CIMS.Permissions p WHERE p.UserID = i.UserID AND p.SystemID = 'StoreMaxMin');
END
GO

CREATE OR ALTER TRIGGER CIMS.trg_SystemLogs_Rolling3Months ON CIMS.SystemLogs AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut DATETIME = DATEADD(MONTH, -3, GETDATE());
    IF EXISTS (SELECT 1 FROM CIMS.SystemLogs WHERE LogDate < @cut)
        DELETE TOP (500) FROM CIMS.SystemLogs WHERE LogDate < @cut;
END
GO

--   only Quantity changed (import, transfer, editing PCS)  -> BoxQuantity = Quantity / PACK SIZE
--   only BoxQuantity changed (editing BOX)                 -> Quantity = BoxQuantity x PACK SIZE
--   both changed in one statement (scan)               -> keep both
CREATE OR ALTER TRIGGER CIMS.trg_PartStocks_BoxSync
ON CIMS.PartStocks
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL(@@PROCID) > 1 RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    IF NOT EXISTS (SELECT 1 FROM deleted)
    BEGIN
        -- new row without a box count -> derive it from the pieces
        UPDATE ps SET BoxQuantity = CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN ps.Quantity / p.PackSize ELSE 0 END
        FROM CIMS.PartStocks ps
        JOIN inserted i ON i.StockID = ps.StockID AND i.PartID = ps.PartID
        JOIN CIMS.Parts p ON p.PartID = ps.PartID
        WHERE i.BoxQuantity = 0 AND i.Quantity > 0;
        RETURN;
    END

    IF UPDATE(Quantity) AND NOT UPDATE(BoxQuantity)
        UPDATE ps SET BoxQuantity = CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN ps.Quantity / p.PackSize ELSE 0 END
        FROM CIMS.PartStocks ps
        JOIN inserted i ON i.StockID = ps.StockID AND i.PartID = ps.PartID
        JOIN deleted d  ON d.StockID = i.StockID AND d.PartID = i.PartID
        JOIN CIMS.Parts p ON p.PartID = ps.PartID
        WHERE i.Quantity <> d.Quantity;
    ELSE IF UPDATE(BoxQuantity) AND NOT UPDATE(Quantity)
        UPDATE ps SET Quantity = ps.BoxQuantity * CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN p.PackSize ELSE 1 END
        FROM CIMS.PartStocks ps
        JOIN inserted i ON i.StockID = ps.StockID AND i.PartID = ps.PartID
        JOIN deleted d  ON d.StockID = i.StockID AND d.PartID = i.PartID
        JOIN CIMS.Parts p ON p.PartID = ps.PartID
        WHERE i.BoxQuantity <> d.BoxQuantity;
END
GO

PRINT 'CIMS full-name rename completed.';
