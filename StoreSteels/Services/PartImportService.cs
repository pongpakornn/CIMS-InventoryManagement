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
        // MAX / MIN / ยอด เป็นทศนิยมได้ (บันทึกตามการตั้งค่า DECIMAL QTY ของแต่ละคลัง) / จำนวนกล่องเป็นจำนวนเต็ม
        public decimal? Max { get; set; }
        public decimal? Min { get; set; }
        public int? StockBox { get; set; }
        public int? Coil { get; set; }   // QTY (COIL) - คลังที่นับ Coil
        public decimal? StockPcs { get; set; }
        public string Remark { get; set; }
        public string Image { get; set; }   // IMAGE = ชื่อไฟล์รูป (เก็บตามไฟล์ / ว่าง = ไม่มีรูป)

        // 🧲 แถว Coil: 1 แถว = 1 Coil (COIL NO + MOTHER COIL + น้ำหนัก = STOCK (UNIT) ของแถวนั้น)
        //    สินค้าเดียวกันหลาย Coil = ใส่ PRODUCT CODE ซ้ำได้หลายแถว (แถวแรก = ข้อมูลสินค้า / แถวถัดไป = เพิ่ม Coil)
        public string CoilNo { get; set; }
        public string MotherCoil { get; set; }
        public decimal CoilWeight { get; set; }
        public bool IsCoilExtra { get; set; }          // แถว Coil เพิ่มของสินค้าที่อยู่แถวก่อนหน้า (ไม่สร้างสินค้าใหม่)
        public PartImportRow CoilOwner { get; set; }   // แถวแรกของสินค้านั้น
        public int CoilStockId { get; set; }           // คลังที่นับ Coil ที่ Coil นี้เข้า
        public bool CoilStockIsMain { get; set; }
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
            ["IMAGE"] = new[] { "IMAGE", "IMAGEFILE", "IMAGEFILENAME", "PICTURE", "รูป", "รูปภาพ" },
            ["COILNO"] = new[] { "COILNO", "COILNUMBER", "เลขCOIL" },
            ["MOTHERCOIL"] = new[] { "MOTHERCOIL", "MOTHER", "COILMOTHER", "COILแม่" },
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
                // QTY (COIL) = จำนวน Coil ของคลัง KG ที่นับ Coil
                if (unit == "COIL" || unit == "COILS") return key == "QTY" ? "COIL" : key;
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
                        Image = Get(r, "IMAGE"),
                        CoilNo = Get(r, "COILNO") is string cn && cn.Length > 0 && cn != "-" ? cn.ToUpperInvariant() : null,
                        MotherCoil = Get(r, "MOTHERCOIL") is string mc && mc.Length > 0 && mc != "-" ? mc.ToUpperInvariant() : null,
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
                    // ทศนิยมได้ (คลังที่ไม่ได้เปิด DECIMAL QTY ปัดเป็นจำนวนเต็มตอนบันทึก)
                    decimal? Num(string key, string label)
                    {
                        string s = Get(r, key);
                        if (s.Length == 0 || item.Error != null) return null;
                        if (CIMS.Helpers.Qty.TryParse(s, out decimal v) && v >= 0) return CIMS.Helpers.Qty.Round(v, true);
                        item.Error = $"{label} ต้องเป็นตัวเลขไม่ติดลบ ({s})";
                        return null;
                    }
                    item.Max = Num("MAX", "MAX");
                    item.Min = Num("MIN", "MIN");
                    item.StockBox = Whole("STOCKBOX", "STOCK (BOX)");
                    item.Coil = Whole("COIL", "STOCK (COIL)");
                    item.StockPcs = Num("STOCKPCS", "STOCK (PCS)");
                    // QTY (KG.) / QTY (SHEET) ... = ยอดคงเหลือของคลัง (คอลัมน์เดียวกับ STOCK (PCS) ของคลังที่นับเป็นชิ้น)
                    decimal? qty = Num("QTY", "QTY");
                    if (item.Error == null && qty.HasValue)
                    {
                        if (item.StockPcs.HasValue && item.StockPcs.Value != qty.Value)
                            item.Error = $"STOCK (UNIT) ({qty}) ไม่ตรงกับ STOCK (PCS) ({item.StockPcs}) - ใส่อย่างใดอย่างหนึ่ง";
                        else item.StockPcs = qty;
                    }
                    // แถว Coil: น้ำหนักของ Coil นี้ = STOCK (UNIT) / STOCK (PCS) ของแถว / 1 แถว = 1 Coil
                    if (item.Error == null && item.CoilNo != null)
                    {
                        if (!item.StockPcs.HasValue || item.StockPcs.Value <= 0) item.Error = $"COIL NO {item.CoilNo}: ใส่น้ำหนักของ Coil นี้ที่ STOCK (UNIT)";
                        else if (item.Coil.HasValue && item.Coil.Value != 1) item.Error = $"COIL NO {item.CoilNo}: 1 แถว = 1 Coil (STOCK (COIL) ว่าง หรือ 1)";
                        else item.CoilWeight = item.StockPcs.Value;
                    }
                    else if (item.Error == null && item.MotherCoil != null) item.Error = "ใส่ MOTHER COIL แล้วต้องใส่ COIL NO ด้วย";
                    string rmk = Get(r, "REMARK");
                    item.Remark = rmk.Length == 0 ? null : rmk;
                    if (item.Error == null && item.Max.HasValue && item.Min.HasValue && item.Max > 0 && item.Min > item.Max)
                        item.Error = $"MIN {item.Min} มากกว่า MAX {item.Max}";

                    if (item.IsValid) ResolveStocks(item, stocks);
                    rows.Add(item);
                }
            }

            ValidateAgainstDb(rows, stocks);
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
        private void ValidateAgainstDb(List<PartImportRow> rows, List<StockModel> stocks)
        {
            var existing = new HashSet<string>();
            Dictionary<string, (int Stock, string StockCode, string Status, int Part)> coils;
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT PartCode, ISNULL(PartA, '') FROM CIMS.Parts", conn))
                using (var rdr = cmd.ExecuteReader())
                    while (rdr.Read()) existing.Add(Key(rdr.GetString(0), rdr.GetString(1)));
                coils = CoilImportService.LoadCoils(conn);
            }

            // สินค้าซ้ำในไฟล์: ทั้งสองแถวเป็นแถว Coil = เพิ่ม Coil ให้สินค้าแถวแรก / ไม่ใช่ = ซ้ำ (ข้าม)
            var first = new Dictionary<string, PartImportRow>();
            foreach (var r in rows.Where(x => x.IsValid))
            {
                string k = Key(r.Code, r.PartA);
                if (existing.Contains(k)) r.Error = "สินค้านี้ลงทะเบียนไว้แล้ว (PRODUCT CODE + PART A) - เพิ่ม / อัพเดทผ่าน IMPORT EXCEL หน้า Store (Max-Min)";
                else if (first.TryGetValue(k, out var owner))
                {
                    if (r.CoilNo != null && owner.CoilNo != null) { r.IsCoilExtra = true; r.CoilOwner = owner; }
                    else r.Error = "ซ้ำกับแถวก่อนหน้าในไฟล์ (PRODUCT CODE + PART A เดียวกัน) - หลาย Coil ให้ใส่ COIL NO ทุกแถว";
                }
                else first[k] = r;
            }

            // คลังที่ Coil เข้า = คลังแรกของสินค้า (แถวแรก) ที่เปิด STOCK (COIL) / Coil ซ้ำในไฟล์หรือมีในทะเบียนแล้ว = ข้าม
            var seenCoil = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows.Where(x => x.IsValid && x.CoilNo != null))
            {
                var owner = r.CoilOwner ?? r;
                var target = (owner.InMain ? stocks.Where(s => s.IsMain) : Enumerable.Empty<StockModel>())
                             .Concat(owner.OtherStockIds.Select(id => stocks.FirstOrDefault(s => s.StkId == id)))
                             .FirstOrDefault(s => s != null && s.CountCoil);
                if (!CIMS.Helpers.DbSchema.HasCoilRegister) r.Error = "ฐานข้อมูลยังไม่มีทะเบียน Coil (Update_20261008.sql)";
                else if (target == null) r.Error = $"COIL NO {r.CoilNo}: คลังของสินค้านี้ไม่ได้นับ Coil (เปิด STOCK (COIL) ที่ EDIT STOCK)";
                else if (seenCoil.TryGetValue(r.CoilNo, out int at)) r.Error = $"COIL NO {r.CoilNo} ซ้ำกับแถว {at}";
                else if (coils.TryGetValue(r.CoilNo, out var c) && c.Status == "IN") r.Error = $"COIL NO {r.CoilNo} อยู่ในคลัง {c.StockCode} แล้ว";
                else { seenCoil[r.CoilNo] = r.RowNumber; r.CoilStockId = target.StkId; r.CoilStockIsMain = target.IsMain; }
            }
            // แถวแรกของสินค้าไม่ผ่าน -> แถว Coil เพิ่มของสินค้านั้นก็ไม่นำเข้า
            foreach (var r in rows.Where(x => x.IsValid && x.IsCoilExtra && !x.CoilOwner.IsValid))
                r.Error = $"สินค้าแถว {r.CoilOwner.RowNumber} ไม่ผ่าน ({r.CoilOwner.Error})";
        }

        // ลงทะเบียนทุกแถวที่ผ่านการตรวจใน Transaction เดียว - พังแถวเดียวยกเลิกทั้งไฟล์
        public int ApplyImport(List<PartImportRow> rows, string userId = null)
        {
            // แถว Coil ของสินค้าเดียวกัน: ยอด KG ของสินค้า = น้ำหนักรวมทุก Coil ในไฟล์ / STOCK (COIL) = นับจากทะเบียน
            var coilRows = rows.Where(r => r.IsValid && r.CoilNo != null).GroupBy(r => r.CoilOwner ?? r).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var kv in coilRows) kv.Key.StockPcs = kv.Value.Sum(r => r.CoilWeight);
            var valid = rows.Where(r => r.IsValid && !r.IsCoilExtra).ToList();
            if (valid.Count == 0) return 0;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                // คลังไหนเปิด DECIMAL QTY (เก็บทศนิยม) / คลังอื่นปัดเป็นจำนวนเต็ม
                var decStock = new Dictionary<int, bool>();
                bool mainDec = false, mainCoil = false;
                var coilStock = new Dictionary<int, bool>();
                using (var cmd = new SqlCommand("SELECT StockID, IsMain, " + (CIMS.Helpers.DbSchema.HasAllowDecimal ? "AllowDecimal" : "CAST(0 AS BIT) AS AllowDecimal")
                                                + ", " + (CIMS.Helpers.DbSchema.HasCountCoil ? "CountCoil" : "CAST(0 AS BIT) AS CountCoil") + " FROM CIMS.Stocks", conn))
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read())
                    {
                        decStock[reader.GetInt32(0)] = reader.GetBoolean(2);
                        coilStock[reader.GetInt32(0)] = reader.GetBoolean(3);
                        if (reader.GetBoolean(1)) { mainDec = reader.GetBoolean(2); mainCoil = reader.GetBoolean(3); }
                    }
                decimal Rnd(decimal v, bool dec) => CIMS.Helpers.Qty.Round(v, dec);

                using (var trans = conn.BeginTransaction())
                {
                    foreach (var r in valid)
                    {
                        int ptId;
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO CIMS.Parts (PartCode, Description, PackSize, QRCode, MaxQuantity, MinQuantity, Category,
                                                  Bin, StockQuantity, IsActive, IsShowInMaster, ImageFileName, Supplier,
                                                  Customer, PartA, PartNumber, Model)
                            VALUES (@code, @name, @psz, @qr, @mmax, @mmin, @cat, @bin, @mqty, 1, @show, @img, @sup,
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
                            cmd.Parameters.AddWithValue("@img", string.IsNullOrWhiteSpace(r.Image) || r.Image.Trim() == "-" ? (object)DBNull.Value : r.Image.Trim());
                            // MAX / MIN / ยอด / REMARK ของคลังหลัก (เฉพาะเมื่อระบุคลังหลัก) - ยอด: STOCK (PCS) ก่อน ไม่มีใช้ BOX x PACKSIZE
                            bool inMain = r.InMain;
                            cmd.Parameters.AddWithValue("@inMain", inMain ? 1 : 0);
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@mmax", inMain ? Rnd(r.Max ?? 0, mainDec) : 0m));
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@mmin", inMain ? Rnd(r.Min ?? 0, mainDec) : 0m));
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@mqty", inMain ? Rnd(r.StockPcs ?? (r.StockBox.HasValue ? r.StockBox.Value * Math.Max(1, r.PackSize) : 0), mainDec) : 0m));
                            cmd.Parameters.AddWithValue("@rmk", (object)r.Remark ?? DBNull.Value);
                            ptId = (int)cmd.ExecuteScalar();
                        }
                        // คลังหลักที่นับ Coil: QTY (COIL) ตามไฟล์
                        if (r.InMain && mainCoil && r.Coil.HasValue)
                            using (var cmd = new SqlCommand("UPDATE CIMS.Parts SET CoilQuantity = @c WHERE PartID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@c", r.Coil.Value);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.ExecuteNonQuery();
                            }

                        foreach (int stkId in r.OtherStockIds)
                        {
                            // STOCK (PCS) + STOCK (BOX): ใส่ทั้งคู่ = ตามไฟล์ / ใส่ PCS อย่างเดียว = BOX คำนวณจาก PACKSIZE / ใส่ BOX อย่างเดียว = PCS = BOX x PACKSIZE
                            bool dec = decStock.TryGetValue(stkId, out bool dv) && dv;
                            // คลังที่นับ Coil: ช่องกล่อง = QTY (COIL) ตามไฟล์ ยอด KG ไม่คำนวณจากกล่อง (Trigger ข้ามคลัง Coil)
                            bool coil = coilStock.TryGetValue(stkId, out bool cv) && cv;
                            decimal pcs = Rnd(r.StockPcs ?? (!coil && r.StockBox.HasValue ? r.StockBox.Value * Math.Max(1, r.PackSize) : 0), dec);
                            int box = coil ? (r.Coil ?? 0) : (r.StockBox ?? 0);
                            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.PartStocks (StockID, PartID, Quantity, BoxQuantity, MaxQuantity, MinQuantity, Remark, IsShow)
                                                              VALUES (@s, @p, @qty, @box, @max, @min, @rmk, @show)", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@s", stkId);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.AddWithValue("@show", r.ShowInStock ? 1 : 0);
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@qty", pcs));
                                cmd.Parameters.AddWithValue("@box", box);
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@max", r.Max.HasValue ? Rnd(r.Max.Value, dec) : (decimal?)null));
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@min", r.Min.HasValue ? Rnd(r.Min.Value, dec) : (decimal?)null));
                                cmd.Parameters.AddWithValue("@rmk", (object)r.Remark ?? DBNull.Value);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        // 🧲 Coil ของสินค้านี้ (ทุกแถวที่ใส่ COIL NO) -> ทะเบียน Coil ของคลังที่นับ Coil + นับ STOCK (COIL) ใหม่
                        if (coilRows.TryGetValue(r, out var cl))
                            CoilImportService.RegisterCoils(conn, trans, cl.Select(c => (c.CoilNo, c.MotherCoil, c.CoilWeight)), ptId, r.CoilStockId, r.CoilStockIsMain, userId, true);
                    }
                    trans.Commit();
                }
            }
            return valid.Count;
        }
    }
}
