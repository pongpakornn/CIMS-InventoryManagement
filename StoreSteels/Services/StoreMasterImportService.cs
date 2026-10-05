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
    // 1 แถวของไฟล์ Import แบบอัพเดทข้อมูล (หน้า Store (Max-Min)) - null / ว่าง = ไม่เปลี่ยนค่านั้น
    public class StoreMasterRow
    {
        public int RowNumber { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Supplier { get; set; }
        public string Category { get; set; }
        public int? PackSize { get; set; }
        public string Bin { get; set; }
        public string QrCode { get; set; }
        public string Customer { get; set; }
        public string PartA { get; set; }
        public string PartNo { get; set; }
        public string Model { get; set; }
        public string Remark { get; set; }
        public decimal? Qty { get; set; }
        public int? Box { get; set; }
        public int? Max { get; set; }
        public int? Min { get; set; }
        public bool? ShowInMain { get; set; }
        public List<StockModel> Stocks { get; } = new List<StockModel>();   // คลังที่ระบุในไฟล์ (ว่าง = คลังของหน้านี้)
        public bool IsNew { get; set; }
        public int PtId { get; set; }
        public string Error { get; set; }
        public bool IsValid => string.IsNullOrEmpty(Error);
        public bool HasStockValues => Qty.HasValue || Box.HasValue || Max.HasValue || Min.HasValue || Remark != null;
    }

    public class StoreMasterResult
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int StockRows { get; set; }
    }

    // 📥 IMPORT EXCEL (หน้า Store (Max-Min)) แบบอัพเดทข้อมูลทั้งแถวตามหัวคอลัมน์ - หัวตารางเหมือน Template ของ Inventory Registration
    //   + QTY / STOCK (BOX) / MAX / MIN / REMARK ของคลัง - ยอดคงคลัง = ตัวเลขในไฟล์ (ตั้งค่าใหม่ ไม่ใช่บวกเพิ่ม)
    //   คลัง: STOCK / STOCK1-10 (รหัสคลัง) - ไม่ระบุ = คลังของหน้าที่กด Import
    //   รหัสสินค้าที่มีอยู่แล้ว = อัพเดท (ช่องว่างไม่เปลี่ยน) / ยังไม่มี = ลงทะเบียนใหม่ (ต้องมี PRODUCT NAME)
    public class StoreMasterImportService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        private static readonly Dictionary<string, string[]> Headers = new Dictionary<string, string[]>
        {
            ["CODE"] = new[] { "PRODUCTCODE", "PDCODE", "PARTCODE", "CODE", "PTCODE", "ITEMCODE", "รหัสสินค้า" },
            ["NAME"] = new[] { "PRODUCTNAME", "PARTNAME", "NAME", "DESCRIPTION", "PTDESC", "ชื่อสินค้า" },
            ["SUPPLIER"] = new[] { "SUPPLIER", "SUPPLIERCODE", "ผู้ขาย" },
            ["CATEGORY"] = new[] { "CATEGORY", "CAT", "ประเภท", "กลุ่ม" },
            ["PACKSIZE"] = new[] { "PACKSIZE", "PSZ", "PACK" },
            ["BIN"] = new[] { "BIN", "LOCATION", "LOC" },
            ["QRCODE"] = new[] { "QRCODE", "QR" },
            ["CUSTOMER"] = new[] { "CUSTOMER", "CUST", "CUSTOMERCODE", "ลูกค้า" },
            ["PARTA"] = new[] { "PARTA", "PARTACODE" },
            ["PARTNO"] = new[] { "PARTNO", "PARTNUMBER" },
            ["MODEL"] = new[] { "MODEL", "MODELCODE", "โมเดล" },
            ["SHOWHIDE"] = new[] { "SHOW/HIDE", "SHOWHIDE", "SHOW", "HIDE", "แสดง/ซ่อน" },
            ["REMARK"] = new[] { "REMARK", "REMARKS", "หมายเหตุ" },
            ["QTY"] = new[] { "QTY", "QUANTITY", "QTYKG", "QTYPCS", "QTYSHEET", "QTYBOX", "STOCKPCS", "STOCKQTY", "BALANCE", "จำนวน", "ยอด" },
            ["BOX"] = new[] { "STOCKBOX" },
            ["MAX"] = new[] { "MAX", "QTYMAX", "MAXKG", "MAXPCS", "MAXBOX", "MAXSHEET", "MAXUNIT" },
            ["MIN"] = new[] { "MIN", "QTYMIN", "MINKG", "MINPCS", "MINBOX", "MINSHEET", "MINUNIT" },
            ["STOCK"] = new[] { "STOCK", "STOCKCODE", "คลัง" },
            ["STOCK1"] = new[] { "STOCK1" }, ["STOCK2"] = new[] { "STOCK2" }, ["STOCK3"] = new[] { "STOCK3" }, ["STOCK4"] = new[] { "STOCK4" },
            ["STOCK5"] = new[] { "STOCK5" }, ["STOCK6"] = new[] { "STOCK6" }, ["STOCK7"] = new[] { "STOCK7" }, ["STOCK8"] = new[] { "STOCK8" },
            ["STOCK9"] = new[] { "STOCK9" }, ["STOCK10"] = new[] { "STOCK10" }
        };

        private static string Norm(string h) =>
            new string((h ?? "").Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '(' && c != ')' && c != '_' && c != '-').ToArray()).ToUpperInvariant();

        // ไฟล์แบบนี้ไหม: มีหัว PRODUCT CODE + PRODUCT NAME (ไฟล์รับเข้าแบบเดิมมีแค่ PD CODE + QTY)
        public static bool IsMasterFile(string path)
        {
            using (var wb = Open(path)) return FindHeader(wb.Worksheet(1), out _) > 0;
        }

        private static XLWorkbook Open(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("ไม่พบไฟล์ที่เลือก", path);
            var ms = new MemoryStream();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.CopyTo(ms);
            ms.Position = 0;
            return new XLWorkbook(ms);
        }

        private static int FindHeader(IXLWorksheet ws, out Dictionary<string, int> cols)
        {
            cols = new Dictionary<string, int>();
            foreach (var row in ws.RowsUsed().Take(10))
            {
                cols.Clear();
                foreach (var cell in row.CellsUsed())
                {
                    string h = Norm(cell.GetString());
                    foreach (var kv in Headers)
                        if (!cols.ContainsKey(kv.Key) && kv.Value.Contains(h)) { cols[kv.Key] = cell.Address.ColumnNumber; break; }
                }
                if (cols.ContainsKey("CODE") && cols.ContainsKey("NAME")) return row.RowNumber();
            }
            return 0;
        }

        private static bool TryDec(string s, out decimal v) =>
            decimal.TryParse((s ?? "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out v);

        public List<StoreMasterRow> Read(string path, StockModel pageStock, List<StockModel> stocks)
        {
            var rows = new List<StoreMasterRow>();
            using (var wb = Open(path))
            {
                var ws = wb.Worksheet(1);
                int header = FindHeader(ws, out var cols);
                if (header == 0) throw new InvalidDataException("ไม่พบหัวคอลัมน์ PRODUCT CODE / PRODUCT NAME ในไฟล์");

                string Get(int r, string k) => cols.TryGetValue(k, out int c) ? ws.Cell(r, c).GetString().Trim() : "";
                string Opt(int r, string k) { string v = Get(r, k); return v.Length == 0 ? null : v; }
                int last = ws.LastRowUsed()?.RowNumber() ?? header;

                for (int r = header + 1; r <= last; r++)
                {
                    var x = new StoreMasterRow
                    {
                        RowNumber = r, Code = Get(r, "CODE"), Name = Opt(r, "NAME"),
                        Supplier = Opt(r, "SUPPLIER"), Category = Opt(r, "CATEGORY"), Bin = Opt(r, "BIN"), QrCode = Opt(r, "QRCODE"),
                        Customer = Opt(r, "CUSTOMER"), PartA = Opt(r, "PARTA"), PartNo = Opt(r, "PARTNO"), Model = Opt(r, "MODEL"),
                        Remark = cols.ContainsKey("REMARK") ? Get(r, "REMARK") : null
                    };
                    // PRODUCT CODE นำเข้าตามไฟล์ (ไม่เอาช่องอื่นมาใส่แทน) - ไม่มีรหัสให้ใส่ "-" ในไฟล์
                    if (x.Code.Length == 0 && x.Name == null) continue;
                    if (x.Code.Length == 0) { x.Error = "ไม่มี PRODUCT CODE (ถ้าไม่มีรหัสให้ใส่ - )"; rows.Add(x); continue; }

                    void Num(string key, string label, Action<decimal> set, bool whole)
                    {
                        string s = Get(r, key);
                        if (s.Length == 0 || x.Error != null) return;
                        if (!TryDec(s, out decimal v) || v < 0 || (whole && v != Math.Floor(v))) x.Error = $"{label} ไม่ถูกต้อง ({s})";
                        else set(v);
                    }
                    Num("PACKSIZE", "PACKSIZE", v => x.PackSize = (int)v, true);
                    Num("QTY", "QTY", v => x.Qty = v, false);
                    Num("BOX", "STOCK (BOX)", v => x.Box = (int)v, true);
                    Num("MAX", "MAX", v => x.Max = (int)Math.Round(v, MidpointRounding.AwayFromZero), false);
                    Num("MIN", "MIN", v => x.Min = (int)Math.Round(v, MidpointRounding.AwayFromZero), false);
                    if (x.Error == null && x.Max.HasValue && x.Min.HasValue && x.Min > x.Max && x.Max > 0) x.Error = $"MIN {x.Min} มากกว่า MAX {x.Max}";

                    string sh = (Get(r, "SHOWHIDE") ?? "").ToUpperInvariant();
                    if (sh == "SHOW" || sh == "Y" || sh == "YES" || sh == "1" || sh == "แสดง") x.ShowInMain = true;
                    else if (sh == "HIDE" || sh == "N" || sh == "NO" || sh == "0" || sh == "ซ่อน") x.ShowInMain = false;
                    else if (sh.Length > 0 && x.Error == null) x.Error = $"SHOW/HIDE ไม่ถูกต้อง ({sh})";

                    // คลังที่ระบุ: STOCK1-10 + STOCK (คั่น , ;) - ไม่ระบุ = คลังของหน้านี้
                    var codes = Enumerable.Range(1, 10).Select(n => Get(r, "STOCK" + n))
                        .Concat(Get(r, "STOCK").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    foreach (var c in codes)
                    {
                        var s = stocks.FirstOrDefault(k => string.Equals(k.Code, c, StringComparison.OrdinalIgnoreCase));
                        if (s == null) { x.Error ??= $"ไม่พบคลัง \"{c}\" ในระบบ"; break; }
                        x.Stocks.Add(s);
                    }
                    if (x.Stocks.Count == 0 && pageStock != null) x.Stocks.Add(pageStock);
                    rows.Add(x);
                }
            }

            // รหัสที่มีอยู่แล้ว = อัพเดท / ยังไม่มี = เพิ่มใหม่ (ต้องมีชื่อ) / รหัสซ้ำในไฟล์ = ใช้แถวแรก
            // สินค้า 1 ตัว = PRODUCT CODE + PART A (PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน)
            //   ไฟล์ไม่ได้ใส่ PART A -> หาจาก PRODUCT CODE อย่างเดียว (ถ้าระบบมีรหัสนี้หลายตัว ต้องใส่ PART A)
            string K(string code, string partA) => (code ?? "").Trim().ToUpperInvariant() + "|" + (partA ?? "").Trim().ToUpperInvariant();
            var byKey = new Dictionary<string, int>();
            var byCode = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT PartID, LTRIM(RTRIM(PartCode)), ISNULL(PartA, '') FROM CIMS.Parts", conn))
            {
                conn.Open();
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read())
                    {
                        int id = rd.GetInt32(0); string code = rd.GetString(1);
                        byKey[K(code, rd.GetString(2))] = id;
                        if (!byCode.TryGetValue(code, out var l)) byCode[code] = l = new List<int>();
                        l.Add(id);
                    }
            }
            var seen = new HashSet<string>();
            foreach (var x in rows.Where(v => v.IsValid))
            {
                if (!seen.Add(K(x.Code, x.PartA))) { x.Error = "ซ้ำกับแถวก่อนหน้าในไฟล์ (PRODUCT CODE + PART A เดียวกัน)"; continue; }
                if (byKey.TryGetValue(K(x.Code, x.PartA), out int id)) x.PtId = id;
                else if (x.PartA == null && byCode.TryGetValue(x.Code, out var ids))
                {
                    if (ids.Count == 1) x.PtId = ids[0];
                    else x.Error = $"PRODUCT CODE {x.Code} มีในระบบ {ids.Count} รายการ กรุณาใส่ PART A ให้ระบุตัวสินค้า";
                }
                else if (x.Name == null) x.Error = "สินค้าใหม่ต้องมี PRODUCT NAME";
                else x.IsNew = true;
            }
            return rows;
        }

        // ทั้งไฟล์ใน Transaction เดียว - พังแถวเดียวยกเลิกทั้งหมด
        public StoreMasterResult Apply(List<StoreMasterRow> rows)
        {
            var res = new StoreMasterResult();
            var valid = rows.Where(r => r.IsValid).ToList();
            if (valid.Count == 0) return res;
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var x in valid)
                    {
                        bool inMain = x.Stocks.Any(s => s.IsMain);
                        if (x.IsNew)
                        {
                            using (var cmd = new SqlCommand(@"
                                INSERT INTO CIMS.Parts (PartCode, Description, PackSize, QRCode, MaxQuantity, MinQuantity, Category, Bin, StockQuantity, IsActive, IsShowInMaster,
                                                      ImageFileName, Supplier, Customer, PartA, PartNumber, Model)
                                VALUES (@code, @name, ISNULL(@psz, 0), ISNULL(@qr, @code), 0, 0, ISNULL(@cat, 'GENERAL'), ISNULL(@bin, 'N/A'), 0, 1, @show,
                                        NULL, @sup, @cust, @parta, @partno, @model);
                                SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tr))
                            {
                                AddPart(cmd, x);
                                // คลังหลักได้สินค้าเฉพาะเมื่อระบุรหัสคลังหลักในไฟล์ (SHOW/HIDE = แสดง/ซ่อน ในคลังที่ระบุ)
                                cmd.Parameters.AddWithValue("@show", inMain && (x.ShowInMain ?? true) ? 1 : 0);
                                x.PtId = (int)cmd.ExecuteScalar();
                            }
                            res.Added++;
                        }
                        else
                        {
                            using (var cmd = new SqlCommand(@"
                                UPDATE CIMS.Parts SET Description = ISNULL(@name, Description), PackSize = ISNULL(@psz, PackSize), QRCode = ISNULL(@qr, QRCode),
                                       Category = ISNULL(@cat, Category), Bin = ISNULL(@bin, Bin), Supplier = ISNULL(@sup, Supplier),
                                       Customer = ISNULL(@cust, Customer), PartA = ISNULL(@parta, PartA), PartNumber = ISNULL(@partno, PartNumber),
                                       Model = ISNULL(@model, Model), IsActive = 1,
                                       IsShowInMaster = CASE WHEN @inMain = 1 THEN ISNULL(@show, 1) ELSE IsShowInMaster END
                                WHERE PartID = @id", conn, tr))
                            {
                                AddPart(cmd, x);
                                cmd.Parameters.AddWithValue("@show", x.ShowInMain.HasValue ? (object)(x.ShowInMain.Value ? 1 : 0) : DBNull.Value);
                                cmd.Parameters.AddWithValue("@inMain", inMain ? 1 : 0);
                                cmd.Parameters.AddWithValue("@id", x.PtId);
                                cmd.ExecuteNonQuery();
                            }
                            res.Updated++;
                        }

                        // ยอด / MAX / MIN / REMARK ของทุกคลังที่ระบุ (ไม่มีแถวในคลังนั้น = เพิ่มเข้าคลัง)
                        foreach (var s in x.Stocks)
                        {
                            string sql = s.IsMain
                                ? @"UPDATE CIMS.Parts SET StockQuantity = ISNULL(@qty, StockQuantity), MaxQuantity = ISNULL(@max, MaxQuantity), MinQuantity = ISNULL(@min, MinQuantity),
                                           Remark = ISNULL(@rmk, Remark) WHERE PartID = @p"
                                : @"IF NOT EXISTS (SELECT 1 FROM CIMS.PartStocks WHERE StockID = @s AND PartID = @p)
                                        INSERT INTO CIMS.PartStocks (StockID, PartID, Quantity) VALUES (@s, @p, 0);
                                    UPDATE CIMS.PartStocks SET MaxQuantity = ISNULL(@max, MaxQuantity), MinQuantity = ISNULL(@min, MinQuantity),
                                           Remark = ISNULL(@rmk, Remark), IsShow = ISNULL(@show, IsShow), UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @p;
                                    -- ยอด: ใส่ QTY อย่างเดียว = กล่องคำนวณตาม Pack Size / ใส่ STOCK (BOX) อย่างเดียว = ชิ้นคำนวณให้ / ใส่ทั้งคู่ = ตามไฟล์
                                    IF @qty IS NOT NULL AND @box IS NOT NULL UPDATE CIMS.PartStocks SET Quantity = @qty, BoxQuantity = @box WHERE StockID = @s AND PartID = @p;
                                    ELSE IF @qty IS NOT NULL UPDATE CIMS.PartStocks SET Quantity = @qty WHERE StockID = @s AND PartID = @p;
                                    ELSE IF @box IS NOT NULL UPDATE CIMS.PartStocks SET BoxQuantity = @box WHERE StockID = @s AND PartID = @p;";
                            using (var cmd = new SqlCommand(sql, conn, tr))
                            {
                                cmd.Parameters.AddWithValue("@s", s.StkId);
                                cmd.Parameters.AddWithValue("@p", x.PtId);
                                // ยอดคงคลังเก็บเป็นจำนวนเต็มทุกคลัง
                                object qty = x.Qty.HasValue ? (object)(int)Math.Round(x.Qty.Value, MidpointRounding.AwayFromZero) : DBNull.Value;
                                cmd.Parameters.AddWithValue("@qty", qty);
                                cmd.Parameters.AddWithValue("@box", (object)x.Box ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@max", (object)x.Max ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@min", (object)x.Min ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@rmk", string.IsNullOrEmpty(x.Remark) ? DBNull.Value : (object)x.Remark);
                                cmd.Parameters.AddWithValue("@show", x.ShowInMain.HasValue ? (object)(x.ShowInMain.Value ? 1 : 0) : DBNull.Value);
                                cmd.ExecuteNonQuery();
                            }
                            res.StockRows++;
                        }
                    }
                    tr.Commit();
                }
            }
            return res;
        }

        private static void AddPart(SqlCommand cmd, StoreMasterRow x)
        {
            object V(string s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
            cmd.Parameters.AddWithValue("@code", x.Code);
            cmd.Parameters.AddWithValue("@name", V(x.Name));
            cmd.Parameters.AddWithValue("@psz", (object)x.PackSize ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@qr", V(x.QrCode));
            cmd.Parameters.AddWithValue("@cat", V(x.Category));
            cmd.Parameters.AddWithValue("@bin", V(x.Bin));
            cmd.Parameters.AddWithValue("@sup", V(x.Supplier));
            cmd.Parameters.AddWithValue("@cust", V(x.Customer));
            cmd.Parameters.AddWithValue("@parta", V(x.PartA));
            cmd.Parameters.AddWithValue("@partno", V(x.PartNo));
            cmd.Parameters.AddWithValue("@model", V(x.Model));
        }
    }
}
