-- Update_20261017b.sql
-- Inventory Registration > ADMIN > QR CODE: two formats per stock (StockID 0 = ALL STOCKS)
--   * QrFields   : what the QR code holds = what the scanner reads, fields joined by | in this order
--                  e.g. CODE,NAME,PACKSIZE -> I2AB-01002|ถุงพลาสติก ...|100 (PRODUCT CODE must be one of them)
--   * QrLabel    : the text printed under the QR code in the PDF, one line per field in this order (first line bold)
--   Field keys: SUPPLIER, CUSTOMER, CATEGORY, PARTA, PARTB, MODEL, CODE, PARTNO, NAME, PACKSIZE, BIN, MOTHER, COILNO
-- Existing settings carry over: QrContent CODE -> QrFields CODE / CODE_NAME -> CODE,NAME; QrLabel = CODE,NAME (as printed today).
-- QrContent stays filled for older app versions. Adds two columns - other data is not changed. Safe to run again.

IF COL_LENGTH('CIMS.ProductControlSettings', 'QrFields') IS NULL
    ALTER TABLE CIMS.ProductControlSettings ADD QrFields VARCHAR(300) NULL;
GO
IF COL_LENGTH('CIMS.ProductControlSettings', 'QrLabel') IS NULL
    ALTER TABLE CIMS.ProductControlSettings ADD QrLabel VARCHAR(300) NULL;
GO

UPDATE CIMS.ProductControlSettings
SET QrFields = CASE WHEN QrContent = 'CODE' THEN 'CODE' ELSE 'CODE,NAME' END
WHERE QrFields IS NULL;

UPDATE CIMS.ProductControlSettings
SET QrLabel = 'CODE,NAME'
WHERE QrLabel IS NULL;
GO

SELECT StockID, QrContent, QrFields, QrLabel FROM CIMS.ProductControlSettings ORDER BY StockID;
