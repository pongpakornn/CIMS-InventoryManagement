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

        public static string NewPath(string name) =>
            Path.Combine(ExportFolder, $"{name}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.xlsx");

        // หัวตารางแบบเดียวกับไฟล์ Template ของบริษัท (#002060 ตัวขาวหนา กึ่งกลาง)
        private static void Header(IXLWorksheet ws, string[] heads, double[] widths)
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

        private static void Body(IXLWorksheet ws, int lastRow, int cols)
        {
            if (lastRow < 2) return;
            var r = ws.Range(2, 1, lastRow, cols);
            r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            ws.Rows(2, lastRow).Height = 20.25;
        }

        private static void Guide(XLWorkbook wb, string title, IEnumerable<(string Col, string Text)> lines)
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
            "PACK SIZE", "BIN", "QR CODE", "STOCK1", "STOCK2", "STOCK3", "SHOW/HIDE", "MAX", "MIN", "QTY", "STOCK (BOX)", "REMARK"
        };

        public void CreatePartTemplate(string path, List<StockModel> stocks)
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("PRODUCTS");
                Header(ws, PartHeads, new double[] { 6.4, 18, 34, 14, 14, 14, 16, 18, 14, 10, 10, 16, 14, 14, 14, 11, 9, 9, 10, 11, 24 });
                Body(ws, 31, PartHeads.Length);   // 30 แถวว่างพร้อมเส้นตาราง
                for (int r = 2; r <= 31; r++) ws.Cell(r, 1).Value = r - 1;

                Guide(wb, "CIMS - Inventory Registration (IMPORT EXCEL)", new[]
                {
                    ("PRODUCT CODE *", "รหัสสินค้า (บังคับ) - ไม่มีรหัสให้ใส่ -  /  สินค้า 1 รายการ = PRODUCT CODE + PART A (ซ้ำกับที่มีในระบบจะถูกข้าม)"),
                    ("PRODUCT NAME *", "ชื่อสินค้า (บังคับ)"),
                    ("SUPPLIER / CUSTOMER", "ผู้ขาย / ลูกค้า (ไม่บังคับ) - ใช้แสดงกลุ่มในคลังที่ตั้งจัดกลุ่มตาม SUPPLIER / CUSTOMER"),
                    ("CATEGORY", "กลุ่มสินค้า - ว่าง = GENERAL"),
                    ("PART A / PART NO / MODEL", "รหัสเพิ่มเติม (ไม่บังคับ) - ใช้ค้นหาและสแกนได้"),
                    ("PACK SIZE", "จำนวนต่อ 1 กล่อง / 1 ป้าย (ตัวเลข)"),
                    ("BIN / QR CODE", "ตำแหน่งเก็บ / รหัส QR ของสินค้า (ไม่บังคับ) - BIN ว่าง = N/A"),
                    ("STOCK1 - STOCK3", "รหัสคลังที่สินค้าอยู่ ช่องละ 1 คลัง (ดูชีท STOCK CODES) - ไม่ใส่เลย = คลังหลัก"),
                    ("SHOW/HIDE", "1 = แสดง / 0 = ซ่อน ในตาราง Store (Max-Min) - ว่าง = แสดง"),
                    ("MAX / MIN", "MAX / MIN ของคลังที่ใส่ (จำนวนเต็ม) - หัวคอลัมน์ใส่หน่วยได้ เช่น MAX (KG.) / MIN (BOX)"),
                    ("QTY / STOCK (BOX)", "ยอดคงเหลือตามหน่วยของคลัง / จำนวนกล่อง - ใส่อย่างใดอย่างหนึ่งหรือทั้งคู่ (ไม่ใส่ = 0)"),
                    ("REMARK", "หมายเหตุ (ไม่บังคับ)"),
                    ("NO", "ลำดับ - ระบบไม่ได้ใช้ ลบหรือเว้นว่างได้"),
                    ("", "กรอกข้อมูลในชีท PRODUCTS (ชีทแรก) ได้ไม่จำกัดแถว แล้วกด IMPORT EXCEL ในหน้า Inventory Registration"),
                });

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
