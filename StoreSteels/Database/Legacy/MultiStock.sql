-- =====================================================================================================
-- Multi-Stock (หลายคลัง) - Run against the Stock DB (CIMS / GlobalConfig.ConnStr), NOT the ERP.
-- Safe to run more than once (every step checks before it creates/alters).
--
-- Design:
--   * Stock-CHR (IS_MAIN = 1) keeps using MST_PART.QTY_STKB / QTY_MAX / QTY_MIN / PT_REMARK exactly as
--     before, so Dashboard, Inventory Registration and every existing SP/View keep working untouched.
--   * Every other stock keeps its own balance per part in MST_PART_STOCK.
--   * MST_PART stays the single registry of steel items (PT_CODE / PT_QR) shared by all stocks.
--
-- Permissions (MST_PERM, Level 1 bypasses everything as before):
--   (current model: see section 16 - Level 1 = admin, everyone else only what is ticked)
--   SYS_ID 'STORE'      Store (Max-Min) page: VIEW / ADD = create stock + format / EDIT = settings + formats / DEL = delete stock
--   SYS_ID = the stock's own STK_CODE, main stock included (e.g. 'STOCK-MAT', 'STOCK-PANTA'):
--                       PERM_VIEW = sees the stock card/table (granted to EVERY user automatically)
--                       PERM_ADD  = Import Excel into this stock
--                       PERM_EDIT = edit Max/Min/Qty/Stock box/pcs in the table
--   Renaming a stock code renames its permission rows (done by the app in the same transaction).
-- =====================================================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- required for the filtered unique index below (sqlcmd defaults it OFF)

-- -----------------------------------------------------------------------------------------------------
-- 0) MST_PERM.SYS_ID varchar(10) -> varchar(30)  ('PackingCard' = 11 chars could never be stored)
-- -----------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.MST_PERM') AND name = 'SYS_ID' AND max_length < 30)
BEGIN
    BEGIN TRAN;
    ALTER TABLE dbo.MST_PERM DROP CONSTRAINT PK_PERM;
    ALTER TABLE dbo.MST_PERM ALTER COLUMN SYS_ID VARCHAR(30) NOT NULL;
    ALTER TABLE dbo.MST_PERM ADD CONSTRAINT PK_PERM PRIMARY KEY (USR_ID, SYS_ID);
    COMMIT;
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 1) MST_STOCK - list of stocks + per-stock display/scan settings
-- -----------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.MST_STOCK', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_STOCK (
        STK_ID          INT IDENTITY(1,1) PRIMARY KEY,
        STK_CODE        VARCHAR(30)     NOT NULL,
        STK_NAME        NVARCHAR(100)   NOT NULL,
        STK_UNIT        VARCHAR(10)     NOT NULL CONSTRAINT DF_STK_UNIT DEFAULT ('KG'),   -- KG / PCS
        IS_MAIN         BIT             NOT NULL CONSTRAINT DF_STK_MAIN DEFAULT (0),      -- Stock-CHR only
        USE_MAXMIN      BIT             NOT NULL CONSTRAINT DF_STK_MAXMIN DEFAULT (1),    -- MAX / MIN / STATUS columns

        -- Columns shown in the Store table
        COL_IMAGE       BIT NOT NULL CONSTRAINT DF_STK_CIMG  DEFAULT (1),
        COL_CODE        BIT NOT NULL CONSTRAINT DF_STK_CCODE DEFAULT (1),
        COL_NAME        BIT NOT NULL CONSTRAINT DF_STK_CNAME DEFAULT (1),
        COL_QTY         BIT NOT NULL CONSTRAINT DF_STK_CQTY  DEFAULT (1),
        COL_REMARK      BIT NOT NULL CONSTRAINT DF_STK_CRMK  DEFAULT (1),

        -- Receive (IN) channels
        IN_PICKLIST     BIT NOT NULL CONSTRAINT DF_STK_IPL  DEFAULT (0),   -- Pick List QR (printed from Pick List page)
        IN_SUPPLIER     BIT NOT NULL CONSTRAINT DF_STK_ISUP DEFAULT (0),   -- Supplier label (MST_STOCK_FMT formats)
        IN_SYSQR        BIT NOT NULL CONSTRAINT DF_STK_ISYS DEFAULT (0),   -- System QR (Inventory Registration)
        IN_EXCEL        BIT NOT NULL CONSTRAINT DF_STK_IXLS DEFAULT (0),   -- Import Excel
        -- Issue (OUT) channels
        OUT_PICKLIST    BIT NOT NULL CONSTRAINT DF_STK_OPL  DEFAULT (0),
        OUT_SUPPLIER    BIT NOT NULL CONSTRAINT DF_STK_OSUP DEFAULT (0),
        OUT_SYSQR       BIT NOT NULL CONSTRAINT DF_STK_OSYS DEFAULT (0),

        SORT_NO         INT             NOT NULL CONSTRAINT DF_STK_SORT DEFAULT (0),
        CREATED_BY      VARCHAR(20)     NULL,
        CREATED_DATE    DATETIME        NOT NULL CONSTRAINT DF_STK_CDATE DEFAULT (GETDATE()),
        UPDATED_BY      VARCHAR(20)     NULL,
        UPDATED_DATE    DATETIME        NULL,
        CONSTRAINT UQ_MST_STOCK_CODE UNIQUE (STK_CODE)
    );
END
GO

-- 1b) Extra display options per stock (Store Max-Min)
--     GROUP_BY      : CATEGORY / CUSTOMER  (group header of the table: "CATEGORY : x" or "CUSTOMER : x")
--     MAXMIN_BASIS  : KG / PCS / SHEET / BOX (unit shown on MAX/MIN headers; BOX compares with STOCK (BOX), others with QTY)
--                     old value UNIT = same as the stock unit
--     COL_NO / COL_MODEL : No. (running number inside each group) / MODEL column
--     OUT_EXCEL     : issue (OUT) by Import Excel
--     COL_*         : optional columns CUSTOMER / PART A / PART NO / STOCK (BOX) / STOCK (PCS)
IF COL_LENGTH('dbo.MST_STOCK', 'GROUP_BY') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD GROUP_BY VARCHAR(10) NOT NULL CONSTRAINT DF_STK_GROUPBY DEFAULT ('CATEGORY');
IF COL_LENGTH('dbo.MST_STOCK', 'MAXMIN_BASIS') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD MAXMIN_BASIS VARCHAR(5) NOT NULL CONSTRAINT DF_STK_MMBASIS DEFAULT ('UNIT');
IF COL_LENGTH('dbo.MST_STOCK', 'COL_CUSTOMER') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD COL_CUSTOMER BIT NOT NULL CONSTRAINT DF_STK_CCUST DEFAULT (0);
IF COL_LENGTH('dbo.MST_STOCK', 'COL_PARTA') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD COL_PARTA BIT NOT NULL CONSTRAINT DF_STK_CPARTA DEFAULT (0);
IF COL_LENGTH('dbo.MST_STOCK', 'COL_PARTNO') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD COL_PARTNO BIT NOT NULL CONSTRAINT DF_STK_CPARTNO DEFAULT (0);
IF COL_LENGTH('dbo.MST_STOCK', 'COL_STK_BOX') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD COL_STK_BOX BIT NOT NULL CONSTRAINT DF_STK_CBOX DEFAULT (0);
IF COL_LENGTH('dbo.MST_STOCK', 'COL_STK_PCS') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD COL_STK_PCS BIT NOT NULL CONSTRAINT DF_STK_CPCS DEFAULT (0);
IF COL_LENGTH('dbo.MST_STOCK', 'COL_NO') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD COL_NO BIT NOT NULL CONSTRAINT DF_STK_CNO DEFAULT (0);
IF COL_LENGTH('dbo.MST_STOCK', 'COL_MODEL') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD COL_MODEL BIT NOT NULL CONSTRAINT DF_STK_CMODEL DEFAULT (0);
IF COL_LENGTH('dbo.MST_STOCK', 'OUT_EXCEL') IS NULL
    ALTER TABLE dbo.MST_STOCK ADD OUT_EXCEL BIT NOT NULL CONSTRAINT DF_STK_OEXCEL DEFAULT (0);
