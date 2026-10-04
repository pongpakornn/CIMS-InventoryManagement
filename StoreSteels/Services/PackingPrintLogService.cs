using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;

namespace CIMS.Services
{
    // ประวัติการพิมพ์ Packing Card เก็บไว้ที่ฐานของเราเอง (Stock DB / GlobalConfig.ConnStr)
    // คนละฐานกับ ERP - ตาราง dbo.PackingPrintLog (ดู Database/PackingPrintLog.sql สำหรับสร้างตาราง)
    public class PackingPrintLogService
    {
        private readonly string _connStr = GlobalConfig.ConnStr;

        public static string MakeKey(string ticketNo, string itemNo)
            => $"{(ticketNo ?? "").Trim()}|{(itemNo ?? "").Trim()}";

        // ใช้กรองรายการที่พิมพ์ไปแล้วออกจาก ERP (แทนการทำ NOT EXISTS ข้ามเซิร์ฟเวอร์ ซึ่ง ERP กับ Stock
        // DB อยู่คนละเครื่องกัน ทำ cross-server query ตรงๆ ไม่ได้ถ้าไม่ตั้ง Linked Server)
        public HashSet<string> GetPrintedKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            const string sql = "SELECT TicketNo, ItemNo FROM dbo.PackingPrintLog";

            using (var conn = new SqlConnection(_connStr))
            using (var cmd = new SqlCommand(sql, conn))
            {
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        keys.Add(MakeKey(rdr["TicketNo"].ToString(), rdr["ItemNo"].ToString()));
                    }
                }
            }

            return keys;
        }

        // ป้องกัน "String or binary data would be truncated" เผื่อฐานที่ deploy จริงยังเป็นสคีมาเดิม
        // (LabelRef VARCHAR(50) ก่อนที่จะขยายเป็น 200 ใน PackingPrintLog.sql) - ตัดให้พอดี 50 เสมอ
        private const int LabelRefMaxLength = 50;

        public void LogPrinted(PackingCardModel item, string userId)
        {
            int.TryParse(item.ItemNo, out int itemNoValue);

            string labelRef = item.QrText ?? "";
            if (labelRef.Length > LabelRefMaxLength)
            {
                labelRef = labelRef.Substring(0, LabelRefMaxLength);
            }

            const string sql = @"
                IF NOT EXISTS (SELECT 1 FROM dbo.PackingPrintLog WHERE TicketNo = @TicketNo AND ItemNo = @ItemNo)
                INSERT INTO dbo.PackingPrintLog
                    (TicketNo, ItemNo, Warehouse, LotNo, MaterialCode, WorkOrder, TicketDate, JobName, Qty, PrintedBy, LabelRef)
                VALUES
                    (@TicketNo, @ItemNo, @Warehouse, @LotNo, @MaterialCode, @WorkOrder, @TicketDate, @JobName, @Qty, @PrintedBy, @LabelRef)";

            using (var conn = new SqlConnection(_connStr))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@TicketNo", item.TicketNo ?? "");
                cmd.Parameters.AddWithValue("@ItemNo", itemNoValue);
                cmd.Parameters.AddWithValue("@Warehouse", (object)item.GroupCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LotNo", (object)item.LotNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MaterialCode", item.MaterialCode ?? "");
                cmd.Parameters.AddWithValue("@WorkOrder", (object)item.WorkOrder ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TicketDate", item.TicketDate == DateTime.MinValue ? (object)DBNull.Value : item.TicketDate.Date);
                cmd.Parameters.AddWithValue("@JobName", (object)item.JobName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Qty", item.Qty);
                cmd.Parameters.AddWithValue("@PrintedBy", userId ?? "Unknown");
                cmd.Parameters.AddWithValue("@LabelRef", string.IsNullOrEmpty(labelRef) ? (object)DBNull.Value : labelRef);

                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
