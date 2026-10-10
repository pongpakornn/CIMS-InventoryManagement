# CIMS database scripts

Since October 2026 every table, column, view, procedure and trigger uses a full name in the `CIMS` schema
(for example `dbo.MST_PART.PT_CODE` became `CIMS.Parts.PartCode`, `dbo.SYS_LOGS` became `CIMS.SystemLogs`).
The program uses only the new names.

| File | Purpose |
|---|---|
| `RenameToFullNames.sql` | Renames an old-style database to the new names and creates the views / procedures / triggers. Safe to run more than once. |
| `NameMap.tsv` | Full list of old name → new name (tables, views, procedures, columns). |
| `Legacy\*.sql` | The old migration scripts (old names). Only for building a database from scratch. |

## Old-style database (still has MST_ / TRN_ tables)

1. Bring it up to date with the legacy scripts: `Legacy\MultiStock.sql`, `Legacy\MaxMinCalc.sql`, `Legacy\PRView.sql`.
2. Back up the database, then run `RenameToFullNames.sql`.

The legacy scripts expect the original StoreSteels base tables. For a brand-new server, copy the schema
from an existing CIMS database (backup / restore) instead.

## Existing database (already renamed)

Run the dated update scripts in order (each one is safe to run more than once):

| File | Purpose |
|---|---|
| `Performance_20261005.sql` | Extra indexes for the scanner, history, PR and log screens. |
| `Update_20261005.sql` | `Stocks.ScanDisplayField` (Multi-Scanner DISPLAY setting) and the rolling 3-month history triggers on ScanTransactions, ScanAdjustments and SystemLogs. |
| `Update_20261005b.sql` | `BarcodeFormats.MatchBy` / `NameFields` (find the product by PRODUCT NAME from the label). |
| `Update_20261005c.sql` | Store(Max-Min) can group a stock by SUPPLIER (`vw_StockMonitoring.GroupKey`, kept as NVARCHAR(200) so the Store page does not wait for a large memory grant). |
| `Update_20261006.sql` | Quantities can have decimals: `Stocks.AllowDecimal` (DECIMAL QTY per stock, KG stocks start ON) and every quantity / balance / MAX / MIN column INT → DECIMAL(18,3). Box counts stay whole numbers. **Back up first.** Without this script the program still opens and every stock works in whole numbers (DECIMAL QTY stays off). |
| `Update_20261006b.sql` | **Temporary:** STORE-PC shows the old StorePC program's data live (`Stocks.LiveSource = 'StorePC'`, `vw_StorePcLive`, images from the StorePC image folder). Read only in CIMS. Stop it with `UPDATE CIMS.Stocks SET LiveSource = NULL WHERE StockCode = 'STORE-PC'`. |
| `Update_20261006c.sql` | STOCK (COIL) for KG stocks: `Stocks.CountCoil` (KG stocks start ON), `Parts.CoilQuantity` (main stock coils; other stocks use `PartStocks.BoxQuantity`), box trigger skips coil stocks, coil stocks start at 0 coils (first run only, KG untouched), `StockTransfers.SourcePartID` (deduct by BIN). Needs `Update_20261006b.sql` first. |
| `Update_20261006d.sql` | MAX / MIN UNIT = COIL (compared with STOCK (COIL); Max-Min Calculator uses PACK SIZE as KG per coil, like BOX) and `Stocks.ColumnOrder` (Store table columns in the order they are ticked). Needs `Update_20261006c.sql` first. |
| `Update_20261008.sql` | COIL register: `CIMS.Coils` (one row per child coil: mother coil, product, stock, weight KG, IN / OUT) and `CIMS.CoilMoves` (every change, linked to the scan so ADJUST SCAN can undo it), `BarcodeFormats.CoilNoPosition` / `MotherCoilPosition` (label fields for the child / mother coil). Adds tables and columns only. Without it the program works as before (no coil detail). |
| `Update_20261008b.sql` | COIL SUB ROWS options per stock: `Stocks.ShowCoilRows` (click PD CODE shows the coils; NULL = follow STOCK (COIL)) and `Stocks.CoilRowColumns` (which sub row columns show: COILNO, MOTHER, WEIGHT, COIL, TON, RECEIVED; NULL = all). Adds columns only. |
| `Update_20261009.sql` | MAX / MIN DECIMAL per stock: `Stocks.MaxMinDecimal` (1 = MAX / MIN with decimals, 0 = whole numbers, NULL = follow DECIMAL QTY). Adds one column only. |
| `Update_20261010.sql` | Panta labels into STOCK-MAT use only **Panta-2**: product by name #6 + #7 (closest match), qty #9, child coil #1, mother coil #11, deducts the mother coil's weight from STOCK-PANTA. Deletes Panta-3, switches Panta off. Data only. |
| `Update_20261010b.sql` | NOT DEDUCTED list: new table `CIMS.DeductMisses` - labels received with SCAN IN whose mother coil could not be cut (not found / not enough weight), saved automatically by the scanner. Adds one table only. |
| `Update_20261010c.sql` | DEDUCTED list: `StockTransfers.ScanTransactionID` links each source-stock cut to its SCAN IN (older SCAN transfers linked by product / stock / time). Adds one column + index. |
| `Update_20261010d.sql` | Product cards per stock: `Stocks.CardStyle` (`IMAGE` card with picture, `TEXT` card without picture, `OFF` no cards; NULL = as before: IMAGE when the stock shows the IMAGE column). Adds one column. |
| `ClearOperationalData.sql` | Clears transactions / logs / stock quantities while keeping users, permissions and setup. Only run it when you really want to reset data. |
