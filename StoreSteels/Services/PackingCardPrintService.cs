using CIMS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CIMS.Services
{
    // พิมพ์ Packing Card สองทาง:
    //
    // 1) Brother b-PAC SDK (COM) - ทางที่ดีที่สุด ไม่มี popup เตือน "ม้วนฉลากไม่ตรงกับที่เลือกไว้ใน
    //    แอปพลิเคชัน" เพราะพิมพ์ผ่าน COM ของ Brother ตรงๆ ไม่ผ่าน Print Spooler แบบ GDI ของ .NET
    //    แต่ "บังคับ" ต้องมีไฟล์ label template (Assets/Labels/PackingCard.lbx) ที่สร้างด้วยโปรแกรม
    //    Brother P-touch Editor ไว้ก่อน (b-PAC ไม่รองรับการสร้าง/แก้ไข layout จากโค้ดเลยตามสเปกของ
    //    Brother เอง) - ดูขั้นตอนสร้างไฟล์นี้ในคอมเมนต์เหนือ PrintViaBpac() ด้านล่าง
    //
    // 2) WPF PrintDialog/PrintVisual (fallback) - ใช้เมื่อยังไม่มีไฟล์ template ข้อ 1 เพื่อให้ "พิมพ์ได้
    //    เหมือนเดิม" ก่อน ไม่ต้องรอสร้าง .lbx ก่อนถึงจะใช้งานได้ ข้อเสียคือไดรเวอร์ QL-800 อาจเด้ง popup
    //    เตือน "ม้วนฉลากหรือเทปภายในเครื่องไม่ตรงกับที่เลือกไว้ในแอปพลิเคชัน" ให้กด "ดำเนินการต่อ" เอง
    //    ทุกครั้งที่พิมพ์ (ไม่ block การพิมพ์ แค่ต้องกดยืนยันเพิ่ม) เพราะ .NET ส่งขนาดกระดาษ custom ผ่าน
    //    Print Spooler แบบ GDI ซึ่งไดรเวอร์ label ของ Brother ไม่รู้จักเป็น preset ที่ถูกต้อง
    //
    // สรุป: มีไฟล์ template แล้ว -> ใช้ b-PAC (ลื่นสุด) / ยังไม่มี -> fallback มาใช้ PrintVisual (พิมพ์ได้
    // ทันทีเหมือนก่อนหน้านี้ แค่ต้องกดยืนยัน popup ของไดรเวอร์เอง)
    public class PackingCardPrintService
    {
        private const double MmToPx = 96.0 / 25.4; // WPF ใช้หน่วย 1/96 นิ้ว
        private const double TwipsPerMm = 1440.0 / 25.4; // Document.Length ของ b-PAC ใช้หน่วย 1/1440 นิ้ว
        private const double RollWidthMm = 62;   // ความกว้างม้วนเทป (ค่าตายตัวของ QL-800)
        private const double LabelLengthMm = 40; // ความยาวป้ายต่อดวง - ตรงกับค่า "ความยาว" ที่ตั้งไว้ใน
                                                  // P-touch Editor ตอนออกแบบเทมเพลต (สื่อ 62mm x ยาว 40mm)

        // ขนาด Visual ที่จะวาดจริง (fallback path) = ขนาดหลังหมุนเป็นแนวนอนแล้ว (ยาว x กว้าง)
        private const double CardWidth = LabelLengthMm * MmToPx;
        private const double CardHeight = RollWidthMm * MmToPx;

        // ขอบกระดาษ - ตรงกับค่าที่ยืนยันแล้วว่าพิมพ์ได้จริงจากไดรเวอร์ QL-800 (ตั้งค่าเครื่องพิมพ์ > ตั้งค่าหน้า)
        private const double MarginLeftMm = 3;
        private const double MarginRightMm = 3;
        private const double MarginTopMm = 1.6;
        private const double MarginBottomMm = 1.5;

        private static readonly string TemplatePath =
            Path.Combine(AppContext.BaseDirectory, "Assets", "Labels", "PackingCard.lbx");

        // คืนรายการที่พิมพ์สำเร็จจริง (เรียงตามลำดับที่พิมพ์) - onProgress แจ้งความคืบหน้าจริงทีละใบ
        public List<PackingCardModel> PrintCards(IList<PackingCardModel> items, Action<int, int> onProgress = null)
        {
            var printed = new List<PackingCardModel>();
            if (items == null || items.Count == 0) return printed;

            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true) return printed;

            return File.Exists(TemplatePath)
                ? PrintViaBpac(printDialog, items, onProgress)
                : PrintViaPrintVisual(printDialog, items, onProgress);
        }

        // ================================================================================
        // ทางที่ 1: Brother b-PAC SDK
        //
        // === ขั้นตอนสร้างไฟล์ label template (ทำครั้งเดียวต่อเครื่อง เมื่อพร้อมเปลี่ยนมาใช้ทางนี้) ===
        // 1) เปิด Brother P-touch Editor > เลือกเครื่องพิมพ์ Brother QL-800 > ม้วนเทปต่อเนื่อง 62mm
        // 2) สร้าง Text object ตั้งชื่อให้ตรงเป๊ะ (คลิกขวา object > Properties > Name):
        //      TicketNo, GroupCode, WorkOrder, LotNo, JobName, Qty, TicketDate
        // 3) สร้าง Barcode object ชื่อ "QrCode" ประเภท Protocol = QR Code วางไว้มุมขวาบน
        // 4) ป้ายกำกับ/โลโก้อื่นๆ (หัวบริษัท, "Bill:", "Group:" ฯลฯ) พิมพ์เป็นข้อความนิ่งได้เลย ไม่ต้องตั้งชื่อ
        // 5) Save As เป็นไฟล์ที่ Assets/Labels/PackingCard.lbx (สร้างโฟลเดอร์ถ้ายังไม่มี - ตั้ง copy ไป
        //    output อัตโนมัติแล้วใน CIMS.csproj)
        // ================================================================================
        private List<PackingCardModel> PrintViaBpac(PrintDialog printDialog, IList<PackingCardModel> items, Action<int, int> onProgress)
        {
            var printed = new List<PackingCardModel>();
            string printerName = printDialog.PrintQueue?.Name;

            bpac.Document doc = new bpac.DocumentClass();
            try
            {
                if (!doc.Open(TemplatePath))
                {
                    throw new InvalidOperationException(
                        $"เปิด Packing Card template ไม่สำเร็จ (b-PAC ErrorCode={doc.ErrorCode}): {TemplatePath}");
                }

                if (!string.IsNullOrEmpty(printerName))
                {
                    // พารามิเตอร์ตัวที่ 2 คือ fitPage (ยืนยันจาก metadata ของ Interop.bpac.dll เอง - ไม่ใช่
                    // "IsDefault" ตามที่เข้าใจผิดตอนแรก) true = ให้ b-PAC ปรับ/พอดีกับสื่อที่ใส่อยู่ในเครื่อง
                    // จริงแทนที่จะเรียกร้องให้ตรงกับสื่อที่ตั้งไว้ในเทมเพลตเป๊ะๆ ซึ่งเป็นสาเหตุของ popup
                    // เตือน "ม้วนฉลากไม่ตรงกับที่เลือกไว้ในแอปพลิเคชัน" ตอนพิมพ์ผ่านโปรแกรม
                    doc.SetPrinter(printerName, true);
                }

                doc.Length = (int)Math.Round(LabelLengthMm * TwipsPerMm);

                int qrIndex = doc.GetBarcodeIndex("QrCode");

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    ApplyFields(doc, item, qrIndex);

                    doc.StartPrint("", bpac.PrintOptionConstants.bpoDefault);
                    doc.PrintOut(1, bpac.PrintOptionConstants.bpoDefault);
                    doc.EndPrint();

                    printed.Add(item);
                    onProgress?.Invoke(printed.Count, items.Count);
                }
            }
            finally
            {
                doc.Close();
                Marshal.ReleaseComObject(doc);
            }

            return printed;
        }

        private static void ApplyFields(bpac.Document doc, PackingCardModel item, int qrIndex)
        {
            SetText(doc, "TicketNo", item.TicketNo);
            SetText(doc, "GroupCode", item.GroupCode);
            SetText(doc, "WorkOrder", item.WorkOrder);
            SetText(doc, "LotNo", item.LotNo);
            SetText(doc, "JobName", item.JobName);
            SetText(doc, "Qty", item.Qty.ToString("0.##"));
            SetText(doc, "TicketDate", item.TicketDate == DateTime.MinValue ? "" : item.TicketDate.ToString("dd-MM-yyyy"));

            // ทั้ง SetBarcodeData(index, data) (วิธีทางการของ b-PAC สำหรับ barcode object) และ Object.Text
            // ตรงๆ ผ่าน SetText เคยลองแยกกันมาแล้วทั้งคู่ แต่สแกนจริงยังขึ้น "PLACEHOLDER" ค้าง (ค่า
            // design-time ที่ save ไว้ใน <pt:data> ของ obj "QrCode" ใน PackingCard.lbx) เลยยิงทั้งสองทาง
            // พร้อมกันไปเลยเผื่อเครื่อง/เวอร์ชัน b-PAC นี้ต้องการอีกทางใดทางหนึ่งเป็นพิเศษ (ไม่ error แม้
            // อีกทางจะไม่มีผลจริงกับ barcode object ก็ตาม)
            if (qrIndex >= 0)
            {
                doc.SetBarcodeData(qrIndex, item.QrText ?? "");
            }

            // ห่อ try/catch ไว้เฉยๆ เผื่อ .Text setter ไม่รองรับกับ object ประเภท barcode จริงๆ แล้ว COM
            // throw exception ออกมา - ไม่ให้พังการพิมพ์ทั้งชุดเพราะ fallback ตัวนี้ตัวเดียว
            try
            {
                SetText(doc, "QrCode", item.QrText);
            }
            catch
            {
                // ข้ามเงียบๆ - ยึดผลจาก SetBarcodeData ด้านบนเป็นหลัก
            }
        }

        // GetObject คืนค่า null ถ้าไม่เจอ object ชื่อนั้นในเทมเพลต (เช่น template ยังสร้างไม่ครบ) -
        // ข้ามเงียบๆ แทนที่จะพัง เพื่อให้ยังพิมพ์ field อื่นที่มีอยู่ได้ตามปกติ
        private static void SetText(bpac.Document doc, string objectName, string value)
        {
            var obj = doc.GetObject(objectName);
            if (obj != null) obj.Text = value ?? "";
        }

        // ================================================================================
        // ทางที่ 2: WPF PrintVisual (fallback ตอนยังไม่มีไฟล์ .lbx) - พิมพ์ได้ทันทีเหมือนเดิม
        // ================================================================================
        private List<PackingCardModel> PrintViaPrintVisual(PrintDialog printDialog, IList<PackingCardModel> items, Action<int, int> onProgress)
        {
            var printed = new List<PackingCardModel>();

            try
            {
                // PageMediaSize ต้องใส่เป็นขนาด "ฐาน" ของม้วน (กว้าง=62mm คงที่, ยาว=40mm) ไม่ใช่ขนาด
                // หลังหมุนแล้ว - แล้วให้ PageOrientation เป็นตัวหมุนแสดงผลเป็นแนวนอนแทน (ไดรเวอร์บางรุ่น
                // อาจยังเด้ง popup เตือนม้วนฉลากไม่ตรง ให้กด "ดำเนินการต่อ" เอง - ไม่ block การพิมพ์)
                printDialog.PrintTicket.PageMediaSize = new PageMediaSize(RollWidthMm * MmToPx, LabelLengthMm * MmToPx);
                printDialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
            }
            catch
            {
                // บาง driver ของเครื่องพิมพ์อาจไม่รองรับการกำหนดขนาดกระดาษเอง - ปล่อยให้ใช้ค่า default ของเครื่องแทน
            }

            for (int i = 0; i < items.Count; i++)
            {
                var visual = BuildCardVisual(items[i]);
                visual.Measure(new Size(CardWidth, CardHeight));
                visual.Arrange(new Rect(new Size(CardWidth, CardHeight)));

                printDialog.PrintVisual(visual, $"Packing Card - {items[i].TicketNo}");
                printed.Add(items[i]);

                onProgress?.Invoke(printed.Count, items.Count);
            }

            return printed;
        }

        // Layout ตามการ์ดตัวอย่าง: หัวการ์ด (โลโก้ + ชื่อบริษัท) + QR มุมขวาบน, แล้วตามด้วย
        // Bill/Group, Work Order/Lot No., Part Name เต็มแถว, Quantity/TicketDate
        public FrameworkElement BuildCardVisual(PackingCardModel item)
        {
            var border = new Border
            {
                Width = CardWidth,
                Height = CardHeight,
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(
                    MarginLeftMm * MmToPx, MarginTopMm * MmToPx,
                    MarginRightMm * MmToPx, MarginBottomMm * MmToPx)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // divider
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // bill/group
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // workorder/lot
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // part name
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // qty/date

            root.Children.Add(BuildHeader(item));
            Grid.SetRow(root.Children[0], 0);

            var divider = new Border { Height = 1, Background = Brushes.LightGray, Margin = new Thickness(0, 6, 0, 6) };
            Grid.SetRow(divider, 1);
            root.Children.Add(divider);

            var billGroup = BuildFieldRow("Bill", item.TicketNo, "Group", item.GroupCode);
            Grid.SetRow(billGroup, 2);
            root.Children.Add(billGroup);

            var workLot = BuildFieldRow("Work Order", item.WorkOrder, "LOT NO.", item.LotNo);
            Grid.SetRow(workLot, 3);
            root.Children.Add(workLot);

            var partNameRow = BuildSingleFieldRow("Part Name", item.JobName);
            Grid.SetRow(partNameRow, 4);
            root.Children.Add(partNameRow);

            var qtyDate = BuildFieldRow("Quantity", $"{item.Qty:0.##}", "TicketDate", item.TicketDate == DateTime.MinValue ? "" : item.TicketDate.ToString("dd-MM-yyyy"));
            Grid.SetRow(qtyDate, 5);
            root.Children.Add(qtyDate);

            border.Child = root;
            return border;
        }

        private static UIElement BuildHeader(PackingCardModel item)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // "CH" เป็นตัวอักษรตัวใหญ่หนาเฉยๆ (ไม่มีกล่องพื้นหลัง) ตามแบบการ์ดตัวอย่างจริง
            var companyPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var logoText = new TextBlock
            {
                Text = "CH",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Black,
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            companyPanel.Children.Add(logoText);

            var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            namePanel.Children.Add(new TextBlock { Text = "CH. RADIATORS CO.LTD.", FontWeight = FontWeights.Black, FontSize = 12, Foreground = Brushes.Black });
            namePanel.Children.Add(new TextBlock { Text = "บริษัท ซีเอชเรดิเอเตอร์ จำกัด", FontSize = 9, Foreground = Brushes.Black });
            companyPanel.Children.Add(namePanel);

            Grid.SetColumn(companyPanel, 0);
            grid.Children.Add(companyPanel);

            var qrBorder = new Border
            {
                Width = 42,
                Height = 42,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1)
            };
            qrBorder.Child = new Image { Source = item.QrImage, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
            Grid.SetColumn(qrBorder, 1);
            grid.Children.Add(qrBorder);

            return grid;
        }

        // แถวคู่ label/value สองชุดในแถวเดียวกัน (เช่น Bill | Group)
        private static UIElement BuildFieldRow(string label1, string value1, string label2, string value2)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var l1 = MakeLabel(label1);
            Grid.SetColumn(l1, 0);
            var v1 = MakeValue(value1, new Thickness(4, 0, 10, 0));
            Grid.SetColumn(v1, 1);
            var l2 = MakeLabel(label2);
            Grid.SetColumn(l2, 2);
            var v2 = MakeValue(value2, new Thickness(4, 0, 0, 0));
            Grid.SetColumn(v2, 3);

            grid.Children.Add(l1);
            grid.Children.Add(v1);
            grid.Children.Add(l2);
            grid.Children.Add(v2);
            return grid;
        }

        // แถว label/value เดี่ยว เต็มแถว (Part Name)
        private static UIElement BuildSingleFieldRow(string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var l = MakeLabel(label);
            Grid.SetColumn(l, 0);
            var v = MakeValue(value, new Thickness(4, 0, 0, 0));
            v.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(v, 1);

            grid.Children.Add(l);
            grid.Children.Add(v);
            return grid;
        }

        private static TextBlock MakeLabel(string text) => new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        private static TextBlock MakeValue(string text, Thickness margin) => new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Bottom
        };
    }
}
