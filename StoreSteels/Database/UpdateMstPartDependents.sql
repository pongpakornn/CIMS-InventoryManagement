-- Run this AFTER MigrateMstPartSchema.sql. Rewrites every view/stored procedure that referenced the
-- old MST_PART columns (PT_ACODE, PT_CUST, PT_MODEL, PT_NO, PT_LOC, QTY_STK) so they match the new
-- schema. VW_PART_MAXMIN_MASTER and sp_ProcessExcelImportAutoDays are intentionally NOT touched here:
-- they belonged to the Max-Min Calculator page, which was already removed from the app, and nothing
-- in the current C# code calls them - left as-is (they will simply error if anyone runs them, same as
-- any other orphaned object, but nothing in this app does).

SET NOCOUNT ON;
GO

CREATE OR ALTER VIEW dbo.v_PartQRReady AS
SELECT
    PT_CAT,             -- กลุ่มสินค้า
    PT_SUPPLIER,        -- ซัพพลายเออร์ (เดิม PT_CUST)
    PT_CODE,            -- รหัสสินค้า (ตัวระบุหลักตัวเดียวตอนนี้ - PT_ACODE ถูกลบแล้ว)
    PT_DESC,            -- ชื่อสินค้า
    PT_PSZ,             -- ขนาดแพ็ค (PackSize)
    ISNULL(PT_QR, '-') AS PT_QR_DISPLAY,
    'CAT:'   + ISNULL(PT_CAT, '-') +
    ' | CD:' + ISNULL(PT_CODE, '-') +
    ' | NM:' + ISNULL(PT_DESC, '-') +
    ' | Q:'  + CAST(ISNULL(PT_PSZ, 0) AS VARCHAR) +
    ' | BC:' + ISNULL(PT_QR, '-') AS PT_QR_FULL,
    QTY_STKB,
    QTY_MAX,
    QTY_MIN,
    PT_BIN,             -- เดิม PT_LOC
    IS_ACTIVE,
    IS_SHOW_MST,
    PT_IMG
FROM dbo.MST_PART
WHERE IS_ACTIVE = 1;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetPartForQR
    @SearchText NVARCHAR(100) = ''
AS
BEGIN
    SET NOCOUNT ON;

    SET @SearchText = ISNULL(TRIM(@SearchText), '');

    SELECT * FROM dbo.v_PartQRReady
    WHERE
        @SearchText = ''
        OR (
            ISNULL(PT_CODE, '')       LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_DESC, '')    LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_SUPPLIER, '') LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_BIN, '')     LIKE '%' + @SearchText + '%'
            OR ISNULL(PT_CAT, '')     LIKE '%' + @SearchText + '%'
        )
    ORDER BY PT_CAT ASC, PT_CODE ASC;
END
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetPartByScan
    @BarcodeInput NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CleanInput NVARCHAR(200) = LTRIM(RTRIM(@BarcodeInput));

    SELECT
        PT_ID,
        PT_CODE,
        PT_DESC,
        QTY_STKB,
        PT_PSZ,
        PT_CAT,
        IS_ACTIVE,
        PT_IMG
    FROM dbo.MST_PART WITH (NOLOCK)
    WHERE (
          LTRIM(RTRIM(PT_CODE)) = @CleanInput
       OR LTRIM(RTRIM(PT_QR)) = @CleanInput
       OR LTRIM(RTRIM(PT_DESC)) = @CleanInput
    )
    AND IS_ACTIVE = 1;
END
GO

CREATE OR ALTER VIEW dbo.VW_StoreMonitoring AS
SELECT
    PT_ID,
    PT_CAT AS Category,        -- ใช้จัดกลุ่มแทน CustomerCode เดิม
    PT_SUPPLIER AS Supplier,
    PT_IMG AS ImageFileName,
    PT_CODE AS PartCode,
    PT_DESC AS PartName,
    PT_PSZ AS PackSize,
    QTY_MAX AS [Max],
    QTY_MIN AS [Min],
    QTY_STKB AS QtyStkb,       -- ตอนนี้เป็นค่าน้ำหนัก (กก.) ตัวเดียว ไม่มี QTY_STK แยกอีกต่อไป
    PT_REMARK AS Remark,
    LIT_STAT AS Priority,
    PT_BIN AS Bin,
    CASE
        WHEN ISNULL(QTY_MAX, 0) = 0 AND ISNULL(QTY_MIN, 0) = 0 THEN 'NO_CONFIG'
        WHEN ISNULL(QTY_STKB, 0) < ISNULL(QTY_MIN, 0) THEN 'UNDER_MIN'
        WHEN ISNULL(QTY_STKB, 0) > ISNULL(QTY_MAX, 0) AND ISNULL(QTY_MAX, 0) > 0 THEN 'OVER_MAX'
        WHEN ISNULL(QTY_STKB, 0) >= ISNULL(QTY_MIN, 0) AND ISNULL(QTY_STKB, 0) <= ISNULL(QTY_MAX, 0) THEN 'NORMAL_GOOD'
        ELSE 'NORMAL'
    END AS StockStatus
FROM dbo.MST_PART
WHERE IS_ACTIVE = 1
  AND IS_SHOW_MST = 1;
GO

PRINT 'v_PartQRReady / sp_GetPartForQR / sp_GetPartByScan / VW_StoreMonitoring updated for the new MST_PART schema.';
