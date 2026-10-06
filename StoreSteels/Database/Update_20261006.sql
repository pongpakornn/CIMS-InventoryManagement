-- =====================================================================================================
-- CIMS update 2026-10-06: quantities can have decimals (e.g. KG) - set per stock
--   * CIMS.Stocks.AllowDecimal (BIT) - ADD / EDIT STOCK -> DECIMAL QTY. Stocks with unit KG start ON (first run only).
--   * every stock quantity / balance / MAX / MIN column INT -> DECIMAL(18,3) (whole numbers stay exactly the same)
--   * box counts stay whole numbers (BoxQuantity INT, boxes = FLOOR(quantity / pack size))
--   * procedures / trigger / view take DECIMAL quantities
-- Back up the database first. Safe to run more than once.
-- =====================================================================================================
SET XACT_ABORT ON;
GO
IF COL_LENGTH('CIMS.Stocks', 'AllowDecimal') IS NULL
BEGIN
    ALTER TABLE CIMS.Stocks ADD AllowDecimal BIT NOT NULL CONSTRAINT DF_Stocks_AllowDecimal DEFAULT (0);
    EXEC (N'UPDATE CIMS.Stocks SET AllowDecimal = 1 WHERE UPPER(LTRIM(RTRIM(Unit))) IN (''KG'', ''KGS'')');
END
GO

-- ---- objects that block ALTER COLUMN: indexes that include the columns, the CHECK and the DEFAULT constraints
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.PartStocks') AND c.name = 'Quantity' AND t.name = 'int')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MST_PART_STOCK_PT' AND object_id = OBJECT_ID('CIMS.PartStocks')) DROP INDEX IX_MST_PART_STOCK_PT ON CIMS.PartStocks;
    IF OBJECT_ID('CIMS.CK_PartStocks_Quantity', 'C') IS NOT NULL ALTER TABLE CIMS.PartStocks DROP CONSTRAINT CK_PartStocks_Quantity;