GO

-- Extra part master fields (Inventory Registration): Customer / Part A / Part No
IF COL_LENGTH('dbo.MST_PART', 'PT_CUST') IS NULL
    ALTER TABLE dbo.MST_PART ADD PT_CUST NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.MST_PART', 'PT_PARTA') IS NULL
    ALTER TABLE dbo.MST_PART ADD PT_PARTA NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.MST_PART', 'PT_PARTNO') IS NULL
    ALTER TABLE dbo.MST_PART ADD PT_PARTNO NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.MST_PART', 'PT_MODEL') IS NULL
    ALTER TABLE dbo.MST_PART ADD PT_MODEL NVARCHAR(100) NULL;
GO

-- Only one main stock: the app never creates a main stock (only the seed below does), so no filtered
-- unique index here - a filtered index would force QUOTED_IDENTIFIER ON for every INSERT/DELETE on
-- MST_STOCK from any tool (sqlcmd defaults it OFF). Drop it if an earlier version of this script made it.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MST_STOCK_MAIN' AND object_id = OBJECT_ID('dbo.MST_STOCK'))
    DROP INDEX UX_MST_STOCK_MAIN ON dbo.MST_STOCK;
GO

-- -----------------------------------------------------------------------------------------------------
-- 2) MST_BARCODE_FMT - user-defined supplier label formats (e.g. Panta)
--    Positions are 1-based field numbers after splitting the scanned text by DELIMITER.
-- -----------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.MST_BARCODE_FMT', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_BARCODE_FMT (
        FMT_ID          INT IDENTITY(1,1) PRIMARY KEY,
        FMT_NAME        NVARCHAR(50)    NOT NULL,
        DELIMITER       NVARCHAR(5)     NOT NULL,
        CODE_POS        INT             NOT NULL,        -- field used to find the part (PT_QR / PT_CODE)
        ALT_CODE_POS    INT             NULL,            -- fallback field if CODE_POS is not registered
        QTY_POS         INT             NULL,            -- field holding the quantity (NULL = use Pack Size)
        MIN_FIELDS      INT             NOT NULL,        -- label must have at least this many fields
        SAMPLE_TEXT     NVARCHAR(500)   NULL,
        IS_ACTIVE       BIT             NOT NULL CONSTRAINT DF_FMT_ACTIVE DEFAULT (1),
        CREATED_BY      VARCHAR(20)     NULL,
        CREATED_DATE    DATETIME        NOT NULL CONSTRAINT DF_FMT_CDATE DEFAULT (GETDATE()),
        UPDATED_BY      VARCHAR(20)     NULL,
        UPDATED_DATE    DATETIME        NULL,
        CONSTRAINT UQ_MST_BARCODE_FMT_NAME UNIQUE (FMT_NAME)
    );
END
GO

-- Auto-deduct: when a label of this format is scanned IN to the main stock (Stock-CHR), the same
-- quantity is deducted from this source stock (e.g. Panta format -> Stock-PANTA). NULL = no deduction.
IF COL_LENGTH('dbo.MST_BARCODE_FMT', 'SOURCE_STK_ID') IS NULL
    ALTER TABLE dbo.MST_BARCODE_FMT ADD SOURCE_STK_ID INT NULL
        CONSTRAINT FK_FMT_SOURCE_STOCK FOREIGN KEY REFERENCES dbo.MST_STOCK (STK_ID) ON DELETE SET NULL;
GO

-- Label filters (Barcode Formats window) - for labels like StorePC:
--   |2A250-00017      0p37    26wo0106080062   zf02   00056   24.00      24.00     piece
--   DELIM_MODE   : CHAR = split by DELIMITER / SPACES = any amount of spaces or tabs counts as one separator
--   TRIM_CHARS   : characters removed from both ends of the whole label first (e.g. "|")
--   CODE_PREFIX  : NONE / DIGIT (drop a leading digit followed by a letter: 2A250-00017 -> A250-00017) / CUT (drop CODE_CUT chars)
--   MATCH_START / MATCH_END : the label must start / end with this text (case-insensitive) - tells label types apart
IF COL_LENGTH('dbo.MST_BARCODE_FMT', 'DELIM_MODE') IS NULL
    ALTER TABLE dbo.MST_BARCODE_FMT ADD DELIM_MODE VARCHAR(10) NOT NULL CONSTRAINT DF_FMT_DELIMMODE DEFAULT ('CHAR');
IF COL_LENGTH('dbo.MST_BARCODE_FMT', 'TRIM_CHARS') IS NULL
    ALTER TABLE dbo.MST_BARCODE_FMT ADD TRIM_CHARS NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.MST_BARCODE_FMT', 'CODE_PREFIX') IS NULL
    ALTER TABLE dbo.MST_BARCODE_FMT ADD CODE_PREFIX VARCHAR(10) NOT NULL CONSTRAINT DF_FMT_CODEPREFIX DEFAULT ('NONE');
IF COL_LENGTH('dbo.MST_BARCODE_FMT', 'CODE_CUT') IS NULL
    ALTER TABLE dbo.MST_BARCODE_FMT ADD CODE_CUT INT NULL;
IF COL_LENGTH('dbo.MST_BARCODE_FMT', 'MATCH_START') IS NULL
    ALTER TABLE dbo.MST_BARCODE_FMT ADD MATCH_START NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.MST_BARCODE_FMT', 'MATCH_END') IS NULL
    ALTER TABLE dbo.MST_BARCODE_FMT ADD MATCH_END NVARCHAR(30) NULL;
GO
-- Which supplier formats each stock accepts
IF OBJECT_ID('dbo.MST_STOCK_FMT', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_STOCK_FMT (
        STK_ID  INT NOT NULL CONSTRAINT FK_STKFMT_STOCK FOREIGN KEY REFERENCES dbo.MST_STOCK (STK_ID) ON DELETE CASCADE,
        FMT_ID  INT NOT NULL CONSTRAINT FK_STKFMT_FMT   FOREIGN KEY REFERENCES dbo.MST_BARCODE_FMT (FMT_ID) ON DELETE CASCADE,
        CONSTRAINT PK_MST_STOCK_FMT PRIMARY KEY (STK_ID, FMT_ID)
    );
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 3) MST_PART_STOCK - balance per part for every stock EXCEPT the main one (main = MST_PART)
-- -----------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.MST_PART_STOCK', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_PART_STOCK (
        STK_ID          INT             NOT NULL CONSTRAINT FK_PSTK_STOCK FOREIGN KEY REFERENCES dbo.MST_STOCK (STK_ID) ON DELETE CASCADE,
        PT_ID           INT             NOT NULL CONSTRAINT FK_PSTK_PART  FOREIGN KEY REFERENCES dbo.MST_PART (PT_ID),
        QTY             INT             NOT NULL CONSTRAINT DF_PSTK_QTY DEFAULT (0),
        QTY_MAX         INT             NULL,
        QTY_MIN         INT             NULL,
        REMARK          NVARCHAR(500)   NULL,
        UPDATED_DATE    DATETIME        NOT NULL CONSTRAINT DF_PSTK_UDATE DEFAULT (GETDATE()),
        CONSTRAINT PK_MST_PART_STOCK PRIMARY KEY (STK_ID, PT_ID),
        CONSTRAINT CK_PSTK_QTY CHECK (QTY >= 0)
    );
