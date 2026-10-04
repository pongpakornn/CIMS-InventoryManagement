// LogService.cs : บันทึกการทำงานของผู้ใช้งานในระบบ เก็บรายละเอียดทุกการเคลื่อนไหว (SYS_LOGS -> หน้า ACTIVITY LOG)

using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Threading.Tasks;

namespace CIMS.Services
{
    public static class LogService
    {
        // ชื่อเครื่องที่ใช้งาน (ดูได้ในหน้า ACTIVITY LOG ว่าทำจากเครื่องไหน)
        private static readonly string Machine = SafeMachineName();
        private static bool _hasPcColumn = true;

        private static string SafeMachineName()
        {
            try { return Environment.MachineName; } catch { return null; }
        }

        // บันทึกเบื้องหลัง ไม่ให้หน้าจอรอ / Log ล้มเหลวไม่กระทบการทำงานหลัก
        private static void Insert(string userId, string action, string detail, string refCode)
        {
            Task.Run(() =>
            {
                try
                {
                    using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                    {
                        conn.Open();
                        try
                        {
                            Exec(conn, _hasPcColumn, userId, action, detail, refCode);
                        }
                        catch (SqlException ex) when (_hasPcColumn && ex.Number == 207)   // ฐานเก่ายังไม่มีคอลัมน์ LOG_PC
                        {
                            _hasPcColumn = false;
                            Exec(conn, false, userId, action, detail, refCode);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Log Error: " + ex.Message);
                }
            });
        }

        private static void Exec(SqlConnection conn, bool withPc, string userId, string action, string detail, string refCode)
        {
            string sql = withPc
                ? "INSERT INTO SYS_LOGS (USR_ID, ACT_TYPE, LOG_DESC, LOG_REF, LOG_DATE, LOG_PC) VALUES (@user, @action, @detail, @ref, GETDATE(), @pc)"
                : "INSERT INTO SYS_LOGS (USR_ID, ACT_TYPE, LOG_DESC, LOG_REF, LOG_DATE) VALUES (@user, @action, @detail, @ref, GETDATE())";
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@user", (object)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@action", (object)action ?? "");
                cmd.Parameters.AddWithValue("@detail", (object)detail ?? "");
                cmd.Parameters.AddWithValue("@ref", (object)refCode ?? "");
                if (withPc) cmd.Parameters.AddWithValue("@pc", (object)Machine ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public static void WriteLog(string userId, string action, string detail, string refCode)
            => Insert(userId, action, detail, refCode);

        public static void WriteUpdateStkLog(string userId, string part, string max, string min, string remark)
        {
            // ครอบด้วย Task.Run เพื่อให้มันทำงานข้างหลัง ไม่ไปขวางหน้าจอ
            Task.Run(() =>
            {
                try
                {
                    using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                    {
                        string sql = @"INSERT INTO STK_UPDATE_LOG (USR_ID, LOG_PART, LOG_MAX, LOG_MIN, LOG_REMARK)
                             VALUES (@uid, @part, @max, @min, @remark)";

                        using (SqlCommand cmd = new SqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@uid", userId);
                            cmd.Parameters.AddWithValue("@part", part);

                            int.TryParse(max, out int maxVal);
                            int.TryParse(min, out int minVal);

                            cmd.Parameters.AddWithValue("@max", maxVal);
                            cmd.Parameters.AddWithValue("@min", minVal);
                            cmd.Parameters.AddWithValue("@remark", (object)remark ?? DBNull.Value);

                            conn.Open();
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Log Error: {ex.Message}");
                }
            });
            Insert(userId, "UPDATE_STK", $"MAX: {max} | MIN: {min} | Remark: {remark}", part);
        }

        public static void WriteQRLog(string userId, string partCode, string actionType)
            => Insert(userId, "QR_SYSTEM", $"{actionType} for Part: {partCode}", partCode);

        public static void WritePRLog(string userId, string actionType, string description, string reference)
            => Insert(userId, "PR_SYSTEM", description, reference);

        // ✅ งานแสกน (Scan In / Scan Out)
        // rawBarcode (ไม่บังคับ): บาร์โค้ดดิบทั้งชุด เช่นป้าย Panta - ต่อท้ายใน LOG_DESC (nvarchar(max)) ให้ย้อนดูได้ครบ
        public static void WriteScanLog(string userId, string actionType, string partCode, string partName, int qty, string rawBarcode = null)
        {
            string description = $"[{actionType}] PD: {partName} | QTY: {qty}"
                + (string.IsNullOrWhiteSpace(rawBarcode) ? "" : $" | BARCODE: {rawBarcode.Trim()}");
            Insert(userId, actionType, description, partCode ?? "");
        }
    }
}
