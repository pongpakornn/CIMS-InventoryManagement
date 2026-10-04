using Microsoft.Data.SqlClient;
using CIMS.Models;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.Data;


namespace CIMS.Services
{
    public class PRService
    {
        private readonly string _connectionString = GlobalConfig.ConnStr;

        public List<PRModel> GetPRList(string searchText = "")
        {
            var list = new List<PRModel>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                using (SqlCommand cmd = new SqlCommand("sp_GetPRList", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@SearchText", searchText ?? "");

                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new PRModel
                            {
                                PR_NO = reader["PR_NO"].ToString(),
                                USR_ID = reader["USR_ID"].ToString(),
                                Requester = reader["REQUESTER"].ToString(), // จะได้ 'N/A' แทน NULL เพราะ ISNULL ใน View
                                PartCode = reader["PT_CODE"]?.ToString() ?? "N/A", // เพิ่มบรรทัดนี้เพื่อรับค่า Code
                                PartName = reader["PT_DESC"].ToString(),
                                QTY = reader["QTY_REQ"] != DBNull.Value ? Convert.ToInt32(reader["QTY_REQ"]) : 0,
                                Department = reader["REQ_DEPT"].ToString(),
                                Status = reader["PR_STAT"].ToString(),
                                PR_DATE = Convert.ToDateTime(reader["PR_DATE"]),
                                PR_REM = reader["PR_REM"]?.ToString(),
                                Unit = reader["QTY_UNIT"]?.ToString()
                            });
                        }
                    }
                }
            }
            return list;
        }

        public string GetNextPRNo()
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // 1. ดึงเลขปี ค.ศ. 2 หลักสุดท้าย (เช่น 2026 -> 26)
                string year = DateTime.Now.ToString("yy", System.Globalization.CultureInfo.InvariantCulture);
                string prefix = year + "PR"; // ผลลัพธ์: 26PR

                // 2. ค้นหาเลขล่าสุดที่ขึ้นต้นด้วย 26PR
                string sql = "SELECT TOP 1 PR_NO FROM TRN_PR_H WHERE PR_NO LIKE @Prefix + '%' ORDER BY PR_NO DESC";

                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Prefix", prefix);
                    var result = cmd.ExecuteScalar();

                    if (result == null || result == DBNull.Value)
                    {
                        // ถ้ายังไม่มีเลยในฐานข้อมูล เริ่มต้นที่ 00000001
                        return prefix + "00000001";
                    }
                    else
                    {
                        // ถ้ามีแล้ว (เช่น 26PR00000005) ให้ตัดเอาเลข 8 หลักสุดท้ายมาบวก 1
                        // เปลี่ยนจาก Substring(4) เป็นการเอา 8 หลักสุดท้าย
                        string lastNo = result.ToString().Trim();
                        if (lastNo.Length >= 8)
                        {
                            string lastDigitStr = lastNo.Substring(lastNo.Length - 8); // เอา 8 หลักท้ายแน่นอน
                            if (int.TryParse(lastDigitStr, out int lastDigit))
                            {
                                return prefix + (lastDigit + 1).ToString("D8");
                            }
                        }
                        return prefix + "00000001"; // Fallback กรณี Parse ไม่ได้
                    }
                }
            }
        }

        // Approve

        public bool UpdatePRStatus(string prNo, string newStatus, string approverId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql;

                if (newStatus == "Rejected")
                {
                    // ลบตามลำดับ: ลบรายการสินค้า (D) ก่อน แล้วค่อยลบหัวเอกสาร (H)
                    sql = @"DELETE FROM TRN_PR_D WHERE PR_NO = @PrNo;
                    DELETE FROM TRN_PR_H WHERE PR_NO = @PrNo;";
                }
                else
                {
                    // ถ้า Approved หรือสถานะอื่น ให้ Update ปกติ
                    sql = @"UPDATE TRN_PR_H 
                    SET PR_STAT = @Status, 
                        APP_USR_ID = CASE WHEN @Status = 'Approved' THEN @AppUserId ELSE NULL END, 
                        APP_DATE = CASE WHEN @Status = 'Approved' THEN GETDATE() ELSE NULL END,
                        IS_EXPORT = CASE WHEN @Status = 'Approved' THEN 'Y' ELSE 'N' END
                    WHERE PR_NO = @PrNo";
                }

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Status", newStatus);
                    cmd.Parameters.AddWithValue("@AppUserId", approverId);
                    cmd.Parameters.AddWithValue("@PrNo", prNo);

                    int rows = cmd.ExecuteNonQuery();
                    // หมายเหตุ: การลบ 2 ตารางพร้อมกันแบบนี้ rows ที่ได้จะเป็นจำนวนแถวรวมที่ถูกลบ
                    return rows > 0;
                }
            }
        }

        // requesterName: ชื่อผู้ขอที่พิมพ์เอง (ว่าง = ใช้ชื่อผู้ใช้ที่ Login) - USR_ID ยังเป็นคนที่ทำรายการเหมือนเดิม
        public bool InsertPR(string prNo, string userId, string dept, string partDesc, int qty, string remark, string targetDept, string requesterName = null)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        // เปลี่ยน 'Waiting' เป็น @Status เพื่อให้ตรงกับ Parameters.Add ด้านล่าง
                        string sqlHeader = @"INSERT INTO TRN_PR_H (PR_NO, PR_DATE, USR_ID, REQ_DEPT, TRG_DEPT, PR_STAT, PR_REM, REQ_NAME, PR_SOURCE)
                                   VALUES (@PrNo, GETDATE(), @UserId, @Dept, @TrgDept, @Status, @Remark, @ReqName, 'MANUAL')";

                        string sqlDetail = @"INSERT INTO TRN_PR_D (PR_NO, PT_DESC, QTY_REQ) 
                                   VALUES (@PrNo, @PartDesc, @Qty)";

                        using (SqlCommand cmdH = new SqlCommand(sqlHeader, conn, trans))
                        {
                            cmdH.Parameters.AddWithValue("@PrNo", prNo);
                            cmdH.Parameters.AddWithValue("@UserId", userId);
                            cmdH.Parameters.AddWithValue("@Dept", dept);
                            cmdH.Parameters.AddWithValue("@TrgDept", targetDept ?? "");
                            cmdH.Parameters.AddWithValue("@Remark", remark ?? "");
                            cmdH.Parameters.AddWithValue("@Status", "Waiting"); //
                            cmdH.Parameters.AddWithValue("@ReqName", string.IsNullOrWhiteSpace(requesterName) ? (object)DBNull.Value : requesterName.Trim());
                            cmdH.ExecuteNonQuery();
                        }

                        using (SqlCommand cmdD = new SqlCommand(sqlDetail, conn, trans))
                        {
                            cmdD.Parameters.AddWithValue("@PrNo", prNo);
                            cmdD.Parameters.AddWithValue("@PartDesc", partDesc);
                            cmdD.Parameters.AddWithValue("@Qty", qty);
                            cmdD.ExecuteNonQuery();
                        }

                        trans.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Insert PR Error: {ex.Message}");
                        trans.Rollback();
                        return false;
                    }
                }
            }
        }

        #region === [ Lookup ช่องพิมพ์เอง (Requester / Department / Remark / Target Dept) ] ===
        // field: REQUESTER / DEPT / REMARK / TARGET (MST_PR_LOOKUP ใน Database/PRView.sql)

        // ค่าที่เคยพิมพ์ไว้ ที่มีคำที่พิมพ์อยู่ (ว่าง = ทั้งหมด) เรียงจากใช้บ่อย/ล่าสุดก่อน
        public List<string> GetLookupValues(string field, string text, int top = 30)
        {
            var list = new List<string>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(@"SELECT TOP (@top) LOOKUP_VALUE FROM MST_PR_LOOKUP
                                                     WHERE FIELD = @field AND (@text = '' OR LOOKUP_VALUE LIKE '%' + @text + '%')
                                                     ORDER BY USE_COUNT DESC, LAST_USED DESC", conn))
            {
                cmd.Parameters.AddWithValue("@top", top);
                cmd.Parameters.AddWithValue("@field", field);
                cmd.Parameters.AddWithValue("@text", (text ?? "").Trim());
                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                    while (rdr.Read()) list.Add(rdr.GetString(0));
            }
            return list;
        }

        // จำค่าที่พิมพ์ (เพิ่มใหม่ หรือเพิ่มจำนวนครั้งที่ใช้) ไว้แนะนำครั้งต่อไป
        public void SaveLookupValues(IDictionary<string, string> values)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                foreach (var kv in values)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                    using (SqlCommand cmd = new SqlCommand("sp_PR_SaveLookup", conn) { CommandType = CommandType.StoredProcedure })
                    {
                        cmd.Parameters.AddWithValue("@Field", kv.Key);
                        cmd.Parameters.AddWithValue("@Value", kv.Value.Trim());
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        #endregion

        // เพิ่มฟังก์ชันนี้ใน PRService.cs
        public List<string> GetProductSuggestions(string searchText)
        {
            var suggestions = new List<string>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // ชื่อสินค้าที่ใกล้เคียงที่สุด 5 รายการ (ทุกคลัง): ค้นจากชื่อ / รหัส / Part No / Model
                // เรียง: ชื่อตรงเป๊ะ > ชื่อขึ้นต้นด้วยคำที่พิมพ์ > รหัสขึ้นต้น > มีคำที่พิมพ์อยู่ในชื่อ/รหัส แล้วชื่อสั้นก่อน
                string sql = @"SELECT TOP 5 PT_DESC FROM (
                                   SELECT PT_DESC,
                                          MIN(CASE WHEN PT_DESC = @Search THEN 0
                                                   WHEN PT_DESC LIKE @Search + '%' THEN 1
                                                   WHEN PT_CODE LIKE @Search + '%' THEN 2
                                                   ELSE 3 END) AS RANK_NO
                                   FROM MST_PART
                                   WHERE IS_ACTIVE = 1 AND ISNULL(PT_DESC, '') <> ''
                                     AND (PT_DESC LIKE '%' + @Search + '%' OR PT_CODE LIKE '%' + @Search + '%'
                                          OR ISNULL(PT_PARTNO, '') LIKE '%' + @Search + '%' OR ISNULL(PT_MODEL, '') LIKE '%' + @Search + '%')
                                   GROUP BY PT_DESC) x
                               ORDER BY RANK_NO, LEN(PT_DESC), PT_DESC";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchText);
                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            suggestions.Add(reader["PT_DESC"].ToString());
                        }
                    }
                }
            }
            return suggestions;
        }

        // ตรวจสอบว่าสินค้ามีอยู่ใน MST_PART หรือไม่
        public bool IsProductExists(string productName)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string sql = "SELECT COUNT(1) FROM MST_PART WHERE PT_DESC = @Name AND IS_ACTIVE = 1";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Name", productName);
                    conn.Open();
                    int count = (int)cmd.ExecuteScalar();
                    return count > 0;
                }
            }
        }

        public List<PRModel> GetPendingExportList()
        {
            var list = new List<PRModel>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // แนะนำให้ใส่เงื่อนไขดักไว้อีกชั้นใน SQL เลยครับนนท์ เพื่อความชัวร์
                string sql = "SELECT * FROM v_PRReady WHERE IS_EXPORT = 'Y' ORDER BY PR_NO ASC";

                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new PRModel
                            {
                                PR_NO = reader["PR_NO"].ToString(),
                                // ... (แมพค่าอื่นๆ ตามเดิม)
                            });
                        }
                    }
                }
            }
            return list;
        }
        #region === [ Approve แบบกำหนดยอด / รายการที่ Approve แล้ว ] ===

        // ✅ Approve: qty = ยอดที่อนุมัติ (เท่ากับที่ขอ = อนุมัติทั้งหมด)
        //    อนุมัติน้อยกว่าที่ขอ: PR นี้เปลี่ยนจำนวนเป็นยอดที่อนุมัติ / keepRemainder = สร้าง PR ใหม่ (Waiting) สำหรับยอดที่เหลือ
        //    คืนเลข PR ใหม่ของยอดที่เหลือ (null = ไม่ได้สร้าง)
        public string ApprovePR(string prNo, int qty, bool keepRemainder, string approverId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    int req;
                    using (var cmd = new SqlCommand("SELECT TOP 1 QTY_REQ FROM TRN_PR_D WITH (UPDLOCK) WHERE PR_NO = @p ORDER BY PR_D_ID", conn, tr))
                    {
                        cmd.Parameters.AddWithValue("@p", prNo);
                        object v = cmd.ExecuteScalar();
                        if (v == null || v == DBNull.Value) throw new InvalidOperationException("ไม่พบรายการ PR นี้ในระบบ (อาจถูกลบไปแล้ว)");
                        req = Convert.ToInt32(v);
                    }
                    if (qty <= 0 || qty > req) throw new InvalidOperationException($"ยอดที่อนุมัติต้องอยู่ระหว่าง 1 - {req}");

                    string newPr = null;
                    if (qty < req)
                    {
                        using (var cmd = new SqlCommand("UPDATE TRN_PR_D SET QTY_REQ = @q WHERE PR_NO = @p", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@q", qty);
                            cmd.Parameters.AddWithValue("@p", prNo);
                            cmd.ExecuteNonQuery();
                        }
                        if (keepRemainder)
                        {
                            string prefix = DateTime.Now.ToString("yy", System.Globalization.CultureInfo.InvariantCulture) + "PR";
                            using (var cmd = new SqlCommand(@"SELECT ISNULL(MAX(TRY_CAST(RIGHT(PR_NO, 8) AS INT)), 0) FROM TRN_PR_H WITH (UPDLOCK, HOLDLOCK)
                                                              WHERE PR_NO LIKE @x + '%' AND LEN(PR_NO) = 12", conn, tr))
                            {
                                cmd.Parameters.AddWithValue("@x", prefix);
                                newPr = prefix + (Convert.ToInt32(cmd.ExecuteScalar()) + 1).ToString("D8");
                            }
                            using (var cmd = new SqlCommand(@"
                                INSERT INTO TRN_PR_H (PR_NO, PR_DATE, USR_ID, REQ_DEPT, TRG_DEPT, PR_STAT, PR_REM, IS_EXPORT, CREATED_AT, REQ_NAME, PR_SOURCE)
                                SELECT @n, PR_DATE, USR_ID, REQ_DEPT, TRG_DEPT, N'Waiting', PR_REM, 'N', GETDATE(), REQ_NAME, ISNULL(PR_SOURCE, 'MANUAL')
                                FROM TRN_PR_H WHERE PR_NO = @p;
                                INSERT INTO TRN_PR_D (PR_NO, PT_DESC, QTY_REQ, QTY_UNIT)
                                SELECT TOP 1 @n, PT_DESC, @rest, QTY_UNIT FROM TRN_PR_D WHERE PR_NO = @p ORDER BY PR_D_ID;", conn, tr))
                            {
                                cmd.Parameters.AddWithValue("@n", newPr);
                                cmd.Parameters.AddWithValue("@p", prNo);
                                cmd.Parameters.AddWithValue("@rest", req - qty);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    using (var cmd = new SqlCommand(@"UPDATE TRN_PR_H SET PR_STAT = 'Approved', APP_USR_ID = @u, APP_DATE = GETDATE(), IS_EXPORT = 'Y' WHERE PR_NO = @p", conn, tr))
                    {
                        cmd.Parameters.AddWithValue("@u", (object)approverId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@p", prNo);
                        cmd.ExecuteNonQuery();
                    }
                    tr.Commit();
                    return newPr;
                }
            }
        }

        // 📋 รายการที่ Approve แล้ว: ช่วงวันที่ Approve + คำค้น (ค้นทุกช่อง เรียงตัวที่ใกล้เคียงที่สุดก่อน)
        public List<PRModel> GetApprovedList(string keyword, DateTime from, DateTime to)
        {
            var list = new List<PRModel>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"
                SELECT * FROM v_PRReady
                WHERE PR_STAT = 'Approved' AND ISNULL(APP_DATE, PR_DATE) >= @from AND ISNULL(APP_DATE, PR_DATE) < @to
                  AND (@k = '' OR PR_NO LIKE '%' + @k + '%' OR PT_DESC LIKE '%' + @k + '%' OR PT_CODE LIKE '%' + @k + '%'
                       OR REQUESTER LIKE '%' + @k + '%' OR ISNULL(REQ_DEPT, '') LIKE '%' + @k + '%' OR ISNULL(TRG_DEPT, '') LIKE '%' + @k + '%'
                       OR ISNULL(PR_REM, '') LIKE '%' + @k + '%')
                ORDER BY CASE WHEN @k = '' THEN 0
                              WHEN PR_NO = @k OR PT_CODE = @k OR PT_DESC = @k THEN 0
                              WHEN PR_NO LIKE @k + '%' OR PT_CODE LIKE @k + '%' OR PT_DESC LIKE @k + '%' THEN 1
                              WHEN REQUESTER LIKE @k + '%' OR ISNULL(REQ_DEPT, '') LIKE @k + '%' THEN 2 ELSE 3 END,
                         ISNULL(APP_DATE, PR_DATE) DESC, PR_NO DESC", conn))
            {
                cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = from.Date;
                cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = to.Date.AddDays(1);
                cmd.Parameters.Add("@k", SqlDbType.NVarChar, 200).Value = (keyword ?? "").Trim();
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new PRModel
                        {
                            PR_NO = r["PR_NO"].ToString(),
                            USR_ID = r["USR_ID"].ToString(),
                            Requester = r["REQUESTER"].ToString(),
                            PartCode = r["PT_CODE"]?.ToString() ?? "N/A",
                            PartName = r["PT_DESC"].ToString(),
                            QTY = r["QTY_REQ"] != DBNull.Value ? Convert.ToInt32(r["QTY_REQ"]) : 0,
                            Unit = r["QTY_UNIT"]?.ToString(),
                            Department = r["REQ_DEPT"].ToString(),
                            TargetDept = r["TRG_DEPT"]?.ToString(),
                            Status = r["PR_STAT"].ToString(),
                            PR_DATE = Convert.ToDateTime(r["PR_DATE"]),
                            PR_REM = r["PR_REM"]?.ToString(),
                            AppDate = r["APP_DATE"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["APP_DATE"]),
                            ExportDate = r["EXPORT_DATE"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["EXPORT_DATE"])
                        });
            }
            return list;
        }

        // ✏ แก้ PR ที่ Approve แล้ว: จำนวน / แผนก / ส่งถึง / หมายเหตุ
        public void UpdatePR(string prNo, int qty, string dept, string target, string remark)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"UPDATE TRN_PR_D SET QTY_REQ = @q WHERE PR_NO = @p;
                                              UPDATE TRN_PR_H SET REQ_DEPT = @d, TRG_DEPT = @t, PR_REM = @r WHERE PR_NO = @p;", conn))
            {
                cmd.Parameters.AddWithValue("@q", qty);
                cmd.Parameters.AddWithValue("@d", (object)dept ?? "");
                cmd.Parameters.AddWithValue("@t", (object)target ?? "");
                cmd.Parameters.AddWithValue("@r", (object)remark ?? "");
                cmd.Parameters.AddWithValue("@p", prNo);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        // 🗑 ลบ PR (รายการสินค้า + หัวเอกสาร)
        public void DeletePR(string prNo)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("DELETE FROM TRN_PR_D WHERE PR_NO = @p; DELETE FROM TRN_PR_H WHERE PR_NO = @p;", conn))
            {
                cmd.Parameters.AddWithValue("@p", prNo);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        public bool UpdateAfterExport(string prNo, string userId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string sql = @"UPDATE TRN_PR_H 
                       SET IS_EXPORT = 'N', 
                           EXPORT_REMARK = @UserId, 
                           EXPORT_DATE = GETDATE() 
                       WHERE PR_NO = @PrNo";
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@PrNo", prNo);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

    }
}