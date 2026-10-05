using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CIMS.Services
{
    // 1 แถวที่อ่านได้จากไฟล์ Excel สำหรับลงทะเบียนสินค้าหน้า Inventory Registration
    public class PartImportRow
    {
        public int RowNumber { get; set; }
        public string Supplier { get; set; }
        public string Category { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public int PackSize { get; set; }
        public string Bin { get; set; }
        public string QrCode { get; set; }
        public string StockText { get; set; }            // รหัสคลังคั่นด้วย , (ว่าง = คลังหลัก)
        public List<string> StockCodes { get; } = new List<string>();   // STOCK1 - STOCK10 (1 ช่อง = 1 รหัสคลัง)
        public string ShowHide { get; set; }              // SHOW / HIDE (แสดงในตารางคลังหลัก) - ว่าง = ตามคลังที่ใส่
        public string Customer { get; set; }
        public string PartA { get; set; }
        public string PartNo { get; set; }
        public string Model { get; set; }

        public bool ShowInMain { get; set; } = true;
        public bool ShowInStock { get; set; } = true;     // SHOW/HIDE ในตารางของคลังอื่นที่ระบุ

        public bool InMain { get; set; }                  // ระบุคลังหลักไว้ (หรือไม่ระบุคลังเลย)

        // คอลัมน์เพิ่มเติม (ไม่บังคับ) - null = ไม่ได้กรอก
        public int? Max { get; set; }
        public int? Min { get; set; }
        public int? StockBox { get; set; }
        public int? StockPcs { get; set; }
        public string Remark { get; set; }
        public List<int> OtherStockIds { get; } = new List<int>();
        public string Error { get; set; }
        public bool IsValid => string.IsNullOrEmpty(Error);
    }

    // Import Excel ลงทะเบียนสินค้าใหม่ - แบบฟอร์มจริงยังรอสรุปกับผู้ใช้งาน จึงหาหัวคอลัมน์เองจาก 10 แถวแรก
    // (ไม่สนตัวพิมพ์เล็ก/ใหญ่ ช่องว่าง จุด วงเล็บ) บังคับแค่ PRODUCT CODE กับ PRODUCT NAME
    public class PartImportService
    {
        private readonly string _connectionString = GlobalConfig.ConnStr;

        private static readonly Dictionary<string, string[]> Headers = new Dictionary<string, string[]>
        {
            ["CODE"] = new[] { "PRODUCTCODE", "PDCODE", "PARTCODE", "CODE", "PTCODE", "ITEMCODE", "รหัสสินค้า" },
            ["NAME"] = new[] { "PRODUCTNAME", "PARTNAME", "NAME", "DESCRIPTION", "PTDESC", "ชื่อสินค้า" },
            ["SUPPLIER"] = new[] { "SUPPLIER", "SUPPLIERCODE", "ผู้ขาย" },
            ["CATEGORY"] = new[] { "CATEGORY", "CAT", "ประเภท", "กลุ่ม" },
            ["PACKSIZE"] = new[] { "PACKSIZE", "PSZ", "PACK", "PCS" },
            ["BIN"] = new[] { "BIN", "LOCATION", "LOC" },
            ["QRCODE"] = new[] { "QRCODE", "QR" },
            ["STOCK"] = new[] { "STOCK", "STOCKCODE", "คลัง" },
            ["CUSTOMER"] = new[] { "CUSTOMER", "CUST", "CUSTOMERCODE", "ลูกค้า" },
            ["PARTA"] = new[] { "PARTA", "PARTACODE" },
            ["PARTNO"] = new[] { "PARTNO", "PARTNUMBER" },
            ["MODEL"] = new[] { "MODEL", "MODELCODE", "รหัสโมเดล", "โมเดล" },
            ["SHOWHIDE"] = new[] { "SHOW/HIDE", "SHOWHIDE", "SHOW", "HIDE", "SHOW/HIDDEN", "แสดง/ซ่อน" },
            // คอลัมน์เพิ่มเติม (ไม่บังคับ) ของคลังที่ระบุ
            ["MAX"] = new[] { "MAX", "QTYMAX", "MAXBOX", "MAXPCS", "MAXKG", "MAXSHEET", "MAXUNIT" },
            ["MIN"] = new[] { "MIN", "QTYMIN", "MINBOX", "MINPCS", "MINKG", "MINSHEET", "MINUNIT" },
            ["STOCKBOX"] = new[] { "STOCKBOX" },
            ["STOCKPCS"] = new[] { "STOCKPCS" },
            // ยอดคงเหลือของคลังตามหน่วยของคลัง เช่น QTY (KG.) - หน่วยอื่นดู MatchUnitHeader
            ["QTY"] = new[] { "QTY", "QUANTITY", "BALANCE", "ONHAND", "STOCKQTY", "จำนวน", "ยอดคงเหลือ", "คงเหลือ" },
            ["REMARK"] = new[] { "REMARK", "REMARKS", "หมายเหตุ" },
            // Template: STOCK1 - STOCK10 ใส่รหัสคลัง (Stock Code ในหน้า Store (Max-Min)) ช่องละ 1 คลัง
            ["STOCK1"] = new[] { "STOCK1" }, ["STOCK2"] = new[] { "STOCK2" }, ["STOCK3"] = new[] { "STOCK3" }, ["STOCK4"] = new[] { "STOCK4" },
            ["STOCK5"] = new[] { "STOCK5" }, ["STOCK6"] = new[] { "STOCK6" }, ["STOCK7"] = new[] { "STOCK7" }, ["STOCK8"] = new[] { "STOCK8" },
            ["STOCK9"] = new[] { "STOCK9" }, ["STOCK10"] = new[] { "STOCK10" }
        };

        private static string Normalize(string h) =>
            new string((h ?? "").Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '(' && c != ')' && c != '_' && c != '-').ToArray()).ToUpperInvariant();

        // หน่วยที่ต่อท้ายหัวคอลัมน์ได้ เช่น QTY (KG.) / MAX (SHEET) / MIN (PCS) / STOCK (KG.) - ใช้หน่วยไหนก็ได้ตามคลัง
        private static readonly string[] Units =
            { "", "KG", "KGS", "PCS", "PC", "SHEET", "SHEETS", "SHT", "UNIT", "UNITS", "EA", "SET", "SETS", "ROLL", "ROLLS",
              "M", "MM", "CM", "METER", "L", "LITER", "LITRE", "TON", "G", "PACK", "PAC", "BAG", "ชิ้น", "แผ่น", "กก" };

        // หัวคอลัมน์ที่ไม่ตรงรายการด้านบน: <MAX|MIN|QTY|STOCK|BALANCE>(หน่วย) -> MAX / MIN / ยอดคงเหลือ
        //   STOCK (BOX) / QTY (BOX) = จำนวนกล่อง, STOCK (PCS) = ชิ้น (แบบ StorePC), หน่วยอื่น = ยอดคงเหลือ (QTY) ของคลัง
        //   MAX (DAY) / MIN (DAY) เป็นค่าของ Max-Min Calculator ไม่ใช่ของคลัง -> ไม่นับ
        private static string MatchUnitHeader(string h)
        {
            foreach (var (prefix, key) in new[] { ("MAX", "MAX"), ("MIN", "MIN"), ("QTY", "QTY"), ("STOCK", "QTY"), ("BALANCE", "QTY"), ("ONHAND", "QTY") })
            {
                if (!h.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string unit = h.Substring(prefix.Length);
                if (unit == "BOX" || unit == "BOXES" || unit == "กล่อง") return key == "QTY" ? "STOCKBOX" : key;
                if (Units.Contains(unit)) return key;
            }
            return null;
        }

        public List<PartImportRow> ReadExcel(string filePath, List<StockModel> stocks)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("ไม่พบไฟล์ที่เลือก", filePath);

            var rows = new List<PartImportRow>();
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var wb = new XLWorkbook(fs))
            {
                var ws = wb.Worksheet(1);
                var cols = new Dictionary<string, int>();
                int headerRow = 0;

                foreach (var row in ws.RowsUsed().Take(10))
                {
                    cols.Clear();
                    foreach (var cell in row.CellsUsed())
                    {
                        string h = Normalize(cell.GetString());
                        bool matched = false;
                        foreach (var kv in Headers)
                            if (!cols.ContainsKey(kv.Key) && kv.Value.Contains(h)) { cols[kv.Key] = cell.Address.ColumnNumber; matched = true; break; }
                        if (!matched)
                        {
                            string key = MatchUnitHeader(h);
                            if (key != null && !cols.ContainsKey(key)) cols[key] = cell.Address.ColumnNumber;
                        }
                    }
                    if (cols.ContainsKey("CODE") && cols.ContainsKey("NAME")) { headerRow = row.RowNumber(); break; }
                }

                if (headerRow == 0)
                    throw new InvalidDataException("ไม่พบหัวคอลัมน์ในไฟล์ Excel\nต้องมีอย่างน้อยคอลัมน์ \"PRODUCT CODE\" และ \"PRODUCT NAME\" (อยู่ใน 10 แถวแรกของชีทแรก)");

                string Get(int r, string key) => cols.TryGetValue(key, out int c) ? ws.Cell(r, c).GetString().Trim() : "";

                int lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
                for (int r = headerRow + 1; r <= lastRow; r++)
                {
                    var item = new PartImportRow
                    {
                        RowNumber = r,
                        Code = Get(r, "CODE"),
                        Name = Get(r, "NAME"),
                        Supplier = Get(r, "SUPPLIER"),
                        Category = Get(r, "CATEGORY"),
                        Bin = Get(r, "BIN"),
                        QrCode = Get(r, "QRCODE"),
                        StockText = Get(r, "STOCK"),
                        Customer = Get(r, "CUSTOMER"),
                        PartA = Get(r, "PARTA"),
                        PartNo = Get(r, "PARTNO"),
                        Model = Get(r, "MODEL"),
                        ShowHide = Get(r, "SHOWHIDE")
                    };
                    for (int n = 1; n <= 10; n++)
                    {
                        string sc = Get(r, "STOCK" + n);
                        if (sc.Length > 0) item.StockCodes.Add(sc);
                    }
                    string psz = Get(r, "PACKSIZE");
                    // PRODUCT CODE นำเข้าตามไฟล์ (ไม่เอาช่องอื่นมาใส่แทน) - ไม่มีรหัสให้ใส่ "-" ในไฟล์
                    if (string.IsNullOrEmpty(item.Code) && string.IsNullOrEmpty(item.Name)) continue;

                    if (string.IsNullOrEmpty(item.Code)) item.Error = "ไม่มี PRODUCT CODE (ถ้าไม่มีรหัสให้ใส่ - )";
                    else if (string.IsNullOrEmpty(item.Name)) item.Error = "ไม่มีชื่อสินค้า";
                    else if (!string.IsNullOrEmpty(psz))
                    {
                        if (decimal.TryParse(psz, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal p) && p >= 0)
                            item.PackSize = (int)Math.Round(p, MidpointRounding.AwayFromZero);
                        else item.Error = $"PACKSIZE ไม่ถูกต้อง ({psz})";
                    }

                    // คอลัมน์เพิ่มเติม (ไม่บังคับ): MAX / MIN / STOCK (BOX) / STOCK (PCS) / REMARK -> บันทึกตามที่พิมพ์ในไฟล์
                    int? Whole(string key, string label)
                    {
                        string s = Get(r, key);
                        if (s.Length == 0 || item.Error != null) return null;
                        if (decimal.TryParse(s.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) && v >= 0 && v == Math.Floor(v)) return (int)v;
                        item.Error = $"{label} ต้องเป็นจำนวนเต็มไม่ติดลบ ({s})";
                        return null;
                    }
                    item.Max = Whole("MAX", "MAX");
                    item.Min = Whole("MIN", "MIN");
                    item.StockBox = Whole("STOCKBOX", "STOCK (BOX)");
                    item.StockPcs = Whole("STOCKPCS", "STOCK (PCS)");
                    // QTY (KG.) / QTY (SHEET) ... = ยอดคงเหลือของคลัง (คอลัมน์เดียวกับ STOCK (PCS) ของคลังที่นับเป็นชิ้น)
                    int? qty = Whole("QTY", "QTY");
                    if (item.Error == null && qty.HasValue)
                    {
                        if (item.StockPcs.HasValue && item.StockPcs.Value != qty.Value)
                            item.Error = $"QTY ({qty}) ไม่ตรงกับ STOCK (PCS) ({item.StockPcs}) - ใส่อย่างใดอย่างหนึ่ง";
                        else item.StockPcs = qty;
                    }
                    string rmk = Get(r, "REMARK");
                    item.Remark = rmk.Length == 0 ? null : rmk;
                    if (item.Error == null && item.Max.HasValue && item.Min.HasValue && item.Max > 0 && item.Min > item.Max)
                        item.Error = $"MIN {item.Min} มากกว่า MAX {item.Max}";

                    if (item.IsValid) ResolveStocks(item, stocks);
                    rows.Add(item);
                }
            }

            ValidateAgainstDb(rows);
            return rows;
        }

        // คลังของสินค้า: STOCK1 - STOCK10 (ช่องละ 1 รหัสคลัง) และ/หรือ STOCK (คั่นด้วย ,)
        //   ไม่ใส่คลังเลย = คลังหลัก / ใส่รหัสคลังหลัก = อยู่ในคลังหลัก / คลังอื่น = เพิ่มเข้าคลังนั้น (ยอดเริ่ม 0)
        //   คลังหลักได้สินค้าเฉพาะเมื่อระบุรหัสคลังหลัก (หรือไม่ระบุคลังเลย) เท่านั้น
        //   SHOW / HIDE (1 / 0) = แสดง / ซ่อน ในตารางของคลังที่ระบุ (ว่าง = แสดง)
        private static void ResolveStocks(PartImportRow item, List<StockModel> stocks)
        {
            var codes = item.StockCodes
                .Concat((item.StockText ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            bool inMain = codes.Count == 0;
            foreach (string code in codes)
            {
                var s = stocks.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
                if (s == null) { item.Error = $"ไม่พบคลัง \"{code}\" ในระบบ"; return; }
                if (s.IsMain) inMain = true;
                else if (!item.OtherStockIds.Contains(s.StkId)) item.OtherStockIds.Add(s.StkId);
            }

            bool show = true;
            string sh = (item.ShowHide ?? "").Trim().ToUpperInvariant();
            if (sh.Length == 0 || sh == "SHOW" || sh == "Y" || sh == "YES" || sh == "1" || sh == "TRUE" || sh == "แสดง") show = true;
            else if (sh == "HIDE" || sh == "N" || sh == "NO" || sh == "0" || sh == "FALSE" || sh == "ซ่อน") show = false;
            else { item.Error = $"SHOW/HIDE ไม่ถูกต้อง ({item.ShowHide}) - ใส่ 1 (แสดง) หรือ 0 (ซ่อน)"; return; }

            item.InMain = inMain;
            item.ShowInMain = inMain && show;   // ไม่ได้ระบุคลังหลัก = ไม่อยู่ในคลังหลัก
            item.ShowInStock = show;
        }

        // สินค้า 1 ตัว = PRODUCT CODE + PART A (PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน เช่น STORE-PC)
        private static string Key(string code, string partA) => (code ?? "").Trim().ToUpperInvariant() + "|" + (partA ?? "").Trim().ToUpperInvariant();

        // ข้ามสินค้าที่มีในระบบแล้ว และแถวที่ซ้ำกันเองในไฟล์ (PRODUCT CODE + PART A เดียวกัน - เก็บแถวแรกไว้)
        private void ValidateAgainstDb(List<PartImportRow> rows)
        {
            var existing = new HashSet<string>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT PartCode, ISNULL(PartA, '') FROM CIMS.Parts", conn))
            {
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                    while (rdr.Read()) existing.Add(Key(rdr.GetString(0), rdr.GetString(1)));
            }

            var seen = new HashSet<string>();
            foreach (var r in rows.Where(x => x.IsValid))
            {
                string k = Key(r.Code, r.PartA);
                if (existing.Contains(k)) r.Error = "สินค้านี้ลงทะเบียนไว้แล้ว (PRODUCT CODE + PART A)";
                else if (!seen.Add(k)) r.Error = "ซ้ำกับแถวก่อนหน้าในไฟล์ (PRODUCT CODE + PART A เดียวกัน)";
            }
        }

        // ลงทะเบียนทุกแถวที่ผ่านการตรวจใน Transaction เดียว - พังแถวเดียวยกเลิกทั้งไฟล์
        public int ApplyImport(List<PartImportRow> rows)
        {
            var valid = rows.Where(r => r.IsValid).ToList();
            if (valid.Count == 0) return 0;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    foreach (var r in valid)
                    {
                        int ptId;
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO CIMS.Parts (PartCode, Description, PackSize, QRCode, MaxQuantity, MinQuantity, Category,
                                                  Bin, StockQuantity, IsActive, IsShowInMaster, ImageFileName, Supplier,
                                                  Customer, PartA, PartNumber, Model)
                            VALUES (@code, @name, @psz, @qr, @mmax, @mmin, @cat, @bin, @mqty, 1, @show, NULL, @sup,
                                    @cust, @parta, @partno, @model);
                            IF @inMain = 1 AND @rmk IS NOT NULL UPDATE CIMS.Parts SET Remark = @rmk WHERE PartID = SCOPE_IDENTITY();
                            SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@code", r.Code);
                            cmd.Parameters.AddWithValue("@name", r.Name);
                            cmd.Parameters.AddWithValue("@psz", r.PackSize);
                            cmd.Parameters.AddWithValue("@qr", r.QrCode ?? "");
                            // ค่าเริ่มต้นเดียวกับการลงทะเบียนทีละรายการ (InsertNewPartAsync)
                            cmd.Parameters.AddWithValue("@cat", string.IsNullOrWhiteSpace(r.Category) ? "GENERAL" : r.Category);
                            cmd.Parameters.AddWithValue("@bin", string.IsNullOrWhiteSpace(r.Bin) ? "N/A" : r.Bin);
                            cmd.Parameters.AddWithValue("@show", r.ShowInMain ? 1 : 0);
                            cmd.Parameters.AddWithValue("@sup", string.IsNullOrWhiteSpace(r.Supplier) ? (object)DBNull.Value : r.Supplier);
                            cmd.Parameters.AddWithValue("@cust", string.IsNullOrWhiteSpace(r.Customer) ? (object)DBNull.Value : r.Customer);
                            cmd.Parameters.AddWithValue("@parta", string.IsNullOrWhiteSpace(r.PartA) ? (object)DBNull.Value : r.PartA);
                            cmd.Parameters.AddWithValue("@partno", string.IsNullOrWhiteSpace(r.PartNo) ? (object)DBNull.Value : r.PartNo);
                            cmd.Parameters.AddWithValue("@model", string.IsNullOrWhiteSpace(r.Model) ? (object)DBNull.Value : r.Model);
                            // MAX / MIN / ยอด / REMARK ของคลังหลัก (เฉพาะเมื่อระบุคลังหลัก) - ยอด: STOCK (PCS) ก่อน ไม่มีใช้ BOX x PACKSIZE
                            bool inMain = r.InMain;
                            cmd.Parameters.AddWithValue("@inMain", inMain ? 1 : 0);
                            cmd.Parameters.AddWithValue("@mmax", inMain ? r.Max ?? 0 : 0);
                            cmd.Parameters.AddWithValue("@mmin", inMain ? r.Min ?? 0 : 0);
                            cmd.Parameters.AddWithValue("@mqty", inMain ? r.StockPcs ?? (r.StockBox.HasValue ? r.StockBox.Value * Math.Max(1, r.PackSize) : 0) : 0);
                            cmd.Parameters.AddWithValue("@rmk", (object)r.Remark ?? DBNull.Value);
                            ptId = (int)cmd.ExecuteScalar();
                        }

                        foreach (int stkId in r.OtherStockIds)
                        {
                            // STOCK (PCS) + STOCK (BOX): ใส่ทั้งคู่ = ตามไฟล์ / ใส่ PCS อย่างเดียว = BOX คำนวณจาก PACKSIZE / ใส่ BOX อย่างเดียว = PCS = BOX x PACKSIZE
                            int pcs = r.StockPcs ?? (r.StockBox.HasValue ? r.StockBox.Value * Math.Max(1, r.PackSize) : 0);
                            int box = r.StockBox ?? 0;
                            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.PartStocks (StockID, PartID, Quantity, BoxQuantity, MaxQuantity, MinQuantity, Remark, IsShow)
                                                              VALUES (@s, @p, @qty, @box, @max, @min, @rmk, @show)", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@s", stkId);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.AddWithValue("@show", r.ShowInStock ? 1 : 0);
                                cmd.Parameters.AddWithValue("@qty", pcs);
                                cmd.Parameters.AddWithValue("@box", box);
                                cmd.Parameters.AddWithValue("@max", (object)r.Max ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@min", (object)r.Min ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@rmk", (object)r.Remark ?? DBNull.Value);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                    trans.Commit();
                }
            }
            return valid.Count;
        }
    }
}
