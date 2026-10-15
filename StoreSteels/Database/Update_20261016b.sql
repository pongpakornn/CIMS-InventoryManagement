-- Update_20261016b.sql
-- NOT DEDUCTED = labels waiting for a person with APPROVE of Multi-Scanner (nothing is added to stock until then)
--   * A label of a format with "deduct from stock" (e.g. Panta -> STOCK-PANTA) whose mother coil cannot be cut, or whose
--     product is not registered, is kept here as PENDING: the receiving stock (STOCK-MAT) is NOT increased yet.
--   * ACTION (APPROVE of Multi-Scanner): APPROVE = receive now + cut the source if possible / PRODUCT CONTROL = register the
--     product, then receive / DELETE = remove (logged). Approved rows show in DEDUCTED.
-- CIMS.DeductMisses:
--   Status   PENDING (waiting, not in stock) / RECEIVED (old rows: already received before this update) /
--            APPROVED / REGISTERED (done - shown in DEDUCTED)
--   PartID   may be NULL (product not registered yet)
--   FormatID barcode format of the label (to read the product name / coil numbers when registering)
--   ActionBy / ActionDate   who approved / registered and when
-- Adds columns only - existing rows become RECEIVED. Safe to run again.

IF COL_LENGTH('CIMS.DeductMisses', 'Status') IS NULL
    ALTER TABLE CIMS.DeductMisses ADD Status VARCHAR(20) NOT NULL CONSTRAINT DF_DeductMisses_Status DEFAULT 'RECEIVED';
GO
IF COL_LENGTH('CIMS.DeductMisses', 'FormatID') IS NULL
    ALTER TABLE CIMS.DeductMisses ADD FormatID INT NULL;
GO
IF COL_LENGTH('CIMS.DeductMisses', 'ActionBy') IS NULL
    ALTER TABLE CIMS.DeductMisses ADD ActionBy VARCHAR(20) NULL;
GO
IF COL_LENGTH('CIMS.DeductMisses', 'ActionDate') IS NULL
    ALTER TABLE CIMS.DeductMisses ADD ActionDate DATETIME NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CIMS.DeductMisses') AND name = 'PartID' AND is_nullable = 0)
    ALTER TABLE CIMS.DeductMisses ALTER COLUMN PartID INT NULL;
GO