END
GO

-- STOCK (BOX) kept separately like StorePC: 1 scanned label = +1 / -1 box, QTY = pieces from the label.
-- Editing one of them in the Store table recalculates the other (trigger below). Starts as QTY / PACK SIZE.
IF COL_LENGTH('dbo.MST_PART_STOCK', 'QTY_BOX') IS NULL
BEGIN
    ALTER TABLE dbo.MST_PART_STOCK ADD QTY_BOX INT NOT NULL CONSTRAINT DF_PSTK_QTY_BOX DEFAULT (0);
    EXEC('UPDATE ps SET QTY_BOX = CASE WHEN ISNULL(p.PT_PSZ, 0) > 0 THEN ps.QTY / p.PT_PSZ ELSE 0 END
          FROM dbo.MST_PART_STOCK ps JOIN dbo.MST_PART p ON p.PT_ID = ps.PT_ID');
END
GO

-- SHOW / HIDE per stock (Inventory Registration import column SHOW/HIDE, button SHOW/HIDE with the stock filter)
--   0 = the part stays in the stock but is hidden from that stock's Store (Max-Min) table
IF COL_LENGTH('dbo.MST_PART_STOCK', 'IS_SHOW') IS NULL
    ALTER TABLE dbo.MST_PART_STOCK ADD IS_SHOW BIT NOT NULL CONSTRAINT DF_PSTK_IS_SHOW DEFAULT (1);
GO

--   only QTY changed (import, transfer, editing PCS)  -> QTY_BOX = QTY / PACK SIZE
--   only QTY_BOX changed (editing BOX)                 -> QTY = QTY_BOX x PACK SIZE
--   both changed in one statement (scan)               -> keep both
CREATE OR ALTER TRIGGER dbo.trg_MST_PART_STOCK_Box
ON dbo.MST_PART_STOCK
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRIGGER_NESTLEVEL(@@PROCID) > 1 RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    IF NOT EXISTS (SELECT 1 FROM deleted)
    BEGIN
        -- new row without a box count -> derive it from the pieces
        UPDATE ps SET QTY_BOX = CASE WHEN ISNULL(p.PT_PSZ, 0) > 0 THEN ps.QTY / p.PT_PSZ ELSE 0 END
        FROM dbo.MST_PART_STOCK ps
        JOIN inserted i ON i.STK_ID = ps.STK_ID AND i.PT_ID = ps.PT_ID
        JOIN dbo.MST_PART p ON p.PT_ID = ps.PT_ID
        WHERE i.QTY_BOX = 0 AND i.QTY > 0;
        RETURN;
    END

    IF UPDATE(QTY) AND NOT UPDATE(QTY_BOX)
        UPDATE ps SET QTY_BOX = CASE WHEN ISNULL(p.PT_PSZ, 0) > 0 THEN ps.QTY / p.PT_PSZ ELSE 0 END
        FROM dbo.MST_PART_STOCK ps
        JOIN inserted i ON i.STK_ID = ps.STK_ID AND i.PT_ID = ps.PT_ID
        JOIN deleted d  ON d.STK_ID = i.STK_ID AND d.PT_ID = i.PT_ID
        JOIN dbo.MST_PART p ON p.PT_ID = ps.PT_ID
        WHERE i.QTY <> d.QTY;
    ELSE IF UPDATE(QTY_BOX) AND NOT UPDATE(QTY)
        UPDATE ps SET QTY = ps.QTY_BOX * CASE WHEN ISNULL(p.PT_PSZ, 0) > 0 THEN p.PT_PSZ ELSE 1 END
        FROM dbo.MST_PART_STOCK ps
        JOIN inserted i ON i.STK_ID = ps.STK_ID AND i.PT_ID = ps.PT_ID
        JOIN deleted d  ON d.STK_ID = i.STK_ID AND d.PT_ID = i.PT_ID
        JOIN dbo.MST_PART p ON p.PT_ID = ps.PT_ID
        WHERE i.QTY_BOX <> d.QTY_BOX;
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 4) History tables
-- -----------------------------------------------------------------------------------------------------
-- Transfers are kept even after the source stock is deleted (they also explain Stock-CHR increases),
-- so stock codes are stored as text and there is no FK to MST_STOCK.
IF OBJECT_ID('dbo.TRN_TRANSFER', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRN_TRANSFER (
        TRF_ID          INT IDENTITY(1,1) PRIMARY KEY,
        FROM_STK_ID     INT             NOT NULL,
        FROM_STK_CODE   VARCHAR(30)     NOT NULL,
        TO_STK_ID       INT             NOT NULL,
        TO_STK_CODE     VARCHAR(30)     NOT NULL,
        PT_ID           INT             NOT NULL,
        PT_CODE         VARCHAR(50)     NOT NULL,
        QTY             INT             NOT NULL,
        TRF_MODE        VARCHAR(10)     NOT NULL,        -- ALL / PARTIAL
        FROM_BAL_AFTER  INT             NOT NULL,
        TO_BAL_AFTER    INT             NOT NULL,
        USR_ID          VARCHAR(20)     NOT NULL,
        TRF_DATE        DATETIME        NOT NULL CONSTRAINT DF_TRF_DATE DEFAULT (GETDATE())
    );
    CREATE INDEX IX_TRN_TRANSFER_DATE ON dbo.TRN_TRANSFER (TRF_DATE);
END
GO

IF OBJECT_ID('dbo.TRN_STOCK_IMPORT', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRN_STOCK_IMPORT (
        IMP_ID          INT IDENTITY(1,1) PRIMARY KEY,
        BATCH_ID        UNIQUEIDENTIFIER NOT NULL,
        STK_ID          INT             NOT NULL CONSTRAINT FK_IMP_STOCK FOREIGN KEY REFERENCES dbo.MST_STOCK (STK_ID) ON DELETE CASCADE,
        PT_ID           INT             NOT NULL,
        PT_CODE         VARCHAR(50)     NOT NULL,
        QTY             INT             NOT NULL,
        BAL_AFTER       INT             NOT NULL,
        FILE_NAME       NVARCHAR(260)   NULL,
        USR_ID          VARCHAR(20)     NOT NULL,
        IMP_DATE        DATETIME        NOT NULL CONSTRAINT DF_IMP_DATE DEFAULT (GETDATE())
    );
END
GO

-- Evidence kept for every deleted stock (reason is mandatory)
IF OBJECT_ID('dbo.MST_STOCK_DEL_LOG', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_STOCK_DEL_LOG (
        DEL_ID          INT IDENTITY(1,1) PRIMARY KEY,
        STK_ID          INT             NOT NULL,
        STK_CODE        VARCHAR(30)     NOT NULL,
        STK_NAME        NVARCHAR(100)   NOT NULL,
        ITEM_COUNT      INT             NOT NULL,
        TOTAL_QTY       INT             NOT NULL,
        REASON          NVARCHAR(500)   NOT NULL,
        USR_ID          VARCHAR(20)     NOT NULL,
        DEL_DATE        DATETIME        NOT NULL CONSTRAINT DF_STKDEL_DATE DEFAULT (GETDATE())
    );
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 5) TRN_SCAN.STK_ID - which stock a scan belongs to (NULL on old rows = Stock-CHR)
-- -----------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.TRN_SCAN', 'STK_ID') IS NULL
    ALTER TABLE dbo.TRN_SCAN ADD STK_ID INT NULL;
GO

-- -----------------------------------------------------------------------------------------------------
-- 6) Seed: Stock-CHR (main) + Panta supplier format
-- -----------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.MST_STOCK WHERE IS_MAIN = 1)
BEGIN
    -- Stock-CHR keeps every channel it has today (Pick List QR, supplier label, system QR - IN and OUT)
    INSERT INTO dbo.MST_STOCK (STK_CODE, STK_NAME, STK_UNIT, IS_MAIN, USE_MAXMIN,
                               COL_IMAGE, COL_CODE, COL_NAME, COL_QTY, COL_REMARK,
                               IN_PICKLIST, IN_SUPPLIER, IN_SYSQR, IN_EXCEL,
                               OUT_PICKLIST, OUT_SUPPLIER, OUT_SYSQR, SORT_NO, CREATED_BY)
    VALUES ('Stock-CHR', N'CHR Main Store', 'KG', 1, 1,
            1, 1, 1, 1, 1,
            1, 1, 1, 0,
            1, 1, 1, 0, 'SYSTEM');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.MST_BARCODE_FMT WHERE FMT_NAME = N'Panta')
