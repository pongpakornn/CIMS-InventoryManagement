-- Update_20261010d.sql
-- Product cards above the Store (Max-Min) table, per stock:
--   * CIMS.Stocks.CardStyle: 'IMAGE' = card with the product image
--                            'TEXT'  = card without an image (big numbers, for stocks that have no pictures)
--                            'OFF'   = no cards
--                            NULL    = as before (IMAGE when the stock shows the IMAGE column, otherwise OFF)
-- Adds the column only - nothing else changes.

IF COL_LENGTH('CIMS.Stocks', 'CardStyle') IS NULL
    ALTER TABLE CIMS.Stocks ADD CardStyle NVARCHAR(10) NULL;
GO
