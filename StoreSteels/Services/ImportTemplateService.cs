using ClosedXML.Excel;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CIMS.Services
{
    // 📄 ไฟล์ Template สำหรับปุ่ม Import (หัวคอลัมน์ตรงกับที่ตัวอ่านไฟล์รู้จัก - กรอกแล้ว Import กลับได้ทันที)
    //    ชีทแรก = ตารางกรอกข้อมูล (ตัวอ่านไฟล์อ่านชีทแรกเท่านั้น) / ชีทถัดไป = วิธีกรอก + รายการอ้างอิง
    public class ImportTemplateService
    {
        public static string ExportFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CIMS_Export");

        // เปิดไฟล์ที่สร้างเสร็จขึ้นมาเลย (Excel) - เปิดไม่ได้ (เช่นเครื่องไม่มี Excel) -> เปิดโฟลเดอร์แล้วเลือกไฟล์ไว้ให้
        public static void OpenFile(string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
            }
        }

        // 📁 ทุก Export / Template แยกโฟลเดอร์ตามหน้าระบบ: Desktop\CIMS_Export\<หน้า>\<ชื่อที่เลือก>_<วัน-เดือน-ปี-เวลา>.xlsx
        //    เช่น Desktop\CIMS_Export\Store Max-Min\STOCK-MAT_10-10-2026-11.11.00.xlsx
        public static class Systems
        {
            public const string Store = "Store Max-Min";
            public const string Product = "Product Control";
            public const string Scanner = "Multi-Scanner";
            public const string PickList = "Pick List";
            public const string MaxMinCalc = "Max-Min Calculator";
            public const string Forecast = "Forecast Order";
            public const string PR = "PR Management";
            public const string ActivityLog = "Activity Log";
            public const string Users = "User Management";
        }

        public static string FolderFor(string system)
        {
            string dir = Path.Combine(ExportFolder, SafeName(system));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string Stamp() => DateTime.Now.ToString("dd-MM-yyyy-HH.mm.ss", CultureInfo.InvariantCulture);

        // ชื่อไฟล์ใหม่ (สร้างโฟลเดอร์ให้เลย) - ext = ".xlsx" / ".pdf"
        public static string NewPath(string system, string name, string ext = ".xlsx")
        {
            string dir = FolderFor(system);
            string file = $"{SafeName(name)}_{Stamp()}";
            string path = Path.Combine(dir, file + ext);
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{file}_{i}{ext}");   // กดซ้ำในวินาทีเดียวกัน
            return path;
        }

        // ข้อความแจ้งผู้ใช้: Desktop\CIMS_Export\Store Max-Min\STOCK-MAT_10-10-2026-11.11.00.xlsx
        public static string ShortPath(string path) =>
            "Desktop\\CIMS_Export\\" + (path.StartsWith(ExportFolder, StringComparison.OrdinalIgnoreCase) ? path.Substring(ExportFolder.Length).TrimStart('\\') : Path.GetFileName(path));

        private static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = (s ?? "").Replace(c, '-');
            return s.Trim();
        }

        // หัวตารางแบบเดียวกับไฟล์ Template ของบริษัท (#002060 ตัวขาวหนา กึ่งกลาง)
        public static void Header(IXLWorksheet ws, string[] heads, double[] widths)
        {
            for (int i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
            var h = ws.Range(1, 1, 1, heads.Length);
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#002060");
            h.Style.Font.FontColor = XLColor.White;
            h.Style.Font.Bold = true;
            h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            h.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            h.Style.Alignment.WrapText = true;
            ws.Row(1).Height = 34.5;
            for (int i = 0; i < widths.Length && i < heads.Length; i++) ws.Column(i + 1).Width = widths[i];
            ws.SheetView.FreezeRows(1);
        }

        public static void Body(IXLWorksheet ws, int lastRow, int cols)
        {
            if (lastRow < 2) return;
            var r = ws.Range(2, 1, lastRow, cols);
            r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Rows(2, lastRow).Height = 20.25;
        }

        // ---------------------------------------------------------------- ไฟล์ EXPORT ทุกหน้าใช้รูปแบบเดียวกัน (หัวตาราง / เส้น / แถวกลุ่ม แบบเดียวกับ Export ของ Forecast)
        // แถวคั่นกลุ่ม (เหมือนแถบกลุ่มในตารางของโปรแกรม)
        public static void GroupRow(IXLWorksheet ws, int row, int cols, string text)
        {
            var g = ws.Range(row, 1, row, cols);
            g.Merge();
            g.FirstCell().Value = text;
            g.Style.Font.Bold = true;
            g.Style.Font.FontColor = XLColor.FromHtml("#002060");
            g.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
            g.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            g.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            g.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // ข้อความยาว (รายละเอียด / บาร์โค้ด / ชื่อสินค้า) ชิดซ้าย
        public static void LeftAlign(IXLWorksheet ws, int lastRow, params int[] cols)
        {
            if (lastRow < 2) return;
            foreach (int c in cols) ws.Range(2, c, lastRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        // ตัวเลขยอดตามการตั้งค่าคลัง (DECIMAL QTY = 1,234.50 / ไม่เปิด = 1,234)
        public static string QtyFormat(bool allowDecimal) => allowDecimal ? "#,##0.00#" : "#,##0";

        public static void Guide(XLWorkbook wb, string title, IEnumerable<(string Col, string Text)> lines)
        {
            var g = wb.AddWorksheet("HOW TO");
            g.Cell(1, 1).Value = title;
            g.Cell(1, 1).Style.Font.Bold = true; g.Cell(1, 1).Style.Font.FontSize = 14;
            g.Cell(3, 1).Value = "COLUMN"; g.Cell(3, 2).Value = "วิธีกรอก";
            var h = g.Range(3, 1, 3, 2);
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#002060"); h.Style.Font.FontColor = XLColor.White; h.Style.Font.Bold = true;
            int r = 4;
            foreach (var (col, text) in lines) { g.Cell(r, 1).Value = col; g.Cell(r, 2).Value = text; g.Cell(r, 1).Style.Font.Bold = true; r++; }
            g.Column(1).Width = 22; g.Column(2).Width = 110;
            g.Range(4, 2, r, 2).Style.Alignment.WrapText = true;
        }

        // ---------------------------------------------------------------- Inventory Registration (IMPORT EXCEL)
        public static readonly string[] PartHeads =
        {
            "NO", "PRODUCT CODE", "PRODUCT NAME", "SUPPLIER", "CUSTOMER", "CATEGORY", "PART A", "PART NO", "MODEL",
            "PACK SIZE", "BIN", "COIL NO", "MOTHER COIL", "QR CODE", "IMAGE", "STOCK1", "STOCK2", "STOCK3", "SHOW/HIDE", "MAX", "MIN",
            "STOCK (UNIT)", "STOCK (BOX)", "STOCK (COIL)", "STOCK (PCS)", "REMARK"
        };

        private static readonly Dictionary<string, double> PartWidths = new Dictionary<string, double>
        {
            ["NO"] = 6.4, ["PRODUCT CODE"] = 18, ["PRODUCT NAME"] = 34, ["SUPPLIER"] = 14, ["CUSTOMER"] = 14, ["CATEGORY"] = 14, ["PART A"] = 16,
            ["PART NO"] = 18, ["MODEL"] = 14, ["PACK SIZE"] = 10, ["BIN"] = 14, ["COIL NO"] = 16, ["MOTHER COIL"] = 16, ["QR CODE"] = 16,
            ["IMAGE"] = 28, ["STOCK1"] = 14, ["STOCK2"] = 14, ["STOCK3"] = 14, ["SHOW/HIDE"] = 11, ["REMARK"] = 24
        };

        // หัวคอลัมน์ของ Template ตามคลังที่เลือก: คอลัมน์ที่คลังนั้นแสดงใน Store (Max-Min) เรียงตามที่ตั้งไว้ (TABLE COLUMNS)
        //   + ข้อมูลที่ต้องใช้ลงทะเบียน (SUPPLIER / CATEGORY / PACK SIZE / BIN / QR CODE / STOCK1 / SHOW/HIDE)
        //   คลังที่นับ Coil มี COIL NO / MOTHER COIL ถัดจาก BIN / หัวยอดใช้หน่วยของคลัง เช่น STOCK (KG.) / MAX (COIL)
        private static bool SameHead(string a, string b) =>
            string.Equals(new string((a ?? "").Where(char.IsLetterOrDigit).ToArray()), new string((b ?? "").Where(char.IsLetterOrDigit).ToArray()), StringComparison.OrdinalIgnoreCase);

        public static string[] PartHeadsFor(StockModel s)
        {
            if (s == null) return PartHeads;
            var heads = new List<string> { "NO" };
            foreach (string key in s.ColumnOrderList())
            {
                switch (key)
                {
                    case "IMAGE": if (s.ColImage) heads.Add("IMAGE"); break;
                    case "CUSTOMER": if (s.ColCustomer) heads.Add("CUSTOMER"); break;
                    case "CODE": heads.Add("PRODUCT CODE"); break;
                    case "PARTA": if (s.ColPartA) heads.Add("PART A"); break;
                    case "PARTNO": if (s.ColPartNo) heads.Add("PART NO"); break;
                    case "MODEL": if (s.ColModel) heads.Add("MODEL"); break;
                    case "NAME": heads.Add("PRODUCT NAME"); break;
                    case "MAXMIN": if (s.UseMaxMin) { heads.Add(s.MaxHeader); heads.Add(s.MinHeader); } break;
                    case "QTY": if (s.ColQty) heads.Add(s.QtyHeader); break;
                    case "BOX": if (s.ColStockBox && !s.CountCoil) heads.Add("STOCK (BOX)"); break;
                    case "COIL": if (s.CountCoil) heads.Add("STOCK (COIL)"); break;
                    // คลังหน่วย PCS: ยอดคงคลังคือ STOCK (PCS.) อยู่แล้ว ไม่ใส่ STOCK (PCS) ซ้ำอีกคอลัมน์
                    case "PCS": if (s.ColStockPcs && !SameHead(s.QtyHeader, "STOCK (PCS)")) heads.Add("STOCK (PCS)"); break;
                    case "REMARK": if (s.ColRemark) heads.Add("REMARK"); break;
                }
            }
            foreach (var must in new[] { "PRODUCT CODE", "PRODUCT NAME" }) if (!heads.Contains(must)) heads.Insert(1, must);
            // ยอดคงคลังต้องมีเสมอ (คลังที่ซ่อนคอลัมน์ยอด) - คลัง Coil ใช้เป็นน้ำหนักของแต่ละ Coil
            if (!heads.Contains(s.QtyHeader)) heads.Add(s.QtyHeader);
            heads.AddRange(new[] { "SUPPLIER", "CATEGORY", "PACK SIZE", "BIN" });
            if (s.CountCoil) heads.AddRange(new[] { "COIL NO", "MOTHER COIL" });
            heads.AddRange(new[] { "QR CODE", "STOCK1", "SHOW/HIDE" });
            return heads.Distinct().ToArray();
        }

        // forStock = null -> Template ทุกคอลัมน์ (ทุกคลัง) / เลือกคลัง -> คอลัมน์ตามคลังนั้น + STOCK1 ใส่รหัสคลังให้แล้ว
        public void CreatePartTemplate(string path, List<StockModel> stocks, StockModel forStock = null)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("PRODUCTS");
                var heads = PartHeadsFor(forStock);
                Header(ws, heads, heads.Select(h => PartWidths.TryGetValue(h, out double w) ? w : 12).ToArray());
                int lastRow = forStock == null ? 31 : 101;   // ทุกคอลัมน์ 30 แถว / ตามคลัง 100 แถว
                Body(ws, lastRow, heads.Length);
                for (int r = 2; r <= lastRow; r++) ws.Cell(r, 1).Value = r - 1;
                int stockCol = Array.IndexOf(heads, "STOCK1") + 1;
                if (forStock != null && stockCol > 0) ws.Range(2, stockCol, lastRow, stockCol).Value = forStock.Code;
                foreach (var h in new[] { "PRODUCT CODE", "BIN", "COIL NO", "MOTHER COIL", "QR CODE", "PART A", "PART NO" })
                {
                    int c = Array.IndexOf(heads, h) + 1;
                    if (c > 0) ws.Range(2, c, lastRow, c).Style.NumberFormat.Format = "@";   // เลขรหัสขึ้นต้น 0 ไม่หาย
                }

                Guide(wb, forStock == null ? "CIMS - Inventory Registration (IMPORT EXCEL)" : $"CIMS - Inventory Registration (IMPORT EXCEL) -> {forStock.Code}  •  STOCK1 = {forStock.Code} ใส่ให้แล้ว", new[]
                {
                    ("PRODUCT CODE *", "รหัสสินค้า (บังคับ) - ไม่มีรหัสให้ใส่ -  /  สินค้า 1 รายการ = PRODUCT CODE + PART A (ซ้ำกับที่มีในระบบจะถูกข้าม)"),
                    ("PRODUCT NAME *", "ชื่อสินค้า (บังคับ)"),
                    ("SUPPLIER / CUSTOMER", "ผู้ขาย / ลูกค้า (ไม่บังคับ) - ใช้แสดงกลุ่มในคลังที่ตั้งจัดกลุ่มตาม SUPPLIER / CUSTOMER"),
                    ("CATEGORY", "กลุ่มสินค้า - ว่าง = GENERAL"),
                    ("PART A / PART NO / MODEL", "รหัสเพิ่มเติม (ไม่บังคับ) - ใช้ค้นหาและสแกนได้"),
                    ("PACK SIZE", "จำนวนต่อ 1 กล่อง / 1 ป้าย (ตัวเลข)"),
                    ("BIN / QR CODE", "ตำแหน่งเก็บ / รหัส QR ของสินค้า (ไม่บังคับ) - BIN ว่าง = N/A"),
                    ("COIL NO", "เลข Coil ตามป้าย (คลังที่นับ Coil เช่น STOCK-MAT / PANTA) - 1 แถว = 1 Coil / ห้ามซ้ำ / น้ำหนักของ Coil นี้ใส่ที่ STOCK (UNIT) ของแถว"),
                    ("สินค้า 1 ตัว หลาย Coil", "แถวแรกใส่ข้อมูลสินค้าครบ + COIL NO / แถวถัดไปใส่แค่ BIN / COIL NO / MOTHER COIL / น้ำหนัก (เว้น PRODUCT CODE ว่างได้ หรือใส่ PRODUCT CODE ซ้ำก็ได้) - แถวละ 1 Coil / STOCK (UNIT) ของสินค้า = น้ำหนักรวมของ Coil ในคลังนั้น / STOCK (COIL) = จำนวน Coil"),
                    ("น้ำหนัก Coil", "ช่องยอดคงคลัง เช่น STOCK (KG.) / STOCK (UNIT) ของแถว Coil = น้ำหนักของ Coil นั้น (ตัวเลขเท่านั้น ห้ามใส่คำว่า KG)"),
                    ("MOTHER COIL", "Coil แม่ที่ Coil นี้ตัดมา (ไม่บังคับ) - Coil ที่เป็น Coil แม่เองให้เว้นว่าง เช่น COIL NO = CWD1477A, MOTHER COIL = ว่าง"),
                    ("IMAGE", "ชื่อไฟล์รูปในโฟลเดอร์ 1. Image Stock เช่น STOCK-4C\\STOCK-4C-01.png (ไม่บังคับ / ว่าง = ไม่มีรูป หรือไม่เปลี่ยนรูปเดิม) - ใส่รูปทีละรายการได้ที่ปุ่มรูปในหน้า Inventory Registration"),
                    ("STOCK1 - STOCK3", "รหัสคลังที่สินค้าอยู่ ช่องละ 1 คลัง (ดูชีท STOCK CODES) - ไม่ใส่เลย = คลังหลัก"),
                    ("SHOW/HIDE", "1 = แสดง / 0 = ซ่อน ในตาราง Store (Max-Min) - ว่าง = แสดง"),
                    ("MAX / MIN", "MAX / MIN ของคลังที่ใส่ (ทศนิยมได้ถ้าคลังเปิด DECIMAL QTY) - หัวคอลัมน์ใส่หน่วยได้ เช่น MAX (KG.) / MIN (BOX)"),
                    ("STOCK (UNIT) / STOCK (BOX)", "ยอดคงเหลือตามหน่วยของคลัง (KG / PCS ...) / จำนวนกล่อง - ใส่อย่างใดอย่างหนึ่งหรือทั้งคู่ (ไม่ใส่ = 0) - หัวคอลัมน์ใส่หน่วยได้ เช่น STOCK (KG.)"),
                    ("STOCK (COIL)", "จำนวน Coil (จำนวนเต็ม) ของคลังที่นับ Coil เช่นคลัง KG - ยอด KG ใส่ที่ STOCK (UNIT) แยกกัน / คลังอื่นไม่ใช้ช่องนี้"),
                    ("STOCK (PCS)", "ยอดเป็นชิ้น (ตัวเลขเดียวกับ STOCK (UNIT) ของคลังที่นับเป็นชิ้น) - ใส่ช่องใดช่องหนึ่ง ถ้าใส่ทั้งคู่ต้องเท่ากัน"),
                    ("REMARK", "หมายเหตุ (ไม่บังคับ)"),
                    ("NO", "ลำดับ - ระบบไม่ได้ใช้ ลบหรือเว้นว่างได้"),
                    ("", "กรอกข้อมูลในชีท PRODUCTS (ชีทแรก) ได้ไม่จำกัดแถว แล้วกด IMPORT EXCEL ในหน้า Inventory Registration"),
                });

                // ตัวอย่างสินค้า 1 ตัว หลาย Coil (อยู่ในชีท HOW TO เท่านั้น - ไม่ถูก Import)
                var g = wb.Worksheet("HOW TO");
                int ex = (g.LastRowUsed()?.RowNumber() ?? 20) + 2;
                g.Cell(ex, 1).Value = "ตัวอย่าง: สินค้า 1 ตัว มี 3 Coil ในคลัง STOCK-MAT (กรอกในชีท PRODUCTS) - แถวที่ 2-3 เว้น PRODUCT CODE ได้ (หรือใส่ซ้ำก็ได้)";
                g.Cell(ex, 1).Style.Font.Bold = true;
                string[] exHeads = { "PRODUCT CODE", "PRODUCT NAME", "SUPPLIER", "BIN", "COIL NO", "MOTHER COIL", "STOCK1", "STOCK (UNIT)" };
                object[][] exRows =
                {
                    new object[] { "CC03-1600-0050", "SPHC-P(SPH270C) 1.6x1295xC", "PANTA", "CWD1477A", "CWD1477A", "", "STOCK-MAT", 3936 },
                    new object[] { "", "", "", "CWD1477B", "CWD1477B", "", "STOCK-MAT", 11470 },
                    new object[] { "", "", "", "CTF0256A", "CTF0256A", "", "STOCK-MAT", 10260 },
                };
                for (int i = 0; i < exHeads.Length; i++) g.Cell(ex + 1, i + 1).Value = exHeads[i];
                var eh = g.Range(ex + 1, 1, ex + 1, exHeads.Length);
                eh.Style.Fill.BackgroundColor = XLColor.FromHtml("#002060"); eh.Style.Font.FontColor = XLColor.White; eh.Style.Font.Bold = true;
                for (int rr = 0; rr < exRows.Length; rr++)
                    for (int cc = 0; cc < exRows[rr].Length; cc++) g.Cell(ex + 2 + rr, cc + 1).Value = XLCellValue.FromObject(exRows[rr][cc]);
                var eb = g.Range(ex + 1, 1, ex + 1 + exRows.Length, exHeads.Length);
                eb.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; eb.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                g.Cell(ex + 2 + exRows.Length, 1).Value = "ผล: สินค้า CC03-1600-0050 1 รายการ  •  STOCK (UNIT) = 25,666 KG  •  STOCK (COIL) = 3  •  กด PD CODE ในหน้า Store (Max-Min) เห็นทั้ง 3 Coil";

                var s = wb.AddWorksheet("STOCK CODES");
                Header(s, new[] { "STOCK CODE", "STOCK NAME", "UNIT", "MAIN" }, new double[] { 18, 32, 10, 8 });
                int row = 2;
                foreach (var st in stocks ?? new List<StockModel>())
                {
                    s.Cell(row, 1).Value = st.Code; s.Cell(row, 2).Value = st.Name; s.Cell(row, 3).Value = st.Unit; s.Cell(row, 4).Value = st.IsMain ? "Y" : "";
                    row++;
                }
                Body(s, row - 1, 4);
                wb.Worksheet(1).SetTabActive();
                wb.SaveAs(path);
            }
        }

        // ---------------------------------------------------------------- Forecast / Order / Delivery
        public static readonly string[] ForecastHeads = { "NO", "CUSTOMER", "PART NO", "PRODUCT NAME", "MONTH", "FORECAST", "ORDER", "DELIVERY", "WORK DAYS" };

        // rows = ข้อมูลเดือนนั้นที่มีอยู่แล้ว (แก้ตัวเลขแล้ว Import กลับได้เลย) / ไม่มี = แถวว่าง
        public void CreateForecastTemplate(string path, DateTime month, IList<ForecastOrderRow> rows)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("FORECAST ORDER");
                Header(ws, ForecastHeads, new double[] { 6.4, 16, 22, 36, 12, 14, 14, 14, 12 });
                string mon = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                int r = 2;
                foreach (var x in rows ?? new List<ForecastOrderRow>())
                {
                    ws.Cell(r, 1).Value = r - 1;
                    ws.Cell(r, 2).Value = x.Customer;
                    ws.Cell(r, 3).Value = x.PartNo;
                    ws.Cell(r, 4).Value = x.ProductName;
                    ws.Cell(r, 5).Value = mon;
                    ws.Cell(r, 6).Value = x.Forecast;
                    ws.Cell(r, 7).Value = x.Order;
                    ws.Cell(r, 8).Value = x.Delivery;
                    r++;
                }
                int last = Math.Max(r - 1, 31);   // แถวว่างพร้อมเส้นตารางให้กรอกต่อ (ไม่ใส่เลข NO ในแถวว่าง)
                Body(ws, last, ForecastHeads.Length);
                ws.Range(2, 5, last, 5).Style.NumberFormat.Format = "@";   // MONTH เป็นข้อความ 2026-10 (Excel ไม่แปลงเป็นวันที่)
                ws.Range(2, 6, last, 8).Style.NumberFormat.Format = "#,##0";

                Guide(wb, "CIMS - Import Forecast / Order / Delivery", new[]
                {
                    ("CUSTOMER *", "รหัสลูกค้า (บังคับ) - ต้องตรงกับ CUSTOMER ของสินค้าในระบบ ระบบจึงจับคู่ชื่อสินค้าได้"),
                    ("PART NO", "รหัสสินค้าของลูกค้า: PART A / PART NO / PRODUCT CODE อย่างใดอย่างหนึ่ง (ว่าง = ยอดรวมของลูกค้า)"),
                    ("PRODUCT NAME", "ไว้ดูเท่านั้น - ระบบไม่ได้อ่าน"),
                    ("MONTH", $"เดือนของยอด เช่น {mon} (หรือ 10/2026, Oct 2026) - ว่าง = เดือนปัจจุบัน"),
                    ("FORECAST / ORDER / DELIVERY", "ยอดพยากรณ์ / ยอดสั่ง / ยอดส่ง (ตัวเลขไม่ติดลบ) - ช่องว่าง = 0"),
                    ("WORK DAYS", "จำนวนวันทำงานของเดือน (ไม่บังคับ)"),
                    ("NO", "ลำดับ - ระบบไม่ได้ใช้"),
                    ("", "ลูกค้า + สินค้า + เดือนเดียวกับที่เคยนำเข้า จะถูกแทนที่ด้วยค่าใหม่ / นำเข้าแล้วระบบคำนวณ MAX / MIN ของคลังที่เปิด AUTO CALC ให้ทันที"),
                });
                wb.Worksheet(1).SetTabActive();
                wb.SaveAs(path);
            }
        }
    }
}
