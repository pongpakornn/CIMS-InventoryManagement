-- =====================================================================================================
-- CIMS update 2026-10-08 (b): COIL SUB ROWS options per stock
--   * CIMS.Stocks.ShowCoilRows: click PD CODE in Store (Max-Min) shows the product's coils as sub rows
--     NULL = follow STOCK (COIL) (coil stocks ON), 1 = ON, 0 = OFF
--   * CIMS.Stocks.CoilRowColumns: what the sub rows show, comma list of
--     COILNO, MOTHER, WEIGHT, COIL, TON, RECEIVED  (NULL = all)
--   Adds columns only. Safe to run more than once.
-- =====================================================================================================
IF COL_LENGTH('CIMS.Stocks', 'ShowCoilRows') IS NULL
    ALTER TABLE CIMS.Stocks ADD ShowCoilRows BIT NULL;
GO
IF COL_LENGTH('CIMS.Stocks', 'CoilRowColumns') IS NULL
    ALTER TABLE CIMS.Stocks ADD CoilRowColumns NVARCHAR(200) NULL;
GO