END
GO
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.ScanTransactions') AND c.name = 'Quantity' AND t.name = 'int')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRN_SCAN_TX_DATE' AND object_id = OBJECT_ID('CIMS.ScanTransactions')) DROP INDEX IX_TRN_SCAN_TX_DATE ON CIMS.ScanTransactions;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScanTransactions_Stock_Date' AND object_id = OBJECT_ID('CIMS.ScanTransactions')) DROP INDEX IX_ScanTransactions_Stock_Date ON CIMS.ScanTransactions;
END
GO
-- default constraints on Parts.StockQuantity / PartStocks.Quantity (names differ per server) -> drop, re-added below
DECLARE @sql NVARCHAR(MAX) = N'';
SELECT @sql += N'ALTER TABLE CIMS.' + QUOTENAME(OBJECT_NAME(dc.parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
JOIN sys.types t ON t.user_type_id = c.user_type_id
JOIN (VALUES ('Parts','StockQuantity'),('PartStocks','Quantity')) v(tbl, col) ON OBJECT_ID('CIMS.' + v.tbl) = dc.parent_object_id AND v.col = c.name
WHERE t.name = 'int';
EXEC (@sql);
GO

-- ---- INT -> DECIMAL(18,3)
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.Parts') AND c.name = 'StockQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.Parts ALTER COLUMN StockQuantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.Parts') AND c.name = 'MaxQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.Parts ALTER COLUMN MaxQuantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.Parts') AND c.name = 'MinQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.Parts ALTER COLUMN MinQuantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.PartStocks') AND c.name = 'Quantity' AND t.name = 'int')
    ALTER TABLE CIMS.PartStocks ALTER COLUMN Quantity DECIMAL(18,3) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.PartStocks') AND c.name = 'MaxQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.PartStocks ALTER COLUMN MaxQuantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.PartStocks') AND c.name = 'MinQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.PartStocks ALTER COLUMN MinQuantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.ScanTransactions') AND c.name = 'Quantity' AND t.name = 'int')
    ALTER TABLE CIMS.ScanTransactions ALTER COLUMN Quantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.ScanTransactions') AND c.name = 'BalanceAfter' AND t.name = 'int')
    ALTER TABLE CIMS.ScanTransactions ALTER COLUMN BalanceAfter DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.ScanAdjustments') AND c.name = 'OldQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.ScanAdjustments ALTER COLUMN OldQuantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.ScanAdjustments') AND c.name = 'NewQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.ScanAdjustments ALTER COLUMN NewQuantity DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.ScanAdjustments') AND c.name = 'StockChange' AND t.name = 'int')
    ALTER TABLE CIMS.ScanAdjustments ALTER COLUMN StockChange DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.ScanAdjustments') AND c.name = 'BalanceAfter' AND t.name = 'int')
    ALTER TABLE CIMS.ScanAdjustments ALTER COLUMN BalanceAfter DECIMAL(18,3) NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.StockTransfers') AND c.name = 'Quantity' AND t.name = 'int')
    ALTER TABLE CIMS.StockTransfers ALTER COLUMN Quantity DECIMAL(18,3) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.StockTransfers') AND c.name = 'FromBalanceAfter' AND t.name = 'int')
    ALTER TABLE CIMS.StockTransfers ALTER COLUMN FromBalanceAfter DECIMAL(18,3) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.StockTransfers') AND c.name = 'ToBalanceAfter' AND t.name = 'int')
    ALTER TABLE CIMS.StockTransfers ALTER COLUMN ToBalanceAfter DECIMAL(18,3) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.StockImports') AND c.name = 'Quantity' AND t.name = 'int')
    ALTER TABLE CIMS.StockImports ALTER COLUMN Quantity DECIMAL(18,3) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.StockImports') AND c.name = 'BalanceAfter' AND t.name = 'int')
    ALTER TABLE CIMS.StockImports ALTER COLUMN BalanceAfter DECIMAL(18,3) NOT NULL;
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID('CIMS.StockDeleteLogs') AND c.name = 'TotalQuantity' AND t.name = 'int')
    ALTER TABLE CIMS.StockDeleteLogs ALTER COLUMN TotalQuantity DECIMAL(18,3) NOT NULL;
GO

-- ---- put the defaults / CHECK / indexes back
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('CIMS.Parts') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('CIMS.Parts'), 'StockQuantity', 'ColumnId'))
    ALTER TABLE CIMS.Parts ADD CONSTRAINT DF_Parts_StockQuantity DEFAULT (0) FOR StockQuantity;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('CIMS.PartStocks') AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('CIMS.PartStocks'), 'Quantity', 'ColumnId'))
    ALTER TABLE CIMS.PartStocks ADD CONSTRAINT DF_PSTK_QTY DEFAULT (0) FOR Quantity;
IF OBJECT_ID('CIMS.CK_PartStocks_Quantity', 'C') IS NULL
    ALTER TABLE CIMS.PartStocks WITH CHECK ADD CONSTRAINT CK_PartStocks_Quantity CHECK (Quantity >= 0);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MST_PART_STOCK_PT' AND object_id = OBJECT_ID('CIMS.PartStocks'))
    CREATE NONCLUSTERED INDEX IX_MST_PART_STOCK_PT ON CIMS.PartStocks (PartID) INCLUDE (Quantity, BoxQuantity);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TRN_SCAN_TX_DATE' AND object_id = OBJECT_ID('CIMS.ScanTransactions'))
    CREATE NONCLUSTERED INDEX IX_TRN_SCAN_TX_DATE ON CIMS.ScanTransactions (TransactionDate) INCLUDE (PartID, StockID, TransactionType, Quantity, UserID);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScanTransactions_Stock_Date' AND object_id = OBJECT_ID('CIMS.ScanTransactions'))
    CREATE NONCLUSTERED INDEX IX_ScanTransactions_Stock_Date ON CIMS.ScanTransactions (StockID, TransactionDate) INCLUDE (PartID, PartACode, Quantity, TransactionType, IsCancelled);
GO

-- ---- procedures / trigger / view with DECIMAL quantities (box counts stay whole numbers)

