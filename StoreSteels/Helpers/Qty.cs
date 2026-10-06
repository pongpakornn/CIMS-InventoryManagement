using System;
using System.Globalization;

namespace CIMS.Helpers
{
    // 🔢 ยอดสินค้า: เก็บเป็น DECIMAL(18,3) ทุกคลัง
    //   คลังที่เปิด DECIMAL QTY (Stocks.AllowDecimal เช่น KG) -> เก็บ / แสดงทศนิยม (สูงสุด 3 ตำแหน่ง แสดง 1,234.50)
    //   คลังอื่น -> ปัดเป็นจำนวนเต็มเหมือนเดิม (แสดง 1,234)
    public static class Qty
    {
        public const int Decimals = 3;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ตัดศูนย์ท้ายที่มาจาก DECIMAL(18,3) ออก (613.000 -> 613, 12.500 -> 12.5)
        public static decimal Normalize(decimal v) => v / 1.000000000000000000000000000000000m;

        public static decimal Read(object dbValue) =>
            dbValue == null || dbValue == DBNull.Value ? 0m : Normalize(Convert.ToDecimal(dbValue, Inv));

        public static decimal? ReadNullable(object dbValue) =>
            dbValue == null || dbValue == DBNull.Value ? (decimal?)null : Normalize(Convert.ToDecimal(dbValue, Inv));

        // ปัดตามการตั้งค่าคลัง
        public static decimal Round(decimal v, bool allowDecimal) =>
            Normalize(Math.Round(v, allowDecimal ? Decimals : 0, MidpointRounding.AwayFromZero));

        // ข้อความแสดงผล: มีตัวคั่นหลักพัน (ตาราง / การ์ด / รายงาน)
        public static string Text(decimal v, bool allowDecimal) =>
            allowDecimal ? Round(v, true).ToString("#,0.00#", Inv) : Round(v, false).ToString("#,0", Inv);

        // ข้อความในช่องที่แก้ไขได้ (ไม่มีตัวคั่นหลักพัน)
        public static string Edit(decimal v, bool allowDecimal) =>
            allowDecimal ? Round(v, true).ToString("0.00#", Inv) : Round(v, false).ToString("0", Inv);

        public static string Text(object dbValue, bool allowDecimal, string empty = "-") =>
            dbValue == null || dbValue == DBNull.Value ? empty : Text(Convert.ToDecimal(dbValue, Inv), allowDecimal);

        // อ่านตัวเลขที่ผู้ใช้พิมพ์ / จากป้าย / จาก Excel (รับ 1,234.5 และ 1234.5)
        public static bool TryParse(string s, out decimal v)
        {
            s = (s ?? "").Trim().Replace(",", "");
            return decimal.TryParse(s, NumberStyles.Number, Inv, out v)
                || decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out v);
        }

        // ข้อความใน Log / แจ้งเตือน (ไม่มีศูนย์ท้าย)
        public static string Plain(decimal v) => Normalize(v).ToString("#,0.###", Inv);
    }

    // พารามิเตอร์ SQL ของยอด = DECIMAL(18,3) ตรงกับคอลัมน์ / Procedure (ไม่ให้ SqlClient เดาชนิดเอง)
    public static class QtyParam
    {
        public static Microsoft.Data.SqlClient.SqlParameter Of(string name, decimal value) =>
            new Microsoft.Data.SqlClient.SqlParameter(name, System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = value };

        public static Microsoft.Data.SqlClient.SqlParameter Of(string name, decimal? value) =>
            new Microsoft.Data.SqlClient.SqlParameter(name, System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 3, Value = (object)value ?? DBNull.Value };
    }
}
