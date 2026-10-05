using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace CIMS.Services
{
    public class ScanService
    {
        private readonly string _connectionString = GlobalConfig.ConnStr;

        // ขนาดคอลัมน์ CIMS.ScanTransactions.ReferenceNo (varchar 200) - บาร์โค้ด Panta ยาว ~115 ตัวอักษร ต้องเก็บได้ครบ
        private const int RefNoMaxLength = 200;

        public ScanItemModel GetPartByScan(string barcode)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                using (SqlCommand cmd = new SqlCommand("CIMS.sp_GetPartByScan", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BarcodeInput", barcode.Trim());

                    conn.Open();
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        if (rdr.Read())
                        {
                            // schema ใหม่ตัด PartACode/PT_NO ออกจาก CIMS.Parts แล้ว - PartCode เป็นตัวระบุหลัก
                            // ตัวเดียว ใส่ PartACode = PartCode ไว้เพื่อความเข้ากันได้กับหน้าจอเดิมที่ยัง
                            // ผูก binding กับ PartACode อยู่
                            string code = rdr["PartCode"].ToString();
                            return new ScanItemModel
                            {
                                PartId = rdr["PartID"] != DBNull.Value ? Convert.ToInt32(rdr["PartID"]) : 0,
                                PartCode = code,
                                PartName = rdr["Description"].ToString(),
                                PartNo = string.Empty,
                                PartACode = code,
                                Qty = rdr["PackSize"] != DBNull.Value ? Convert.ToInt32(rdr["PackSize"]) : 1,
                                UpdateTime = DateTime.Now
                            };
                        }
                    }
                }
            }
            return null;
        }

        // 🔎 ค้นสินค้าจากป้าย Supplier ตามรูปแบบ: ค้นด้วยชื่อ (MATCH BY PRODUCT NAME) หรือรหัสทีละตัว
        // note = เหตุผลที่ไม่เจอ (เช่น ชื่อนี้ตรงกับสินค้าหลายรายการ) ไว้แจ้งผู้ใช้
        public ScanItemModel FindPart(BarcodeFormatModel fmt, List<string> codes, out string note)
        {
            note = null;
            if (codes == null || codes.Count == 0) return null;
            int start = 0;
            if (fmt != null && fmt.MatchByName)
            {
                var byName = GetPartByName(codes[0], out note);
                if (byName != null) return byName;
                start = 1;   // ไม่เจอชื่อ -> ลองรหัสสำรอง (ALT CODE) ต่อ
            }
            for (int i = start; i < codes.Count; i++)
            {
                var part = GetPartByScan(codes[i]);
                if (part != null) { note = null; return part; }
            }
            return null;
        }

        // ชื่อสินค้าจากป้าย -> สินค้าในระบบ (เทียบแบบ NormalizeName)
        //   1) ชื่อตรงกันทั้งหมด  2) ไม่มี -> ชื่อในระบบมีชื่อจากป้ายอยู่ข้างใน หรือกลับกัน
        //   ต้องเจอรายการเดียวเท่านั้น เจอหลายรายการ = ไม่บันทึก (กันลงผิดสินค้า)
        public ScanItemModel GetPartByName(string labelName, out string note)
        {
            note = null;
            string key = BarcodeFormatModel.NormalizeName(labelName);
            if (key.Length == 0) return null;

            var rows = new List<(int Id, string Code, string Name, int Pack, string Norm)>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT PartID, PartCode, Description, PackSize FROM CIMS.Parts WITH (NOLOCK) WHERE IsActive = 1 AND ISNULL(Description, '') <> ''", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        string name = r["Description"].ToString();
                        rows.Add((Convert.ToInt32(r["PartID"]), r["PartCode"].ToString(), name,
                                  r["PackSize"] == DBNull.Value ? 1 : Convert.ToInt32(r["PackSize"]), BarcodeFormatModel.NormalizeName(name)));
                    }
            }

            var hits = rows.Where(x => x.Norm == key).ToList();
            if (hits.Count == 0 && key.Length >= 4)
                hits = rows.Where(x => x.Norm.Length >= 4 && (x.Norm.Contains(key) || key.Contains(x.Norm))).ToList();

            if (hits.Count == 1)
            {
                var h = hits[0];
                return new ScanItemModel { PartId = h.Id, PartCode = h.Code, PartName = h.Name, PartNo = string.Empty, PartACode = h.Code, Qty = h.Pack, UpdateTime = DateTime.Now };
            }
            if (hits.Count > 1)
                note = $"ชื่อสินค้าบนป้ายตรงกับสินค้าในระบบ {hits.Count} รายการ ({string.Join(", ", hits.Take(3).Select(x => x.Code))}{(hits.Count > 3 ? " ..." : "")}) กรุณาแก้ชื่อสินค้าให้ไม่ซ้ำ";
            return null;
        }

        // stock = null -> ทุกคลัง (พฤติกรรมเดิม), ระบุคลัง -> เฉพาะรายการของคลังนั้น
        // (แถวเก่าก่อนมีระบบหลายคลังมี StockID = NULL นับเป็นของ Stock-CHR)
        // 🏷️ ค่าที่แสดงแทนรหัสของแต่ละคลัง (CIMS.Stocks.ScanDisplayField) - ยังไม่ตั้ง = ไม่มีในรายการ
        public Dictionary<int, string> GetScanDisplayMap()
        {
            var map = new Dictionary<int, string>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT StockID, ScanDisplayField FROM CIMS.Stocks WHERE ISNULL(ScanDisplayField, '') <> ''", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) map[r.GetInt32(0)] = r.GetString(1);
            }
            return map;
        }

        public void SaveScanDisplayMap(Dictionary<int, string> map, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var kv in map)
                        using (var cmd = new SqlCommand("UPDATE CIMS.Stocks SET ScanDisplayField = @v, UpdatedBy = @u, UpdatedDate = GETDATE() WHERE StockID = @s", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@v", string.IsNullOrWhiteSpace(kv.Value) || kv.Value == Helpers.ScanDisplay.Default ? (object)DBNull.Value : kv.Value);
                            cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@s", kv.Key);
                            cmd.ExecuteNonQuery();
                        }
                    tr.Commit();
                }
            }
        }

        // ข้อมูลสินค้าสำหรับเลือกค่าที่แสดง (หลังสแกน 1 รายการ)
        public (string PartA, string PartNumber, string Model) GetPartDisplayInfo(int ptId)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT ISNULL(PartA, ''), ISNULL(PartNumber, ''), ISNULL(Model, '') FROM CIMS.Parts WHERE PartID = @p", conn))
            {
                cmd.Parameters.AddWithValue("@p", ptId);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? (r.GetString(0), r.GetString(1), r.GetString(2)) : ("", "", "");
            }
        }

        public List<ScanItemModel> GetTodayTransactions(StockModel stock = null)
        {
            var list = new List<ScanItemModel>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // schema ใหม่: CIMS.Parts ไม่มี PT_NO แล้ว - ตัดออกจาก SELECT (CIMS.ScanTransactions.PartACode เป็นคอลัมน์
                // ของตัวเองในตาราง log ไม่เกี่ยวกับ CIMS.Parts.PartACode ที่ถูกลบ เลยยังอ่านได้ตามเดิม)
                string sql = @"SELECT
                                    ISNULL(m.PartCode, '') AS PartCode,
                                    ISNULL(m.Description, 'Unknown Part') AS Description,
                                    ISNULL(t.PartACode, '') AS PartACode,
                                    t.PartID,
                                    t.Quantity,
                                    t.TransactionDate,
                                    t.TransactionType,
                                    ISNULL(m.PartA, '') AS PartA,
                                    ISNULL(m.PartNumber, '') AS PartNumber,
                                    ISNULL(m.Model, '') AS Model,
                                    ISNULL(t.ReferenceNo, '') AS ReferenceNo
                               FROM CIMS.ScanTransactions t
                               LEFT JOIN CIMS.Parts m ON t.PartID = m.PartID
                               WHERE t.TransactionDate >= CAST(CAST(GETDATE() AS DATE) AS DATETIME)
                                 AND t.TransactionDate < DATEADD(DAY, 1, CAST(CAST(GETDATE() AS DATE) AS DATETIME))
                                 AND ISNULL(t.IsCancelled, 0) = 0
                                 AND (@stk IS NULL OR t.StockID = @stk OR (@isMain = 1 AND t.StockID IS NULL))
                               ORDER BY t.TransactionDate ASC";

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
                                PartId = rdr["PartID"] != DBNull.Value ? Convert.ToInt32(rdr["PartID"]) : 0,
                                PartCode = rdr["PartCode"].ToString(),
                                PartName = rdr["Description"].ToString(),
                                PartACode = rdr["PartACode"].ToString(),
                                PartNo = string.Empty,
                                Qty = Convert.ToInt32(rdr["Quantity"]),
                                UpdateTime = Convert.ToDateTime(rdr["TransactionDate"]),
                                Status = rdr["TransactionType"].ToString(),
                                PartA = rdr["PartA"].ToString(),
                                PartNumber = rdr["PartNumber"].ToString(),
                                Model = rdr["Model"].ToString(),
                                RefNo = rdr["ReferenceNo"].ToString()
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
        // ✅ เพิ่มพารามิเตอร์ refNo = บาร์โค้ดดิบ "ทั้งชุด" ที่แสกนเนอร์ยิงเข้ามา เก็บลง ReferenceNo
        // schema ใหม่: CIMS.Parts เหลือ StockQuantity ตัวเดียว (ไม่มี QTY_STK แยกกล่อง/ชิ้นอีกต่อไป) - บวก/ลบ
        // ตรงๆ ด้วยจำนวนที่สแกนเข้ามาจริง (qty) แทนการ +1 กล่องแบบเดิม
        // txType: ปกติ "IN" (ค่า default คงพฤติกรรมเดิม) ใช้ "RETURN" สำหรับกรณีรับคืนเหล็กเหลือจากการผลิต
        // เพื่อแยกสถานะออกจากการรับเข้าปกติใน CIMS.ScanTransactions (คอลัมน์ TransactionType เป็น varchar(20) รองรับได้สบาย)
        // stock: คลังที่รับเข้า (null / Stock-CHR = CIMS.Parts.StockQuantity เหมือนเดิม, คลังอื่น = CIMS.PartStocks ผ่าน CIMS.sp_Stock_AddQty)
        public bool UpdateStock(int ptId, string partCode, string partACode, int qty, string userId, string refNo, string txType = "IN", StockModel stock = null, bool remainder = false)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                SqlTransaction trans = conn.BeginTransaction();

                try
                {
                    string updateSql = @"UPDATE CIMS.Parts
                                 SET StockQuantity = ISNULL(StockQuantity, 0) + @Qty
                                 WHERE PartID = @PtId";

                    // ✅ เพิ่มคอลัมน์ ReferenceNo + StockID (คลังที่แสกน)
                    string insertLogSql = @"INSERT INTO CIMS.ScanTransactions (UserID, Quantity, TransactionType, TransactionDate, PartID, PartACode, ReferenceNo, StockID, BoxChange)
                                    VALUES (@UserId, @Qty, @TxType, GETDATE(), @PtId, @PtACode, @RefNo, @StkId, @Box)";

                    // กล่องที่รายการนี้ขยับ (คลังที่ไม่ใช่คลังหลัก): สแกนป้าย = +1 / สแกนเศษ = 0 / คืนเหล็ก = คิดจากชิ้น (NULL)
                    bool isOtherStock = stock != null && !stock.IsMain;
                    object boxMove = !isOtherStock || txType == "RETURN" ? (object)DBNull.Value : (remainder ? 0 : 1);

                    if (stock != null && !stock.IsMain)
                    {
                        using (SqlCommand cmdAdd = new SqlCommand("CIMS.sp_Stock_AddQty", conn, trans) { CommandType = CommandType.StoredProcedure })
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

                        // 🛡️ กัน Truncate Error เผื่อบาร์โค้ดยาวเกินขนาดคอลัมน์ ReferenceNo (varchar 200)
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
        // Transaction เดียว: CIMS.Parts.StockQuantity + qty (รับเข้าเต็มจำนวนตามป้ายเสมอ เพราะของอยู่หน้างานแล้ว),
        // ตัด CIMS.PartStocks ของคลังต้นทาง "เท่าที่มี" (ไม่ติดลบ), บันทึก CIMS.ScanTransactions (IN) + CIMS.StockTransfers (TransferMode = SCAN)
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
                        using (var cmd = new SqlCommand("UPDATE CIMS.Parts SET StockQuantity = ISNULL(StockQuantity, 0) + @Qty WHERE PartID = @PtId", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Qty", qty);
                            cmd.Parameters.AddWithValue("@PtId", ptId);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SqlCommand(@"INSERT INTO CIMS.ScanTransactions (UserID, Quantity, TransactionType, TransactionDate, PartID, PartACode, ReferenceNo, StockID)
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
                        using (var cmd = new SqlCommand("SELECT StockCode FROM CIMS.Stocks WHERE StockID = @s", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", sourceStkId);
                            result.SourceCode = cmd.ExecuteScalar()?.ToString() ?? $"STK_{sourceStkId}";
                        }

                        using (var cmd = new SqlCommand("SELECT Quantity FROM CIMS.PartStocks WITH (UPDLOCK, HOLDLOCK) WHERE StockID = @s AND PartID = @p", conn, trans))
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
                            using (var cmd = new SqlCommand("UPDATE CIMS.PartStocks SET Quantity = Quantity - @q, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@q", result.Deducted);
                                cmd.Parameters.AddWithValue("@s", sourceStkId);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.ExecuteNonQuery();
                            }

                            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.StockTransfers (FromStockID, FromStockCode, ToStockID, ToStockCode, PartID, PartCode,
                                                                  Quantity, TransferMode, FromBalanceAfter, ToBalanceAfter, UserID)
                                                              SELECT @s, @sc, @m, @mc, @p, @pc, @q, 'SCAN', @fa, ISNULL(StockQuantity, 0), @u
                                                              FROM CIMS.Parts WHERE PartID = @p", conn, trans))
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
                            ? @"SELECT ISNULL(Quantity, 0), ISNULL(BoxQuantity, 0) FROM CIMS.PartStocks WITH (UPDLOCK, HOLDLOCK) WHERE StockID = @StkId AND PartID = @PtId"
                            : @"SELECT ISNULL(StockQuantity, 0), 0 FROM CIMS.Parts WHERE PartID = @PtId";
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
                            ? @"UPDATE CIMS.PartStocks SET Quantity = Quantity - @Qty, BoxQuantity = BoxQuantity + @BoxOut, UpdatedDate = GETDATE() WHERE StockID = @StkId AND PartID = @PtId"
                            : @"UPDATE CIMS.Parts
                                     SET StockQuantity = ISNULL(StockQuantity, 0) - @Qty
                                     WHERE PartID = @PtId";

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

                        // ✅ เพิ่มคอลัมน์ ReferenceNo
                        string insertLogSql = @"INSERT INTO CIMS.ScanTransactions (UserID, Quantity, TransactionType, TransactionDate, PartID, PartACode, ReferenceNo, StockID, BoxChange)
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
        // คลังหลัก = แสดงในตารางคลังหลัก (IsShowInMaster) / คลังอื่น = มีแถวใน CIMS.PartStocks
        // (ตรงกับชิป STOCK ในหน้า Inventory Registration)
        public HashSet<int> GetPartStockIds(int ptId)
        {
            var ids = new HashSet<int>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            using (SqlCommand cmd = new SqlCommand(@"
                SELECT ps.StockID FROM CIMS.PartStocks ps WHERE ps.PartID = @PtId
                UNION
                SELECT s.StockID FROM CIMS.Stocks s
                WHERE s.IsMain = 1 AND EXISTS (SELECT 1 FROM CIMS.Parts p WHERE p.PartID = @PtId AND p.IsShowInMaster = 1)", conn))
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
                // คิวรีดึงยอดคงเหลือปัจจุบันจาก Master Table ตรงๆ (คลังอื่นดึงจาก CIMS.PartStocks)
                string sql = isOther
                    ? "SELECT ISNULL(Quantity, 0) FROM CIMS.PartStocks WHERE StockID = @StkId AND PartID = @PtId"
                    : "SELECT ISNULL(StockQuantity, 0) FROM CIMS.Parts WHERE PartID = @PtId";
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
