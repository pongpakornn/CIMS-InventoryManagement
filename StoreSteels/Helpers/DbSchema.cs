using CIMS.Core;
using Microsoft.Data.SqlClient;
using System;

namespace CIMS.Helpers
{
    // ตรวจว่าฐานข้อมูลรัน Script อัพเดทแล้วหรือยัง (เช็กครั้งเดียวต่อการเปิดโปรแกรม)
    // ยังไม่รัน -> โปรแกรมเวอร์ชันใหม่ยังเปิดใช้กับฐานเดิมได้ (ฟีเจอร์ใหม่ปิดไว้ก่อน) แทนที่จะ Error ทั้งโปรแกรม
    public static class DbSchema
    {
        private static bool? _allowDecimal;
        private static bool? _liveSource;
        private static bool? _countCoil;
        private static bool? _columnOrder;
        private static bool? _coilRegister;

        // Update_20261015.sql: CIMS.UserScanStocks (คลังสแกนที่ Admin กำหนดให้แต่ละ UserID) / CIMS.AdminContacts (ผู้ติดต่อ Admin)
        private static bool? _userScanStocks, _adminContacts;
        public static bool HasUserScanStocks => _userScanStocks ?? (_userScanStocks = TableExists("CIMS.UserScanStocks")) ?? false;
        public static bool HasAdminContacts => _adminContacts ?? (_adminContacts = TableExists("CIMS.AdminContacts")) ?? false;

        // Update_20261016.sql: CIMS.UserScanStocks.ShowDeduct (ปุ่ม DEDUCTED / NOT DEDUCTED ตาม UserID + คลัง)
        private static bool? _scanDeduct;
        public static bool HasScanDeduct
        {
            get
            {
                if (_scanDeduct.HasValue) return _scanDeduct.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('CIMS.UserScanStocks', 'ShowDeduct') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _scanDeduct = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _scanDeduct.Value;
            }
        }

        // Update_20261016b.sql: CIMS.DeductMisses.Status (PENDING = ยังไม่รับเข้า รอ APPROVE / RECEIVED / APPROVED / REGISTERED)
        private static bool? _missStatus;
        public static bool HasMissStatus
        {
            get
            {
                if (_missStatus.HasValue) return _missStatus.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('CIMS.DeductMisses', 'Status') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _missStatus = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _missStatus.Value;
            }
        }

