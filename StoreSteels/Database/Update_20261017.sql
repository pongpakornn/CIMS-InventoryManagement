-- Update_20261017.sql
-- PR by stock:
--   * CIMS.PRHeaders.StockID  = stock of the PR (PR page shows one stock at a time - stocks with AUTO PR on in PR SETTINGS)
--   * CIMS.PRDetails.PartID   = the product chosen by PRODUCT CODE (old rows: matched by name as before)
--   * CIMS.Stocks.PRExportPath = Excel file that APPROVED LIST -> EXPORT PATH EXCEL appends to (per stock, set in User Management -> PR SETTINGS)
--   * existing SCAN OUT PRs get StockID / PartID from their scan; CIMS.vw_PRReady / CIMS.sp_GetPRList / trigger CIMS.trg_ScanTransactions_AutoPR
--     return / filter / save the stock and product.
-- Adds columns and fills the new ones only - quantities / statuses are not changed. Safe to run again.

IF COL_LENGTH('CIMS.PRHeaders', 'StockID') IS NULL
    ALTER TABLE CIMS.PRHeaders ADD StockID INT NULL;
GO
IF COL_LENGTH('CIMS.PRDetails', 'PartID') IS NULL
    ALTER TABLE CIMS.PRDetails ADD PartID INT NULL;
GO
IF COL_LENGTH('CIMS.Stocks', 'PRExportPath') IS NULL
    ALTER TABLE CIMS.Stocks ADD PRExportPath NVARCHAR(400) NULL;
GO
-- where each PR field goes in that Excel file (User Management -> SETUP EXCEL PATH):
--   H:<FIELD>=<CELL> = one value in a fixed cell (e.g. H:DATE=C1) / T:<FIELD>=<COLUMN> = one row per PR item from PRExcelStartRow (e.g. T:CODE=B)
IF COL_LENGTH('CIMS.Stocks', 'PRExcelMap') IS NULL
    ALTER TABLE CIMS.Stocks ADD PRExcelMap NVARCHAR(2000) NULL;
GO
IF COL_LENGTH('CIMS.Stocks', 'PRExcelStartRow') IS NULL
    ALTER TABLE CIMS.Stocks ADD PRExcelStartRow INT NULL;
GO

-- SCAN OUT PRs: stock + product from the scan (scan without StockID = main stock)
UPDATE h SET h.StockID = ISNULL(t.StockID, (SELECT TOP 1 m.StockID FROM CIMS.Stocks m WHERE m.IsMain = 1))
FROM CIMS.PRHeaders h JOIN CIMS.ScanTransactions t ON t.TransactionID = h.ScanTransactionID
WHERE h.StockID IS NULL;

UPDATE d SET d.PartID = t.PartID
FROM CIMS.PRDetails d JOIN CIMS.PRHeaders h ON h.PRNumber = d.PRNumber JOIN CIMS.ScanTransactions t ON t.TransactionID = h.ScanTransactionID
WHERE d.PartID IS NULL;

-- starting Excel file + cells of STORE-SP (change it in User Management -> SETUP EXCEL PATH)
UPDATE CIMS.Stocks SET PRExportPath = N'C:\Users\pongp\Desktop\WorkMe\4. RPA\1. RPA - PR\1. Spare Part\RPA - PR-StoreSP.xlsx'
WHERE StockCode = N'STORE-SP' AND PRExportPath IS NULL;
UPDATE CIMS.Stocks SET PRExcelMap = N'H:DATE=C1;H:DEPARTMENT=C2;H:REMARK=E1;H:TARGET=E2;T:NO=A;T:CODE=B;T:NAME=C;T:QTY=D', PRExcelStartRow = 4
WHERE StockCode = N'STORE-SP' AND PRExcelMap IS NULL;
GO

CREATE OR ALTER VIEW CIMS.vw_PRReady AS
SELECT
    h.PRNumber,
    h.PRDate,
    h.UserID,
    COALESCE(NULLIF(LTRIM(RTRIM(h.RequesterName)), N''), u.FullName, N'N/A') AS REQUESTER,
    ISNULL(ISNULL(pp.PartCode, p.PartCode), 'N/A') AS PartCode,
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
    h.ScanTransactionID,
    h.StockID,
    s.StockCode,
    d.PartID,
    -- extra fields for SETUP EXCEL PATH (product of the PR / coil of the scan)
    ISNULL(pp.Bin, p.Bin)               AS Bin,
    ISNULL(pp.PartA, p.PartA)           AS PartA,
    ISNULL(pp.PartB, p.PartB)           AS PartB,
    ISNULL(pp.PartNumber, p.PartNumber) AS PartNumber,
    ISNULL(pp.Model, p.Model)           AS Model,
    CAST(ISNULL(pp.Supplier, p.Supplier) AS NVARCHAR(200)) AS Supplier,
    ISNULL(pp.Customer, p.Customer)     AS Customer,
    ISNULL(pp.Category, p.Category)     AS Category,
    c.CoilNo,
    c.MotherCoil
