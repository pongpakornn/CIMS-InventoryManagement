-- =====================================================================================================
-- Max-Min Calculator (ported from StorePC MaxMinCalculatorView, extended for multi-stock)
-- Run against the Stock DB (CIMS) AFTER MultiStock.sql. Safe to run more than once.
--
--  1) MST_CALC_CONFIG      : MAX (DAYS) / MIN (DAYS) per stock + part  (+ STK_ID, unique STK_ID + PT_ID)
--  2) MST_CALC_FORMULA     : list of calculation formulas (FORMULA button, like Barcode Formats)
--     MST_CALC_STOCK       : formula + AUTO CALC chosen per stock
--  3) MST_CUST_WORKDAY     : customer working days, one row per working date (WORKDAYS button / import)
--  4) TRN_IMPORT_STAGE     : Forecast / Order / Delivery per customer (+ part) per month (IMPORT FORECAST & ORDER)
--                            rows are kept as history for the Dashboard charts (re-import replaces the same month)
--  5) sp_MaxMin_Calculate  : Order (or Forecast) / Workdays -> per day -> / PackSize -> x days -> MAX / MIN
--                            written into the Store (Max-Min) values of the stock
--  6) Dashboard views      : pie = orders of the current month (previous month still shown on day 1 when the
--                            new month has not been imported yet), bar = Forecast / Order / Delivery of this year
-- =====================================================================================================
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1) MST_CALC_CONFIG ------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.MST_CALC_CONFIG', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_CALC_CONFIG (
        CONFIG_ID   INT IDENTITY(1,1) PRIMARY KEY,
        CUST_CODE   NVARCHAR(100) NOT NULL,
        PT_ID       INT           NOT NULL,
        PT_ACODE    NVARCHAR(100) NOT NULL,
        DAY_MIN     INT NULL,
        DAY_MAX     INT NULL,
        UPDATE_BY   VARCHAR(20) NULL,
        UPDATE_DATE DATETIME NULL
    );
END
GO
IF COL_LENGTH('dbo.MST_CALC_CONFIG', 'STK_ID') IS NULL
    ALTER TABLE dbo.MST_CALC_CONFIG ADD STK_ID INT NULL;
GO
-- old rows (StorePC style, no stock) belong to the main stock
UPDATE dbo.MST_CALC_CONFIG SET STK_ID = (SELECT TOP 1 STK_ID FROM dbo.MST_STOCK WHERE IS_MAIN = 1) WHERE STK_ID IS NULL;
GO
-- one config per stock + part (the old customer + part key blocks the same part in two stocks)
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UC_Cust_PartID_Days' AND parent_object_id = OBJECT_ID('dbo.MST_CALC_CONFIG'))
    ALTER TABLE dbo.MST_CALC_CONFIG DROP CONSTRAINT UC_Cust_PartID_Days;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UC_Cust_PartID_Days' AND object_id = OBJECT_ID('dbo.MST_CALC_CONFIG'))
    DROP INDEX UC_Cust_PartID_Days ON dbo.MST_CALC_CONFIG;
GO
;WITH d AS (SELECT CONFIG_ID, ROW_NUMBER() OVER (PARTITION BY STK_ID, PT_ID ORDER BY UPDATE_DATE DESC, CONFIG_ID DESC) AS rn FROM dbo.MST_CALC_CONFIG)
DELETE FROM d WHERE rn > 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CALC_CONFIG_STK_PT' AND object_id = OBJECT_ID('dbo.MST_CALC_CONFIG'))
    CREATE UNIQUE INDEX UX_CALC_CONFIG_STK_PT ON dbo.MST_CALC_CONFIG (STK_ID, PT_ID);
GO

