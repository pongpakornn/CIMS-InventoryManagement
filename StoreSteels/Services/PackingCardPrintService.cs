using CIMS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
        // queue = เครื่องพิมพ์ที่เลือกในหน้าต่าง SELECT PRINTER / a4 = true -> พิมพ์หลายใบต่อแผ่น A4 (cols x rows)
        public List<PackingCardModel> PrintCards(IList<PackingCardModel> items, PrintQueue queue, bool a4, int cols, int rows, Action<int, int> onProgress = null)
        {
            var printed = new List<PackingCardModel>();
            if (items == null || items.Count == 0 || queue == null) return printed;

            var printDialog = new PrintDialog { PrintQueue = queue };
            try { printDialog.PrintTicket = queue.UserPrintTicket ?? queue.DefaultPrintTicket; } catch { }

            if (a4) return PrintA4(printDialog, items, cols, rows, onProgress);

            // เครื่องพิมพ์ฉลาก QL-800 (62mm) เหมือนเดิม
            return File.Exists(TemplatePath)
                ? PrintViaBpac(printDialog, items, onProgress)
                : PrintViaPrintVisual(printDialog, items, onProgress);
        }

        // เครื่องพิมพ์ฉลาก Brother (QL-800 ฯลฯ) = พิมพ์ทีละดวงแบบเดิม / เครื่องอื่น (เช่น Fuji ApeosPort) = A4
        public static bool IsLabelPrinter(string printerName)
        {
            string n = (printerName ?? "").ToUpperInvariant();
            return n.Contains("QL-") || n.Contains("BROTHER") || n.Contains("P-TOUCH") || n.Contains("PTOUCH");
        }

        public static int PageCount(int cards, int cols, int rows) => Math.Max(1, (int)Math.Ceiling(cards / (double)Math.Max(1, cols * rows)));

        // ================================================================================
        // A4: การ์ดแบบเดียวกับ Pick List เดิม (ขยายใหญ่) หลายใบต่อแผ่น + เส้นประไว้ตัด
        // ================================================================================
        private const double A4WidthMm = 210, A4HeightMm = 297;
        private const double A4MarginMm = 8;      // ขอบกระดาษ (เครื่องพิมพ์ส่วนใหญ่พิมพ์ชิดขอบได้ไม่เกิน ~4-5mm)
        private const double A4FooterMm = 6;      // บรรทัดท้ายหน้า (เลขหน้า)

        private List<PackingCardModel> PrintA4(PrintDialog printDialog, IList<PackingCardModel> items, int cols, int rows, Action<int, int> onProgress)
        {
            try
            {
                printDialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
                printDialog.PrintTicket.PageOrientation = PageOrientation.Portrait;
            }
            catch
            {
                // driver บางรุ่นไม่ให้ตั้งค่าจากโปรแกรม - ใช้ค่ากระดาษของเครื่อง (ปกติ A4 อยู่แล้ว)
            }

            var doc = BuildA4Document(items, cols, rows, (done) => onProgress?.Invoke(done, items.Count));
            printDialog.PrintDocument(doc.DocumentPaginator, $"Pick List A4 - {items.Count} cards");
            onProgress?.Invoke(items.Count, items.Count);
            return new List<PackingCardModel>(items);
        }

        public FixedDocument BuildA4Document(IList<PackingCardModel> items, int cols, int rows, Action<int> onBuilt = null)
        {
            double w = A4WidthMm * MmToPx, h = A4HeightMm * MmToPx;
            int perPage = cols * rows, pages = PageCount(items.Count, cols, rows);
            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(w, h);
            for (int p = 0; p < pages; p++)
            {
                var pageItems = new List<PackingCardModel>();
                for (int i = p * perPage; i < Math.Min(items.Count, (p + 1) * perPage); i++) pageItems.Add(items[i]);

                var page = new FixedPage { Width = w, Height = h, Background = Brushes.White };
                var content = BuildA4Page(pageItems, cols, rows, p + 1, pages);
                content.Measure(new Size(w, h));
                content.Arrange(new Rect(0, 0, w, h));
                page.Children.Add(content);
                var pc = new PageContent();
                ((System.Windows.Markup.IAddChild)pc).AddChild(page);
                doc.Pages.Add(pc);
                onBuilt?.Invoke(Math.Min(items.Count, (p + 1) * perPage));
            }
            return doc;
        }

        // 1 หน้า A4: ตาราง cols x rows ของการ์ด / ช่องที่ไม่มีการ์ดปล่อยว่าง / เลขหน้าด้านล่าง
        public FrameworkElement BuildA4Page(IList<PackingCardModel> pageItems, int cols, int rows, int pageNo, int pageCount)
        {
            double w = A4WidthMm * MmToPx, h = A4HeightMm * MmToPx, m = A4MarginMm * MmToPx;
            var root = new Grid { Width = w, Height = h, Background = Brushes.White };

            var cards = new UniformGrid { Columns = cols, Rows = rows, Margin = new Thickness(m, m, m, m + A4FooterMm * MmToPx) };

            for (int i = 0; i < cols * rows; i++)
            {
                // เส้นประรอบช่อง = แนวตัดการ์ด
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                    BorderThickness = new Thickness(0.6),
                    Padding = new Thickness(1.8 * MmToPx)
                };
                var dash = new System.Windows.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
                    StrokeThickness = 0.8,
                    StrokeDashArray = new DoubleCollection { 6, 4 }
                };
                var holder = new Grid();
                holder.Children.Add(dash);
                if (i < pageItems.Count) { cell.BorderThickness = new Thickness(0); cell.Child = BuildA4Card(pageItems[i], rows); }
                holder.Children.Add(cell);
                cards.Children.Add(holder);
            }
            root.Children.Add(cards);

            var footer = new TextBlock
            {
                Text = $"CIMS  •  PICK LIST  •  PAGE {pageNo} / {pageCount}",
                FontSize = 9, Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, m)
            };
            root.Children.Add(footer);
            return root;
        }

        // การ์ด A4 (10 / 12 / 14 ใบต่อแผ่น) = ข้อมูลชุดเดียวกับการ์ด Pick List เดิม (โลโก้บริษัท / QR / Bill-Group,
        // Work Order-LOT NO., Part Name, Quantity-TicketDate) จัดให้พอดีช่อง: QR อยู่ขวาข้างหัวการ์ด + แถว Bill-Group
        // แถวที่เหลือยาวเต็มการ์ด - ข้อมูลแสดงอย่างเดียว ไม่แก้ไขอะไร
        // ขนาดตามจำนวนใบต่อแผ่น (2 คอลัมน์ x rows แถว): 10 ใบ = 2x5 / 12 ใบ = 2x6 / 14 ใบ = 2x7
        private static (double S, double QrMm, double LogoMm) CardSizes(int rows) =>
            rows >= 7 ? (0.64, 13.5, 6.2) : rows == 6 ? (0.72, 16, 7.4) : (0.82, 19, 8.8);

        private static readonly Lazy<System.Windows.Media.Imaging.BitmapImage> Logo = new Lazy<System.Windows.Media.Imaging.BitmapImage>(() =>
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/CIMS;component/Assets/Images/CH_Logo.png");
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        });

        public FrameworkElement BuildA4Card(PackingCardModel item, int rows = 5)
        {
            var (s, qrMm, logoMm) = CardSizes(rows);
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3 * MmToPx * s, 2.2 * MmToPx * s, 3 * MmToPx * s, 2 * MmToPx * s)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 0 header
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 1 divider
            // 2 Bill-Group / 3 Work Order-LOT / 4 Part Name (สูงกว่า ให้ชื่อยาวขึ้น 2 บรรทัดได้ครบ) / 5 Quantity-TicketDate
            foreach (double weight in new[] { 1.0, 1.0, 1.7, 1.0 }) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(weight, GridUnitType.Star) });

            // หัวการ์ด: โลโก้บริษัท (รูป CH. RADIATORS CO.,LTD. + ชื่อไทย) อย่างเดียว
            var logo = new Image
            {
                Source = Logo.Value,
                Height = logoMm * MmToPx,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 3 * s, 0)
            };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            g.Children.Add(logo);
            // QR มุมขวาบน กินความสูงหัวการ์ด + แถว Bill-Group
            double qr = qrMm * MmToPx;
            var qrBox = new Border { Width = qr, Height = qr, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.8), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2 * MmToPx, 0, 0, 0) };
            qrBox.Child = new Image { Source = item.QrImage, Stretch = Stretch.Uniform, Margin = new Thickness(1.5) };
            RenderOptions.SetBitmapScalingMode(qrBox.Child, BitmapScalingMode.NearestNeighbor);   // QR คมชัด สแกนติด
            Grid.SetColumn(qrBox, 1);
            Grid.SetRowSpan(qrBox, 3);
            g.Children.Add(qrBox);

            var divider = new Border { Height = 1, Background = Brushes.Gray, Margin = new Thickness(0, 1.5 * MmToPx * s, 0, 0.5 * MmToPx * s) };
            Grid.SetRow(divider, 1);
            g.Children.Add(divider);

            // ปี ค.ศ. เสมอ ตรงกับพรีวิวการ์ดในหน้า Pick List (เครื่องที่ตั้งปฏิทินไทยจะได้ 2569 ถ้าใช้รูปแบบของเครื่อง)
            string date = item.TicketDate == DateTime.MinValue ? "" : item.TicketDate.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);
            AddA4Row(g, 2, 1, s, ("Bill", item.TicketNo), ("Group", item.GroupCode));
            AddA4Row(g, 3, 2, s, ("Work Order", item.WorkOrder), ("LOT NO.", item.LotNo));
            AddA4Row(g, 4, 2, s, ("Part Name", item.JobName));
            AddA4Row(g, 5, 2, s, ("Quantity", $"{item.Qty:0.##}"), ("TicketDate", date));

            card.Child = g;
            return card;
        }
        // แถวของการ์ด A4: ป้ายชื่อตัวหนา + ค่าบนเส้นใต้ (แบบเดียวกับการ์ดเดิม) - 1 หรือ 2 คู่ต่อแถว
        private static void AddA4Row(Grid g, int row, int colSpan, double s, params (string Label, string Value)[] fields)
        {
            var line = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0.6 * MmToPx * s) };
            for (int i = 0; i < fields.Length; i++)
            {
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var label = new TextBlock { Text = fields[i].Label, FontSize = 13.5 * s, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6 * s, 2 * s) };
                Grid.SetColumn(label, i * 2);
                bool wide = fields.Length == 1;
                var value = new TextBlock
                {
                    Text = fields[i].Value ?? "",
                    FontSize = 15 * s,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = (wide ? 2 : 1) * 15 * s * 1.6,   // Part Name ยาวได้ 2 บรรทัด / ค่าอื่น 1 บรรทัด
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Padding = new Thickness(0, 0, 0, 1.5 * s)
                };
                var under = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 0, i < fields.Length - 1 ? 8 * s : 0, 0),
                    Child = value
                };
                Grid.SetColumn(under, i * 2 + 1);
                line.Children.Add(label);
                line.Children.Add(under);
            }
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, colSpan);
            g.Children.Add(line);
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
