using ClosedXML.Excel;
using System.IO;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using CIMS.Models;
using System;
using System.Collections.Generic;

namespace CIMS.Services
{
    public class ExportService
    {
        // QuestPDF (ตั้งแต่ 2023.4) บังคับต้อง declare license ก่อนสร้างเอกสารใดๆ ไม่งั้นจะโยน exception
        // ตอนรันจริงทุกครั้งที่กด Export - ใช้ static constructor เพื่อให้ตั้งค่าแค่ครั้งเดียวต่อการรันโปรแกรม
        // Community tier ใช้ได้ฟรีสำหรับองค์กรที่มีรายได้รวมต่อปีต่ำกว่า 1 ล้านดอลลาร์
        static ExportService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // คืน path ของไฟล์ PDF: Desktop\CIMS_Export\Product Control\QR_<วัน-เดือน-ปี-เวลา>.pdf
        // codeOnly: QR มีแค่ PRODUCT CODE (ตั้งต่อคลังที่ปุ่ม ADMIN หน้า Inventory Registration) / false = PRODUCT CODE|PRODUCT NAME (ค่าเดิม)
        public string GenerateA4Pdf(List<ProductControlModel> items, bool codeOnly = false)
        {
            string fullPath = ImportTemplateService.NewPath(ImportTemplateService.Systems.Product, "QR", ".pdf");

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            {
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1, Unit.Centimetre);
                        page.Content().Grid(grid =>
                        {
                            grid.Columns(4);
                            foreach (var item in items)
                            {
                                // ✅ .ShowEntire() บังคับให้ทั้งกลุ่ม (QR + รหัส + ชื่อ) เรนเดอร์เป็นก้อนเดียว
                                // ถ้าพื้นที่ที่เหลือในหน้าปัจจุบันไม่พอ จะข้ามทั้งกลุ่มไปหน้าถัดไปแทนที่จะตัดขาดครึ่ง
                                grid.Item().ShowEntire().AlignCenter().Padding(5).Column(col =>
                                {
                                    // QR นี้ไม่ได้บันทึกลง Database - เป็น QR เฉพาะสำหรับ "คืนเหล็กที่เหลือจากการผลิต"
                                    // เข้ารหัสแค่ Product Code | Product Name ตามที่ต้องการ
                                    string qrContent = codeOnly ? (item.PartCode ?? "N/A") : $"{item.PartCode ?? "N/A"}|{item.PartName ?? "N/A"}";

                                    // สร้าง QR Code (ใช้ ECCLevel.M เพื่อให้สแกนง่ายในขณะที่ข้อมูลค่อนข้างยาว)
                                    QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrContent, QRCodeGenerator.ECCLevel.M);
                                    PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);
                                    byte[] qrBytes = qrCode.GetGraphic(20);

                                    // 1. แสดงรูป QR Code (ขนาดเท่าเดิม จัดกึ่งกลาง)
                                    col.Item().Width(85).AlignCenter().Image(qrBytes);

                                    // 2. แสดง Product Code แล้วตามด้วย Product Name บรรทัดถัดไป จัดกึ่งกลางใต้ QR พอดี
                                    col.Item().Width(85).AlignCenter().PaddingTop(2).Text(item.PartCode).Bold().FontSize(9);
                                    col.Item().Width(85).AlignCenter().Text(item.PartName).FontSize(7).FontColor(Colors.Grey.Medium);

                                    col.Item().PaddingBottom(15);
                                });
                            }
                        });
                    });
                }).GeneratePdf(fullPath);
            }
            return fullPath;
        }


        #region === [ PR : EXPORT ตามคลัง / EXPORT PATH EXCEL ] ===

        // 📤 EXPORT (APPROVED LIST): Desktop\CIMS_Export\PR Management\<รหัสคลัง>\PR-yyyy-MM-dd.xlsx (วันเดียวกันซ้ำ = PR-yyyy-MM-dd (2).xlsx)
        public string ExportPRFile(IList<PRModel> items, string stockCode)
        {
            string dir = Path.Combine(ImportTemplateService.FolderFor(ImportTemplateService.Systems.PR), string.IsNullOrWhiteSpace(stockCode) ? "ALL STOCKS" : stockCode.Trim());
            Directory.CreateDirectory(dir);
            string name = "PR-" + DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, name + ".xlsx");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name} ({i}).xlsx");

            string[] heads = { "NO", "PR NO", "DATE", "APPROVED", "STOCK", "DEPARTMENT", "PRODUCT CODE", "PRODUCT NAME", "QTY", "UNIT", "REMARK", "TARGET", "REQUESTER" };
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("PR");
                ImportTemplateService.Header(ws, heads, new double[] { 6.4, 15, 12, 17, 13, 26, 18, 40, 9, 8, 26, 18, 18 });
                int r = 2;
                foreach (var x in items)
                {
                    ws.Cell(r, 1).Value = r - 1;
                    ws.Cell(r, 2).Value = x.PRNumber;
                    ws.Cell(r, 3).Value = x.PRDate; ws.Cell(r, 3).Style.DateFormat.Format = "dd/MM/yyyy";
                    ws.Cell(r, 4).Value = x.AppDateDisplay;
                    ws.Cell(r, 5).Value = x.StockCode ?? stockCode ?? "";
                    ws.Cell(r, 6).Value = x.Department ?? "";
                    ws.Cell(r, 7).Value = x.PartCode ?? "";
                    ws.Cell(r, 8).Value = x.PartName ?? "";
                    ws.Cell(r, 9).Value = x.QTY; ws.Cell(r, 9).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(r, 10).Value = x.Unit ?? "";
                    ws.Cell(r, 11).Value = x.Remark ?? "";
                    ws.Cell(r, 12).Value = x.TargetDept ?? "";
                    ws.Cell(r, 13).Value = x.Requester ?? "";
                    r++;
                }
                ImportTemplateService.Body(ws, r - 1, heads.Length);
                ImportTemplateService.LeftAlign(ws, r - 1, 8);
                ws.Column(1).Style.Font.Bold = true;
                ws.SheetView.FreezeRows(1);
                wb.SaveAs(path);
            }
            return path;
        }

        // 📥 EXPORT PATH EXCEL: ต่อข้อมูลลงไฟล์ Excel ของคลังตามช่องที่ตั้งไว้ (User Management -> SETUP EXCEL PATH)
        //   ช่องเซลล์เดียว (H) = เขียนทับทุกครั้ง เช่น DATE = C1 (วันนี้ dd/MM/yyyy) · DEPARTMENT = C2
        //   ช่องตาราง (T) = แถวละ 1 รายการ เริ่ม StartRow - มีข้อมูลอยู่แล้ว = ต่อจากแถวสุดท้ายที่มีข้อมูล (ดูทุกคอลัมน์ของตารางยกเว้น NO)
        //   ทุกช่องที่เขียน: ฟอนต์ Aptos · กึ่งกลาง · แถวตารางมีเส้น (เกินกรอบเดิม = ตีเส้นให้)
        //   คืนแถวแรก / แถวสุดท้ายที่เขียน
        public (int First, int Last) ExportPRToPath(IList<PRModel> items, PRExcelLayout layout)
        {
            string path = layout?.Path?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException($"ไม่พบไฟล์ Excel ที่ตั้งไว้:\n{path}");
            if (IsFileLocked(new FileInfo(path))) throw new IOException("ไฟล์ Excel ถูกเปิดค้างอยู่ กรุณาปิดไฟล์ก่อน Export");
            var heads = layout.Entries.Where(e => !e.IsTable && PRExcelLayout.ValidCell(e.Target)).ToList();
            var cols = layout.Entries.Where(e => e.IsTable && PRExcelLayout.ValidColumn(e.Target)).ToList();
            if (heads.Count == 0 && cols.Count == 0) throw new InvalidOperationException("ยังไม่ได้กำหนดช่องที่ลงข้อมูล (User Management -> SETUP EXCEL PATH)");

            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet(1);
                void Put(IXLCell c, object v)
                {
                    if (v is DateTime d) { c.Value = d; c.Style.DateFormat.Format = "dd/MM/yyyy"; }
                    else if (v is int i) c.Value = i;
                    else c.Value = Convert.ToString(v) ?? "";
                    c.Style.Font.FontName = "Aptos";
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    c.Style.Alignment.WrapText = true;
                }

                foreach (var h in heads) Put(ws.Cell(h.Target.ToUpperInvariant()), PRExcelFields.HeaderValue(h.Field, items));

                int first = 0, row = 0;
                if (cols.Count > 0)
                {
                    int start = Math.Max(1, layout.StartRow);
                    var colNos = cols.Select(c => XLHelper.GetColumnNumberFromLetter(c.Target.ToUpperInvariant())).ToList();
                    var dataCols = cols.Where(c => c.Field != "NO").Select(c => XLHelper.GetColumnNumberFromLetter(c.Target.ToUpperInvariant())).ToList();
                    if (dataCols.Count == 0) dataCols = colNos;
                    // แถวสุดท้ายที่มีข้อมูล ตั้งแต่ StartRow ลงไป
                    int last = start - 1;
                    int bottom = ws.RangeUsed()?.LastRow().RowNumber() ?? start;
                    for (int r = bottom; r >= start; r--)
                        if (dataCols.Any(c => !ws.Cell(r, c).IsEmpty())) { last = r; break; }

                    first = last + 1; row = first;
                    int minCol = colNos.Min(), maxCol = colNos.Max();
                    foreach (var x in items)
                    {
                        int no = row - start + 1;
                        foreach (var c in cols) Put(ws.Cell(row, XLHelper.GetColumnNumberFromLetter(c.Target.ToUpperInvariant())), PRExcelFields.Value(c.Field, x, no));
                        var rg = ws.Range(row, minCol, row, maxCol);
                        rg.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        rg.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                        if (ws.Row(row).Height < 21.75) ws.Row(row).Height = 21.75;
                        row++;
                    }
                }
                wb.Save();
                return (first, row - 1);
            }
        }
        #endregion

        #region === [ Export PR to Excel ] ===

        // ใน ExportService.cs
        public void ExportPRToExcel(IEnumerable<PRModel> dataList)
        {
            string targetPath = @"\\192.168.10.3\01_นารีพร 31-10-15 17.00\1.Pongpakorn_Non\5. RPA - Product ( All Part )\RPA - PR\RPA - PR Spare Part\PR StoreSP.xlsx";

            try
            {
                if (!File.Exists(targetPath))
                {
                    // ใช้ Exception เฉพาะทางเพื่อให้ catch แยกประเภทได้ชัดเจน
                    throw new FileNotFoundException($"ไม่พบไฟล์ที่เส้นทาง: {targetPath}");
                }

                if (IsFileLocked(new FileInfo(targetPath)))
                {
                    // ตัวนี้แหละครับที่จะเด้งไปที่ Catch
                    throw new Exception("ไฟล์ Excel ถูกเปิดค้างอยู่ กรุณาปิดไฟล์ก่อนทำการ Export");
                }

                using (var workbook = new XLWorkbook(targetPath))
                {
                    var worksheet = workbook.Worksheet(1);
                    var lastRowUsed = worksheet.LastRowUsed();
                    int currentRow = (lastRowUsed != null) ? lastRowUsed.RowNumber() + 1 : 2;

                    foreach (var item in dataList)
                    {
                        var dateCell = worksheet.Cell(currentRow, 1);
                        dateCell.Value = item.PRDate;
                        dateCell.Style.DateFormat.Format = "dd/MM/yyyy";

                        worksheet.Cell(currentRow, 2).Value = item.Department;
                        worksheet.Cell(currentRow, 3).Value = item.PartCode;
                        worksheet.Cell(currentRow, 4).Value = item.PartName;
                        worksheet.Cell(currentRow, 5).Value = item.QTY;
                        worksheet.Cell(currentRow, 6).Value = item.Remark;
                        worksheet.Cell(currentRow, 7).Value = "ชนนิกานต์";
                        worksheet.Cell(currentRow, 8).Value = "41304131-ปั๊ม 1@469214,403";           // <-- เพิ่มเติม : ถ้าเป็นค่า Fix นอกเหนือจากโปรแกรมสามารถเพิ่มเข้าไปที่ช่องนี้ได้เลยครับ

                        worksheet.Range(currentRow, 1, currentRow, 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        currentRow++;
                    }
                    workbook.Save();
                }
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        // Helper Function สำหรับเช็คไฟล์ Lock
        private bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException) { return true; }
            return false;
        }

        #endregion

    }
}