-- 2) MST_CALC_FORMULA (list of formulas, like Barcode Formats) + MST_CALC_STOCK (formula chosen per stock) ------
--    QTY_SOURCE  : ORDER / FORECAST            (monthly quantity used)
--    ROUND_MODE  : UP / NEAREST / DOWN         (rounding of the quantity per day; then x MAX / MIN DAYS)
--    DEF_*       : defaults for parts without their own MAX / MIN (DAYS), and workdays when a customer
--                  has no calendar for that month and the import did not give WORK DAYS
--    IS_DEFAULT  : used by stocks that have not chosen a formula (cannot be deleted)
--    MST_CALC_STOCK.AUTO_CALC : 1 = recalculate the stock automatically after IMPORT FORECAST & ORDER

-- old shape (one formula row per stock, STK_ID key) -> moved into the new tables below
IF OBJECT_ID('dbo.MST_CALC_FORMULA', 'U') IS NOT NULL AND COL_LENGTH('dbo.MST_CALC_FORMULA', 'STK_ID') IS NOT NULL
    EXEC sp_rename 'dbo.MST_CALC_FORMULA', 'MST_CALC_FORMULA_OLD';
GO
IF OBJECT_ID('dbo.MST_CALC_FORMULA', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_CALC_FORMULA (
        FORMULA_ID    INT IDENTITY(1,1) CONSTRAINT PK_MST_CALC_FORMULA_ID PRIMARY KEY,
        FORMULA_NAME  NVARCHAR(50) NOT NULL CONSTRAINT UQ_CALC_FORMULA_NAME UNIQUE,
        QTY_SOURCE    VARCHAR(10)  NOT NULL CONSTRAINT DF_CALCF2_SRC DEFAULT ('ORDER'),
        ROUND_MODE    VARCHAR(10)  NOT NULL CONSTRAINT DF_CALCF2_RMODE DEFAULT ('UP'),
        DEF_DAY_MAX   INT          NOT NULL CONSTRAINT DF_CALCF2_DMAX DEFAULT (3),
        DEF_DAY_MIN   INT          NOT NULL CONSTRAINT DF_CALCF2_DMIN DEFAULT (1),
        DEF_WORKDAYS  INT          NOT NULL CONSTRAINT DF_CALCF2_WD DEFAULT (21),
        IS_DEFAULT    BIT          NOT NULL CONSTRAINT DF_CALCF2_DEF DEFAULT (0),
        UPDATED_BY    VARCHAR(20)  NULL,
        UPDATED_DATE  DATETIME     NULL
    );
END
GO
IF OBJECT_ID('dbo.MST_CALC_STOCK', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_CALC_STOCK (
        STK_ID       INT NOT NULL CONSTRAINT PK_MST_CALC_STOCK PRIMARY KEY
                         CONSTRAINT FK_CALC_STOCK_STOCK FOREIGN KEY REFERENCES dbo.MST_STOCK (STK_ID) ON DELETE CASCADE,
        FORMULA_ID   INT NULL CONSTRAINT FK_CALC_STOCK_FORMULA FOREIGN KEY REFERENCES dbo.MST_CALC_FORMULA (FORMULA_ID),
        AUTO_CALC    BIT NOT NULL CONSTRAINT DF_CALCS_AUTO DEFAULT (0),
        LAST_CALC    DATETIME    NULL,
        UPDATED_BY   VARCHAR(20) NULL,
        UPDATED_DATE DATETIME    NULL
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.MST_CALC_FORMULA WHERE IS_DEFAULT = 1)
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.MST_CALC_FORMULA WHERE FORMULA_NAME = N'STANDARD')
        UPDATE dbo.MST_CALC_FORMULA SET IS_DEFAULT = 1 WHERE FORMULA_NAME = N'STANDARD';
    ELSE
        INSERT INTO dbo.MST_CALC_FORMULA (FORMULA_NAME, QTY_SOURCE, ROUND_MODE, DEF_DAY_MAX, DEF_DAY_MIN, DEF_WORKDAYS, IS_DEFAULT, UPDATED_BY, UPDATED_DATE)
        VALUES (N'STANDARD', 'ORDER', 'UP', 3, 1, 21, 1, 'SYSTEM', GETDATE());
END
GO
IF OBJECT_ID('dbo.MST_CALC_FORMULA_OLD', 'U') IS NOT NULL
BEGIN
    EXEC ('
        INSERT INTO dbo.MST_CALC_FORMULA (FORMULA_NAME, QTY_SOURCE, ROUND_MODE, DEF_DAY_MAX, DEF_DAY_MIN, DEF_WORKDAYS, UPDATED_BY, UPDATED_DATE)
        SELECT LEFT(N''FORMULA '' + s.STK_CODE, 50), o.QTY_SOURCE, o.ROUND_MODE, o.DEF_DAY_MAX, o.DEF_DAY_MIN, o.DEF_WORKDAYS, o.UPDATED_BY, o.UPDATED_DATE
        FROM dbo.MST_CALC_FORMULA_OLD o JOIN dbo.MST_STOCK s ON s.STK_ID = o.STK_ID
        WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_CALC_FORMULA f WHERE f.FORMULA_NAME = LEFT(N''FORMULA '' + s.STK_CODE, 50));
        INSERT INTO dbo.MST_CALC_STOCK (STK_ID, FORMULA_ID, AUTO_CALC, LAST_CALC, UPDATED_BY, UPDATED_DATE)
        SELECT o.STK_ID, f.FORMULA_ID, o.AUTO_CALC, o.LAST_CALC, o.UPDATED_BY, o.UPDATED_DATE
        FROM dbo.MST_CALC_FORMULA_OLD o JOIN dbo.MST_STOCK s ON s.STK_ID = o.STK_ID
        JOIN dbo.MST_CALC_FORMULA f ON f.FORMULA_NAME = LEFT(N''FORMULA '' + s.STK_CODE, 50)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_CALC_STOCK c WHERE c.STK_ID = o.STK_ID);');
    DROP TABLE dbo.MST_CALC_FORMULA_OLD;
