-- =====================================================================================================
-- CIMS update 2026-10-06 (d): MAX / MIN in COIL + table column order
--   * MAX / MIN UNIT = COIL (like BOX for StorePC): MAX / MIN compared with STOCK (COIL)
--     Max-Min Calculator: per day / PACK SIZE (PACK SIZE = KG per coil), the same way as BOX
--   * CIMS.Stocks.ColumnOrder: Store table columns in the order they were ticked (ADD / EDIT STOCK)
-- Needs Update_20261006c.sql first. Safe to run more than once.
-- =====================================================================================================
SET XACT_ABORT ON;
GO
IF COL_LENGTH('CIMS.Stocks', 'CountCoil') IS NULL
    RAISERROR('Run Update_20261006c.sql first', 16, 1);
GO
IF COL_LENGTH('CIMS.Stocks', 'ColumnOrder') IS NULL
    ALTER TABLE CIMS.Stocks ADD ColumnOrder NVARCHAR(300) NULL;
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
           CASE WHEN k.MaxMinBasis IN ('BOX', 'COIL') THEN 1 ELSE 0 END AS IN_BOX   -- COIL: PACK SIZE = KG per coil
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

CREATE OR ALTER VIEW CIMS.vw_StockMonitoring AS
WITH base AS (
    -- main stock: balance / max / min / remark live in CIMS.Parts
    SELECT s.StockID, s.UseMaxMin, s.MaxMinBasis, s.GroupBy, p.PartID,
           p.MaxQuantity AS QMAX, p.MinQuantity AS QMIN, ISNULL(p.StockQuantity, 0) AS Quantity, p.Remark AS RMK,
           CASE WHEN s.CountCoil = 1 THEN p.CoilQuantity ELSE CAST(NULL AS INT) END AS BOXQ,   -- KG stock: QTY (COIL) = coils scanned
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
        WHEN (CASE WHEN c.MaxMinBasis IN ('BOX', 'COIL') THEN c.BoxQuantity ELSE c.Quantity END) < ISNULL(c.QMIN, 0) THEN 'UNDER_MIN'
        WHEN (CASE WHEN c.MaxMinBasis IN ('BOX', 'COIL') THEN c.BoxQuantity ELSE c.Quantity END) > ISNULL(c.QMAX, 0) AND ISNULL(c.QMAX, 0) > 0 THEN 'OVER_MAX'
        WHEN (CASE WHEN c.MaxMinBasis IN ('BOX', 'COIL') THEN c.BoxQuantity ELSE c.Quantity END) BETWEEN ISNULL(c.QMIN, 0) AND ISNULL(c.QMAX, 0) THEN 'NORMAL_GOOD'
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