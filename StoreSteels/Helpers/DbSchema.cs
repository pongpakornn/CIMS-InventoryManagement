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
