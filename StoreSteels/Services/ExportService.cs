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
        public string GenerateA4Pdf(List<ProductControlModel> items)
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
                                    string qrContent = $"{item.PartCode ?? "N/A"}|{item.PartName ?? "N/A"}";

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