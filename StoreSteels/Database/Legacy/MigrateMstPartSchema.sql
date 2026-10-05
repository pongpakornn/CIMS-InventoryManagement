-- Migrates dbo.MST_PART to the new schema (run once against the Stock DB / GlobalConfig.ConnStr).
-- Safe to run: MST_PART currently has 0 rows in the target environment (verified before writing this).
-- Renames PT_CUST -> PT_SUPPLIER, PT_LOC -> PT_BIN; drops PT_MODEL, PT_ACODE, PT_NO, QTY_STK,
-- LAST_GEN_QR (no longer part of the schema); PT_CODE becomes the sole business key (PT_ACODE removed).
--
-- Resulting column list matches exactly what was requested:
-- PT_ID, PT_IMG, PT_SUPPLIER, PT_CAT, PT_CODE, PT_DESC, PT_PSZ, QTY_MAX, QTY_MIN, QTY_STKB,
-- PT_REMARK, LIT_STAT, PT_BIN, PT_QR, IS_ACTIVE, IS_SHOW_MST

SET NOCOUNT ON;

IF COL_LENGTH('dbo.MST_PART', 'PT_CUST') IS NOT NULL AND COL_LENGTH('dbo.MST_PART', 'PT_SUPPLIER') IS NULL
    EXEC sp_rename 'dbo.MST_PART.PT_CUST', 'PT_SUPPLIER', 'COLUMN';

IF COL_LENGTH('dbo.MST_PART', 'PT_LOC') IS NOT NULL AND COL_LENGTH('dbo.MST_PART', 'PT_BIN') IS NULL
    EXEC sp_rename 'dbo.MST_PART.PT_LOC', 'PT_BIN', 'COLUMN';

-- QTY_STK has a default constraint - drop it before dropping the column
DECLARE @dfName NVARCHAR(200);
SELECT @dfName = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE dc.parent_object_id = OBJECT_ID('dbo.MST_PART') AND c.name = 'QTY_STK';

IF @dfName IS NOT NULL
    EXEC('ALTER TABLE dbo.MST_PART DROP CONSTRAINT ' + @dfName);

IF COL_LENGTH('dbo.MST_PART', 'QTY_STK') IS NOT NULL
    ALTER TABLE dbo.MST_PART DROP COLUMN QTY_STK;

IF COL_LENGTH('dbo.MST_PART', 'PT_MODEL') IS NOT NULL
    ALTER TABLE dbo.MST_PART DROP COLUMN PT_MODEL;

IF COL_LENGTH('dbo.MST_PART', 'PT_ACODE') IS NOT NULL
    ALTER TABLE dbo.MST_PART DROP COLUMN PT_ACODE;

IF COL_LENGTH('dbo.MST_PART', 'PT_NO') IS NOT NULL
    ALTER TABLE dbo.MST_PART DROP COLUMN PT_NO;

IF COL_LENGTH('dbo.MST_PART', 'LAST_GEN_QR') IS NOT NULL
    ALTER TABLE dbo.MST_PART DROP COLUMN LAST_GEN_QR;

-- PT_CODE is now the sole business key (PT_ACODE is gone) - enforce NOT NULL + UNIQUE
ALTER TABLE dbo.MST_PART ALTER COLUMN PT_CODE VARCHAR(50) NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.MST_PART') AND name = 'UQ_MST_PART_PT_CODE'
)
    ALTER TABLE dbo.MST_PART ADD CONSTRAINT UQ_MST_PART_PT_CODE UNIQUE (PT_CODE);

PRINT 'MST_PART schema migration complete.';
