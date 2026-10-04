using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Data;

namespace CIMS.Services
{
    public class ScanService
    {
        private readonly string _connectionString = GlobalConfig.ConnStr;

        // ขนาดคอลัมน์ TRN_SCAN.REF_NO (varchar 200) - บาร์โค้ด Panta ยาว ~115 ตัวอักษร ต้องเก็บได้ครบ
        private const int RefNoMaxLength = 200;

        public ScanItemModel GetPartByScan(string barcode)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                using (SqlCommand cmd = new SqlCommand("sp_GetPartByScan", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BarcodeInput", barcode.Trim());

                    conn.Open();
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        if (rdr.Read())
                        {
                            // schema ใหม่ตัด PT_ACODE/PT_NO ออกจาก MST_PART แล้ว - PT_CODE เป็นตัวระบุหลัก
                            // ตัวเดียว ใส่ PartACode = PartCode ไว้เพื่อความเข้ากันได้กับหน้าจอเดิมที่ยัง
                            // ผูก binding กับ PartACode อยู่
                            string code = rdr["PT_CODE"].ToString();
                            return new ScanItemModel
                            {
                                PartId = rdr["PT_ID"] != DBNull.Value ? Convert.ToInt32(rdr["PT_ID"]) : 0,
                                PartCode = code,
                                PartName = rdr["PT_DESC"].ToString(),
                                PartNo = string.Empty,
                                PartACode = code,
                                Qty = rdr["PT_PSZ"] != DBNull.Value ? Convert.ToInt32(rdr["PT_PSZ"]) : 1,
                                UpdateTime = DateTime.Now
                            };
                        }
                    }
                }
            }
            return null;
        }

        // stock = null -> ทุกคลัง (พฤติกรรมเดิม), ระบุคลัง -> เฉพาะรายการของคลังนั้น
        // (แถวเก่าก่อนมีระบบหลายคลังมี STK_ID = NULL นับเป็นของ Stock-CHR)
        public List<ScanItemModel> GetTodayTransactions(StockModel stock = null)
        {
            var list = new List<ScanItemModel>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // schema ใหม่: MST_PART ไม่มี PT_NO แล้ว - ตัดออกจาก SELECT (TRN_SCAN.PT_ACODE เป็นคอลัมน์
                // ของตัวเองในตาราง log ไม่เกี่ยวกับ MST_PART.PT_ACODE ที่ถูกลบ เลยยังอ่านได้ตามเดิม)
                string sql = @"SELECT
                                    ISNULL(m.PT_CODE, '') AS PT_CODE,
                                    ISNULL(m.PT_DESC, 'Unknown Part') AS PT_DESC,
                                    ISNULL(t.PT_ACODE, '') AS PT_ACODE,
                                    t.PT_ID,
                                    t.TX_QTY,
                                    t.TX_DATE,
                                    t.TX_TYPE
                               FROM TRN_SCAN t
                               LEFT JOIN MST_PART m ON t.PT_ID = m.PT_ID
                               WHERE CAST(t.TX_DATE AS DATE) = CAST(GETDATE() AS DATE)
                                 AND ISNULL(t.IS_CANCEL, 0) = 0
                                 AND (@stk IS NULL OR t.STK_ID = @stk OR (@isMain = 1 AND t.STK_ID IS NULL))
                               ORDER BY t.TX_DATE ASC";

                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@stk", SqlDbType.Int).Value = (object)stock?.StkId ?? DBNull.Value;
                    cmd.Parameters.Add("@isMain", SqlDbType.Bit).Value = stock?.IsMain ?? false;
                    conn.Open();
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new ScanItemModel
                            {
                                PartId = rdr["PT_ID"] != DBNull.Value ? Convert.ToInt32(rdr["PT_ID"]) : 0,
                                PartCode = rdr["PT_CODE"].ToString(),
                                PartName = rdr["PT_DESC"].ToString(),
                                PartACode = rdr["PT_ACODE"].ToString(),
                                PartNo = string.Empty,
                                Qty = Convert.ToInt32(rdr["TX_QTY"]),
                                UpdateTime = Convert.ToDateTime(rdr["TX_DATE"]),
                                Status = rdr["TX_TYPE"].ToString()
                            });
                        }
                    }
                }
            }
            return list;
        }

        // ==========================================
        // 📥 ขาเข้า: UpdateStock
        // ==========================================
        // ✅ เพิ่มพารามิเตอร์ refNo = บาร์โค้ดดิบ "ทั้งชุด" ที่แสกนเนอร์ยิงเข้ามา เก็บลง REF_NO
        // schema ใหม่: MST_PART เหลือ QTY_STKB ตัวเดียว (ไม่มี QTY_STK แยกกล่อง/ชิ้นอีกต่อไป) - บวก/ลบ
        // ตรงๆ ด้วยจำนวนที่สแกนเข้ามาจริง (qty) แทนการ +1 กล่องแบบเดิม
        // txType: ปกติ "IN" (ค่า default คงพฤติกรรมเดิม) ใช้ "RETURN" สำหรับกรณีรับคืนเหล็กเหลือจากการผลิต
        // เพื่อแยกสถานะออกจากการรับเข้าปกติใน TRN_SCAN (คอลัมน์ TX_TYPE เป็น varchar(20) รองรับได้สบาย)
        // stock: คลังที่รับเข้า (null / Stock-CHR = MST_PART.QTY_STKB เหมือนเดิม, คลังอื่น = MST_PART_STOCK ผ่าน sp_Stock_AddQty)
        public bool UpdateStock(int ptId, string partCode, string partACode, int qty, string userId, string refNo, string txType = "IN", StockModel stock = null, bool remainder = false)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                SqlTransaction trans = conn.BeginTransaction();

                try
                {
                    string updateSql = @"UPDATE MST_PART
                                 SET QTY_STKB = ISNULL(QTY_STKB, 0) + @Qty
                                 WHERE PT_ID = @PtId";

                    // ✅ เพิ่มคอลัมน์ REF_NO + STK_ID (คลังที่แสกน)
                    string insertLogSql = @"INSERT INTO TRN_SCAN (USR_ID, TX_QTY, TX_TYPE, TX_DATE, PT_ID, PT_ACODE, REF_NO, STK_ID, TX_BOX)
                                    VALUES (@UserId, @Qty, @TxType, GETDATE(), @PtId, @PtACode, @RefNo, @StkId, @Box)";

                    // กล่องที่รายการนี้ขยับ (คลังที่ไม่ใช่คลังหลัก): สแกนป้าย = +1 / สแกนเศษ = 0 / คืนเหล็ก = คิดจากชิ้น (NULL)
                    bool isOtherStock = stock != null && !stock.IsMain;
                    object boxMove = !isOtherStock || txType == "RETURN" ? (object)DBNull.Value : (remainder ? 0 : 1);

                    if (stock != null && !stock.IsMain)
                    {
                        using (SqlCommand cmdAdd = new SqlCommand("sp_Stock_AddQty", conn, trans) { CommandType = CommandType.StoredProcedure })
                        {
                            cmdAdd.Parameters.AddWithValue("@StkId", stock.StkId);
                            cmdAdd.Parameters.AddWithValue("@PtId", ptId);
                            cmdAdd.Parameters.AddWithValue("@Qty", qty);
                            cmdAdd.Parameters.Add("@NewBal", SqlDbType.Int).Direction = ParameterDirection.Output;
                            // แบบ StorePC: สแกน 1 ป้าย = +1 กล่อง (ชิ้น = ตามป้าย) / คืนเหล็ก (กรอกจำนวนเอง) = กล่องคิดจากชิ้น
                            cmdAdd.Parameters.AddWithValue("@Box", boxMove);
                            cmdAdd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (SqlCommand cmdUpdate = new SqlCommand(updateSql, conn, trans))
                        {
                            cmdUpdate.Parameters.AddWithValue("@Qty", qty);
                            cmdUpdate.Parameters.AddWithValue("@PtId", ptId);
                            cmdUpdate.ExecuteNonQuery();
                        }
                    }

                    using (SqlCommand cmdLog = new SqlCommand(insertLogSql, conn, trans))
                    {
                        cmdLog.Parameters.Add("@StkId", SqlDbType.Int).Value = (object)stock?.StkId ?? DBNull.Value;
                        cmdLog.Parameters.AddWithValue("@UserId", userId);
                        cmdLog.Parameters.AddWithValue("@Qty", qty);
                        cmdLog.Parameters.AddWithValue("@TxType", string.IsNullOrWhiteSpace(txType) ? "IN" : txType);
                        cmdLog.Parameters.Add("@Box", SqlDbType.Int).Value = boxMove;
                        cmdLog.Parameters.AddWithValue("@PtId", ptId);
                        cmdLog.Parameters.AddWithValue("@PtACode", string.IsNullOrWhiteSpace(partACode) ? DBNull.Value : (object)partACode.Trim());

                        // 🛡️ กัน Truncate Error เผื่อบาร์โค้ดยาวเกินขนาดคอลัมน์ REF_NO (varchar 200)
                        string safeRefNo = string.IsNullOrWhiteSpace(refNo)
                            ? null
                            : (refNo.Length > RefNoMaxLength ? refNo.Substring(0, RefNoMaxLength) : refNo);
                        cmdLog.Parameters.AddWithValue("@RefNo", (object)safeRefNo ?? DBNull.Value);

                        cmdLog.ExecuteNonQuery();
                    }

                    trans.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateStock Error: {ex.Message}");
                    trans.Rollback();
                    return false;
                }
            }
        }

        // ==========================================
        // 📥🔁 รับเข้าคลังหลัก + ตัดยอดคลังต้นทางอัตโนมัติ (เช่น แสกนป้าย Panta -> ตัด STOCK-PANTA)
        // ==========================================
        // Transaction เดียว: MST_PART.QTY_STKB + qty (รับเข้าเต็มจำนวนตามป้ายเสมอ เพราะของอยู่หน้างานแล้ว),
        // ตัด MST_PART_STOCK ของคลังต้นทาง "เท่าที่มี" (ไม่ติดลบ), บันทึก TRN_SCAN (IN) + TRN_TRANSFER (TRF_MODE = SCAN)
        public class DeductResult
        {
            public bool Saved { get; set; }
            public int Deducted { get; set; }          // ตัดจากคลังต้นทางได้จริง
            public int SourceBefore { get; set; }      // ยอดคลังต้นทางก่อนตัด
            public int SourceAfter { get; set; }
            public string SourceCode { get; set; }
            public bool Short => Saved && Deducted < RequestedQty;
            public int RequestedQty { get; set; }
        }

        public DeductResult UpdateStockWithDeduct(int ptId, string partCode, string partACode, int qty, string userId, string refNo,
                                                  StockModel mainStock, int sourceStkId)
        {
            var result = new DeductResult { RequestedQty = qty };
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        // 1) รับเข้าคลังหลัก
                        using (var cmd = new SqlCommand("UPDATE MST_PART SET QTY_STKB = ISNULL(QTY_STKB, 0) + @Qty WHERE PT_ID = @PtId", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Qty", qty);
                            cmd.Parameters.AddWithValue("@PtId", ptId);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SqlCommand(@"INSERT INTO TRN_SCAN (USR_ID, TX_QTY, TX_TYPE, TX_DATE, PT_ID, PT_ACODE, REF_NO, STK_ID)
                                                          VALUES (@UserId, @Qty, 'IN', GETDATE(), @PtId, @PtACode, @RefNo, @StkId)", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@UserId", userId);
                            cmd.Parameters.AddWithValue("@Qty", qty);
                            cmd.Parameters.AddWithValue("@PtId", ptId);
                            cmd.Parameters.AddWithValue("@PtACode", string.IsNullOrWhiteSpace(partACode) ? DBNull.Value : (object)partACode.Trim());
                            string safeRefNo = string.IsNullOrWhiteSpace(refNo) ? null : (refNo.Length > RefNoMaxLength ? refNo.Substring(0, RefNoMaxLength) : refNo);
                            cmd.Parameters.AddWithValue("@RefNo", (object)safeRefNo ?? DBNull.Value);
                            cmd.Parameters.Add("@StkId", SqlDbType.Int).Value = (object)mainStock?.StkId ?? DBNull.Value;
                            cmd.ExecuteNonQuery();
                        }

                        // 2) ตัดคลังต้นทางเท่าที่มี
                        using (var cmd = new SqlCommand("SELECT STK_CODE FROM MST_STOCK WHERE STK_ID = @s", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", sourceStkId);
                            result.SourceCode = cmd.ExecuteScalar()?.ToString() ?? $"STK_{sourceStkId}";
                        }

                        using (var cmd = new SqlCommand("SELECT QTY FROM MST_PART_STOCK WITH (UPDLOCK, HOLDLOCK) WHERE STK_ID = @s AND PT_ID = @p", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", sourceStkId);
                            cmd.Parameters.AddWithValue("@p", ptId);
                            object r = cmd.ExecuteScalar();
                            result.SourceBefore = r == null || r == DBNull.Value ? 0 : Convert.ToInt32(r);
                        }

                        result.Deducted = Math.Max(0, Math.Min(qty, result.SourceBefore));
                        result.SourceAfter = result.SourceBefore - result.Deducted;

                        if (result.Deducted > 0)
                        {
                            using (var cmd = new SqlCommand("UPDATE MST_PART_STOCK SET QTY = QTY - @q, UPDATED_DATE = GETDATE() WHERE STK_ID = @s AND PT_ID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@q", result.Deducted);
                                cmd.Parameters.AddWithValue("@s", sourceStkId);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.ExecuteNonQuery();
                            }

                            using (var cmd = new SqlCommand(@"INSERT INTO TRN_TRANSFER (FROM_STK_ID, FROM_STK_CODE, TO_STK_ID, TO_STK_CODE, PT_ID, PT_CODE,
                                                                  QTY, TRF_MODE, FROM_BAL_AFTER, TO_BAL_AFTER, USR_ID)
                                                              SELECT @s, @sc, @m, @mc, @p, @pc, @q, 'SCAN', @fa, ISNULL(QTY_STKB, 0), @u
                                                              FROM MST_PART WHERE PT_ID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@s", sourceStkId);
                                cmd.Parameters.AddWithValue("@sc", result.SourceCode);
                                cmd.Parameters.AddWithValue("@m", (object)mainStock?.StkId ?? 0);
                                cmd.Parameters.AddWithValue("@mc", mainStock?.Code ?? "MAIN");
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.AddWithValue("@pc", partCode ?? "");
                                cmd.Parameters.AddWithValue("@q", result.Deducted);
                                cmd.Parameters.AddWithValue("@fa", result.SourceAfter);
                                cmd.Parameters.AddWithValue("@u", userId);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        trans.Commit();
                        result.Saved = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateStockWithDeduct Error: {ex.Message}");
                        trans.Rollback();
                        result.Saved = false;
                    }
                }
            }
            return result;
        }

        // ==========================================
        // 📤 ขาออก: UpdateStockOut
        // ==========================================
        public bool UpdateStockOut(int ptId, string partCode, string partACode, int qty, string userId, string refNo, StockModel stock = null, bool remainder = false)
        {
            bool isOther = stock != null && !stock.IsMain;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        string checkSql = isOther
                            ? @"SELECT ISNULL(QTY, 0), ISNULL(QTY_BOX, 0) FROM MST_PART_STOCK WITH (UPDLOCK, HOLDLOCK) WHERE STK_ID = @StkId AND PT_ID = @PtId"
                            : @"SELECT ISNULL(QTY_STKB, 0), 0 FROM MST_PART WHERE PT_ID = @PtId";
                        int currentStock = 0, currentBox = 0;
                        using (SqlCommand cmdCheck = new SqlCommand(checkSql, conn, trans))
                        {
                            cmdCheck.Parameters.Add("@PtId", SqlDbType.Int).Value = ptId;
                            if (isOther) cmdCheck.Parameters.Add("@StkId", SqlDbType.Int).Value = stock.StkId;
                            using (var rd = cmdCheck.ExecuteReader())
                                if (rd.Read())
                                {
                                    currentStock = Convert.ToInt32(rd[0]);
                                    currentBox = Convert.ToInt32(rd[1]);
                                }
                        }
                        // กล่องที่ลดจริง: สแกนป้าย = -1 (ถ้ายังมีกล่องเหลือ) / สแกนเศษ = 0
                        int boxOut = isOther && !remainder && currentBox > 0 ? -1 : 0;

                        if (currentStock < qty)
                        {
                            trans.Rollback();
                            return false;
                        }

                        string updateSql = isOther
                            ? @"UPDATE MST_PART_STOCK SET QTY = QTY - @Qty, QTY_BOX = QTY_BOX + @BoxOut, UPDATED_DATE = GETDATE() WHERE STK_ID = @StkId AND PT_ID = @PtId"
                            : @"UPDATE MST_PART
                                     SET QTY_STKB = ISNULL(QTY_STKB, 0) - @Qty
                                     WHERE PT_ID = @PtId";

                        using (SqlCommand cmdUpdate = new SqlCommand(updateSql, conn, trans))
                        {
                            cmdUpdate.Parameters.Add("@Qty", SqlDbType.Int).Value = qty;
                            cmdUpdate.Parameters.Add("@PtId", SqlDbType.Int).Value = ptId;
                            if (isOther)
                            {
                                cmdUpdate.Parameters.Add("@StkId", SqlDbType.Int).Value = stock.StkId;
                                cmdUpdate.Parameters.Add("@BoxOut", SqlDbType.Int).Value = boxOut;
                            }
                            cmdUpdate.ExecuteNonQuery();
                        }

                        // ✅ เพิ่มคอลัมน์ REF_NO
                        string insertLogSql = @"INSERT INTO TRN_SCAN (USR_ID, TX_QTY, TX_TYPE, TX_DATE, PT_ID, PT_ACODE, REF_NO, STK_ID, TX_BOX)
                                        VALUES (@UserId, @Qty, 'OUT', GETDATE(), @PtId, @PtACode, @RefNo, @StkId, @Box)";
                        using (SqlCommand cmdLog = new SqlCommand(insertLogSql, conn, trans))
                        {
                            cmdLog.Parameters.Add("@Box", SqlDbType.Int).Value = isOther ? (object)boxOut : DBNull.Value;
                            cmdLog.Parameters.Add("@StkId", SqlDbType.Int).Value = (object)stock?.StkId ?? DBNull.Value;
                            cmdLog.Parameters.Add("@UserId", SqlDbType.NVarChar).Value = userId;
                            cmdLog.Parameters.Add("@Qty", SqlDbType.Int).Value = qty;
                            cmdLog.Parameters.Add("@PtId", SqlDbType.Int).Value = ptId;
                            cmdLog.Parameters.Add("@PtACode", SqlDbType.NVarChar).Value =
                                string.IsNullOrWhiteSpace(partACode) ? DBNull.Value : (object)partACode.Trim();

                            string safeRefNo = string.IsNullOrWhiteSpace(refNo)
                                ? null
                                : (refNo.Length > RefNoMaxLength ? refNo.Substring(0, RefNoMaxLength) : refNo);
                            cmdLog.Parameters.Add("@RefNo", SqlDbType.VarChar, RefNoMaxLength).Value = (object)safeRefNo ?? DBNull.Value;

                            cmdLog.ExecuteNonQuery();
                        }

                        trans.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateStockOut Error: {ex.Message}");
                        trans.Rollback();
                        return false;
                    }
                }
            }
        }

        // คลังที่สินค้านี้อยู่ (ใช้ตอนสแกนร่วมหลายคลัง / AUTO ให้ระบบเลือกคลังเอง)
        // คลังหลัก = แสดงในตารางคลังหลัก (IS_SHOW_MST) / คลังอื่น = มีแถวใน MST_PART_STOCK
        // (ตรงกับชิป STOCK ในหน้า Inventory Registration)
        public HashSet<int> GetPartStockIds(int ptId)
        {
            var ids = new HashSet<int>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(@"
                SELECT ps.STK_ID FROM MST_PART_STOCK ps WHERE ps.PT_ID = @PtId
                UNION
                SELECT s.STK_ID FROM MST_STOCK s
                WHERE s.IS_MAIN = 1 AND EXISTS (SELECT 1 FROM MST_PART p WHERE p.PT_ID = @PtId AND p.IS_SHOW_MST = 1)", conn))
            {
                cmd.Parameters.AddWithValue("@PtId", ptId);
                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                    while (rdr.Read()) ids.Add(Convert.ToInt32(rdr[0]));
            }
            return ids;
        }

        public int GetInventoryBalance(int ptId, StockModel stock = null)
        {
            bool isOther = stock != null && !stock.IsMain;
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // คิวรีดึงยอดคงเหลือปัจจุบันจาก Master Table ตรงๆ (คลังอื่นดึงจาก MST_PART_STOCK)
                string sql = isOther
                    ? "SELECT ISNULL(QTY, 0) FROM MST_PART_STOCK WHERE STK_ID = @StkId AND PT_ID = @PtId"
                    : "SELECT ISNULL(QTY_STKB, 0) FROM MST_PART WHERE PT_ID = @PtId";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@PtId", ptId);
                    if (isOther) cmd.Parameters.AddWithValue("@StkId", stock.StkId);
                    try
                    {
                        conn.Open();
                        var res = cmd.ExecuteScalar();
                        return res != null ? Convert.ToInt32(res) : 0;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"GetInventoryBalance Error: {ex.Message}");
                        return 0;
                    }
                }
            }
        }
    }
}
