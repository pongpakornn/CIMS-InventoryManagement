-- =====================================================================================================
-- PR Management - Run against the Stock DB (CIMS / GlobalConfig.ConnStr). Safe to run more than once.
--
--  1) TRN_PR_H: REQ_NAME (typed requester), PR_SOURCE (MANUAL / SCAN_OUT), SCAN_TX_ID
--  2) MST_PR_LOOKUP: values typed on the PR page (Requester / Department / Remark / Target Dept)
--     so the page can suggest them next time (filter while typing)
--  3) sp_PR_SaveLookup: add / bump a typed value
--  4) trg_TRN_SCAN_AutoPR: every real SCAN OUT (Multi-Scanner) creates a "Waiting" PR automatically,
--     so it shows up in the PR table for users with Approve rights
--  4b) MST_STOCK.PR_AUTO: which stocks create the automatic PR on SCAN OUT (PR SETTINGS button on the PR page).
--      First run turns it ON for every existing stock (same as before); stocks created later start OFF.
--  5) v_PRReady: used by sp_GetPRList (PR list) and PRService.GetPendingExportList. It was never
--     created (production reports it as an "Unresolved Entity"), so the PR page could not load.
-- =====================================================================================================

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1) New PR header columns ------------------------------------------------------------------------------
IF COL_LENGTH('dbo.TRN_PR_H', 'REQ_NAME') IS NULL
    ALTER TABLE dbo.TRN_PR_H ADD REQ_NAME NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.TRN_PR_H', 'PR_SOURCE') IS NULL
    ALTER TABLE dbo.TRN_PR_H ADD PR_SOURCE VARCHAR(20) NULL;
GO
IF COL_LENGTH('dbo.TRN_PR_H', 'SCAN_TX_ID') IS NULL
    ALTER TABLE dbo.TRN_PR_H ADD SCAN_TX_ID INT NULL;
GO

-- 2) Typed-value lookup ---------------------------------------------------------------------------------
IF OBJECT_ID('dbo.MST_PR_LOOKUP', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MST_PR_LOOKUP (
        FIELD           VARCHAR(20)     NOT NULL,       -- REQUESTER / DEPT / REMARK / TARGET
        LOOKUP_VALUE    NVARCHAR(200)   NOT NULL,
        USE_COUNT       INT             NOT NULL CONSTRAINT DF_PRLK_COUNT DEFAULT (1),
        LAST_USED       DATETIME        NOT NULL CONSTRAINT DF_PRLK_LAST DEFAULT (GETDATE()),
        CONSTRAINT PK_MST_PR_LOOKUP PRIMARY KEY (FIELD, LOOKUP_VALUE)
    );
END
GO

-- seed: the values that used to be fixed on the page + anything already used in PRs + user names
MERGE dbo.MST_PR_LOOKUP AS t
USING (
    SELECT 'DEPT' AS FIELD, N'41304134-บำรุงรักษาแม่พิมพ์' AS V
    UNION SELECT 'REMARK', N'ซ่อมแม่พิมพ์/ประตู2B'
    UNION SELECT 'TARGET', N'ชนนิกานต์'
    UNION SELECT 'DEPT',   LTRIM(RTRIM(REQ_DEPT)) FROM dbo.TRN_PR_H WHERE ISNULL(LTRIM(RTRIM(REQ_DEPT)), '') <> ''
    UNION SELECT 'REMARK', LTRIM(RTRIM(PR_REM))   FROM dbo.TRN_PR_H WHERE ISNULL(LTRIM(RTRIM(PR_REM)), '') <> '' AND LEN(PR_REM) <= 200
    UNION SELECT 'TARGET', LTRIM(RTRIM(TRG_DEPT)) FROM dbo.TRN_PR_H WHERE ISNULL(LTRIM(RTRIM(TRG_DEPT)), '') <> ''
    UNION SELECT 'REQUESTER', LTRIM(RTRIM(USR_NAME)) FROM dbo.MST_USER WHERE ISNULL(LTRIM(RTRIM(USR_NAME)), '') <> ''
) AS s ON t.FIELD = s.FIELD AND t.LOOKUP_VALUE = s.V
WHEN NOT MATCHED THEN INSERT (FIELD, LOOKUP_VALUE, USE_COUNT, LAST_USED) VALUES (s.FIELD, s.V, 1, GETDATE());
GO

-- 3) Save / bump a typed value ----------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_PR_SaveLookup
    @Field VARCHAR(20),
    @Value NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    SET @Value = LTRIM(RTRIM(ISNULL(@Value, N'')));
    IF @Value = N'' RETURN;

    UPDATE dbo.MST_PR_LOOKUP SET USE_COUNT = USE_COUNT + 1, LAST_USED = GETDATE()
    WHERE FIELD = @Field AND LOOKUP_VALUE = @Value;

    IF @@ROWCOUNT = 0
        INSERT INTO dbo.MST_PR_LOOKUP (FIELD, LOOKUP_VALUE) VALUES (@Field, @Value);