BEGIN
    -- DCAE9031B-006;CHR; 26071606; -; DCAE9031B-006;SGACE 45/45; 1.000 X 175.00 X COIL; 1; 613.00; 21/08/2026; DCAE9031B;
    --  field 11 = item code, field 1 = coil no. (fallback), field 9 = quantity
    INSERT INTO dbo.MST_BARCODE_FMT (FMT_NAME, DELIMITER, CODE_POS, ALT_CODE_POS, QTY_POS, MIN_FIELDS, SAMPLE_TEXT, CREATED_BY)
    VALUES (N'Panta', N';', 11, 1, 9, 11,
            N'DCAE9031B-006;CHR; 26071606; -; DCAE9031B-006;SGACE 45/45; 1.000 X 175.00 X COIL; 1; 613.00; 21/08/2026; DCAE9031B;',
            'SYSTEM');
END
GO

INSERT INTO dbo.MST_STOCK_FMT (STK_ID, FMT_ID)
SELECT s.STK_ID, f.FMT_ID
FROM dbo.MST_STOCK s CROSS JOIN dbo.MST_BARCODE_FMT f
WHERE s.IS_MAIN = 1 AND f.FMT_NAME = N'Panta'
  AND NOT EXISTS (SELECT 1 FROM dbo.MST_STOCK_FMT x WHERE x.STK_ID = s.STK_ID AND x.FMT_ID = f.FMT_ID);
GO

-- Old scans belong to Stock-CHR
UPDATE t SET STK_ID = s.STK_ID
FROM dbo.TRN_SCAN t CROSS JOIN dbo.MST_STOCK s
WHERE s.IS_MAIN = 1 AND t.STK_ID IS NULL;
GO

-- -----------------------------------------------------------------------------------------------------
-- 7) VW_StockMonitoring - VW_StoreMonitoring columns + StkId + Customer/PartA/PartNo/StockBox/StockPcs/GroupKey
--    (VW_StoreMonitoring itself is left untouched for the Dashboard)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.VW_StockMonitoring AS
WITH base AS (
    -- main stock: balance / max / min / remark live in MST_PART
    SELECT s.STK_ID, s.USE_MAXMIN, s.MAXMIN_BASIS, s.GROUP_BY, p.PT_ID,
           p.QTY_MAX AS QMAX, p.QTY_MIN AS QMIN, ISNULL(p.QTY_STKB, 0) AS QTY, p.PT_REMARK AS RMK, CAST(NULL AS INT) AS BOXQ
    FROM dbo.MST_STOCK s
    JOIN dbo.MST_PART p ON s.IS_MAIN = 1
    WHERE p.IS_ACTIVE = 1 AND p.IS_SHOW_MST = 1

    UNION ALL

    -- other stocks: balance per part in MST_PART_STOCK
    SELECT s.STK_ID, s.USE_MAXMIN, s.MAXMIN_BASIS, s.GROUP_BY, p.PT_ID,
           ps.QTY_MAX, ps.QTY_MIN, ps.QTY, ps.REMARK, ps.QTY_BOX
    FROM dbo.MST_PART_STOCK ps
    JOIN dbo.MST_STOCK s ON s.STK_ID = ps.STK_ID AND s.IS_MAIN = 0
    JOIN dbo.MST_PART p  ON p.PT_ID  = ps.PT_ID
    WHERE p.IS_ACTIVE = 1 AND ps.IS_SHOW = 1
),
calc AS (
    SELECT b.*,
           -- STOCK (BOX): other stocks keep their own box count (scan = +1 / -1 box), main stock = balance / pack size
           CASE WHEN b.BOXQ IS NOT NULL THEN b.BOXQ WHEN ISNULL(p.PT_PSZ, 0) > 0 THEN b.QTY / p.PT_PSZ ELSE 0 END AS QTY_BOX
    FROM base b JOIN dbo.MST_PART p ON p.PT_ID = b.PT_ID
)
SELECT
    c.STK_ID        AS StkId,
    p.PT_ID,
    p.PT_CAT        AS Category,
    p.PT_SUPPLIER   AS Supplier,
    p.PT_IMG        AS ImageFileName,
    p.PT_CODE       AS PartCode,
    p.PT_DESC       AS PartName,
    p.PT_PSZ        AS PackSize,
    c.QMAX          AS [Max],
    c.QMIN          AS [Min],
    c.QTY           AS QtyStkb,
    c.RMK           AS Remark,
    p.LIT_STAT      AS Priority,
    p.PT_BIN        AS Bin,
    p.PT_CUST       AS Customer,
    p.PT_PARTA      AS PartA,
    p.PT_PARTNO     AS PartNo,
    p.PT_MODEL      AS Model,
    c.QTY_BOX       AS StockBox,
    c.QTY           AS StockPcs,
    -- group header of the Store table: Category (default) or Customer
    CASE WHEN c.GROUP_BY = 'CUSTOMER' THEN ISNULL(NULLIF(p.PT_CUST, ''), '-') ELSE ISNULL(NULLIF(p.PT_CAT, ''), '-') END AS GroupKey,
    CASE
        WHEN c.USE_MAXMIN = 0 THEN 'NORMAL'      -- stock without Max/Min: plain rows (not the grey NO_CONFIG look)
        WHEN ISNULL(c.QMAX, 0) = 0 AND ISNULL(c.QMIN, 0) = 0 THEN 'NO_CONFIG'
        -- MAX / MIN compared with QTY (stock unit) or with STOCK (BOX)
        WHEN (CASE WHEN c.MAXMIN_BASIS = 'BOX' THEN c.QTY_BOX ELSE c.QTY END) < ISNULL(c.QMIN, 0) THEN 'UNDER_MIN'
        WHEN (CASE WHEN c.MAXMIN_BASIS = 'BOX' THEN c.QTY_BOX ELSE c.QTY END) > ISNULL(c.QMAX, 0) AND ISNULL(c.QMAX, 0) > 0 THEN 'OVER_MAX'
        WHEN (CASE WHEN c.MAXMIN_BASIS = 'BOX' THEN c.QTY_BOX ELSE c.QTY END) BETWEEN ISNULL(c.QMIN, 0) AND ISNULL(c.QMAX, 0) THEN 'NORMAL_GOOD'
        ELSE 'NORMAL'
    END AS StockStatus