END
GO

-- the StorePC procedure was never finished and is not used (the calculation is sp_MaxMin_Calculate)
IF OBJECT_ID('dbo.sp_ProcessExcelImportAutoDays', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ProcessExcelImportAutoDays;
GO

-- 3) MST_CUST_WORKDAY -----------------------------------------------------------------------------------
IF OBJECT_ID('dbo.MST_CUST_WORKDAY', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_CUST_WORKDAY (
        CUST_CODE    NVARCHAR(100) NOT NULL,
        WORK_DATE    DATE          NOT NULL,
        UPDATED_BY   VARCHAR(20)   NULL,
        UPDATED_DATE DATETIME      NOT NULL CONSTRAINT DF_CUSTWD_DATE DEFAULT (GETDATE()),
        CONSTRAINT PK_MST_CUST_WORKDAY PRIMARY KEY (CUST_CODE, WORK_DATE)
    );
END
GO

-- 4) TRN_IMPORT_STAGE -----------------------------------------------------------------------------------
IF OBJECT_ID('dbo.TRN_IMPORT_STAGE', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRN_IMPORT_STAGE (
        IMPORT_ID    INT IDENTITY(1,1) PRIMARY KEY,
        CUST_CODE    NVARCHAR(100) NULL,
        PT_ACODE     NVARCHAR(100) NULL,
        FORECAST_QTY DECIMAL(18,2) NULL,
        ORDER_QTY    DECIMAL(18,2) NULL,
        WORK_DAYS    INT NULL,
        TARGET_DATE  DATE NULL,
        GUID_RUN     UNIQUEIDENTIFIER NULL,
        CREATED_AT   DATETIME NULL CONSTRAINT DF_STAGE_CREATED DEFAULT (GETDATE()),
        DELIVERY_QTY DECIMAL(18,2) NULL
    );
END
GO
IF COL_LENGTH('dbo.TRN_IMPORT_STAGE', 'DELIVERY_QTY') IS NULL
    ALTER TABLE dbo.TRN_IMPORT_STAGE ADD DELIVERY_QTY DECIMAL(18,2) NULL;
IF COL_LENGTH('dbo.TRN_IMPORT_STAGE', 'CREATED_BY') IS NULL
    ALTER TABLE dbo.TRN_IMPORT_STAGE ADD CREATED_BY VARCHAR(20) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_STAGE_CUST_DATE' AND object_id = OBJECT_ID('dbo.TRN_IMPORT_STAGE'))
    CREATE INDEX IX_STAGE_CUST_DATE ON dbo.TRN_IMPORT_STAGE (CUST_CODE, TARGET_DATE);
GO

-- rounding used by sp_MaxMin_Calculate (UP / NEAREST / DOWN)
CREATE OR ALTER FUNCTION dbo.fn_MaxMin_Round (@v DECIMAL(18,6), @mode VARCHAR(10))
RETURNS DECIMAL(18,0)
AS
BEGIN
    RETURN CASE @mode WHEN 'DOWN' THEN FLOOR(@v) WHEN 'NEAREST' THEN ROUND(@v, 0) ELSE CEILING(@v) END;
END
GO
-- 5) Calculation -----------------------------------------------------------------------------------------
--    @StkId NULL = every stock with AUTO_CALC = 1 (after import) / a stock id = that stock (CALCULATE NOW)
--    @PtId  NULL = every part shown in the Store (Max-Min) of the stock / a part = only that part (after SAVE)
--    Formula of the stock (MST_CALC_STOCK) or the default formula when the stock has not chosen one.
--    Month used: the latest month <= this month that has an Order (or Forecast) for the customer + part.
--    Parts without an Order / Forecast are left unchanged (reported as skipped).
--    MAX = Round(Qty / Workdays [/ PackSize when the stock MAX / MIN is in BOX]) x MAX DAYS  (MIN the same)
CREATE OR ALTER PROCEDURE dbo.sp_MaxMin_Calculate
    @StkId  INT = NULL,
    @PtId   INT = NULL,
    @UserId VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @thisMonth DATE = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @defFormula INT = (SELECT TOP 1 FORMULA_ID FROM dbo.MST_CALC_FORMULA WHERE IS_DEFAULT = 1 ORDER BY FORMULA_ID);

    SELECT s.STK_ID, s.IS_MAIN, s.MAXMIN_BASIS, f.QTY_SOURCE, f.ROUND_MODE, f.DEF_DAY_MAX, f.DEF_DAY_MIN, f.DEF_WORKDAYS
    INTO #stk
    FROM dbo.MST_STOCK s
    LEFT JOIN dbo.MST_CALC_STOCK cs ON cs.STK_ID = s.STK_ID
    JOIN dbo.MST_CALC_FORMULA f ON f.FORMULA_ID = ISNULL(cs.FORMULA_ID, @defFormula)
    WHERE (@StkId IS NULL AND cs.AUTO_CALC = 1) OR s.STK_ID = @StkId;

    -- parts shown in the Store (Max-Min) of those stocks
    SELECT v.StkId AS STK_ID, v.PT_ID, LTRIM(RTRIM(ISNULL(p.PT_CUST, ''))) AS CUST,
           LTRIM(RTRIM(ISNULL(p.PT_PARTA, ''))) AS PARTA, LTRIM(RTRIM(ISNULL(p.PT_PARTNO, ''))) AS PARTNO, LTRIM(RTRIM(p.PT_CODE)) AS CODE,
           CASE WHEN ISNULL(p.PT_PSZ, 0) > 0 THEN p.PT_PSZ ELSE 1 END AS PSZ,
           ISNULL(c.DAY_MAX, k.DEF_DAY_MAX) AS DMAX, ISNULL(c.DAY_MIN, k.DEF_DAY_MIN) AS DMIN,
           k.QTY_SOURCE, k.ROUND_MODE, k.DEF_WORKDAYS, k.IS_MAIN,
           CASE WHEN k.MAXMIN_BASIS = 'BOX' THEN 1 ELSE 0 END AS IN_BOX
    INTO #parts
    FROM dbo.VW_StockMonitoring v
    JOIN #stk k ON k.STK_ID = v.StkId
    JOIN dbo.MST_PART p ON p.PT_ID = v.PT_ID
    LEFT JOIN dbo.MST_CALC_CONFIG c ON c.STK_ID = v.StkId AND c.PT_ID = v.PT_ID
    WHERE @PtId IS NULL OR v.PT_ID = @PtId;

    -- monthly quantity per part (latest month <= this month)
    SELECT x.STK_ID, x.PT_ID, m.MON, m.QTY, m.WD_IMPORT
    INTO #qty
    FROM #parts x
    CROSS APPLY (
        SELECT TOP 1 DATEFROMPARTS(YEAR(st.TARGET_DATE), MONTH(st.TARGET_DATE), 1) AS MON,
               CASE WHEN x.QTY_SOURCE = 'FORECAST' THEN SUM(ISNULL(st.FORECAST_QTY, 0)) ELSE SUM(ISNULL(st.ORDER_QTY, 0)) END AS QTY,
               MAX(st.WORK_DAYS) AS WD_IMPORT
        FROM dbo.TRN_IMPORT_STAGE st
        WHERE LTRIM(RTRIM(ISNULL(st.CUST_CODE, ''))) = x.CUST
          AND LTRIM(RTRIM(ISNULL(st.PT_ACODE, ''))) <> ''
          AND LTRIM(RTRIM(st.PT_ACODE)) IN (x.PARTA, x.PARTNO, x.CODE)
          AND st.TARGET_DATE < DATEADD(MONTH, 1, @thisMonth)
        GROUP BY DATEFROMPARTS(YEAR(st.TARGET_DATE), MONTH(st.TARGET_DATE), 1)
        HAVING CASE WHEN x.QTY_SOURCE = 'FORECAST' THEN SUM(ISNULL(st.FORECAST_QTY, 0)) ELSE SUM(ISNULL(st.ORDER_QTY, 0)) END > 0
        ORDER BY DATEFROMPARTS(YEAR(st.TARGET_DATE), MONTH(st.TARGET_DATE), 1) DESC
    ) m;

    -- workdays: customer calendar > WORK DAYS in the import > formula default
    SELECT x.STK_ID, x.PT_ID, x.IS_MAIN, x.DMAX, x.DMIN,
           -- per day (/ pack size when the stock MAX / MIN is in BOX), rounded as set in the formula
           dbo.fn_MaxMin_Round(q.QTY / CAST(COALESCE(NULLIF(wd.CNT, 0), NULLIF(q.WD_IMPORT, 0), NULLIF(x.DEF_WORKDAYS, 0), 1) AS DECIMAL(18,6))
                               / CASE WHEN x.IN_BOX = 1 THEN x.PSZ ELSE 1 END, x.ROUND_MODE) AS PER_DAY
    INTO #per
    FROM #parts x
    JOIN #qty q ON q.STK_ID = x.STK_ID AND q.PT_ID = x.PT_ID
    OUTER APPLY (SELECT COUNT(*) AS CNT FROM dbo.MST_CUST_WORKDAY w
                 WHERE w.CUST_CODE = x.CUST AND w.WORK_DATE >= q.MON AND w.WORK_DATE < DATEADD(MONTH, 1, q.MON)) wd;

    SELECT STK_ID, PT_ID, IS_MAIN, CAST(PER_DAY * DMAX AS INT) AS NEW_MAX, CAST(PER_DAY * DMIN AS INT) AS NEW_MIN
    INTO #result FROM #per;

    BEGIN TRAN;
        UPDATE mp SET mp.QTY_MAX = r.NEW_MAX, mp.QTY_MIN = r.NEW_MIN
        FROM dbo.MST_PART mp JOIN #result r ON r.PT_ID = mp.PT_ID AND r.IS_MAIN = 1;

        UPDATE ps SET ps.QTY_MAX = r.NEW_MAX, ps.QTY_MIN = r.NEW_MIN, ps.UPDATED_DATE = GETDATE()
        FROM dbo.MST_PART_STOCK ps JOIN #result r ON r.STK_ID = ps.STK_ID AND r.PT_ID = ps.PT_ID AND r.IS_MAIN = 0;

        IF @PtId IS NULL
        BEGIN
            UPDATE cs SET cs.LAST_CALC = GETDATE() FROM dbo.MST_CALC_STOCK cs JOIN #stk k ON k.STK_ID = cs.STK_ID;
            INSERT INTO dbo.MST_CALC_STOCK (STK_ID, FORMULA_ID, AUTO_CALC, LAST_CALC, UPDATED_BY, UPDATED_DATE)
            SELECT k.STK_ID, NULL, 0, GETDATE(), @UserId, GETDATE() FROM #stk k
            WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_CALC_STOCK cs WHERE cs.STK_ID = k.STK_ID);
        END
    COMMIT;

    SELECT (SELECT COUNT(*) FROM #result) AS Updated,
           (SELECT COUNT(*) FROM #parts) - (SELECT COUNT(*) FROM #result) AS Skipped,
           (SELECT COUNT(*) FROM #stk) AS Stocks;
END
GO

-- 6) Dashboard charts -----------------------------------------------------------------------------------
-- pie: orders per customer of the current month; on the 1st day of a month that has not been imported yet,
--      the previous month is still shown
CREATE OR ALTER VIEW dbo.VW_Dashboard_PieChart AS
WITH mon AS (
    SELECT CASE
             WHEN EXISTS (SELECT 1 FROM dbo.TRN_IMPORT_STAGE WHERE TARGET_DATE >= DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
                                                               AND TARGET_DATE <  DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)))
                  OR DAY(GETDATE()) > 1
             THEN DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1)
             ELSE DATEADD(MONTH, -1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
           END AS M
),
rows_ AS (
    SELECT s.CUST_CODE, ISNULL(s.ORDER_QTY, 0) AS ORDER_QTY
    FROM dbo.TRN_IMPORT_STAGE s CROSS JOIN mon
    WHERE s.TARGET_DATE >= mon.M AND s.TARGET_DATE < DATEADD(MONTH, 1, mon.M)
)
SELECT CUST_CODE AS CustomerName,
       SUM(ORDER_QTY) AS OrderAmount,
       SUM(ORDER_QTY) * 100.0 / NULLIF((SELECT SUM(ORDER_QTY) FROM rows_), 0) AS Percentage
FROM rows_
GROUP BY CUST_CODE
HAVING SUM(ORDER_QTY) > 0;
GO

-- bar: Forecast / Order / Delivery per month of the current year
CREATE OR ALTER VIEW dbo.VW_Dashboard_BarChart AS
SELECT LEFT(DATENAME(MONTH, DATEFROMPARTS(YEAR(GETDATE()), MONTH(TARGET_DATE), 1)), 3) AS MonthName,
       MONTH(TARGET_DATE) AS MonthOrder,
       SUM(ISNULL(FORECAST_QTY, 0)) AS ForecastValue,
       SUM(ISNULL(ORDER_QTY, 0))    AS OrderValue,
       SUM(ISNULL(DELIVERY_QTY, 0)) AS DeliveryValue
FROM dbo.TRN_IMPORT_STAGE
WHERE YEAR(TARGET_DATE) = YEAR(GETDATE())
GROUP BY MONTH(TARGET_DATE);
GO

PRINT 'Max-Min calculator module updated.';