END
GO

-- 4b) Per-stock AUTO PR switch --------------------------------------------------------------------------
IF OBJECT_ID('dbo.MST_STOCK', 'U') IS NOT NULL AND COL_LENGTH('dbo.MST_STOCK', 'PR_AUTO') IS NULL
BEGIN
    ALTER TABLE dbo.MST_STOCK ADD PR_AUTO BIT NOT NULL CONSTRAINT DF_MST_STOCK_PR_AUTO DEFAULT (0);
    EXEC ('UPDATE dbo.MST_STOCK SET PR_AUTO = 1;');   -- existing stocks keep creating PRs like before
END
GO

-- 4) SCAN OUT -> "Waiting" PR -----------------------------------------------------------------------------
-- Department / Target Dept = the values used most recently on the PR page (fallback: the old defaults).
-- Wrapped in TRY/CATCH so a PR problem can never block the scan itself.
CREATE OR ALTER TRIGGER dbo.trg_TRN_SCAN_AutoPR
ON dbo.TRN_SCAN
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT OFF;

    IF NOT EXISTS (SELECT 1 FROM inserted WHERE TX_TYPE = 'OUT') RETURN;

    BEGIN TRY
        DECLARE @prefix VARCHAR(4) = RIGHT(CAST(YEAR(GETDATE()) AS VARCHAR(4)), 2) + 'PR';

        DECLARE @dept NVARCHAR(200) = ISNULL((SELECT TOP 1 LOOKUP_VALUE FROM dbo.MST_PR_LOOKUP WHERE FIELD = 'DEPT' ORDER BY LAST_USED DESC), N'41304134-บำรุงรักษาแม่พิมพ์');
        DECLARE @target NVARCHAR(200) = ISNULL((SELECT TOP 1 LOOKUP_VALUE FROM dbo.MST_PR_LOOKUP WHERE FIELD = 'TARGET' ORDER BY LAST_USED DESC), N'ชนนิกานต์');

        DECLARE @last INT = ISNULL((
            SELECT MAX(TRY_CAST(RIGHT(PR_NO, 8) AS INT))
            FROM dbo.TRN_PR_H WITH (UPDLOCK, HOLDLOCK)
            WHERE PR_NO LIKE @prefix + '%' AND LEN(PR_NO) = 12), 0);

        DECLARE @rows TABLE (RN INT, TX_ID INT, USR_ID VARCHAR(20), PT_DESC NVARCHAR(200), QTY INT, UNIT NVARCHAR(20), STK_CODE VARCHAR(30));

        INSERT INTO @rows (RN, TX_ID, USR_ID, PT_DESC, QTY, UNIT, STK_CODE)
        SELECT ROW_NUMBER() OVER (ORDER BY i.TX_ID), i.TX_ID, i.USR_ID, p.PT_DESC, i.TX_QTY,
               ISNULL(s.STK_UNIT, 'KG'), ISNULL(s.STK_CODE, 'Stock-CHR')
        FROM inserted i
        JOIN dbo.MST_PART p       ON p.PT_ID = i.PT_ID
        -- old rows without STK_ID belong to the main stock
        LEFT JOIN dbo.MST_STOCK s ON s.STK_ID = ISNULL(i.STK_ID, (SELECT TOP 1 m.STK_ID FROM dbo.MST_STOCK m WHERE m.IS_MAIN = 1))
        WHERE i.TX_TYPE = 'OUT'
          AND ISNULL(s.PR_AUTO, 1) = 1;      -- only stocks switched ON in PR SETTINGS

        IF NOT EXISTS (SELECT 1 FROM @rows) RETURN;

        INSERT INTO dbo.TRN_PR_H (PR_NO, PR_DATE, USR_ID, REQ_NAME, REQ_DEPT, TRG_DEPT, PR_STAT, PR_REM,
                                  IS_EXPORT, PR_SOURCE, SCAN_TX_ID, CREATED_AT)
        SELECT @prefix + RIGHT('00000000' + CAST(@last + r.RN AS VARCHAR(8)), 8),
               CAST(GETDATE() AS DATE), r.USR_ID, u.USR_NAME, @dept, @target, N'Waiting',
               N'AUTO: SCAN OUT (' + r.STK_CODE + N')', 'N', 'SCAN_OUT', r.TX_ID, GETDATE()
        FROM @rows r
        LEFT JOIN dbo.MST_USER u ON u.USR_ID = r.USR_ID;

        INSERT INTO dbo.TRN_PR_D (PR_NO, PT_DESC, QTY_REQ, QTY_UNIT)
        SELECT @prefix + RIGHT('00000000' + CAST(@last + r.RN AS VARCHAR(8)), 8), r.PT_DESC, r.QTY, r.UNIT
        FROM @rows r;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> -1
            INSERT INTO dbo.SYS_LOGS (USR_ID, ACT_TYPE, LOG_DESC, LOG_REF, LOG_DATE)
            VALUES (NULL, 'AUTO_PR_ERROR', N'Scan out -> PR failed: ' + ERROR_MESSAGE(), '', GETDATE());
    END CATCH