FROM calc c
JOIN dbo.MST_PART p ON p.PT_ID = c.PT_ID;
GO

-- Card summary: number of items (not quantity) per stock
CREATE OR ALTER VIEW dbo.VW_StockSummary AS
SELECT
    s.STK_ID, s.STK_CODE, s.STK_NAME, s.STK_UNIT, s.IS_MAIN, s.SORT_NO,
    ISNULL(c.ItemCount, 0) AS ItemCount
FROM dbo.MST_STOCK s
LEFT JOIN (SELECT StkId, COUNT(*) AS ItemCount FROM dbo.VW_StockMonitoring GROUP BY StkId) c
       ON c.StkId = s.STK_ID;
GO

-- -----------------------------------------------------------------------------------------------------
-- 8) Stored procedures
-- -----------------------------------------------------------------------------------------------------

-- Give every user VIEW on one stock (called right after a stock is created)
CREATE OR ALTER PROCEDURE dbo.sp_Stock_GrantViewAll
    @StkId INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SysId VARCHAR(30) = (SELECT STK_CODE FROM dbo.MST_STOCK WHERE STK_ID = @StkId AND IS_MAIN = 0);
    IF @SysId IS NULL RETURN;

    INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
    SELECT u.USR_ID, @SysId, 'Y', 'N', 'N', 'N', 'N'
    FROM dbo.MST_USER u
    WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID = @SysId);

    -- user already had a row (e.g. re-created stock id) -> make sure VIEW is on
    UPDATE dbo.MST_PERM SET PERM_VIEW = 'Y' WHERE SYS_ID = @SysId AND ISNULL(PERM_VIEW, 'N') <> 'Y';
    -- ADD / EDIT in a new stock are granted by an admin per person (section 16 permission model)
END
GO

-- Permission rows created by an earlier version of this script used SYS_ID 'STK_<id>'
-- -> rename them to the stock code (skip a row if that user already has a row under the code)
UPDATE p SET SYS_ID = s.STK_CODE
FROM dbo.MST_PERM p
JOIN dbo.MST_STOCK s ON p.SYS_ID = 'STK_' + CAST(s.STK_ID AS VARCHAR(10)) AND s.IS_MAIN = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_PERM x WHERE x.USR_ID = p.USR_ID AND x.SYS_ID = s.STK_CODE);

DELETE p FROM dbo.MST_PERM p
JOIN dbo.MST_STOCK s ON p.SYS_ID = 'STK_' + CAST(s.STK_ID AS VARCHAR(10)) AND s.IS_MAIN = 0;
GO

-- Add quantity to a non-main stock (creates the part row on first receive). Returns the new balance.
-- Used by Import Excel and by Multi-Scanner IN for non-main stocks. Runs inside the caller's transaction.
CREATE OR ALTER PROCEDURE dbo.sp_Stock_AddQty
    @StkId  INT,
    @PtId   INT,
    @Qty    INT,
    @NewBal INT OUTPUT,
    @Box    INT = NULL      -- scan: +1 box per label (NULL = box count follows QTY / PACK SIZE)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.MST_STOCK WHERE STK_ID = @StkId AND IS_MAIN = 1)
    BEGIN
        UPDATE dbo.MST_PART SET QTY_STKB = ISNULL(QTY_STKB, 0) + @Qty WHERE PT_ID = @PtId;
        SELECT @NewBal = ISNULL(QTY_STKB, 0) FROM dbo.MST_PART WHERE PT_ID = @PtId;
        RETURN;
    END

    IF @Box IS NULL
        UPDATE dbo.MST_PART_STOCK WITH (UPDLOCK, HOLDLOCK)
           SET QTY = QTY + @Qty, UPDATED_DATE = GETDATE()
         WHERE STK_ID = @StkId AND PT_ID = @PtId;
    ELSE
        UPDATE dbo.MST_PART_STOCK WITH (UPDLOCK, HOLDLOCK)
           SET QTY = QTY + @Qty, QTY_BOX = CASE WHEN QTY_BOX + @Box < 0 THEN 0 ELSE QTY_BOX + @Box END, UPDATED_DATE = GETDATE()
         WHERE STK_ID = @StkId AND PT_ID = @PtId;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.MST_PART_STOCK (STK_ID, PT_ID, QTY, QTY_BOX) VALUES (@StkId, @PtId, @Qty, CASE WHEN ISNULL(@Box, 0) > 0 THEN @Box ELSE 0 END);

    SELECT @NewBal = QTY FROM dbo.MST_PART_STOCK WHERE STK_ID = @StkId AND PT_ID = @PtId;
END
GO

-- Transfer from a non-main stock into Stock-CHR.  @Qty NULL = transfer everything.
-- Result set: TRF_ID, QTY, FROM_BAL_AFTER, TO_BAL_AFTER.  Errors are raised with Thai messages.
CREATE OR ALTER PROCEDURE dbo.sp_Stock_Transfer
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

    SELECT @FromCode = STK_CODE FROM dbo.MST_STOCK WHERE STK_ID = @FromStkId AND IS_MAIN = 0;
    SELECT @ToId = STK_ID, @ToCode = STK_CODE FROM dbo.MST_STOCK WHERE IS_MAIN = 1;
    SELECT @PtCode = PT_CODE FROM dbo.MST_PART WHERE PT_ID = @PtId;

    IF @FromCode IS NULL THROW 50001, N'ไม่พบคลังต้นทาง หรือคลังต้นทางเป็นคลังหลัก', 1;
    IF @ToId IS NULL     THROW 50002, N'ไม่พบคลังหลัก (Stock-CHR)', 1;
    IF @PtCode IS NULL   THROW 50003, N'ไม่พบข้อมูลสินค้าในระบบ', 1;

    BEGIN TRAN;

    SELECT @Bal = QTY FROM dbo.MST_PART_STOCK WITH (UPDLOCK, HOLDLOCK)
    WHERE STK_ID = @FromStkId AND PT_ID = @PtId;

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

    UPDATE dbo.MST_PART_STOCK SET QTY = QTY - @Qty, UPDATED_DATE = GETDATE()
    WHERE STK_ID = @FromStkId AND PT_ID = @PtId;

    UPDATE dbo.MST_PART SET QTY_STKB = ISNULL(QTY_STKB, 0) + @Qty WHERE PT_ID = @PtId;

    SELECT @FromAfter = QTY FROM dbo.MST_PART_STOCK WHERE STK_ID = @FromStkId AND PT_ID = @PtId;
    SELECT @ToAfter = ISNULL(QTY_STKB, 0) FROM dbo.MST_PART WHERE PT_ID = @PtId;

    INSERT INTO dbo.TRN_TRANSFER (FROM_STK_ID, FROM_STK_CODE, TO_STK_ID, TO_STK_CODE, PT_ID, PT_CODE,
                                  QTY, TRF_MODE, FROM_BAL_AFTER, TO_BAL_AFTER, USR_ID)
    VALUES (@FromStkId, @FromCode, @ToId, @ToCode, @PtId, @PtCode, @Qty, @Mode, @FromAfter, @ToAfter, @UserId);

    DECLARE @TrfId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.SYS_LOGS (USR_ID, ACT_TYPE, LOG_DESC, LOG_REF, LOG_DATE)
    VALUES (@UserId, 'STOCK_TRANSFER',
            CONCAT('[', @Mode, '] ', @FromCode, ' -> ', @ToCode, ' | PD: ', @PtCode, ' | QTY: ', @Qty,
                   ' | BAL ', @FromCode, ': ', @FromAfter, ' | BAL ', @ToCode, ': ', @ToAfter),
            @PtCode, GETDATE());

    COMMIT;

    SELECT @TrfId AS TRF_ID, @Qty AS QTY, @Mode AS TRF_MODE, @FromAfter AS FROM_BAL_AFTER, @ToAfter AS TO_BAL_AFTER;
