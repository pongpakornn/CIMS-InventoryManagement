-- Update_20261013c.sql
-- Inventory Registration: the product list (CIMS.sp_GetPartForQR) asks SQL Server for a small memory grant,
-- so filtering by a stock does not wait in the memory queue behind other queries (seen on SQL Express).
-- Same result and order as before - only OPTION (MAX_GRANT_PERCENT = 5) is added. Safe to run again.
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
    ORDER BY Category ASC, PartCode ASC
    OPTION (MAX_GRANT_PERCENT = 5);   -- small memory grant: does not wait behind other queries (SQL Express)
END

GO
