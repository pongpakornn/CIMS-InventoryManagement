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
        // preferStocks = คลังที่รับ: ชื่อตรงหลายรายการ -> เลือกสินค้าที่อยู่คลังนี้ก่อน
        public ScanItemModel FindPart(BarcodeFormatModel fmt, List<string> codes, out string note, ICollection<int> preferStocks = null)
        {
            note = null;
            if (codes == null || codes.Count == 0) return null;
            int start = 0;
            if (fmt != null && fmt.MatchByName)
            {
                var byName = GetPartByName(codes[0], out note, preferStocks);
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
        //   ใกล้เคียงที่สุด (ป้าย Panta: SPCC-SD + 1.000 X 22.00 X COIL):
        //   1) ชื่อเต็ม (เกรด + ขนาด) ตรงกัน  2) ชื่อในระบบมีชื่อเต็มอยู่ข้างใน  3) ขนาดตรงกัน (1X22XC) แล้วเกรดใกล้สุด
        //   แต่ละขั้นเจอหลายรายการ -> เลือกตัวที่อยู่คลังที่รับ (preferStocks) / ยังซ้ำ = ไม่บันทึก (กันลงผิดสินค้า)
        public ScanItemModel GetPartByName(string labelName, out string note, ICollection<int> preferStocks = null)
        {
            note = null;
            string key = BarcodeFormatModel.NormalizeName(labelName);
            if (key.Length == 0) return null;

            var rows = new List<NameRow>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"SELECT p.PartID, p.PartCode, p.Description, p.PackSize,
                                                     InStocks = STUFF((SELECT ',' + CAST(ps.StockID AS VARCHAR(10)) FROM CIMS.PartStocks ps WHERE ps.PartID = p.PartID FOR XML PATH('')), 1, 1, ''),
                                                     InMain = CASE WHEN ISNULL(p.IsShowInMaster, 0) = 1 THEN 1 ELSE 0 END
                                              FROM CIMS.Parts p WITH (NOLOCK) WHERE p.IsActive = 1 AND ISNULL(p.Description, '') <> ''", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        string name = r["Description"].ToString();
                        var stocks = new HashSet<int>(r["InStocks"].ToString().Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse));
                        rows.Add(new NameRow
                        {
                            Id = Convert.ToInt32(r["PartID"]), Code = r["PartCode"].ToString(), Name = name,
                            Pack = r["PackSize"] == DBNull.Value ? 1 : Convert.ToInt32(r["PackSize"]),
                            Norm = BarcodeFormatModel.NormalizeName(name), Stocks = stocks, InMain = Convert.ToInt32(r["InMain"]) == 1
                        });
                    }
            }
            HashSet<int> mainIds = null;
            if (preferStocks != null && preferStocks.Count > 0)
                using (var conn = new SqlConnection(_connectionString))
                using (var cmd = new SqlCommand("SELECT StockID FROM CIMS.Stocks WHERE IsMain = 1", conn))
                {
                    conn.Open();
                    mainIds = new HashSet<int>();
                    using (var r = cmd.ExecuteReader()) while (r.Read()) mainIds.Add(r.GetInt32(0));
                }
            bool InPrefer(NameRow x) => preferStocks != null && preferStocks.Any(s => x.Stocks.Contains(s) || (x.InMain && mainIds != null && mainIds.Contains(s)));

            // ขั้นที่เจอรายการเดียว (หรือเดียวในคลังที่รับ) = ใช้ / หลายรายการ = จำไว้แจ้งผู้ใช้แล้วลองขั้นถัดไป
            List<NameRow> ambiguous = null;
            NameRow Pick(List<NameRow> hits)
            {
                if (hits.Count == 1) return hits[0];
                if (hits.Count == 0) return null;
                var mine = hits.Where(InPrefer).ToList();
                if (mine.Count == 1) return mine[0];
                if (ambiguous == null) ambiguous = mine.Count > 1 ? mine : hits;
                return null;
            }

            var hit = Pick(rows.Where(x => x.Norm == key).ToList());
            if (hit == null && key.Length >= 4)
                hit = Pick(rows.Where(x => x.Norm.Length >= 4 && (ContainsWhole(x.Norm, key) || ContainsWhole(key, x.Norm))).ToList());

            // ขนาดเหล็กบนป้าย (หนา X กว้าง X ยาว/COIL) ตรงกัน -> เกรดที่เหมือนกันมากสุด
            string size = SizeKey(labelName);
            if (hit == null && size != null)
            {
                var bySize = rows.Where(x => ContainsWhole(x.Norm, size)).ToList();
                string grade = key.Replace(size, "");
                if (bySize.Count > 1 && grade.Length > 0)
                {
                    int best = bySize.Max(x => CommonPrefix(x.Norm.Replace(size, ""), grade));
                    if (best > 0) bySize = bySize.Where(x => CommonPrefix(x.Norm.Replace(size, ""), grade) == best).ToList();
                }
                hit = Pick(bySize);
            }

            if (hit != null)
                return new ScanItemModel { PartId = hit.Id, PartCode = hit.Code, PartName = hit.Name, PartNo = string.Empty, PartACode = hit.Code, Qty = hit.Pack, UpdateTime = DateTime.Now };
            if (ambiguous != null)
                note = $"ชื่อสินค้าบนป้ายตรงกับสินค้าในระบบ {ambiguous.Count} รายการ ({string.Join(", ", ambiguous.Take(3).Select(x => x.Code))}{(ambiguous.Count > 3 ? " ..." : "")}) กรุณาแก้ชื่อสินค้าให้ไม่ซ้ำ";
            return null;
        }

        private class NameRow
        {
            public int Id; public string Code; public string Name; public int Pack; public string Norm;
            public HashSet<int> Stocks; public bool InMain;
        }

        // ขนาดเหล็ก "1.000 X 175.00 X COIL" / "2.800 X 1285.00 X 810" -> 1X175XC / 2.8X1285X810 (null = ป้ายไม่มีขนาด)
        internal static string SizeKey(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name ?? "", @"\d+(?:\.\d+)?\s*[Xx]\s*\d+(?:\.\d+)?\s*[Xx]\s*(?:COIL|C|\d+(?:\.\d+)?)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? BarcodeFormatModel.NormalizeName(m.Value) : null;
        }

        // มี part อยู่ใน s โดยตัวเลขไม่ติดกัน (1X175XC ไม่นับว่าอยู่ใน 11X175XC / 1X175XC0)
        internal static bool ContainsWhole(string s, string part)
        {
            if (string.IsNullOrEmpty(part)) return false;
            for (int i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + 1, StringComparison.Ordinal))
            {
                bool okL = i == 0 || !(char.IsDigit(s[i - 1]) || s[i - 1] == '.') || !(char.IsDigit(part[0]) || part[0] == '.');
                int e = i + part.Length;
                bool okR = e == s.Length || !(char.IsDigit(s[e]) || s[e] == '.') || !(char.IsDigit(part[part.Length - 1]) || part[part.Length - 1] == '.');
                if (okL && okR) return true;
            }
            return false;
        }

        private static int CommonPrefix(string a, string b)
        {
            int n = 0;
            while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
            return n;
        }

        // stock = null -> ทุกคลัง (พฤติกรรมเดิม), ระบุคลัง -> เฉพาะรายการของคลังนั้น
        // (แถวเก่าก่อนมีระบบหลายคลังมี StockID = NULL นับเป็นของ Stock-CHR)
        // 🏷️ ค่าที่แสดงแทนรหัสของแต่ละคลัง: ค่าของผู้ใช้ (CIMS.UserScanDisplays) > ค่าของคลัง (CIMS.Stocks.ScanDisplayField)
        //    userId = null -> ค่าของคลังอย่างเดียว / ยังไม่ตั้ง = ไม่มีในรายการ (แสดง PRODUCT CODE)
        public Dictionary<int, string> GetScanDisplayMap(string userId = null)
        {
            var map = new Dictionary<int, string>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT StockID, ScanDisplayField FROM CIMS.Stocks WHERE ISNULL(ScanDisplayField, '') <> ''", conn))
                using (var r = cmd.ExecuteReader()) while (r.Read()) map[r.GetInt32(0)] = r.GetString(1);
                if (!string.IsNullOrWhiteSpace(userId) && Helpers.DbSchema.HasUserScanDisplays)
                    using (var cmd = new SqlCommand("SELECT StockID, DisplayField FROM CIMS.UserScanDisplays WHERE UserID = @u", conn))
                    {
                        cmd.Parameters.AddWithValue("@u", userId);
                        using (var r = cmd.ExecuteReader()) while (r.Read()) map[r.GetInt32(0)] = r.GetString(1);
                    }
            }
            // PRODUCT CODE (ค่าเริ่มต้น) ที่ผู้ใช้เลือกเอง = แสดงรหัสปกติ
            foreach (var k in map.Where(kv => kv.Value == Helpers.ScanDisplay.Default).Select(kv => kv.Key).ToList()) map.Remove(k);
            return map;
        }

        // บันทึก DISPLAY ของผู้ใช้คนนั้น (ไม่กระทบผู้ใช้อื่น) / ฐานยังไม่รัน Update_20261013.sql -> บันทึกที่คลังแบบเดิม
        public void SaveScanDisplayMap(Dictionary<int, string> map, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    if (!string.IsNullOrWhiteSpace(userId) && Helpers.DbSchema.HasUserScanDisplays)
                    {
                        foreach (var kv in map)
                            using (var cmd = new SqlCommand(@"IF EXISTS (SELECT 1 FROM CIMS.UserScanDisplays WHERE UserID = @u AND StockID = @s)
                                                                  UPDATE CIMS.UserScanDisplays SET DisplayField = @v, UpdatedDate = GETDATE() WHERE UserID = @u AND StockID = @s
                                                              ELSE
                                                                  INSERT INTO CIMS.UserScanDisplays (UserID, StockID, DisplayField) VALUES (@u, @s, @v)", conn, tr))
                            {
                                cmd.Parameters.AddWithValue("@u", userId);
                                cmd.Parameters.AddWithValue("@s", kv.Key);
                                cmd.Parameters.AddWithValue("@v", string.IsNullOrWhiteSpace(kv.Value) ? Helpers.ScanDisplay.Default : kv.Value);
                                cmd.ExecuteNonQuery();
                            }
                        tr.Commit();
                        return;
                    }
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

        // 📜 รายการสแกน + รายการที่ถูกตัดจากคลังต้นทางตอนสแกนรับเข้าคลังอื่น (เช่น รับเข้า STOCK-MAT -> ตัด Coil แม่ STOCK-PANTA)
        //    แถวตัดคลังต้นทาง = OUT ของคลังต้นทาง (StockID = คลังต้นทาง, สินค้าต้นทาง, จำนวนที่ตัด, คนสแกน, ป้ายเดิม)
        //    ยกเลิกรายการสแกนรับเข้า (ADJUST SCAN) = แถวตัดคลังต้นทางยกเลิกตามไปด้วย / TransactionID ติดลบ = แถวตัด (ไม่ใช่รายการสแกนจริง)
        //    ReferenceNo = บาร์โค้ดเดิมของป้าย (ไม่เติมข้อความ) เพื่อให้ DISPLAY ของคลังต้นทาง (เช่น Panta-2 #1 = Coil ลูก) อ่านช่องได้ถูก
        //    ฐานที่ยังไม่รัน Update_20261010c.sql (ไม่มี StockTransfers.ScanTransactionID) = รายการสแกนอย่างเดียว
        public static string ScanRowsSource => CIMS.Helpers.DbSchema.HasTransferScanTx ? @"(
                SELECT t.TransactionID, t.TransactionDate, t.StockID, t.TransactionType, t.PartID, t.PartACode, t.Quantity,
                       t.UserID, t.ReferenceNo, t.IsCancelled
                FROM CIMS.ScanTransactions t
                UNION ALL
                SELECT -st.TransferID, st.TransferDate, st.FromStockID, 'OUT', ISNULL(st.SourcePartID, st.PartID), sc.PartACode, st.Quantity,
                       sc.UserID, sc.ReferenceNo, sc.IsCancelled
                FROM CIMS.StockTransfers st
                JOIN CIMS.ScanTransactions sc ON sc.TransactionID = st.ScanTransactionID
                WHERE st.TransferMode = 'SCAN')" : "CIMS.ScanTransactions";

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
                                    ISNULL(t.ReferenceNo, '') AS ReferenceNo,
                                    t.TransactionID
                               FROM " + ScanRowsSource + @" t
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
                                Qty = CIMS.Helpers.Qty.Read(rdr["Quantity"]),
                                UpdateTime = Convert.ToDateTime(rdr["TransactionDate"]),
                                Status = rdr["TransactionType"].ToString(),
                                PartA = rdr["PartA"].ToString(),
                                PartNumber = rdr["PartNumber"].ToString(),
                                Model = rdr["Model"].ToString(),
                                RefNo = rdr["ReferenceNo"].ToString(),
                                IsDeductOut = Convert.ToInt64(rdr["TransactionID"]) < 0
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
        // กล่อง / Coil ที่การสแกนรับเข้าขยับ (บันทึกลง ScanTransactions.BoxChange ด้วย)
        //   คลังนับ Coil (KG): สแกนเข้า / คืน = +1 Coil, สแกนเศษ = 0 (รวมคลังหลัก)
        //   คลังอื่น (กล่อง): สแกนป้าย = +1 / สแกนเศษ = 0 / คืนเหล็ก = คิดจากชิ้น (NULL)
        //   คลังหลักที่ไม่นับ Coil: ไม่มีจำนวนกล่องแยก (NULL)
        private static object BoxMove(StockModel stock, string txType, bool remainder)
        {
            if (stock != null && stock.CountCoil) return remainder ? 0 : 1;
            bool isOtherStock = stock != null && !stock.IsMain;
            return !isOtherStock || txType == "RETURN" ? (object)DBNull.Value : (remainder ? 0 : 1);
        }

        // ==========================================
        // 🧲 ทะเบียน Coil (CIMS.Coils) - เลข Coil ลูก / แม่ จากป้าย (Barcode Format ที่ตั้ง COIL NO FIELD #)
        // ==========================================
        public class CoilLabel
        {
            public string CoilNo { get; set; }       // Coil ลูก เช่น CWE0885B-006
            public string MotherCoil { get; set; }   // Coil แม่ เช่น CWE0885B
            public bool HasCoilNo => !string.IsNullOrWhiteSpace(CoilNo);
        }

        public class CoilState
        {
            public int CoilId { get; set; }
            public int StockId { get; set; }
            public string StockCode { get; set; }
            public int PartId { get; set; }
            public string Status { get; set; }
        }

        // Coil ลูกนี้อยู่ที่ไหน (null = ยังไม่มีในทะเบียน)
        public CoilState FindCoil(string coilNo)
        {
            if (string.IsNullOrWhiteSpace(coilNo) || !CIMS.Helpers.DbSchema.HasCoilRegister) return null;
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"SELECT c.CoilID, c.StockID, s.StockCode, c.PartID, c.Status FROM CIMS.Coils c
                                              JOIN CIMS.Stocks s ON s.StockID = c.StockID WHERE c.CoilNo = @c", conn))
            {
                cmd.Parameters.AddWithValue("@c", coilNo.Trim());
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? new CoilState { CoilId = r.GetInt32(0), StockId = r.GetInt32(1), StockCode = r.GetString(2), PartId = r.GetInt32(3), Status = r.GetString(4) } : null;
            }
        }

        // รับ Coil ลูกเข้าคลัง (ใหม่ = สร้าง / มีอยู่แล้ว = ย้ายมา) - บันทึก CoilMoves ผูกกับรายการสแกน (ใช้ตอน ADJUST ยกเลิก)
        internal static void CoilReceive(SqlConnection conn, SqlTransaction trans, CoilLabel coil, int ptId, int stkId, decimal weight, int? txId, string userId, string action = null)
        {
            if (coil == null || !coil.HasCoilNo || !CIMS.Helpers.DbSchema.HasCoilRegister) return;
            int coilId = 0, fromStock = 0, fromPart = 0; string fromStatus = null; decimal fromWeight = 0;
            using (var cmd = new SqlCommand("SELECT CoilID, StockID, PartID, Status, WeightKG FROM CIMS.Coils WITH (UPDLOCK, HOLDLOCK) WHERE CoilNo = @c", conn, trans))
            {
                cmd.Parameters.AddWithValue("@c", coil.CoilNo.Trim());
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { coilId = r.GetInt32(0); fromStock = r.GetInt32(1); fromPart = r.GetInt32(2); fromStatus = r.GetString(3); fromWeight = r.GetDecimal(4); }
            }
            if (coilId == 0)
            {
                using (var cmd = new SqlCommand(@"INSERT INTO CIMS.Coils (CoilNo, MotherCoil, PartID, StockID, WeightKG, Status, ReceivedBy)
                                                  VALUES (@c, @m, @p, @s, @w, 'IN', @u); SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans))
                {
                    cmd.Parameters.AddWithValue("@c", coil.CoilNo.Trim());
                    cmd.Parameters.AddWithValue("@m", string.IsNullOrWhiteSpace(coil.MotherCoil) ? (object)DBNull.Value : coil.MotherCoil.Trim());
                    cmd.Parameters.AddWithValue("@p", ptId);
                    cmd.Parameters.AddWithValue("@s", stkId);
                    cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@w", weight));
                    cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                    coilId = Convert.ToInt32(cmd.ExecuteScalar());
                }
                AddCoilMove(conn, trans, coilId, action ?? "CREATE", txId, null, null, null, null, stkId, ptId, "IN", weight, userId);
            }
            else
            {
                using (var cmd = new SqlCommand(@"UPDATE CIMS.Coils SET StockID = @s, PartID = @p, WeightKG = @w, Status = 'IN', OutDate = NULL,
                                                         MotherCoil = ISNULL(@m, MotherCoil), ReceivedDate = GETDATE(), ReceivedBy = @u, UpdatedDate = GETDATE()
                                                  WHERE CoilID = @id", conn, trans))
                {
                    cmd.Parameters.AddWithValue("@id", coilId);
                    cmd.Parameters.AddWithValue("@s", stkId);
                    cmd.Parameters.AddWithValue("@p", ptId);
                    cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@w", weight));
                    cmd.Parameters.AddWithValue("@m", string.IsNullOrWhiteSpace(coil.MotherCoil) ? (object)DBNull.Value : coil.MotherCoil.Trim());
                    cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
                AddCoilMove(conn, trans, coilId, action ?? "MOVE", txId, fromStock, fromPart, fromStatus, fromWeight, stkId, ptId, "IN", weight, userId);
            }
        }

        // Coil แม่ (COIL NO = เลข Coil แม่) ในคลังต้นทาง: หักน้ำหนักเท่าที่ตัด / เหลือ 0 = OUT (ใช้หมดแล้ว)
        private static void ConsumeMotherCoil(SqlConnection conn, SqlTransaction trans, string mother, int stkId, int partId, decimal used, int txId, string userId)
        {
            int coilId = 0; decimal w = 0;
            using (var cmd = new SqlCommand(@"SELECT TOP 1 CoilID, WeightKG FROM CIMS.Coils WITH (UPDLOCK, HOLDLOCK)
                                              WHERE CoilNo = @m AND StockID = @s AND PartID = @p AND Status = 'IN'", conn, trans))
            {
                cmd.Parameters.AddWithValue("@m", mother);
                cmd.Parameters.AddWithValue("@s", stkId);
                cmd.Parameters.AddWithValue("@p", partId);
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { coilId = r.GetInt32(0); w = r.GetDecimal(1); }
            }
            if (coilId == 0) return;   // ไม่มี Coil แม่ลงทะเบียนเอง (มีแค่ Coil ลูกที่อ้างถึง) -> ไม่ต้องหัก
            decimal left = Math.Max(0, w - used);
            string status = left <= 0 ? "OUT" : "IN";
            using (var cmd = new SqlCommand(@"UPDATE CIMS.Coils SET WeightKG = @w, Status = @st, OutDate = CASE WHEN @st = 'OUT' THEN GETDATE() ELSE OutDate END,
                                                     UpdatedDate = GETDATE() WHERE CoilID = @id", conn, trans))
            {
                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@w", left));
                cmd.Parameters.AddWithValue("@st", status);
                cmd.Parameters.AddWithValue("@id", coilId);
                cmd.ExecuteNonQuery();
            }
            AddCoilMove(conn, trans, coilId, "CONSUME", txId, stkId, partId, "IN", w, stkId, partId, status, left, userId);
        }

        // จ่าย Coil ลูกออกทั้งลูก (อยู่ในคลังนี้เท่านั้น)
        private static void CoilOut(SqlConnection conn, SqlTransaction trans, CoilLabel coil, int stkId, int? txId, string userId)
        {
            if (coil == null || !coil.HasCoilNo || !CIMS.Helpers.DbSchema.HasCoilRegister) return;
            int coilId = 0, part = 0; decimal w = 0;
            using (var cmd = new SqlCommand("SELECT CoilID, PartID, WeightKG FROM CIMS.Coils WITH (UPDLOCK, HOLDLOCK) WHERE CoilNo = @c AND StockID = @s AND Status = 'IN'", conn, trans))
            {
                cmd.Parameters.AddWithValue("@c", coil.CoilNo.Trim());
                cmd.Parameters.AddWithValue("@s", stkId);
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { coilId = r.GetInt32(0); part = r.GetInt32(1); w = r.GetDecimal(2); }
            }
            if (coilId == 0) return;
            using (var cmd = new SqlCommand("UPDATE CIMS.Coils SET Status = 'OUT', OutDate = GETDATE(), UpdatedDate = GETDATE() WHERE CoilID = @id", conn, trans))
            {
                cmd.Parameters.AddWithValue("@id", coilId);
                cmd.ExecuteNonQuery();
            }
            AddCoilMove(conn, trans, coilId, "OUT", txId, stkId, part, "IN", w, stkId, part, "OUT", w, userId);
        }

        private static void AddCoilMove(SqlConnection conn, SqlTransaction trans, int coilId, string action, int? txId, int? fromStock, int? fromPart, string fromStatus, decimal? fromWeight,
                                        int toStock, int toPart, string toStatus, decimal weight, string userId)
        {
            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.CoilMoves (CoilID, Action, ScanTransactionID, FromStockID, FromPartID, FromStatus, FromWeightKG, ToStockID, ToPartID, ToStatus, WeightKG, UserID)
                                              VALUES (@c, @a, @tx, @fs, @fp, @fst, @fw, @ts, @tp, @tst, @w, @u)", conn, trans))
            {
                cmd.Parameters.AddWithValue("@c", coilId);
                cmd.Parameters.AddWithValue("@a", action);
                cmd.Parameters.AddWithValue("@tx", (object)txId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fs", (object)fromStock ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fp", (object)fromPart ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fst", (object)fromStatus ?? DBNull.Value);
                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@fw", fromWeight));
                cmd.Parameters.AddWithValue("@ts", toStock);
                cmd.Parameters.AddWithValue("@tp", toPart);
                cmd.Parameters.AddWithValue("@tst", toStatus);
                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@w", weight));
                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
        public bool UpdateStock(int ptId, string partCode, string partACode, decimal qty, string userId, string refNo, string txType = "IN", StockModel stock = null, bool remainder = false, CoilLabel coil = null)
        {
            // กันพลาด: ปัดตาม DECIMAL QTY ของคลัง (คลังที่ไม่เปิด = จำนวนเต็มเสมอ)
            if (stock != null) qty = CIMS.Helpers.Qty.Round(qty, stock.AllowDecimal);
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                SqlTransaction trans = conn.BeginTransaction();

                try
                {
                    // คลังหลักที่นับ Coil (STOCK-4C): Coil ของคลังหลักเก็บที่ CIMS.Parts.CoilQuantity
                    string updateSql = @"UPDATE CIMS.Parts
                                 SET StockQuantity = ISNULL(StockQuantity, 0) + @Qty, CoilQuantity = CoilQuantity + @Coil
                                 WHERE PartID = @PtId";
                    if (!CIMS.Helpers.DbSchema.HasCountCoil) updateSql = updateSql.Replace(", CoilQuantity = CoilQuantity + @Coil", "");

                    // ✅ เพิ่มคอลัมน์ ReferenceNo + StockID (คลังที่แสกน)
                    string insertLogSql = @"INSERT INTO CIMS.ScanTransactions (UserID, Quantity, TransactionType, TransactionDate, PartID, PartACode, ReferenceNo, StockID, BoxChange)
                                    VALUES (@UserId, @Qty, @TxType, GETDATE(), @PtId, @PtACode, @RefNo, @StkId, @Box);
                                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    // กล่อง / Coil ที่รายการนี้ขยับ: ดู BoxMove()
                    object boxMove = BoxMove(stock, txType, remainder);

                    if (stock != null && !stock.IsMain)
                    {
                        using (SqlCommand cmdAdd = new SqlCommand("CIMS.sp_Stock_AddQty", conn, trans) { CommandType = CommandType.StoredProcedure })
                        {
                            cmdAdd.Parameters.AddWithValue("@StkId", stock.StkId);
                            cmdAdd.Parameters.AddWithValue("@PtId", ptId);
                            cmdAdd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Qty", qty));
                            cmdAdd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@NewBal", 0m)).Direction = ParameterDirection.Output;
                            // แบบ StorePC: สแกน 1 ป้าย = +1 กล่อง (ชิ้น = ตามป้าย) / คืนเหล็ก (กรอกจำนวนเอง) = กล่องคิดจากชิ้น
                            cmdAdd.Parameters.AddWithValue("@Box", boxMove);
                            cmdAdd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (SqlCommand cmdUpdate = new SqlCommand(updateSql, conn, trans))
                        {
                            cmdUpdate.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Qty", qty));
                            cmdUpdate.Parameters.AddWithValue("@PtId", ptId);
                            cmdUpdate.Parameters.AddWithValue("@Coil", boxMove is int c ? c : 0);
                            cmdUpdate.ExecuteNonQuery();
                        }
                    }

                    using (SqlCommand cmdLog = new SqlCommand(insertLogSql, conn, trans))
                    {
                        cmdLog.Parameters.Add("@StkId", SqlDbType.Int).Value = (object)stock?.StkId ?? DBNull.Value;
                        cmdLog.Parameters.AddWithValue("@UserId", userId);
                        cmdLog.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Qty", qty));
                        cmdLog.Parameters.AddWithValue("@TxType", string.IsNullOrWhiteSpace(txType) ? "IN" : txType);
                        cmdLog.Parameters.Add("@Box", SqlDbType.Int).Value = boxMove;
                        cmdLog.Parameters.AddWithValue("@PtId", ptId);
                        cmdLog.Parameters.AddWithValue("@PtACode", string.IsNullOrWhiteSpace(partACode) ? DBNull.Value : (object)partACode.Trim());

                        // 🛡️ กัน Truncate Error เผื่อบาร์โค้ดยาวเกินขนาดคอลัมน์ ReferenceNo (varchar 200)
                        string safeRefNo = string.IsNullOrWhiteSpace(refNo)
                            ? null
                            : (refNo.Length > RefNoMaxLength ? refNo.Substring(0, RefNoMaxLength) : refNo);
                        cmdLog.Parameters.AddWithValue("@RefNo", (object)safeRefNo ?? DBNull.Value);

                        int txId = Convert.ToInt32(cmdLog.ExecuteScalar());
                        // 🧲 ป้ายที่มีเลข Coil ลูก: เก็บ / ย้าย Coil ลูกเข้าคลังนี้ (สแกนเศษไม่นับ Coil)
                        if (stock != null && stock.CountCoil && !remainder) CoilReceive(conn, trans, coil, ptId, stock.StkId, qty, txId, userId);
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
            public decimal Deducted { get; set; }      // ตัดจากคลังต้นทางได้จริง
            public decimal SourceBefore { get; set; }  // ยอดคลังต้นทางก่อนตัด
            public decimal SourceAfter { get; set; }
            public string SourceCode { get; set; }
            public string SourcePartCode { get; set; } // สินค้าในคลังต้นทางที่ถูกตัด (BIN เดียวกัน / สินค้าเดียวกัน)
            public bool MatchedByBin { get; set; }
            public string MatchedHow { get; set; }      // COIL / MOTHER COIL / BIN / SAME PRODUCT
            public bool Short => Saved && Deducted < RequestedQty;
            public decimal? MotherBefore { get; set; }  // น้ำหนัก Coil แม่ก่อนตัด (null = ไม่มี Coil แม่ลงทะเบียน)
            public string MotherCoil { get; set; }
            public decimal RequestedQty { get; set; }
            public bool Held { get; set; }              // ตัดต้นทางไม่ได้ -> ค้างไว้ที่ NOT DEDUCTED (ยังไม่รับเข้า)
            public int TxId { get; set; }
        }

        // 📋 NOT DEDUCTED แบบรอดำเนินการ (Update_20261016b.sql): ป้ายที่ยังไม่รับเข้าคลัง - รอผู้มีสิทธิ์ APPROVE ของ Multi-Scanner
        //    reason: NOT_FOUND (ไม่มี Coil แม่ในคลังต้นทาง) / SHORT (Coil แม่เหลือไม่พอ) / NO_PRODUCT (สินค้ายังไม่ลงทะเบียน)
        public int AddPendingMiss(int stockId, int sourceStkId, int? partId, CoilLabel coil, decimal qty, decimal canCut, string reason,
                                  string barcode, string userId, int? formatId)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.DeductMisses (TransactionID, StockID, SourceStockID, PartID, CoilNo, MotherCoil, LabelQty, Deducted, Reason, Barcode, UserID, Status, FormatID)
                                              VALUES (NULL, @s, @src, @p, @c, @m, @q, @d, @why, @b, @u, 'PENDING', @f);
                                              SELECT CAST(SCOPE_IDENTITY() AS INT);", conn))
            {
                cmd.Parameters.AddWithValue("@s", stockId);
                cmd.Parameters.AddWithValue("@src", sourceStkId);
                cmd.Parameters.AddWithValue("@p", (object)partId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@c", coil?.HasCoilNo == true ? coil.CoilNo.Trim() : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@m", string.IsNullOrWhiteSpace(coil?.MotherCoil) ? (object)DBNull.Value : coil.MotherCoil.Trim());
                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@q", qty));
                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@d", canCut));
                cmd.Parameters.AddWithValue("@why", reason);
                string bc = barcode ?? "";
                cmd.Parameters.AddWithValue("@b", bc.Length > 300 ? bc.Substring(0, 300) : bc);
                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@f", (object)formatId ?? DBNull.Value);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // ป้ายเดียวกันค้างอยู่ใน NOT DEDUCTED แล้ว (กันสแกนซ้ำ) - เทียบเลข Coil ลูก ไม่มีเลข Coil = เทียบป้ายทั้งชุด
        public bool HasPendingMiss(string coilNo, string barcode)
        {
            if (!CIMS.Helpers.DbSchema.HasMissStatus) return false;
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"SELECT COUNT(*) FROM CIMS.DeductMisses WHERE Status = 'PENDING'
                                                AND ((@c <> '' AND CoilNo = @c) OR (@c = '' AND Barcode = @b))", conn))
            {
                cmd.Parameters.AddWithValue("@c", (coilNo ?? "").Trim());
                string bc = barcode ?? "";
                cmd.Parameters.AddWithValue("@b", bc.Length > 300 ? bc.Substring(0, 300) : bc);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // BIN ที่ใช้จับคู่ได้ (ว่าง / N/A / - = ไม่มี BIN)
        private static string BinKey(object bin)
        {
            string b = (bin == null || bin == DBNull.Value ? "" : bin.ToString()).Trim().ToUpperInvariant();
            return b == "N/A" || b == "-" ? "" : b;
        }

        // 📥🔁 รับเข้าคลังที่สแกน (ทุกคลัง ไม่จำกัดคลังหลัก) + ตัดยอดคลังต้นทางของป้ายอัตโนมัติ (เช่น Panta -> STOCK-PANTA)
        //   สินค้าในคลังต้นทางที่ถูกตัด = สินค้าที่ BIN เดียวกับสินค้าที่สแกน (PRODUCT CODE 2 คลังต่างกันได้)
        //   ไม่มี BIN / ไม่เจอ BIN เดียวกัน = สินค้าตัวเดียวกัน (แบบเดิม) / BIN ซ้ำหลายตัว = ตัวเดียวกันก่อน แล้วตัวที่ยอดมากสุด
        //   รับเข้าเต็มจำนวนตามป้ายเสมอ (ของอยู่หน้างานแล้ว) ตัดต้นทาง "เท่าที่มี" (ไม่ติดลบ) / คลังนับ Coil: รับเข้า +1 Coil, ต้นทาง -1 Coil
        //   holdPending   = ตัดต้นทางตาม Coil แม่ไม่ได้ / ไม่ครบ -> ไม่รับเข้าเลย เก็บเป็น NOT DEDUCTED รอดำเนินการ (Held = true)
        //   approveMissId = APPROVE / ลงทะเบียนจาก NOT DEDUCTED: รับเข้า + ตัดเท่าที่ตัดได้ แล้วปิดรายการนั้น (approveStatus) ไม่สร้างรายการใหม่
        //   existingTxId  = รายการเดิมที่รับเข้าไปแล้ว (RECEIVED ก่อนอัพเดทนี้): ไม่รับเข้าซ้ำ ตัดต้นทางอย่างเดียว ผูกกับรายการสแกนเดิม
        public DeductResult UpdateStockWithDeduct(int ptId, string partCode, string partACode, decimal qty, string userId, string refNo,
                                                  StockModel stock, int sourceStkId, bool remainder = false, CoilLabel coil = null,
                                                  bool holdPending = false, int? approveMissId = null, string approveStatus = "APPROVED",
                                                  int? existingTxId = null, int? formatId = null)
        {
            if (stock != null) qty = CIMS.Helpers.Qty.Round(qty, stock.AllowDecimal);
            var result = new DeductResult { RequestedQty = qty };
            object boxMove = BoxMove(stock, "IN", remainder);
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        decimal toBalance = 0; int txId = existingTxId ?? 0;

                        // 2) คลังต้นทาง + สินค้าที่จะตัด: Coil ลูกเดียวกัน > Coil แม่เดียวกัน > BIN เดียวกัน > สินค้าตัวเดียวกัน
                        bool sourceCoil = false;
                        using (var cmd = new SqlCommand("SELECT StockCode, " + (CIMS.Helpers.DbSchema.HasCountCoil ? "CountCoil" : "CAST(0 AS BIT)") + " FROM CIMS.Stocks WHERE StockID = @s", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", sourceStkId);
                            using (var r = cmd.ExecuteReader())
                                if (r.Read()) { result.SourceCode = r.GetString(0); sourceCoil = Convert.ToBoolean(r[1]); }
                        }
                        if (result.SourceCode == null) result.SourceCode = $"STK_{sourceStkId}";

                        // ไม่ใช้ BIN แล้ว (ต.ค. 2026): หาต้นทางจากทะเบียน Coil (Coil ลูก > Coil แม่) ไม่เจอ = สินค้าตัวเดียวกัน

                        // 🧲 ทะเบียน Coil: Coil ลูกนี้อยู่ในคลังต้นทาง -> สินค้าของลูกนั้น / ไม่เจอลูก -> สินค้าที่มี Coil แม่เดียวกันในคลังต้นทาง
                        int coilPart = 0; string how = null;
                        if (coil != null && CIMS.Helpers.DbSchema.HasCoilRegister)
                        {
                            if (coil.HasCoilNo)
                                using (var cmd = new SqlCommand("SELECT PartID FROM CIMS.Coils WHERE CoilNo = @c AND StockID = @s AND Status = 'IN'", conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@c", coil.CoilNo.Trim());
                                    cmd.Parameters.AddWithValue("@s", sourceStkId);
                                    object v = cmd.ExecuteScalar();
                                    if (v != null && v != DBNull.Value) { coilPart = Convert.ToInt32(v); how = "COIL"; }
                                }
                            if (coilPart == 0 && !string.IsNullOrWhiteSpace(coil.MotherCoil))
                                // Coil แม่ = Coil ลูกที่มี MOTHER COIL นี้ หรือ Coil แม่ที่ลงทะเบียนไว้เอง (COIL NO = เลข Coil แม่)
                                using (var cmd = new SqlCommand(@"SELECT TOP 1 PartID FROM CIMS.Coils WHERE (MotherCoil = @m OR CoilNo = @m) AND StockID = @s AND Status = 'IN'
                                                                  GROUP BY PartID ORDER BY COUNT(*) DESC, PartID", conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@m", coil.MotherCoil.Trim());
                                    cmd.Parameters.AddWithValue("@s", sourceStkId);
                                    object v = cmd.ExecuteScalar();
                                    if (v != null && v != DBNull.Value) { coilPart = Convert.ToInt32(v); how = "MOTHER COIL"; }
                                }
                        }
                        // ป้ายมีเลข Coil แม่ = ตัดได้เฉพาะ Coil แม่นั้นในคลังต้นทาง (ไม่ตัดสินค้าตัวเดียวกันแทน)
                        bool byMotherOnly = coil != null && !string.IsNullOrWhiteSpace(coil.MotherCoil) && CIMS.Helpers.DbSchema.HasCoilRegister;
                        result.MotherCoil = byMotherOnly ? coil.MotherCoil.Trim() : null;
                        int srcPart = 0; int srcCoil = 0;
                        if (!(byMotherOnly && coilPart == 0))
                        using (var cmd = new SqlCommand(@"
                            SELECT TOP 1 ps.PartID, ps.Quantity, ps.BoxQuantity, p.PartCode
                            FROM CIMS.PartStocks ps WITH (UPDLOCK, HOLDLOCK)
                            JOIN CIMS.Parts p ON p.PartID = ps.PartID
                            WHERE ps.StockID = @s AND ps.PartID = CASE WHEN @cp > 0 THEN @cp ELSE @p END", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", sourceStkId);
                            cmd.Parameters.AddWithValue("@p", ptId);
                            cmd.Parameters.AddWithValue("@cp", coilPart);
                            using (var r = cmd.ExecuteReader())
                                if (r.Read())
                                {
                                    srcPart = r.GetInt32(0);
                                    result.SourceBefore = CIMS.Helpers.Qty.Read(r[1]);
                                    srcCoil = Convert.ToInt32(r[2]);
                                    result.SourcePartCode = r.GetString(3);
                                    result.MatchedByBin = coilPart > 0 && srcPart != ptId;   // ตัดจากคนละรหัส (ผ่านทะเบียน Coil)
                                    result.MatchedHow = how ?? "SAME PRODUCT";
                                }
                        }

                        result.Deducted = srcPart == 0 ? 0 : Math.Max(0, Math.Min(qty, result.SourceBefore));
                        // Coil แม่ลงทะเบียนเป็นม้วนของตัวเอง -> ตัดได้ไม่เกินน้ำหนักที่ Coil แม่เหลือ
                        if (how == "MOTHER COIL" && result.Deducted > 0)
                            using (var cmd = new SqlCommand("SELECT TOP 1 WeightKG FROM CIMS.Coils WHERE CoilNo = @m AND StockID = @s AND PartID = @p AND Status = 'IN'", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@m", coil.MotherCoil.Trim());
                                cmd.Parameters.AddWithValue("@s", sourceStkId);
                                cmd.Parameters.AddWithValue("@p", srcPart);
                                object v = cmd.ExecuteScalar();
                                if (v != null && v != DBNull.Value) result.MotherBefore = Convert.ToDecimal(v);
                                if (result.MotherBefore.HasValue) result.Deducted = Math.Min(result.Deducted, result.MotherBefore.Value);
                            }
                        result.SourceAfter = result.SourceBefore - result.Deducted;

                        // ⏸ ตัด Coil แม่ไม่ได้ / ไม่ครบ -> ไม่รับเข้า เก็บเป็น NOT DEDUCTED รอผู้มีสิทธิ์ APPROVE (ไม่มีอะไรเปลี่ยนในคลัง)
                        if (holdPending && result.MotherCoil != null && result.Deducted < qty && !remainder && CIMS.Helpers.DbSchema.HasMissStatus)
                        {
                            trans.Rollback();
                            AddPendingMiss(stock.StkId, sourceStkId, ptId, coil, qty, result.Deducted, result.MatchedHow == null ? "NOT_FOUND" : "SHORT", refNo, userId, formatId);
                            result.Held = true;
                            return result;
                        }

                        // 1) รับเข้าคลังที่สแกน (CIMS.sp_Stock_AddQty: คลังหลัก = CIMS.Parts / คลังอื่น = CIMS.PartStocks) - รายการเดิมที่รับไปแล้ว = ข้าม
                        if (existingTxId == null)
                        {
                            using (var cmd = new SqlCommand("CIMS.sp_Stock_AddQty", conn, trans) { CommandType = CommandType.StoredProcedure })
                            {
                                cmd.Parameters.AddWithValue("@StkId", stock.StkId);
                                cmd.Parameters.AddWithValue("@PtId", ptId);
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Qty", qty));
                                var nb = cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@NewBal", 0m)); nb.Direction = ParameterDirection.Output;
                                cmd.Parameters.AddWithValue("@Box", boxMove);
                                cmd.ExecuteNonQuery();
                                toBalance = CIMS.Helpers.Qty.Read(nb.Value);
                            }

                            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.ScanTransactions (UserID, Quantity, TransactionType, TransactionDate, PartID, PartACode, ReferenceNo, StockID, BoxChange)
                                                              VALUES (@UserId, @Qty, 'IN', GETDATE(), @PtId, @PtACode, @RefNo, @StkId, @Box);
                                                              SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@UserId", userId);
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Qty", qty));
                                cmd.Parameters.AddWithValue("@PtId", ptId);
                                cmd.Parameters.AddWithValue("@PtACode", string.IsNullOrWhiteSpace(partACode) ? DBNull.Value : (object)partACode.Trim());
                                string safeRefNo = string.IsNullOrWhiteSpace(refNo) ? null : (refNo.Length > RefNoMaxLength ? refNo.Substring(0, RefNoMaxLength) : refNo);
                                cmd.Parameters.AddWithValue("@RefNo", (object)safeRefNo ?? DBNull.Value);
                                cmd.Parameters.Add("@StkId", SqlDbType.Int).Value = stock.StkId;
                                cmd.Parameters.Add("@Box", SqlDbType.Int).Value = boxMove;
                                txId = Convert.ToInt32(cmd.ExecuteScalar());
                            }
                        }
                        else
                            using (var cmd = new SqlCommand(stock.IsMain ? "SELECT StockQuantity FROM CIMS.Parts WHERE PartID = @p"
                                                                         : "SELECT Quantity FROM CIMS.PartStocks WHERE PartID = @p AND StockID = @s", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.AddWithValue("@s", stock.StkId);
                                toBalance = CIMS.Helpers.Qty.Read(cmd.ExecuteScalar());
                            }
                        result.TxId = txId;

                        if (result.Deducted > 0)
                        {
                            // คลังต้นทางนับ Coil: -1 Coil (ไม่ต่ำกว่า 0) ใน Statement เดียวกับ KG (Trigger จึงไม่คำนวณกล่องใหม่)
                            //   เจอผ่านทะเบียน Coil -> ไม่ลบ 1 ตรงนี้ นับใหม่จากทะเบียนด้านล่างแทน (Coil แม่ 1 ม้วน = Coil ลูกหลายม้วน)
                            bool coilOut = sourceCoil && !remainder && srcCoil > 0 && how == null;
                            string sql = sourceCoil
                                ? "UPDATE CIMS.PartStocks SET Quantity = Quantity - @q, BoxQuantity = BoxQuantity - @c, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @sp"
                                : "UPDATE CIMS.PartStocks SET Quantity = Quantity - @q, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @sp";
                            using (var cmd = new SqlCommand(sql, conn, trans))
                            {
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@q", result.Deducted));
                                cmd.Parameters.AddWithValue("@c", coilOut ? 1 : 0);
                                cmd.Parameters.AddWithValue("@s", sourceStkId);
                                cmd.Parameters.AddWithValue("@sp", srcPart);
                                cmd.ExecuteNonQuery();
                            }

                            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.StockTransfers (FromStockID, FromStockCode, ToStockID, ToStockCode, PartID, PartCode,
                                                                  Quantity, TransferMode, FromBalanceAfter, ToBalanceAfter, UserID, SourcePartID)
                                                              VALUES (@s, @sc, @m, @mc, @p, @pc, @q, 'SCAN', @fa, @ta, @u, @sp)", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@s", sourceStkId);
                                cmd.Parameters.AddWithValue("@sc", result.SourceCode);
                                cmd.Parameters.AddWithValue("@m", stock.StkId);
                                cmd.Parameters.AddWithValue("@mc", stock.Code ?? "MAIN");
                                cmd.Parameters.AddWithValue("@p", ptId);
                                // ตัดจากคนละรหัส (BIN เดียวกัน) -> เก็บรหัสต้นทางไว้ด้วย
                                string pc = result.MatchedByBin ? $"{partCode} <- {result.SourcePartCode}" : (partCode ?? "");
                                cmd.Parameters.AddWithValue("@pc", pc.Length > 50 ? pc.Substring(0, 50) : pc);
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@q", result.Deducted));
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@fa", result.SourceAfter));
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@ta", toBalance));
                                cmd.Parameters.AddWithValue("@u", userId);
                                cmd.Parameters.AddWithValue("@sp", srcPart);
                                if (!CIMS.Helpers.DbSchema.HasCountCoil) { cmd.CommandText = cmd.CommandText.Replace(", SourcePartID)", ")").Replace(", @sp)", ")"); }
                                // รายการ DEDUCTED: ผูกกับรายการแสกนนี้ (Update_20261010c.sql)
                                if (CIMS.Helpers.DbSchema.HasTransferScanTx)
                                {
                                    cmd.CommandText = cmd.CommandText.Replace("UserID, SourcePartID)", "UserID, SourcePartID, ScanTransactionID)").Replace("@u, @sp)", "@u, @sp, @stx)");
                                    cmd.Parameters.AddWithValue("@stx", txId);
                                }
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // 🧲 Coil แม่ในคลังต้นทาง: หักน้ำหนักที่ตัดออก / เหลือ 0 = Coil แม่หมด (OUT) - บันทึก CoilMoves ผูกรายการสแกน (ยกเลิกแล้วคืนได้)
                        if (how == "MOTHER COIL" && result.Deducted > 0) ConsumeMotherCoil(conn, trans, coil.MotherCoil.Trim(), sourceStkId, srcPart, result.Deducted, txId, userId);

                        // 🧲 ย้าย Coil ลูก (มีอยู่ในคลังต้นทาง) / เพิ่มใหม่ เข้าคลังที่รับ - น้ำหนักตามป้าย (รายการเดิมที่รับไปแล้ว = ทำไปแล้ว)
                        if (stock.CountCoil && !remainder && existingTxId == null) CoilReceive(conn, trans, coil, ptId, stock.StkId, qty, txId, userId);

                        // STOCK (COIL) ของต้นทาง = จำนวน Coil ในทะเบียน (เมื่อหาเจอผ่านทะเบียน Coil)
                        if (how != null && sourceCoil && srcPart > 0) CoilImportService.RecountCoils(conn, trans, srcPart, sourceStkId, false);

                        // ✔ ปิดรายการ NOT DEDUCTED ที่ APPROVE / ลงทะเบียน -> ไปแสดงที่ DEDUCTED
                        if (approveMissId.HasValue)
                            using (var cmd = new SqlCommand(@"UPDATE CIMS.DeductMisses SET Status = @st, TransactionID = @tx, PartID = @p, Deducted = @d,
                                                                     ActionBy = @u, ActionDate = GETDATE() WHERE MissID = @id", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@st", approveStatus);
                                cmd.Parameters.AddWithValue("@tx", txId);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@d", result.Deducted));
                                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@id", approveMissId.Value);
                                cmd.ExecuteNonQuery();
                            }

                        // 📋 ตัดตาม Coil แม่ไม่ได้ / ได้ไม่ครบ -> เก็บไว้ในรายการ NOT DEDUCTED (ปุ่มหน้า Multi-Scanner) - ฐานเดิมที่ยังไม่มีสถานะรอดำเนินการ
                        if (approveMissId == null && !holdPending && result.MotherCoil != null && result.Deducted < qty && !remainder && CIMS.Helpers.DbSchema.HasDeductMisses)
                            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.DeductMisses (TransactionID, StockID, SourceStockID, PartID, CoilNo, MotherCoil, LabelQty, Deducted, Reason, Barcode, UserID)
                                                              VALUES (@tx, @s, @src, @p, @c, @m, @q, @d, @why, @b, @u)", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@tx", txId);
                                cmd.Parameters.AddWithValue("@s", stock.StkId);
                                cmd.Parameters.AddWithValue("@src", sourceStkId);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.AddWithValue("@c", coil?.HasCoilNo == true ? coil.CoilNo.Trim() : (object)DBNull.Value);
                                cmd.Parameters.AddWithValue("@m", result.MotherCoil);
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@q", qty));
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@d", result.Deducted));
                                cmd.Parameters.AddWithValue("@why", result.MatchedHow == null ? "NOT_FOUND" : "SHORT");
                                string bc = refNo ?? "";
                                cmd.Parameters.AddWithValue("@b", bc.Length > 300 ? bc.Substring(0, 300) : bc);
                                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                                cmd.ExecuteNonQuery();
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
        public bool UpdateStockOut(int ptId, string partCode, string partACode, decimal qty, string userId, string refNo, StockModel stock = null, bool remainder = false, CoilLabel coil = null)
        {
            if (stock != null) qty = CIMS.Helpers.Qty.Round(qty, stock.AllowDecimal);
            bool isOther = stock != null && !stock.IsMain;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        // คลังหลักที่นับ Coil: Coil อยู่ที่ CIMS.Parts.CoilQuantity
                        bool mainCoil = !isOther && stock != null && stock.CountCoil && CIMS.Helpers.DbSchema.HasCountCoil;
                        string checkSql = isOther
                            ? @"SELECT ISNULL(Quantity, 0), ISNULL(BoxQuantity, 0) FROM CIMS.PartStocks WITH (UPDLOCK, HOLDLOCK) WHERE StockID = @StkId AND PartID = @PtId"
                            : mainCoil
                            ? @"SELECT ISNULL(StockQuantity, 0), CoilQuantity FROM CIMS.Parts WITH (UPDLOCK, HOLDLOCK) WHERE PartID = @PtId"
                            : @"SELECT ISNULL(StockQuantity, 0), 0 FROM CIMS.Parts WHERE PartID = @PtId";
                        decimal currentStock = 0; int currentBox = 0;
                        using (SqlCommand cmdCheck = new SqlCommand(checkSql, conn, trans))
                        {
                            cmdCheck.Parameters.Add("@PtId", SqlDbType.Int).Value = ptId;
                            if (isOther) cmdCheck.Parameters.Add("@StkId", SqlDbType.Int).Value = stock.StkId;
                            using (var rd = cmdCheck.ExecuteReader())
                                if (rd.Read())
                                {
                                    currentStock = CIMS.Helpers.Qty.Read(rd[0]);
                                    currentBox = Convert.ToInt32(rd[1]);
                                }
                        }
                        // กล่อง / Coil ที่ลดจริง: สแกนป้าย = -1 (ถ้ายังเหลือ) / สแกนเศษ = 0
                        int boxOut = (isOther || mainCoil) && !remainder && currentBox > 0 ? -1 : 0;

                        if (currentStock < qty)
                        {
                            trans.Rollback();
                            return false;
                        }

                        string updateSql = isOther
                            ? @"UPDATE CIMS.PartStocks SET Quantity = Quantity - @Qty, BoxQuantity = BoxQuantity + @BoxOut, UpdatedDate = GETDATE() WHERE StockID = @StkId AND PartID = @PtId"
                            : mainCoil
                            ? @"UPDATE CIMS.Parts SET StockQuantity = ISNULL(StockQuantity, 0) - @Qty, CoilQuantity = CoilQuantity + @BoxOut WHERE PartID = @PtId"
                            : @"UPDATE CIMS.Parts
                                     SET StockQuantity = ISNULL(StockQuantity, 0) - @Qty
                                     WHERE PartID = @PtId";

                        using (SqlCommand cmdUpdate = new SqlCommand(updateSql, conn, trans))
                        {
                            cmdUpdate.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Qty", qty));
                            cmdUpdate.Parameters.Add("@PtId", SqlDbType.Int).Value = ptId;
                            if (isOther) cmdUpdate.Parameters.Add("@StkId", SqlDbType.Int).Value = stock.StkId;
                            if (isOther || mainCoil) cmdUpdate.Parameters.Add("@BoxOut", SqlDbType.Int).Value = boxOut;
                            cmdUpdate.ExecuteNonQuery();
                        }

                        // ✅ เพิ่มคอลัมน์ ReferenceNo
                        string insertLogSql = @"INSERT INTO CIMS.ScanTransactions (UserID, Quantity, TransactionType, TransactionDate, PartID, PartACode, ReferenceNo, StockID, BoxChange)
                                        VALUES (@UserId, @Qty, 'OUT', GETDATE(), @PtId, @PtACode, @RefNo, @StkId, @Box);
                                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
                        using (SqlCommand cmdLog = new SqlCommand(insertLogSql, conn, trans))
                        {
                            cmdLog.Parameters.Add("@Box", SqlDbType.Int).Value = isOther || mainCoil ? (object)boxOut : DBNull.Value;
                            cmdLog.Parameters.Add("@StkId", SqlDbType.Int).Value = (object)stock?.StkId ?? DBNull.Value;
                            cmdLog.Parameters.Add("@UserId", SqlDbType.NVarChar).Value = userId;
                            cmdLog.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Qty", qty));
                            cmdLog.Parameters.Add("@PtId", SqlDbType.Int).Value = ptId;
                            cmdLog.Parameters.Add("@PtACode", SqlDbType.NVarChar).Value =
                                string.IsNullOrWhiteSpace(partACode) ? DBNull.Value : (object)partACode.Trim();

                            string safeRefNo = string.IsNullOrWhiteSpace(refNo)
                                ? null
                                : (refNo.Length > RefNoMaxLength ? refNo.Substring(0, RefNoMaxLength) : refNo);
                            cmdLog.Parameters.Add("@RefNo", SqlDbType.VarChar, RefNoMaxLength).Value = (object)safeRefNo ?? DBNull.Value;

                            int txId = Convert.ToInt32(cmdLog.ExecuteScalar());
                            // 🧲 จ่ายออกทั้งลูก: Coil ลูกนี้ (อยู่ในคลังนี้) -> OUT / สแกนเศษไม่นับ Coil
                            if (stock != null && stock.CountCoil && !remainder) CoilOut(conn, trans, coil, stock.StkId, txId, userId);
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

        public decimal GetInventoryBalance(int ptId, StockModel stock = null)
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
                        return CIMS.Helpers.Qty.Read(res);
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