        // Update_20261016c.sql: CIMS.Parts.PartB + CIMS.Stocks.ShowColPartB (PART B) / CIMS.ProductControlSettings (ตารางหน้า Inventory Registration + QR ต่อคลัง)
        private static bool? _partB, _pcSettings;
        public static bool HasPartB
        {
            get
            {
                if (_partB.HasValue) return _partB.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('CIMS.Parts', 'PartB') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'ShowColPartB') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _partB = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _partB.Value;
            }
        }
        // Update_20261017.sql: PR ตามคลัง (CIMS.PRHeaders.StockID / PRDetails.PartID / Stocks.PRExportPath)
        private static bool? _prStock;
        public static bool HasPrStock
        {
            get
            {
                if (_prStock.HasValue) return _prStock.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('CIMS.PRHeaders', 'StockID') IS NOT NULL AND COL_LENGTH('CIMS.PRDetails', 'PartID') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'PRExportPath') IS NOT NULL AND COL_LENGTH('CIMS.Stocks', 'PRExcelMap') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _prStock = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _prStock.Value;
            }
        }

        public static bool HasProductControlSettings => _pcSettings ?? (_pcSettings = TableExists("CIMS.ProductControlSettings")) ?? false;

        private static bool? TableExists(string name)
        {
            try
            {
                using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@n, 'U') IS NOT NULL THEN 1 ELSE 0 END", conn))
                {
                    cmd.Parameters.AddWithValue("@n", name);
                    conn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                }
            }
            catch { return null; }   // ต่อฐานไม่ได้ = ถามใหม่รอบหน้า
        }

        // Update_20261014.sql: CIMS.PickListPrintLogs.PrintCount / LastPrintedBy / LastPrintedDate (นับการพิมพ์ซ้ำ)
        private static bool? _printCount;
        public static bool HasPickListPrintCount
        {
            get
            {
                if (_printCount.HasValue) return _printCount.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('CIMS.PickListPrintLogs', 'PrintCount') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _printCount = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _printCount.Value;
            }
        }

        // Update_20261013.sql: CIMS.UserScanDisplays (DISPLAY ของหน้าสแกน แยกตามผู้ใช้)
        private static bool? _userScanDisplays;
        public static bool HasUserScanDisplays
        {
            get
            {
                if (_userScanDisplays.HasValue) return _userScanDisplays.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('CIMS.UserScanDisplays', 'U') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _userScanDisplays = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _userScanDisplays.Value;
            }
        }

        // Update_20261008.sql: ทะเบียน Coil (CIMS.Coils / CIMS.CoilMoves + BarcodeFormats.CoilNoPosition)
        public static bool HasCoilRegister
        {
            get
            {
                if (_coilRegister.HasValue) return _coilRegister.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('CIMS.Coils', 'U') IS NOT NULL AND COL_LENGTH('CIMS.BarcodeFormats', 'CoilNoPosition') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _coilRegister = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _coilRegister.Value;
            }
        }

        // Update_20261010b.sql: CIMS.DeductMisses (ป้ายที่รับเข้าแล้วแต่ตัดคลังต้นทางไม่ได้ / ได้ไม่ครบ)
        private static bool? _deductMisses;
        public static bool HasDeductMisses
        {
            get
            {
                if (_deductMisses.HasValue) return _deductMisses.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('CIMS.DeductMisses', 'U') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _deductMisses = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _deductMisses.Value;
            }
        }

        // Update_20261010c.sql: CIMS.StockTransfers.ScanTransactionID (รายการ DEDUCTED ผูกกับรายการแสกน)
        private static bool? _transferScanTx;
        public static bool HasTransferScanTx
        {
            get
            {
                if (_transferScanTx.HasValue) return _transferScanTx.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN COL_LENGTH('CIMS.StockTransfers', 'ScanTransactionID') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _transferScanTx = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _transferScanTx.Value;
            }
        }

        // Update_20261010d.sql: CIMS.Stocks.CardStyle (การ์ดสินค้า มีรูป / ไม่มีรูป / ปิด)
        private static bool? _cardStyle;
        public static bool HasCardStyle => _cardStyle ?? (_cardStyle = ColumnExists("CardStyle")) ?? false;

        // Update_20261009.sql: CIMS.Stocks.MaxMinDecimal (MAX / MIN แสดง / เก็บทศนิยม แยกจาก DECIMAL QTY)
        private static bool? _maxMinDecimal;
        public static bool HasMaxMinDecimal => _maxMinDecimal ?? (_maxMinDecimal = ColumnExists("MaxMinDecimal")) ?? false;

        // Update_20261008b.sql: CIMS.Stocks.ShowCoilRows (กด PD CODE แล้วแสดงแถว Coil ย่อย - ตั้งได้ตอนสร้าง / แก้คลัง)
        private static bool? _coilRows;
        public static bool HasCoilRows => _coilRows ?? (_coilRows = ColumnExists("ShowCoilRows")) ?? false;

        // Update_20261006d.sql: CIMS.Stocks.ColumnOrder (ลำดับคอลัมน์ตามที่ติ๊กเลือก)
        public static bool HasColumnOrder => _columnOrder ?? (_columnOrder = ColumnExists("ColumnOrder")) ?? false;

        // Update_20261006c.sql: CIMS.Stocks.CountCoil + CIMS.Parts.CoilQuantity (QTY (COIL) ของคลัง KG)
        public static bool HasCountCoil => _countCoil ?? (_countCoil = ColumnExists("CountCoil")) ?? false;

        // Update_20261006b.sql: CIMS.Stocks.LiveSource (คลังที่แสดงข้อมูลสดจาก StorePC ชั่วคราว)
        public static bool HasLiveSource => _liveSource ?? (_liveSource = ColumnExists("LiveSource")) ?? false;

        private static bool? ColumnExists(string column)
        {
            try
            {
                using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                using (var cmd = new SqlCommand("SELECT COL_LENGTH('CIMS.Stocks', @c)", conn))
                {
                    cmd.Parameters.AddWithValue("@c", column);
                    conn.Open();
                    object v = cmd.ExecuteScalar();
                    return v != null && v != DBNull.Value;
                }
            }
            catch { return null; }   // ต่อฐานไม่ได้ตอนนี้ -> ลองใหม่ครั้งหน้า
        }

        // Update_20261006.sql: CIMS.Stocks.AllowDecimal (DECIMAL QTY) - ไม่มี = ทุกคลังเป็นจำนวนเต็มเหมือนเดิม
        public static bool HasAllowDecimal
        {
            get
            {
                if (_allowDecimal.HasValue) return _allowDecimal.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT COL_LENGTH('CIMS.Stocks', 'AllowDecimal')", conn))
                    {
                        conn.Open();
                        object v = cmd.ExecuteScalar();
                        _allowDecimal = v != null && v != DBNull.Value;
                    }
                }
                catch { return false; }   // ต่อฐานไม่ได้ตอนนี้ -> ลองใหม่ครั้งหน้า
                return _allowDecimal.Value;
            }
        }
    }
}
