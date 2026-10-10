-- =============================================
-- Update_20261010.sql  (CIMS)
-- Panta labels received into STOCK-MAT use one format: Panta-2
--   DCAE9031B-006;CHR; 26071606; -; DCAE9031B-006;SGACE 45/45; 1.000 X 175.00 X COIL; 1; 613.00; 21/08/2026; DCAE9031B;
--   product      = closest PRODUCT NAME to #6 + #7 (SGACE 45/45 + 1.000 X 175.00 X COIL; size #7 alone if needed)
--   child coil   = #1  (DCAE9031B-006)      quantity = #9 (613.00 KG)
--   mother coil  = #11 (DCAE9031B)          -> deduct that weight from the mother coil in STOCK-PANTA
-- Panta-3 is deleted, Panta is switched off (kept, not linked to any stock).
-- Data only - no schema change. Safe to run more than once.
-- =============================================
SET NOCOUNT ON;

DECLARE @mat INT = (SELECT StockID FROM CIMS.Stocks WHERE StockCode = 'STOCK-MAT');
DECLARE @panta INT = (SELECT StockID FROM CIMS.Stocks WHERE StockCode = 'STOCK-PANTA');
DECLARE @p2 INT = (SELECT FormatID FROM CIMS.BarcodeFormats WHERE FormatName = 'Panta-2');

IF @mat IS NULL OR @panta IS NULL
BEGIN
    PRINT 'STOCK-MAT / STOCK-PANTA not found - nothing changed';
    RETURN;
END

BEGIN TRAN;

IF @p2 IS NULL
BEGIN
    INSERT INTO CIMS.BarcodeFormats (FormatName, DELIMITER, CodePosition, MinFields, SampleText, IsActive, CreatedBy)
    VALUES ('Panta-2', ';', 7, 11, N'DCAE9031B-006;CHR; 26071606; -; DCAE9031B-006;SGACE 45/45; 1.000 X 175.00 X COIL; 1; 613.00; 21/08/2026; DCAE9031B;', 1, 'SYSTEM');
    SET @p2 = SCOPE_IDENTITY();
END

UPDATE CIMS.BarcodeFormats
   SET DELIMITER = ';', DelimiterMode = 'CHAR', TrimChars = NULL, CodePrefix = 'NONE', CodeCut = NULL, MatchStart = NULL, MatchEnd = NULL,
       CodePosition = 7, AltCodePosition = 1, QuantityPosition = 9, MinFields = 11,
       MatchBy = 'NAME', NameFields = '6,7', CoilNoPosition = 1, MotherCoilPosition = 11,
       SourceStockID = @panta, IsActive = 1, UpdatedBy = 'SYSTEM', UpdatedDate = GETDATE()
 WHERE FormatID = @p2;

-- STOCK-MAT reads Panta labels with Panta-2 only
DELETE FROM CIMS.StockBarcodeFormats
 WHERE StockID = @mat AND FormatID IN (SELECT FormatID FROM CIMS.BarcodeFormats WHERE FormatName IN ('Panta', 'Panta-3'));
IF NOT EXISTS (SELECT 1 FROM CIMS.StockBarcodeFormats WHERE StockID = @mat AND FormatID = @p2)
    INSERT INTO CIMS.StockBarcodeFormats (StockID, FormatID) VALUES (@mat, @p2);

-- Panta-3: deleted / Panta: off and not linked (kept for reference)
DELETE FROM CIMS.StockBarcodeFormats WHERE FormatID IN (SELECT FormatID FROM CIMS.BarcodeFormats WHERE FormatName IN ('Panta', 'Panta-3'));
DELETE FROM CIMS.BarcodeFormats WHERE FormatName = 'Panta-3';
UPDATE CIMS.BarcodeFormats SET IsActive = 0, UpdatedBy = 'SYSTEM', UpdatedDate = GETDATE() WHERE FormatName = 'Panta';

COMMIT;

SELECT f.FormatID, f.FormatName, f.IsActive, f.MatchBy, f.NameFields, f.QuantityPosition, f.CoilNoPosition, f.MotherCoilPosition,
       DeductFrom = s.StockCode,
       Stocks = STUFF((SELECT ',' + x.StockCode FROM CIMS.StockBarcodeFormats l JOIN CIMS.Stocks x ON x.StockID = l.StockID WHERE l.FormatID = f.FormatID FOR XML PATH('')), 1, 1, '')
  FROM CIMS.BarcodeFormats f LEFT JOIN CIMS.Stocks s ON s.StockID = f.SourceStockID;
