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
| `ClearOperationalData.sql` | Clears transactions / logs / stock quantities while keeping users, permissions and setup. Only run it when you really want to reset data. |
