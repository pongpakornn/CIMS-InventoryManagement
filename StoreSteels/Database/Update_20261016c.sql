-- Update_20261016c.sql
-- Inventory Registration (Product Control):
--   * PART B: new product field CIMS.Parts.PartB (next to PART A) + Store (Max-Min) column CIMS.Stocks.ShowColPartB
--     STORE-SUB shows PART B instead of PART A.
--   * CIMS.ProductControlSettings: per stock, the columns of the Inventory Registration table (in order), how the table is
--     grouped (CATEGORY / CUSTOMER / SUPPLIER / NONE) and what the exported QR code holds (CODE = PRODUCT CODE only /
--     CODE_NAME = PRODUCT CODE|PRODUCT NAME). StockID 0 = ALL STOCKS. Set by Level 1 in the ADMIN window of the page.
--   * CIMS.vw_PartQRReady / CIMS.sp_GetPartForQR return and search PART B.
-- Adds columns / one table and sets the starting layouts - other data is not changed. Safe to run again.

IF COL_LENGTH('CIMS.Parts', 'PartB') IS NULL
    ALTER TABLE CIMS.Parts ADD PartB NVARCHAR(100) NULL;
GO
IF COL_LENGTH('CIMS.Stocks', 'ShowColPartB') IS NULL
    ALTER TABLE CIMS.Stocks ADD ShowColPartB BIT NOT NULL CONSTRAINT DF_Stocks_ShowColPartB DEFAULT 0;
GO

IF OBJECT_ID('CIMS.ProductControlSettings', 'U') IS NULL
    CREATE TABLE CIMS.ProductControlSettings (
        StockID     INT NOT NULL CONSTRAINT PK_ProductControlSettings PRIMARY KEY,   -- 0 = ALL STOCKS
        ColumnOrder NVARCHAR(400) NULL,   -- e.g. SUPPLIER,CODE,NAME,PACKSIZE,MOTHER,COILNO,BIN (ACTION is always last)
        GroupBy     VARCHAR(20) NULL,     -- CATEGORY / CUSTOMER / SUPPLIER / NONE
        QrContent   VARCHAR(20) NULL,     -- CODE / CODE_NAME
        UpdatedBy   VARCHAR(20) NULL,
        UpdatedDate DATETIME NOT NULL CONSTRAINT DF_ProductControlSettings_UpdatedDate DEFAULT GETDATE()
    );
GO

-- starting layouts (only for stocks that have no setting yet)
DECLARE @seed TABLE (StockCode NVARCHAR(50), ColumnOrder NVARCHAR(400), GroupBy VARCHAR(20));
INSERT INTO @seed VALUES
    (N'STOCK-MAT',   N'SUPPLIER,CODE,NAME,PACKSIZE,MOTHER,COILNO,BIN', 'CATEGORY'),
    (N'STOCK-MSH',   N'SUPPLIER,CODE,NAME,PACKSIZE,MOTHER,BIN',        'CATEGORY'),
    (N'STOCK-PANTA', N'SUPPLIER,CODE,NAME,PACKSIZE,MOTHER,BIN',        'CATEGORY'),
    (N'STORE-SP',    N'CODE,NAME,PACKSIZE',                            'CUSTOMER'),
    (N'STORE-PC',    N'IMAGE,CATEGORY,MODEL,CODE,PARTA,NAME,PACKSIZE', 'CUSTOMER'),
    (N'STORE-SUB',   N'IMAGE,CATEGORY,MODEL,CODE,PARTA,NAME,PACKSIZE', 'CUSTOMER'),
    (N'STORE-CPN',   N'IMAGE,CODE,NAME,PACKSIZE,MOTHER,BIN',           'CATEGORY'),
    (N'STOCK-CONS',  N'IMAGE,CODE,NAME,PACKSIZE',                      'CATEGORY');

INSERT INTO CIMS.ProductControlSettings (StockID, ColumnOrder, GroupBy, QrContent, UpdatedBy)
SELECT s.StockID, x.ColumnOrder, x.GroupBy, 'CODE_NAME', 'SYSTEM'
FROM @seed x JOIN CIMS.Stocks s ON s.StockCode = x.StockCode
WHERE NOT EXISTS (SELECT 1 FROM CIMS.ProductControlSettings c WHERE c.StockID = s.StockID);

IF NOT EXISTS (SELECT 1 FROM CIMS.ProductControlSettings WHERE StockID = 0)
    INSERT INTO CIMS.ProductControlSettings (StockID, ColumnOrder, GroupBy, QrContent, UpdatedBy)
    VALUES (0, N'IMAGE,SUPPLIER,CATEGORY,CODE,NAME,PACKSIZE,BIN,QRCODE', 'CATEGORY', 'CODE_NAME', 'SYSTEM');
GO

-- STORE-SUB: Store (Max-Min) shows PART B instead of PART A (column order keeps its place)
UPDATE CIMS.Stocks
SET ShowColPartA = 0, ShowColPartB = 1,
    ColumnOrder = CASE WHEN ColumnOrder IS NULL THEN NULL ELSE REPLACE(ColumnOrder, 'PARTA', 'PARTB') END
WHERE StockCode = N'STORE-SUB' AND ShowColPartB = 0;
GO

-- Inventory Registration list: + PART B
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
    Model,
    PartB
FROM CIMS.Parts
WHERE IsActive = 1;
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
            OR ISNULL(PartB, '')    LIKE '%' + @SearchText + '%'
            OR ISNULL(PartNumber, '')   LIKE '%' + @SearchText + '%'
            OR ISNULL(Model, '')    LIKE '%' + @SearchText + '%'
        )
    )
    ORDER BY Category ASC, PartCode ASC
    OPTION (MAX_GRANT_PERCENT = 5);   -- small memory grant: does not wait behind other queries (SQL Express)
END
GO

PRINT 'Update_20261016c: PART B (CIMS.Parts.PartB / CIMS.Stocks.ShowColPartB), CIMS.ProductControlSettings, STORE-SUB -> PART B.';
