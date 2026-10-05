-- =====================================================================================================
-- CIMS update 2026-10-05 (b): Barcode Format can find the product by PRODUCT NAME
--   MatchBy    = 'CODE' (default, old behaviour) / 'NAME'
--   NameFields = label fields joined into the product name, e.g. '6,7'  (NULL = CodePosition)
-- Safe to run more than once.
-- =====================================================================================================
IF COL_LENGTH('CIMS.BarcodeFormats', 'MatchBy') IS NULL
    ALTER TABLE CIMS.BarcodeFormats ADD MatchBy VARCHAR(10) NULL;
GO
IF COL_LENGTH('CIMS.BarcodeFormats', 'NameFields') IS NULL
    ALTER TABLE CIMS.BarcodeFormats ADD NameFields VARCHAR(40) NULL;
GO
PRINT 'CIMS.BarcodeFormats.MatchBy / NameFields ready.';
GO
