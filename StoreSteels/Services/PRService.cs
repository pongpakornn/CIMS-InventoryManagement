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
                using (SqlCommand cmd = new SqlCommand("CIMS.sp_GetPRList", conn))
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
                                PRNumber = reader["PRNumber"].ToString(),
                                UserID = reader["UserID"].ToString(),
                                Requester = reader["REQUESTER"].ToString(), // จะได้ 'N/A' แทน NULL เพราะ ISNULL ใน View
                                PartCode = reader["PartCode"]?.ToString() ?? "N/A", // เพิ่มบรรทัดนี้เพื่อรับค่า Code
                                PartName = reader["Description"].ToString(),
                                QTY = reader["RequestQuantity"] != DBNull.Value ? Convert.ToInt32(reader["RequestQuantity"]) : 0,
                                Department = reader["RequestDepartment"].ToString(),
                                Status = reader["Status"].ToString(),
                                PRDate = Convert.ToDateTime(reader["PRDate"]),
                                Remark = reader["Remark"]?.ToString(),
                                Unit = reader["Unit"]?.ToString()
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
                string sql = "SELECT TOP 1 PRNumber FROM CIMS.PRHeaders WHERE PRNumber LIKE @Prefix + '%' ORDER BY PRNumber DESC";

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
                    sql = @"DELETE FROM CIMS.PRDetails WHERE PRNumber = @PrNo;
                    DELETE FROM CIMS.PRHeaders WHERE PRNumber = @PrNo;";
                }
                else
                {
                    // ถ้า Approved หรือสถานะอื่น ให้ Update ปกติ
                    sql = @"UPDATE CIMS.PRHeaders 
                    SET Status = @Status, 
                        ApprovedBy = CASE WHEN @Status = 'Approved' THEN @AppUserId ELSE NULL END, 
                        ApprovedDate = CASE WHEN @Status = 'Approved' THEN GETDATE() ELSE NULL END,
                        IsExported = CASE WHEN @Status = 'Approved' THEN 'Y' ELSE 'N' END
                    WHERE PRNumber = @PrNo";
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

        // requesterName: ชื่อผู้ขอที่พิมพ์เอง (ว่าง = ใช้ชื่อผู้ใช้ที่ Login) - UserID ยังเป็นคนที่ทำรายการเหมือนเดิม
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
                        string sqlHeader = @"INSERT INTO CIMS.PRHeaders (PRNumber, PRDate, UserID, RequestDepartment, TargetDepartment, Status, Remark, RequesterName, Source)
                                   VALUES (@PrNo, GETDATE(), @UserId, @Dept, @TrgDept, @Status, @Remark, @ReqName, 'MANUAL')";

                        string sqlDetail = @"INSERT INTO CIMS.PRDetails (PRNumber, Description, RequestQuantity) 
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
        // field: REQUESTER / DEPT / REMARK / TARGET (CIMS.PRLookups ใน Database/PRView.sql)

        // ค่าที่เคยพิมพ์ไว้ ที่มีคำที่พิมพ์อยู่ (ว่าง = ทั้งหมด) เรียงจากใช้บ่อย/ล่าสุดก่อน
        public List<string> GetLookupValues(string field, string text, int top = 30)
        {
            var list = new List<string>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(@"SELECT TOP (@top) LookupValue FROM CIMS.PRLookups
                                                     WHERE FieldName = @field AND (@text = '' OR LookupValue LIKE '%' + @text + '%')
                                                     ORDER BY UseCount DESC, LastUsed DESC", conn))
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
                    using (SqlCommand cmd = new SqlCommand("CIMS.sp_PR_SaveLookup", conn) { CommandType = CommandType.StoredProcedure })
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
                string sql = @"SELECT TOP 5 Description FROM (
                                   SELECT Description,
                                          MIN(CASE WHEN Description = @Search THEN 0
                                                   WHEN Description LIKE @Search + '%' THEN 1
                                                   WHEN PartCode LIKE @Search + '%' THEN 2
                                                   ELSE 3 END) AS RANK_NO
                                   FROM CIMS.Parts
                                   WHERE IsActive = 1 AND ISNULL(Description, '') <> ''
                                     AND (Description LIKE '%' + @Search + '%' OR PartCode LIKE '%' + @Search + '%'
                                          OR ISNULL(PartNumber, '') LIKE '%' + @Search + '%' OR ISNULL(Model, '') LIKE '%' + @Search + '%')
                                   GROUP BY Description) x
                               ORDER BY RANK_NO, LEN(Description), Description";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Search", searchText);
                    conn.Open();
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            suggestions.Add(reader["Description"].ToString());
                        }
                    }
                }
            }
            return suggestions;
        }

        // ตรวจสอบว่าสินค้ามีอยู่ใน CIMS.Parts หรือไม่
        public bool IsProductExists(string productName)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string sql = "SELECT COUNT(1) FROM CIMS.Parts WHERE Description = @Name AND IsActive = 1";
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
                string sql = "SELECT * FROM CIMS.vw_PRReady WHERE IsExported = 'Y' ORDER BY PRNumber ASC";

                conn.Open();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new PRModel
                            {
                                PRNumber = reader["PRNumber"].ToString(),
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
                    using (var cmd = new SqlCommand("SELECT TOP 1 RequestQuantity FROM CIMS.PRDetails WITH (UPDLOCK) WHERE PRNumber = @p ORDER BY PRDetailID", conn, tr))
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
                        using (var cmd = new SqlCommand("UPDATE CIMS.PRDetails SET RequestQuantity = @q WHERE PRNumber = @p", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@q", qty);
                            cmd.Parameters.AddWithValue("@p", prNo);
                            cmd.ExecuteNonQuery();
                        }
                        if (keepRemainder)
                        {
                            string prefix = DateTime.Now.ToString("yy", System.Globalization.CultureInfo.InvariantCulture) + "PR";
                            using (var cmd = new SqlCommand(@"SELECT ISNULL(MAX(TRY_CAST(RIGHT(PRNumber, 8) AS INT)), 0) FROM CIMS.PRHeaders WITH (UPDLOCK, HOLDLOCK)
                                                              WHERE PRNumber LIKE @x + '%' AND LEN(PRNumber) = 12", conn, tr))
                            {
                                cmd.Parameters.AddWithValue("@x", prefix);
                                newPr = prefix + (Convert.ToInt32(cmd.ExecuteScalar()) + 1).ToString("D8");
                            }
                            using (var cmd = new SqlCommand(@"
                                INSERT INTO CIMS.PRHeaders (PRNumber, PRDate, UserID, RequestDepartment, TargetDepartment, Status, Remark, IsExported, CreatedAt, RequesterName, Source)
                                SELECT @n, PRDate, UserID, RequestDepartment, TargetDepartment, N'Waiting', Remark, 'N', GETDATE(), RequesterName, ISNULL(Source, 'MANUAL')
                                FROM CIMS.PRHeaders WHERE PRNumber = @p;
                                INSERT INTO CIMS.PRDetails (PRNumber, Description, RequestQuantity, Unit)
                                SELECT TOP 1 @n, Description, @rest, Unit FROM CIMS.PRDetails WHERE PRNumber = @p ORDER BY PRDetailID;", conn, tr))
                            {
                                cmd.Parameters.AddWithValue("@n", newPr);
                                cmd.Parameters.AddWithValue("@p", prNo);
                                cmd.Parameters.AddWithValue("@rest", req - qty);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }

                    using (var cmd = new SqlCommand(@"UPDATE CIMS.PRHeaders SET Status = 'Approved', ApprovedBy = @u, ApprovedDate = GETDATE(), IsExported = 'Y' WHERE PRNumber = @p", conn, tr))
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
                SELECT * FROM CIMS.vw_PRReady
                WHERE Status = 'Approved' AND ISNULL(ApprovedDate, PRDate) >= @from AND ISNULL(ApprovedDate, PRDate) < @to
                  AND (@k = '' OR PRNumber LIKE '%' + @k + '%' OR Description LIKE '%' + @k + '%' OR PartCode LIKE '%' + @k + '%'
                       OR REQUESTER LIKE '%' + @k + '%' OR ISNULL(RequestDepartment, '') LIKE '%' + @k + '%' OR ISNULL(TargetDepartment, '') LIKE '%' + @k + '%'
                       OR ISNULL(Remark, '') LIKE '%' + @k + '%')
                ORDER BY CASE WHEN @k = '' THEN 0
                              WHEN PRNumber = @k OR PartCode = @k OR Description = @k THEN 0
                              WHEN PRNumber LIKE @k + '%' OR PartCode LIKE @k + '%' OR Description LIKE @k + '%' THEN 1
                              WHEN REQUESTER LIKE @k + '%' OR ISNULL(RequestDepartment, '') LIKE @k + '%' THEN 2 ELSE 3 END,
                         ISNULL(ApprovedDate, PRDate) DESC, PRNumber DESC", conn))
            {
                cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = from.Date;
                cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = to.Date.AddDays(1);
                cmd.Parameters.Add("@k", SqlDbType.NVarChar, 200).Value = (keyword ?? "").Trim();
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new PRModel
                        {
                            PRNumber = r["PRNumber"].ToString(),
                            UserID = r["UserID"].ToString(),
                            Requester = r["REQUESTER"].ToString(),
                            PartCode = r["PartCode"]?.ToString() ?? "N/A",
                            PartName = r["Description"].ToString(),
                            QTY = r["RequestQuantity"] != DBNull.Value ? Convert.ToInt32(r["RequestQuantity"]) : 0,
                            Unit = r["Unit"]?.ToString(),
                            Department = r["RequestDepartment"].ToString(),
                            TargetDept = r["TargetDepartment"]?.ToString(),
                            Status = r["Status"].ToString(),
                            PRDate = Convert.ToDateTime(r["PRDate"]),
                            Remark = r["Remark"]?.ToString(),
                            AppDate = r["ApprovedDate"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["ApprovedDate"]),
                            ExportDate = r["ExportDate"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["ExportDate"])
                        });
            }
            return list;
        }

        // ✏ แก้ PR ที่ Approve แล้ว: จำนวน / แผนก / ส่งถึง / หมายเหตุ
        public void UpdatePR(string prNo, int qty, string dept, string target, string remark)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"UPDATE CIMS.PRDetails SET RequestQuantity = @q WHERE PRNumber = @p;
                                              UPDATE CIMS.PRHeaders SET RequestDepartment = @d, TargetDepartment = @t, Remark = @r WHERE PRNumber = @p;", conn))
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
            using (var cmd = new SqlCommand("DELETE FROM CIMS.PRDetails WHERE PRNumber = @p; DELETE FROM CIMS.PRHeaders WHERE PRNumber = @p;", conn))
            {
                cmd.Parameters.AddWithValue("@p", prNo);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        // 🗑 ลบ PR หลายรายการใน Transaction เดียว (ปุ่ม DELETE ALL) -> จำนวน PR ที่ลบได้
        public int DeletePRs(IList<string> prNos)
        {
            int n = 0;
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (string pr in prNos)
                        using (var cmd = new SqlCommand("DELETE FROM CIMS.PRDetails WHERE PRNumber = @p; DELETE FROM CIMS.PRHeaders WHERE PRNumber = @p; SELECT @@ROWCOUNT;", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@p", pr);
                            n += Convert.ToInt32(cmd.ExecuteScalar()) > 0 ? 1 : 0;
                        }
                    tr.Commit();
                }
            }
            return n;
        }

        #endregion

        public bool UpdateAfterExport(string prNo, string userId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string sql = @"UPDATE CIMS.PRHeaders 
                       SET IsExported = 'N', 
                           ExportRemark = @UserId, 
                           ExportDate = GETDATE() 
                       WHERE PRNumber = @PrNo";
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