END
GO

-- Delete a stock and everything that belongs to it. Reason is mandatory and kept in MST_STOCK_DEL_LOG.
-- Kept: TRN_TRANSFER history (it also explains Stock-CHR increases) and SYS_LOGS.
CREATE OR ALTER PROCEDURE dbo.sp_Stock_Delete
    @StkId  INT,
    @Reason NVARCHAR(500),
    @UserId VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Code VARCHAR(30), @Name NVARCHAR(100), @IsMain BIT, @Items INT, @Total INT;
    SELECT @Code = STK_CODE, @Name = STK_NAME, @IsMain = IS_MAIN FROM dbo.MST_STOCK WHERE STK_ID = @StkId;

    IF @Code IS NULL THROW 50011, N'ไม่พบคลังที่ต้องการลบ', 1;
    IF @IsMain = 1   THROW 50012, N'ไม่สามารถลบคลังหลัก (Stock-CHR) ได้', 1;
    IF LEN(LTRIM(RTRIM(ISNULL(@Reason, N'')))) = 0 THROW 50013, N'กรุณาระบุเหตุผลในการลบคลัง', 1;

    SELECT @Items = COUNT(*), @Total = ISNULL(SUM(QTY), 0) FROM dbo.MST_PART_STOCK WHERE STK_ID = @StkId;

    BEGIN TRAN;

    INSERT INTO dbo.MST_STOCK_DEL_LOG (STK_ID, STK_CODE, STK_NAME, ITEM_COUNT, TOTAL_QTY, REASON, USR_ID)
    VALUES (@StkId, @Code, @Name, @Items, @Total, LTRIM(RTRIM(@Reason)), @UserId);

    DELETE FROM dbo.MST_PERM WHERE SYS_ID = @Code;
    DELETE FROM dbo.TRN_SCAN WHERE STK_ID = @StkId;
    DELETE FROM dbo.MST_STOCK WHERE STK_ID = @StkId;   -- cascades MST_PART_STOCK, MST_STOCK_FMT, TRN_STOCK_IMPORT

    INSERT INTO dbo.SYS_LOGS (USR_ID, ACT_TYPE, LOG_DESC, LOG_REF, LOG_DATE)
    VALUES (@UserId, 'STOCK_DELETE',
            CONCAT(N'Deleted stock ', @Code, N' (', @Name, N') | Items: ', @Items, N' | Total qty: ', @Total,
                   N' | Reason: ', LTRIM(RTRIM(@Reason))),
            @Code, GETDATE());

    COMMIT;
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 9) Trigger: a newly created user automatically gets VIEW on the Store (Max-Min) page (SYS_ID STORE)
--    and VIEW on every stock, main stock included (SYS_ID = stock code)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER TRIGGER dbo.trg_MST_USER_GrantStockView
ON dbo.MST_USER
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
    SELECT i.USR_ID, s.STK_CODE, 'Y', 'N', 'N', 'N', 'N'
    FROM inserted i
    CROSS JOIN dbo.MST_STOCK s
    WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_PERM p
                      WHERE p.USR_ID = i.USR_ID AND p.SYS_ID = s.STK_CODE);

    INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
    SELECT i.USR_ID, 'STORE', 'Y', 'N', 'N', 'N', 'N'
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = i.USR_ID AND p.SYS_ID = 'STORE');
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 10) Inventory Registration list: + Customer / Part A / Part No (also searchable)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.v_PartQRReady AS
SELECT
    PT_ID,
    PT_CAT,
    PT_SUPPLIER,
    PT_CODE,
    PT_DESC,
    PT_PSZ,
    ISNULL(PT_QR, '-') AS PT_QR_DISPLAY,
    'CAT:'   + ISNULL(PT_CAT, '-') +
    ' | CD:' + ISNULL(PT_CODE, '-') +
    ' | NM:' + ISNULL(PT_DESC, '-') +
    ' | Q:'  + CAST(ISNULL(PT_PSZ, 0) AS VARCHAR) +
    ' | BC:' + ISNULL(PT_QR, '-') AS PT_QR_FULL,
    QTY_STKB,
    QTY_MAX,
    QTY_MIN,
    PT_BIN,
    IS_ACTIVE,
    IS_SHOW_MST,
    PT_IMG,
    PT_CUST,
    PT_PARTA,
    PT_PARTNO,
    PT_MODEL
FROM dbo.MST_PART
WHERE IS_ACTIVE = 1;
GO

-- @StkId: show only the parts in that stock (main = IS_SHOW_MST / other = row in MST_PART_STOCK), NULL = all
CREATE OR ALTER PROCEDURE dbo.sp_GetPartForQR
    @SearchText NVARCHAR(100) = '',
    @StkId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @SearchText = ISNULL(TRIM(@SearchText), '');
    DECLARE @isMain BIT = ISNULL((SELECT IS_MAIN FROM dbo.MST_STOCK WHERE STK_ID = @StkId), 0);

    -- IS_SHOW_VIEW: SHOW/HIDE of the filtered stock (other stock = MST_PART_STOCK.IS_SHOW / main or all = IS_SHOW_MST)
    SELECT v.*,
           CASE WHEN @StkId IS NOT NULL AND @isMain = 0
                THEN ISNULL((SELECT ps.IS_SHOW FROM dbo.MST_PART_STOCK ps WHERE ps.STK_ID = @StkId AND ps.PT_ID = v.PT_ID), 1)
                ELSE v.IS_SHOW_MST END AS IS_SHOW_VIEW
    FROM dbo.v_PartQRReady v
    WHERE
        (@StkId IS NULL
         OR (@isMain = 1 AND v.IS_SHOW_MST = 1)
         OR (@isMain = 0 AND EXISTS (SELECT 1 FROM dbo.MST_PART_STOCK ps WHERE ps.STK_ID = @StkId AND ps.PT_ID = v.PT_ID)))
    AND (
        @SearchText = ''
        OR (
            ISNULL(PT_CODE, '')        LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_DESC, '')     LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_SUPPLIER, '') LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_BIN, '')      LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_CAT, '')      LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_CUST, '')     LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_PARTA, '')    LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_PARTNO, '')   LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_MODEL, '')    LIKE '%' + @SearchText + '%'
        )
    )
    ORDER BY PT_CAT ASC, PT_CODE ASC;
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 11) Scan lookup: also by PART A / PART NO (like StorePC, so supplier labels that carry the Part A work)
-- -----------------------------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GetPartByScan
    @BarcodeInput NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CleanInput NVARCHAR(200) = LTRIM(RTRIM(@BarcodeInput));

    SELECT TOP 1 PT_ID, PT_CODE, PT_DESC, QTY_STKB, PT_PSZ, PT_CAT, IS_ACTIVE, PT_IMG
    FROM dbo.MST_PART WITH (NOLOCK)
    WHERE IS_ACTIVE = 1
      AND (   LTRIM(RTRIM(PT_CODE)) = @CleanInput
           OR LTRIM(RTRIM(PT_QR)) = @CleanInput
           OR LTRIM(RTRIM(ISNULL(PT_PARTA, ''))) = @CleanInput
           OR LTRIM(RTRIM(ISNULL(PT_PARTNO, ''))) = @CleanInput
           OR LTRIM(RTRIM(PT_DESC)) = @CleanInput)
    -- PD CODE first, then QR CODE, PART A, PART NO, name
    ORDER BY CASE WHEN LTRIM(RTRIM(PT_CODE)) = @CleanInput THEN 0
                  WHEN LTRIM(RTRIM(PT_QR)) = @CleanInput THEN 1
                  WHEN LTRIM(RTRIM(ISNULL(PT_PARTA, ''))) = @CleanInput THEN 2
                  WHEN LTRIM(RTRIM(ISNULL(PT_PARTNO, ''))) = @CleanInput THEN 3 ELSE 4 END, PT_ID;
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 12) Performance / no blocking: many scanners + Store screens refreshing every few seconds
--     - READ COMMITTED SNAPSHOT: screens that read never wait for (or block) a scan that is writing
--     - indexes for history / log / realtime queries
--     - 3-month clean-up triggers: delete only when something is actually old (index seek, small batches)
-- -----------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_read_committed_snapshot_on = 0)
BEGIN
    DECLARE @rcsi NVARCHAR(300) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK AFTER 10 SECONDS';
    EXEC (@rcsi);