FROM CIMS.PRHeaders h
JOIN CIMS.PRDetails d      ON d.PRNumber = h.PRNumber
LEFT JOIN CIMS.Users u     ON u.UserID = h.UserID
LEFT JOIN CIMS.Stocks s    ON s.StockID = h.StockID
LEFT JOIN CIMS.Parts pp    ON pp.PartID = d.PartID
-- old rows store the product by name (Description) only: pick one matching product for display
OUTER APPLY (SELECT TOP 1 mp.PartCode, mp.Bin, mp.PartA, mp.PartB, mp.PartNumber, mp.Model, mp.Supplier, mp.Customer, mp.Category
             FROM CIMS.Parts mp WHERE d.PartID IS NULL AND mp.Description = d.Description ORDER BY mp.IsActive DESC, mp.PartID) p
-- coil scanned out by the SCAN OUT that created the PR
OUTER APPLY (SELECT TOP 1 co.CoilNo, co.MotherCoil FROM CIMS.CoilMoves m JOIN CIMS.Coils co ON co.CoilID = m.CoilID
             WHERE h.ScanTransactionID IS NOT NULL AND m.ScanTransactionID = h.ScanTransactionID ORDER BY m.MoveID) c;
GO

-- @StkId: one stock (NULL = all)
CREATE OR ALTER PROCEDURE CIMS.sp_GetPRList
    @SearchText NVARCHAR(100) = '',
    @StkId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @SearchText = ISNULL(@SearchText, '');
    SELECT * FROM CIMS.vw_PRReady
    WHERE (@StkId IS NULL OR StockID = @StkId)
      AND (PRNumber LIKE '%' + @SearchText + '%'
       OR PartCode LIKE '%' + @SearchText + '%'
       OR Description LIKE '%' + @SearchText + '%'
       OR REQUESTER LIKE '%' + @SearchText + '%'
       OR RequestDepartment LIKE '%' + @SearchText + '%'
       OR Status LIKE '%' + @SearchText + '%')
    ORDER BY PRDate DESC, PRNumber DESC;   -- newest first
END
GO

-- SCAN OUT -> "Waiting" PR (same as before + StockID / PartID)
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

        DECLARE @rows TABLE (RN INT, TransactionID INT, UserID VARCHAR(20), Description NVARCHAR(200), Quantity INT, UNIT NVARCHAR(20), StockCode VARCHAR(30),
                             StockID INT, PartID INT);

        INSERT INTO @rows (RN, TransactionID, UserID, Description, Quantity, UNIT, StockCode, StockID, PartID)
        SELECT ROW_NUMBER() OVER (ORDER BY i.TransactionID), i.TransactionID, i.UserID, p.Description, i.Quantity,
               ISNULL(s.Unit, 'KG'), ISNULL(s.StockCode, 'Stock-CHR'), s.StockID, i.PartID
        FROM inserted i
        JOIN CIMS.Parts p       ON p.PartID = i.PartID
        -- old rows without StockID belong to the main stock
        LEFT JOIN CIMS.Stocks s ON s.StockID = ISNULL(i.StockID, (SELECT TOP 1 m.StockID FROM CIMS.Stocks m WHERE m.IsMain = 1))
        WHERE i.TransactionType = 'OUT'
          AND ISNULL(s.AutoPR, 1) = 1;      -- only stocks switched ON in PR SETTINGS

        IF NOT EXISTS (SELECT 1 FROM @rows) RETURN;

        INSERT INTO CIMS.PRHeaders (PRNumber, PRDate, UserID, RequesterName, RequestDepartment, TargetDepartment, Status, Remark,
                                  IsExported, Source, ScanTransactionID, CreatedAt, StockID)
        SELECT @prefix + RIGHT('00000000' + CAST(@last + r.RN AS VARCHAR(8)), 8),
               CAST(GETDATE() AS DATE), r.UserID, u.FullName, @dept, @target, N'Waiting',
               N'AUTO: SCAN OUT (' + r.StockCode + N')', 'N', 'SCAN_OUT', r.TransactionID, GETDATE(), r.StockID
        FROM @rows r
        LEFT JOIN CIMS.Users u ON u.UserID = r.UserID;

        INSERT INTO CIMS.PRDetails (PRNumber, Description, RequestQuantity, Unit, PartID)
        SELECT @prefix + RIGHT('00000000' + CAST(@last + r.RN AS VARCHAR(8)), 8), r.Description, r.Quantity, r.UNIT, r.PartID
        FROM @rows r;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> -1
            INSERT INTO CIMS.SystemLogs (UserID, ActionType, Description, Reference, LogDate)
            VALUES (NULL, 'AUTO_PR_ERROR', N'Scan out -> PR failed: ' + ERROR_MESSAGE(), '', GETDATE());
    END CATCH
END
GO

PRINT 'Update_20261017: PR by stock (PRHeaders.StockID / PRDetails.PartID / Stocks.PRExportPath), vw_PRReady, sp_GetPRList, trg_ScanTransactions_AutoPR.';