-- Add quantity to a non-main stock (creates the part row on first receive). Returns the new balance.
-- Used by Import Excel and by Multi-Scanner IN for non-main stocks. Runs inside the caller's transaction.
CREATE OR ALTER PROCEDURE CIMS.sp_Stock_AddQty
    @StkId  INT,
    @PtId   INT,
    @Qty    DECIMAL(18,3),
    @NewBal DECIMAL(18,3) OUTPUT,
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

-- Transfer from a non-main stock into Stock-CHR.  @Qty NULL = transfer everything.
-- Result set: TransferID, Quantity, FromBalanceAfter, ToBalanceAfter.  Errors are raised with Thai messages.
CREATE OR ALTER PROCEDURE CIMS.sp_Stock_Transfer
    @FromStkId  INT,
    @PtId       INT,
    @Qty        DECIMAL(18,3) = NULL,
    @UserId     VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @FromCode VARCHAR(30), @ToId INT, @ToCode VARCHAR(30), @PtCode VARCHAR(50);
    DECLARE @Bal DECIMAL(18,3), @Mode VARCHAR(10), @FromAfter DECIMAL(18,3), @ToAfter DECIMAL(18,3);

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

    DECLARE @Code VARCHAR(30), @Name NVARCHAR(100), @IsMain BIT, @Items INT, @Total DECIMAL(18,3);
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
        UPDATE ps SET BoxQuantity = CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN CAST(FLOOR(ps.Quantity / p.PackSize) AS INT) ELSE 0 END
        FROM CIMS.PartStocks ps
        JOIN inserted i ON i.StockID = ps.StockID AND i.PartID = ps.PartID
        JOIN CIMS.Parts p ON p.PartID = ps.PartID
        WHERE i.BoxQuantity = 0 AND i.Quantity > 0;
        RETURN;
    END

    IF UPDATE(Quantity) AND NOT UPDATE(BoxQuantity)
        UPDATE ps SET BoxQuantity = CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN CAST(FLOOR(ps.Quantity / p.PackSize) AS INT) ELSE 0 END
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
-- =====================================================================================================
-- CIMS update 2026-10-05 (c): Store(Max-Min) can group a stock by SUPPLIER (Stocks.GroupBy = 'SUPPLIER')
--   CIMS.vw_StockMonitoring.GroupKey: CUSTOMER -> Parts.Customer / SUPPLIER -> Parts.Supplier / else Parts.Category
-- Safe to run more than once.
-- =====================================================================================================
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
           CASE WHEN b.BOXQ IS NOT NULL THEN b.BOXQ WHEN ISNULL(p.PackSize, 0) > 0 THEN CAST(FLOOR(b.Quantity / p.PackSize) AS INT) ELSE 0 END AS BoxQuantity
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
    -- group header of the Store table: Category (default) / Customer / Supplier
    -- fixed length (NVARCHAR(200)): Supplier is NVARCHAR(MAX) - a MAX GroupKey makes DISTINCT / ORDER BY ask for a huge memory grant
    CAST(CASE WHEN c.GroupBy = 'CUSTOMER' THEN ISNULL(NULLIF(p.Customer, ''), '-')
              WHEN c.GroupBy = 'SUPPLIER' THEN ISNULL(NULLIF(CAST(p.Supplier AS NVARCHAR(200)), ''), '-')
              ELSE ISNULL(NULLIF(p.Category, ''), '-') END AS NVARCHAR(200)) AS GroupKey,
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

-- ---- refresh every CIMS view so its column types follow the tables
DECLARE @v NVARCHAR(300);
DECLARE vc CURSOR LOCAL FAST_FORWARD FOR SELECT QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) FROM sys.views WHERE SCHEMA_NAME(schema_id) = 'CIMS';
OPEN vc; FETCH NEXT FROM vc INTO @v;
WHILE @@FETCH_STATUS = 0 BEGIN EXEC sp_refreshview @v; FETCH NEXT FROM vc INTO @v; END
CLOSE vc; DEALLOCATE vc;
GO
PRINT 'CIMS decimal quantities ready.';
GO