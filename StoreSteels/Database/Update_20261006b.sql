-- =====================================================================================================
-- CIMS update 2026-10-06 (b): TEMPORARY live view of the old StorePC program (database StorePC, same server)
--   * CIMS.Stocks.LiveSource: 'StorePC' = this stock shows StorePC.dbo.MST_PART live (read only in CIMS)
--     STORE-PC is set to 'StorePC' on the first run when the StorePC database exists
--   * CIMS.vw_StorePcLive: StorePC products in CIMS column names (images: 'STOREPC:<file>' -> ImagePaths)
--   * CIMS.vw_StockMonitoring: a LiveSource stock reads vw_StorePcLive instead of CIMS.PartStocks
--   Nothing is written to StorePC. To stop the live view: UPDATE CIMS.Stocks SET LiveSource = NULL WHERE StockCode = 'STORE-PC'
-- Safe to run more than once.
-- =====================================================================================================
SET XACT_ABORT ON;
GO
IF COL_LENGTH('CIMS.Stocks', 'LiveSource') IS NULL
BEGIN
    ALTER TABLE CIMS.Stocks ADD LiveSource NVARCHAR(30) NULL;
    IF DB_ID('StorePC') IS NOT NULL
        EXEC (N'UPDATE CIMS.Stocks SET LiveSource = N''StorePC'' WHERE StockCode = ''STORE-PC''');
END
GO