END
GO
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND snapshot_isolation_state = 0)
BEGIN
    DECLARE @si NVARCHAR(300) = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET ALLOW_SNAPSHOT_ISOLATION ON';
    EXEC (@si);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRN_SCAN_TX_DATE' AND object_id = OBJECT_ID('dbo.TRN_SCAN'))
    CREATE INDEX IX_TRN_SCAN_TX_DATE ON dbo.TRN_SCAN (TX_DATE) INCLUDE (PT_ID, STK_ID, TX_TYPE, TX_QTY, USR_ID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRN_SCAN_PT' AND object_id = OBJECT_ID('dbo.TRN_SCAN'))
    CREATE INDEX IX_TRN_SCAN_PT ON dbo.TRN_SCAN (PT_ID, TX_DATE);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SYS_LOGS_DATE' AND object_id = OBJECT_ID('dbo.SYS_LOGS'))
    CREATE INDEX IX_SYS_LOGS_DATE ON dbo.SYS_LOGS (LOG_DATE) INCLUDE (USR_ID, ACT_TYPE);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MST_PART_STOCK_PT' AND object_id = OBJECT_ID('dbo.MST_PART_STOCK'))
    CREATE INDEX IX_MST_PART_STOCK_PT ON dbo.MST_PART_STOCK (PT_ID) INCLUDE (QTY, QTY_BOX);
GO

IF OBJECT_ID('dbo.trg_ScanLog_Rolling3Months', 'TR') IS NOT NULL
EXEC('ALTER TRIGGER dbo.trg_ScanLog_Rolling3Months ON dbo.TRN_SCAN AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut DATETIME = DATEADD(MONTH, -3, GETDATE());
    IF EXISTS (SELECT 1 FROM dbo.TRN_SCAN WHERE TX_DATE < @cut)
        DELETE TOP (500) FROM dbo.TRN_SCAN WHERE TX_DATE < @cut;
END');
GO
IF OBJECT_ID('dbo.trg_SysLogs_Rolling3Months', 'TR') IS NOT NULL
EXEC('ALTER TRIGGER dbo.trg_SysLogs_Rolling3Months ON dbo.SYS_LOGS AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cut DATETIME = DATEADD(MONTH, -3, GETDATE());
    IF EXISTS (SELECT 1 FROM dbo.SYS_LOGS WHERE LOG_DATE < @cut)
        DELETE TOP (500) FROM dbo.SYS_LOGS WHERE LOG_DATE < @cut;
END');
GO

-- -----------------------------------------------------------------------------------------------------
-- 13) ACTIVITY LOG: which computer the action came from + permission row for the ACTIVITY LOG page
-- -----------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.SYS_LOGS', 'LOG_PC') IS NULL
    ALTER TABLE dbo.SYS_LOGS ADD LOG_PC NVARCHAR(100) NULL;
GO

-- managers (STOCK_MGR) can open ACTIVITY LOG; others get the row switched off (turn on in user permissions)
INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
SELECT u.USR_ID, 'ActivityLog',
       CASE WHEN EXISTS (SELECT 1 FROM dbo.MST_PERM m WHERE m.USR_ID = u.USR_ID AND m.SYS_ID = 'STOCK_MGR' AND m.PERM_VIEW = 'Y') THEN 'Y' ELSE 'N' END,
       'N', 'N', 'N', 'N'
FROM dbo.MST_USER u
WHERE NOT EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID = 'ActivityLog');
GO

-- -----------------------------------------------------------------------------------------------------
-- 14) PRODUCT CODE may repeat when PART A differs (e.g. STORE-PC: code 818 = A960-00079 and A960-00032)
--     one product = PRODUCT CODE + PART A (the app identifies rows by PT_ID)
-- -----------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.MST_PART') AND name = 'UQ_MST_PART_PT_CODE')
    ALTER TABLE dbo.MST_PART DROP CONSTRAINT UQ_MST_PART_PT_CODE;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.MST_PART') AND name = 'UX_MST_PART_CODE_PARTA')
    CREATE UNIQUE INDEX UX_MST_PART_CODE_PARTA ON dbo.MST_PART (PT_CODE, PT_PARTA);
GO

-- -----------------------------------------------------------------------------------------------------
-- 15) User profile: + แผนก (SECTION) / ฝ่าย (DIVISION)  (ตำแหน่ง = USR_POS, หน่วยงาน = USR_DEPT มีอยู่แล้ว)
-- -----------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.MST_USER', 'USR_SECTION') IS NULL
    ALTER TABLE dbo.MST_USER ADD USR_SECTION NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.MST_USER', 'USR_DIVISION') IS NULL
    ALTER TABLE dbo.MST_USER ADD USR_DIVISION NVARCHAR(100) NULL;
GO

-- -----------------------------------------------------------------------------------------------------
-- 16) Permission model (Oct 2026): Level 1 = admin (everything), Level 2 / 3 = only the ticked rights
--     STORE      : Store (Max-Min) page  VIEW / ADD (stock + barcode format) / EDIT (settings + formats) / DEL (stock)
--     <stock code>: every stock incl. the main stock  VIEW / ADD (import excel) / EDIT (Max / Min / Qty / Box / Pcs)
--     SCANNER    : Multi-Scanner  VIEW / ADD (scan in + return) / EDIT (scan out)
--     Old rows migrated: STK -> main stock code, STOCK_MGR -> STORE, SCAN_IN / SCAN_OUT -> SCANNER
-- -----------------------------------------------------------------------------------------------------
DECLARE @Main VARCHAR(30) = (SELECT TOP 1 STK_CODE FROM dbo.MST_STOCK WHERE IS_MAIN = 1);

