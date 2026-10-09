-- =====================================================================================================
-- CIMS update 2026-10-09: MAX / MIN DECIMAL per stock
--   * CIMS.Stocks.MaxMinDecimal: show / keep MAX and MIN with decimals (1 = 5.50, 0 = 6)
--     NULL = follow DECIMAL QTY (AllowDecimal) as before
--   Adds one column only. Safe to run more than once.
-- =====================================================================================================
IF COL_LENGTH('CIMS.Stocks', 'MaxMinDecimal') IS NULL
    ALTER TABLE CIMS.Stocks ADD MaxMinDecimal BIT NULL;
GO