-- StorePC products (only what the old Store page shows: active + shown) - CIMS PartID when the product was
-- copied into CIMS before (PartCode + PartA), otherwise a negative id (display only)
-- no StorePC database on this server -> empty view with the same columns (vw_StockMonitoring still works)
DECLARE @live NVARCHAR(MAX) = CASE WHEN DB_ID('StorePC') IS NOT NULL THEN N'
CREATE OR ALTER VIEW CIMS.vw_StorePcLive AS
SELECT ISNULL(cp.PartID, -m.PT_ID) AS PartID,
       m.PT_ID,
       CAST(m.QTY_MAX AS DECIMAL(18,3)) AS QMAX,
       CAST(m.QTY_MIN AS DECIMAL(18,3)) AS QMIN,
       CAST(ISNULL(m.QTY_STK, 0) AS DECIMAL(18,3)) AS Quantity,
       m.PT_REMARK COLLATE DATABASE_DEFAULT AS RMK,
       ISNULL(m.QTY_STKB, 0) AS BOXQ,
       m.PT_CAT COLLATE DATABASE_DEFAULT AS Category,
       CAST(NULL AS NVARCHAR(MAX)) AS Supplier,
       CASE WHEN ISNULL(m.PT_IMG, '''') = '''' THEN NULL ELSE N''STOREPC:'' + m.PT_IMG COLLATE DATABASE_DEFAULT END AS ImageFileName,
       ISNULL(NULLIF(LTRIM(RTRIM(m.PT_CODE)), ''''), ''-'') COLLATE DATABASE_DEFAULT AS PartCode,
       m.PT_DESC COLLATE DATABASE_DEFAULT AS Description,
       m.PT_PSZ AS PackSize,
       m.LIT_STAT AS Status,
       m.PT_LOC COLLATE DATABASE_DEFAULT AS Bin,
       m.PT_CUST COLLATE DATABASE_DEFAULT AS Customer,
       m.PT_ACODE COLLATE DATABASE_DEFAULT AS PartA,
       m.PT_NO COLLATE DATABASE_DEFAULT AS PartNumber,
       m.PT_MODEL COLLATE DATABASE_DEFAULT AS Model
FROM StorePC.dbo.MST_PART m
OUTER APPLY (SELECT TOP 1 p.PartID FROM CIMS.Parts p
             WHERE p.PartCode = ISNULL(NULLIF(LTRIM(RTRIM(m.PT_CODE)), ''''), ''-'') COLLATE DATABASE_DEFAULT
               AND ISNULL(p.PartA, '''') = ISNULL(LTRIM(RTRIM(m.PT_ACODE)), '''') COLLATE DATABASE_DEFAULT
             ORDER BY p.PartID) cp
WHERE m.IS_ACTIVE = 1 AND ISNULL(m.IS_SHOW_MST, 1) = 1'
ELSE N'
CREATE OR ALTER VIEW CIMS.vw_StorePcLive AS
SELECT CAST(0 AS INT) AS PartID, CAST(0 AS INT) AS PT_ID, CAST(0 AS DECIMAL(18,3)) AS QMAX, CAST(0 AS DECIMAL(18,3)) AS QMIN,
       CAST(0 AS DECIMAL(18,3)) AS Quantity, CAST(NULL AS NVARCHAR(500)) AS RMK, CAST(0 AS INT) AS BOXQ, CAST(NULL AS NVARCHAR(100)) AS Category,
       CAST(NULL AS NVARCHAR(MAX)) AS Supplier, CAST(NULL AS NVARCHAR(300)) AS ImageFileName, CAST(NULL AS VARCHAR(100)) AS PartCode,
       CAST(NULL AS NVARCHAR(500)) AS Description, CAST(0 AS INT) AS PackSize, CAST(0 AS INT) AS Status, CAST(NULL AS NVARCHAR(100)) AS Bin,
       CAST(NULL AS NVARCHAR(100)) AS Customer, CAST(NULL AS NVARCHAR(100)) AS PartA, CAST(NULL AS NVARCHAR(100)) AS PartNumber, CAST(NULL AS NVARCHAR(100)) AS Model
WHERE 1 = 0' END;
EXEC (@live);
GO

CREATE OR ALTER VIEW CIMS.vw_StockMonitoring AS
WITH base AS (
    -- main stock: balance / max / min / remark live in CIMS.Parts
    SELECT s.StockID, s.UseMaxMin, s.MaxMinBasis, s.GroupBy, p.PartID,
           p.MaxQuantity AS QMAX, p.MinQuantity AS QMIN, ISNULL(p.StockQuantity, 0) AS Quantity, p.Remark AS RMK, CAST(NULL AS INT) AS BOXQ,
           p.Category, p.Supplier, p.ImageFileName, p.PartCode, p.Description, p.PackSize, p.Status, p.Bin, p.Customer, p.PartA, p.PartNumber, p.Model
    FROM CIMS.Stocks s
    JOIN CIMS.Parts p ON s.IsMain = 1
    WHERE p.IsActive = 1 AND p.IsShowInMaster = 1

    UNION ALL

    -- other stocks: balance per part in CIMS.PartStocks
    SELECT s.StockID, s.UseMaxMin, s.MaxMinBasis, s.GroupBy, p.PartID,
           ps.MaxQuantity, ps.MinQuantity, ps.Quantity, ps.Remark, ps.BoxQuantity,
           p.Category, p.Supplier, p.ImageFileName, p.PartCode, p.Description, p.PackSize, p.Status, p.Bin, p.Customer, p.PartA, p.PartNumber, p.Model
    FROM CIMS.PartStocks ps
    JOIN CIMS.Stocks s ON s.StockID = ps.StockID AND s.IsMain = 0 AND s.LiveSource IS NULL
    JOIN CIMS.Parts p  ON p.PartID  = ps.PartID
    WHERE p.IsActive = 1 AND ps.IsShow = 1

    UNION ALL

    -- TEMPORARY: stock shown live from the old StorePC program (read only)
    SELECT s.StockID, s.UseMaxMin, s.MaxMinBasis, s.GroupBy, l.PartID,
           l.QMAX, l.QMIN, l.Quantity, l.RMK, l.BOXQ,
           l.Category, l.Supplier, l.ImageFileName, l.PartCode, l.Description, l.PackSize, l.Status, l.Bin, l.Customer, l.PartA, l.PartNumber, l.Model
    FROM CIMS.vw_StorePcLive l
    JOIN CIMS.Stocks s ON s.LiveSource = N'StorePC' AND s.IsMain = 0
),
calc AS (
    SELECT b.*,
           -- STOCK (BOX): other stocks keep their own box count (scan = +1 / -1 box), main stock = balance / pack size
           CASE WHEN b.BOXQ IS NOT NULL THEN b.BOXQ WHEN ISNULL(b.PackSize, 0) > 0 THEN CAST(FLOOR(b.Quantity / b.PackSize) AS INT) ELSE 0 END AS BoxQuantity
    FROM base b
)
SELECT
    c.StockID        AS StkId,
    c.PartID,
    c.Category       AS Category,
    c.Supplier       AS Supplier,
    c.ImageFileName  AS ImageFileName,
    c.PartCode       AS PartCode,
    c.Description    AS PartName,
    c.PackSize       AS PackSize,
    c.QMAX           AS [Max],
    c.QMIN           AS [Min],
    c.Quantity       AS QtyStkb,
    c.RMK            AS Remark,
    c.Status         AS Priority,
    c.Bin            AS Bin,
    c.Customer       AS Customer,
    c.PartA          AS PartA,
    c.PartNumber     AS PartNo,
    c.Model          AS Model,
    c.BoxQuantity    AS StockBox,
    c.Quantity       AS StockPcs,
    -- group header of the Store table: Category (default) / Customer / Supplier
    -- fixed length (NVARCHAR(200)): Supplier is NVARCHAR(MAX) - a MAX GroupKey makes DISTINCT / ORDER BY ask for a huge memory grant
    CAST(CASE WHEN c.GroupBy = 'CUSTOMER' THEN ISNULL(NULLIF(c.Customer, ''), '-')
              WHEN c.GroupBy = 'SUPPLIER' THEN ISNULL(NULLIF(CAST(c.Supplier AS NVARCHAR(200)), ''), '-')
              ELSE ISNULL(NULLIF(c.Category, ''), '-') END AS NVARCHAR(200)) AS GroupKey,
    CASE
        WHEN c.UseMaxMin = 0 THEN 'NORMAL'      -- stock without Max/Min: plain rows (not the grey NO_CONFIG look)
        WHEN ISNULL(c.QMAX, 0) = 0 AND ISNULL(c.QMIN, 0) = 0 THEN 'NO_CONFIG'
        -- MAX / MIN compared with Quantity (stock unit) or with STOCK (BOX)
        WHEN (CASE WHEN c.MaxMinBasis = 'BOX' THEN c.BoxQuantity ELSE c.Quantity END) < ISNULL(c.QMIN, 0) THEN 'UNDER_MIN'
        WHEN (CASE WHEN c.MaxMinBasis = 'BOX' THEN c.BoxQuantity ELSE c.Quantity END) > ISNULL(c.QMAX, 0) AND ISNULL(c.QMAX, 0) > 0 THEN 'OVER_MAX'
        WHEN (CASE WHEN c.MaxMinBasis = 'BOX' THEN c.BoxQuantity ELSE c.Quantity END) BETWEEN ISNULL(c.QMIN, 0) AND ISNULL(c.QMAX, 0) THEN 'NORMAL_GOOD'
        ELSE 'NORMAL'
    END AS StockStatus
FROM calc c;
GO

-- views that select from vw_StockMonitoring pick up the new definition
DECLARE @v NVARCHAR(300);
DECLARE vc CURSOR LOCAL FAST_FORWARD FOR
    SELECT QUOTENAME(OBJECT_SCHEMA_NAME(object_id)) + '.' + QUOTENAME(name) FROM sys.views
    WHERE OBJECT_SCHEMA_NAME(object_id) = 'CIMS' AND name NOT IN ('vw_StockMonitoring', 'vw_StorePcLive');
OPEN vc; FETCH NEXT FROM vc INTO @v;
WHILE @@FETCH_STATUS = 0 BEGIN BEGIN TRY EXEC sp_refreshview @v; END TRY BEGIN CATCH END CATCH; FETCH NEXT FROM vc INTO @v; END
CLOSE vc; DEALLOCATE vc;
GO