-- a) STK -> main stock code (merge the ticks when the user already has a row under the code)
IF @Main IS NOT NULL
BEGIN
    UPDATE m SET PERM_VIEW = CASE WHEN m.PERM_VIEW = 'Y' OR k.PERM_VIEW = 'Y' THEN 'Y' ELSE 'N' END,
                 PERM_ADD  = CASE WHEN m.PERM_ADD  = 'Y' OR k.PERM_ADD  = 'Y' THEN 'Y' ELSE 'N' END,
                 PERM_EDIT = CASE WHEN m.PERM_EDIT = 'Y' OR k.PERM_EDIT = 'Y' THEN 'Y' ELSE 'N' END
    FROM dbo.MST_PERM m
    JOIN dbo.MST_PERM k ON k.USR_ID = m.USR_ID AND k.SYS_ID = 'STK'
    WHERE m.SYS_ID = @Main;

    UPDATE k SET SYS_ID = @Main, PERM_DEL = 'N', PERM_APP = 'N'
    FROM dbo.MST_PERM k
    WHERE k.SYS_ID = 'STK'
      AND NOT EXISTS (SELECT 1 FROM dbo.MST_PERM m WHERE m.USR_ID = k.USR_ID AND m.SYS_ID = @Main);

    -- scanner staff used to scan the main stock without a stock row -> keep that working
    INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
    SELECT DISTINCT s.USR_ID, @Main, 'Y', 'N', 'N', 'N', 'N'
    FROM dbo.MST_PERM s
    WHERE s.SYS_ID IN ('SCAN_IN', 'SCAN_OUT') AND s.PERM_VIEW = 'Y'
      AND NOT EXISTS (SELECT 1 FROM dbo.MST_PERM m WHERE m.USR_ID = s.USR_ID AND m.SYS_ID = @Main);
    UPDATE m SET PERM_VIEW = 'Y'
    FROM dbo.MST_PERM m
    WHERE m.SYS_ID = @Main
      AND EXISTS (SELECT 1 FROM dbo.MST_PERM s WHERE s.USR_ID = m.USR_ID AND s.SYS_ID IN ('SCAN_IN', 'SCAN_OUT') AND s.PERM_VIEW = 'Y');
END

-- b) STORE: the Store menu used to open for Level 1 / 2 (and the main stock showed for them)
--    -> VIEW for those users (only while old rows still exist = first run), ADD / EDIT / DEL from STOCK_MGR
IF EXISTS (SELECT 1 FROM dbo.MST_PERM WHERE SYS_ID IN ('STK', 'STOCK_MGR', 'SCAN_IN', 'SCAN_OUT'))
BEGIN
    INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
    SELECT u.USR_ID, 'STORE',
           CASE WHEN u.USR_LVL <= 2 OR g.PERM_VIEW = 'Y' THEN 'Y' ELSE 'N' END,
           ISNULL(g.PERM_ADD, 'N'), ISNULL(g.PERM_EDIT, 'N'), ISNULL(g.PERM_DEL, 'N'), 'N'
    FROM dbo.MST_USER u
    LEFT JOIN dbo.MST_PERM g ON g.USR_ID = u.USR_ID AND g.SYS_ID = 'STOCK_MGR'
    WHERE (u.USR_LVL <= 2 OR g.USR_ID IS NOT NULL)
      AND NOT EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID = 'STORE');

    IF @Main IS NOT NULL
    BEGIN
        INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
        SELECT u.USR_ID, @Main, 'Y', 'N', 'N', 'N', 'N'
        FROM dbo.MST_USER u
        WHERE u.USR_LVL <= 2
          AND NOT EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID = @Main);
        UPDATE p SET PERM_VIEW = 'Y'
        FROM dbo.MST_PERM p JOIN dbo.MST_USER u ON u.USR_ID = p.USR_ID
        WHERE p.SYS_ID = @Main AND u.USR_LVL <= 2;
    END
END

-- c) SCANNER: VIEW = had SCAN_IN or SCAN_OUT / ADD = SCAN_IN / EDIT = SCAN_OUT
INSERT INTO dbo.MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP)
SELECT u.USR_ID, 'SCANNER', 'Y',
       CASE WHEN EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID = 'SCAN_IN'  AND p.PERM_VIEW = 'Y') THEN 'Y' ELSE 'N' END,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID = 'SCAN_OUT' AND p.PERM_VIEW = 'Y') THEN 'Y' ELSE 'N' END,
       'N', 'N'
FROM dbo.MST_USER u
WHERE EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID IN ('SCAN_IN', 'SCAN_OUT') AND p.PERM_VIEW = 'Y')
  AND NOT EXISTS (SELECT 1 FROM dbo.MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.SYS_ID = 'SCANNER');

-- d) old rows are no longer used by the app
DELETE FROM dbo.MST_PERM WHERE SYS_ID IN ('STK', 'STOCK_MGR', 'SCAN_IN', 'SCAN_OUT');
GO

-- -----------------------------------------------------------------------------------------------------
-- 17) ADJUST SCAN (Level 1 admin, Multi-Scanner page): edit the qty of a scan or cancel it - the stock
--     balance is corrected in the same transaction and every change is kept in TRN_SCAN_ADJ with the reason.
--     TX_BOX = boxes the scan moved in a non-main stock (+1 scan in / -1 scan out / 0 remainder scan,
--              NULL = box count follows the pieces, e.g. RETURN). Cancelled scans stay in TRN_SCAN (IS_CANCEL = 1).
-- -----------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.TRN_SCAN', 'TX_BOX') IS NULL
    ALTER TABLE dbo.TRN_SCAN ADD TX_BOX INT NULL;
GO
IF COL_LENGTH('dbo.TRN_SCAN', 'IS_CANCEL') IS NULL
    ALTER TABLE dbo.TRN_SCAN ADD IS_CANCEL BIT NOT NULL CONSTRAINT DF_TRN_SCAN_IS_CANCEL DEFAULT (0);
GO
IF COL_LENGTH('dbo.TRN_SCAN', 'ADJ_NOTE') IS NULL
    ALTER TABLE dbo.TRN_SCAN ADD ADJ_NOTE NVARCHAR(300) NULL;      -- last adjustment shown in the lists
GO
IF OBJECT_ID('dbo.TRN_SCAN_ADJ', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TRN_SCAN_ADJ (
        ADJ_ID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TRN_SCAN_ADJ PRIMARY KEY,
        TX_ID       INT            NOT NULL,
        ADJ_ACTION  VARCHAR(20)    NOT NULL,      -- EDIT_QTY / CANCEL
        TX_TYPE     VARCHAR(20)    NULL,
        STK_ID      INT            NULL,
        PT_ID       INT            NULL,
        OLD_QTY     INT            NULL,
        NEW_QTY     INT            NULL,
        STOCK_DELTA INT            NULL,          -- change applied to the stock balance
        BAL_AFTER   INT            NULL,
        REASON      NVARCHAR(500)  NOT NULL,
        USR_ID      VARCHAR(20)    NULL,
        ADJ_DATE    DATETIME       NOT NULL CONSTRAINT DF_TRN_SCAN_ADJ_DATE DEFAULT (GETDATE())
    );
    CREATE INDEX IX_TRN_SCAN_ADJ_TX ON dbo.TRN_SCAN_ADJ (TX_ID);
END
GO

PRINT 'MultiStock migration completed.';