END
GO

-- 5) PR list view --------------------------------------------------------------------------------------
CREATE OR ALTER VIEW dbo.v_PRReady AS
SELECT
    h.PR_NO,
    h.PR_DATE,
    h.USR_ID,
    COALESCE(NULLIF(LTRIM(RTRIM(h.REQ_NAME)), N''), u.USR_NAME, N'N/A') AS REQUESTER,
    ISNULL(p.PT_CODE, 'N/A')     AS PT_CODE,
    d.PT_DESC,
    d.QTY_REQ,
    d.QTY_UNIT,
    h.REQ_DEPT,
    h.TRG_DEPT,
    h.PR_STAT,
    h.PR_REM,
    h.APP_USR_ID,
    h.APP_DATE,
    ISNULL(h.IS_EXPORT, 'N')     AS IS_EXPORT,
    h.EXPORT_REMARK,
    h.EXPORT_DATE,
    ISNULL(h.PR_SOURCE, 'MANUAL') AS PR_SOURCE,
    h.SCAN_TX_ID
FROM dbo.TRN_PR_H h
JOIN dbo.TRN_PR_D d      ON d.PR_NO = h.PR_NO
LEFT JOIN dbo.MST_USER u ON u.USR_ID = h.USR_ID
-- PR stores the product by name (PT_DESC); pick one matching code for display
OUTER APPLY (SELECT TOP 1 mp.PT_CODE FROM dbo.MST_PART mp WHERE mp.PT_DESC = d.PT_DESC ORDER BY mp.IS_ACTIVE DESC, mp.PT_ID) p;
GO

PRINT 'PR module updated.';
