-- =====================================================================================================
-- CIMS update 2026-10-06 (c): QTY (COIL) for KG stocks
--   * CIMS.Stocks.CountCoil (BIT): stock counts coils - scan IN = +1 coil, OUT = -1, REMAINDER = 0 (quantity as scanned)
--     stocks with unit KG start ON (first run only) - ADD / EDIT STOCK -> QTY (COIL)
--   * CIMS.Parts.CoilQuantity: coils of the main stock (other stocks keep coils in CIMS.PartStocks.BoxQuantity)
--   * trg_PartStocks_BoxSync: coil stocks keep coils and KG independent (no box <-> pack size recalculation)
--   * sp_Stock_AddQty: main stock coil count follows @Box
--   * vw_StockMonitoring: StockBox = coils for a coil stock
--   * CIMS.StockTransfers.SourcePartID: product deducted in the source stock (matched by BIN)
-- Safe to run more than once.
-- =====================================================================================================
SET XACT_ABORT ON;
GO
IF COL_LENGTH('CIMS.Stocks', 'CountCoil') IS NULL
BEGIN
    ALTER TABLE CIMS.Stocks ADD CountCoil BIT NOT NULL CONSTRAINT DF_Stocks_CountCoil DEFAULT (0);
    EXEC (N'UPDATE CIMS.Stocks SET CountCoil = 1 WHERE UPPER(LTRIM(RTRIM(Unit))) IN (''KG'', ''KGS'')');
END
GO
-- scan IN that deducts the label's source stock: the source product can be another code with the same BIN
IF COL_LENGTH('CIMS.StockTransfers', 'SourcePartID') IS NULL
    ALTER TABLE CIMS.StockTransfers ADD SourcePartID INT NULL;
GO
IF COL_LENGTH('CIMS.Parts', 'CoilQuantity') IS NULL
    ALTER TABLE CIMS.Parts ADD CoilQuantity INT NOT NULL CONSTRAINT DF_Parts_CoilQuantity DEFAULT (0);
GO

-- box <-> pieces sync, except coil stocks (coils = scans, not KG / pack size)
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
        JOIN CIMS.Stocks s ON s.StockID = ps.StockID AND s.CountCoil = 0
        WHERE i.BoxQuantity = 0 AND i.Quantity > 0;
        RETURN;
    END

    IF UPDATE(Quantity) AND NOT UPDATE(BoxQuantity)
        UPDATE ps SET BoxQuantity = CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN CAST(FLOOR(ps.Quantity / p.PackSize) AS INT) ELSE 0 END
        FROM CIMS.PartStocks ps
        JOIN inserted i ON i.StockID = ps.StockID AND i.PartID = ps.PartID
        JOIN deleted d  ON d.StockID = i.StockID AND d.PartID = i.PartID
        JOIN CIMS.Parts p ON p.PartID = ps.PartID
        JOIN CIMS.Stocks s ON s.StockID = ps.StockID AND s.CountCoil = 0
        WHERE i.Quantity <> d.Quantity;
    ELSE IF UPDATE(BoxQuantity) AND NOT UPDATE(Quantity)
        UPDATE ps SET Quantity = ps.BoxQuantity * CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN p.PackSize ELSE 1 END
        FROM CIMS.PartStocks ps
        JOIN inserted i ON i.StockID = ps.StockID AND i.PartID = ps.PartID
        JOIN deleted d  ON d.StockID = i.StockID AND d.PartID = i.PartID
        JOIN CIMS.Parts p ON p.PartID = ps.PartID
        JOIN CIMS.Stocks s ON s.StockID = ps.StockID AND s.CountCoil = 0
        WHERE i.BoxQuantity <> d.BoxQuantity;
END
GO

-- Add quantity to a stock (non-main: creates the part row on first receive). Returns the new balance.
-- @Box: scan = +1 box / coil per label (NULL = box count follows Quantity / PACK SIZE)
CREATE OR ALTER PROCEDURE CIMS.sp_Stock_AddQty
    @StkId  INT,
    @PtId   INT,
    @Qty    DECIMAL(18,3),
    @NewBal DECIMAL(18,3) OUTPUT,
    @Box    INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM CIMS.Stocks WHERE StockID = @StkId AND IsMain = 1)
    BEGIN
        UPDATE CIMS.Parts SET StockQuantity = ISNULL(StockQuantity, 0) + @Qty,
               CoilQuantity = CASE WHEN @Box IS NOT NULL AND EXISTS (SELECT 1 FROM CIMS.Stocks WHERE StockID = @StkId AND CountCoil = 1)
                                   THEN CASE WHEN CoilQuantity + @Box < 0 THEN 0 ELSE CoilQuantity + @Box END ELSE CoilQuantity END
         WHERE PartID = @PtId;
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
-- first run only: coil stocks start at 0 coils (the old box count was KG / pack size, not coils) - KG is not touched
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('CIMS.Stocks') AND minor_id = 0 AND name = 'CIMS_CoilStart')
BEGIN
    DISABLE TRIGGER CIMS.trg_PartStocks_BoxSync ON CIMS.PartStocks;
    UPDATE ps SET BoxQuantity = 0 FROM CIMS.PartStocks ps JOIN CIMS.Stocks s ON s.StockID = ps.StockID AND s.CountCoil = 1;
    ENABLE TRIGGER CIMS.trg_PartStocks_BoxSync ON CIMS.PartStocks;
    UPDATE CIMS.Parts SET CoilQuantity = 0;
    EXEC sp_addextendedproperty @name = N'CIMS_CoilStart', @value = N'coil stocks set to 0 coils (Update_20261006c.sql)',
         @level0type = N'SCHEMA', @level0name = N'CIMS', @level1type = N'TABLE', @level1name = N'Stocks';
END
GO
IF OBJECT_ID('CIMS.vw_StorePcLive') IS NULL OR COL_LENGTH('CIMS.Stocks', 'LiveSource') IS NULL
    RAISERROR('Run Update_20261006b.sql first', 16, 1);
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