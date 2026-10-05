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
