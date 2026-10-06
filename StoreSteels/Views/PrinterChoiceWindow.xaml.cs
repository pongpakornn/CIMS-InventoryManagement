using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.Views
{
    // 🖨 เลือกเครื่องพิมพ์ก่อนพิมพ์ Pick List
    //   เครื่องฉลาก Brother (QL-800) -> พิมพ์ทีละดวง 62mm แบบเดิม
    //   เครื่องอื่น (เช่น Fuji ApeosPort VII C3373) -> A4 10 ใบต่อแผ่น (2 x 5) พร้อมเส้นประไว้ตัด
    //   จำเครื่องที่เลือกล่าสุดไว้ในเครื่องนี้ (UiPrefs)
    public partial class PrinterChoiceWindow : Window
    {
        public class PrinterItem
        {
            public PrintQueue Queue { get; set; }
            public string Name { get; set; }
            public bool IsLabel { get; set; }
            public bool IsDefault { get; set; }
            public string Kind => IsLabel ? "LABEL 62 MM" : "A4";
            public Brush KindBrush => IsLabel ? new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)) : new SolidColorBrush(Color.FromRgb(0x67, 0x50, 0xA4));
            public string Detail => IsLabel ? "Brother label printer • one label per card" : "A4 paper • several cards per page";
            public Visibility DefaultVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;
        }

        private readonly IList<PackingCardModel> _items;
        private readonly PackingCardPrintService _print = new PackingCardPrintService();
        private bool _ready;

        public PrintQueue SelectedQueue { get; private set; }
        public bool IsA4 { get; private set; }
        public int Cols => 2;
        // A4: 10 ใบ (2 x 5) / 12 ใบ (2 x 6) / 14 ใบ (2 x 7) ต่อแผ่น
        public int Rows => rb14.IsChecked == true ? 7 : rb12.IsChecked == true ? 6 : 5;

        private PrinterChoiceWindow(IList<PackingCardModel> items, List<PrinterItem> printers)
        {
            InitializeComponent();
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            _items = items;
            txtSub.Text = $"{items.Count:N0} card(s) selected • choose where to print";

            lbPrinters.ItemsSource = printers;
            string last = UiPrefs.Get("PickListPrinter");
            lbPrinters.SelectedItem = printers.FirstOrDefault(p => p.Name == last)
                                      ?? printers.FirstOrDefault(p => p.IsDefault)
                                      ?? printers.FirstOrDefault();
            int lastRows = UiPrefs.GetInt("PickListA4Rows", 5);
            (lastRows == 7 ? rb14 : lastRows == 6 ? rb12 : rb10).IsChecked = true;
            _ready = true;
            Refresh();
        }

        // คืน null = ยกเลิก / ไม่มีเครื่องพิมพ์
        public static PrinterChoiceWindow Choose(IList<PackingCardModel> items)
        {
            var printers = LoadPrinters();
            if (printers.Count == 0)
            {
                DialogHelper.ShowWarning("ไม่พบเครื่องพิมพ์ในเครื่องนี้\nกรุณาติดตั้งเครื่องพิมพ์ใน Windows ก่อน (Settings > Printers & scanners)");
                return null;
            }
            var w = new PrinterChoiceWindow(items, printers);
            return w.ShowDialog() == true ? w : null;
        }

        // เครื่องพิมพ์ที่ติดตั้งใน Windows (ในเครื่อง + เครื่องพิมพ์ Network ที่เชื่อมต่อไว้)
        private static List<PrinterItem> LoadPrinters()
        {
            var list = new List<PrinterItem>();
            try
            {
                var server = new LocalPrintServer();
                string def = null;
                try { def = LocalPrintServer.GetDefaultPrintQueue()?.FullName; } catch { }
                foreach (var q in server.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections }))
                {
                    string name = q.FullName;
                    list.Add(new PrinterItem { Queue = q, Name = name, IsLabel = PackingCardPrintService.IsLabelPrinter(name), IsDefault = name == def });
                }
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("อ่านรายชื่อเครื่องพิมพ์ไม่สำเร็จ\n" + ex.Message);
            }
            // เครื่องฉลาก / เครื่องที่ตั้งเป็นค่าเริ่มต้นขึ้นก่อน
            return list.OrderByDescending(p => p.IsLabel).ThenByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToList();
        }

        private PrinterItem Current => lbPrinters.SelectedItem as PrinterItem;

        private void Printers_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => Refresh();
        private void Layout_Checked(object sender, RoutedEventArgs e) => Refresh();

        // สรุป + ตัวอย่างหน้าแรก ตามเครื่องพิมพ์ / รูปแบบที่เลือก
        private void Refresh()
        {
            if (!_ready) return;
            var p = Current;
            btnPrint.IsEnabled = p != null;
            bool a4 = p != null && !p.IsLabel;
            pnlLayout.IsEnabled = a4;
            pnlLayout.Opacity = a4 ? 1 : 0.5;
            if (p == null) { txtSummary.Text = ""; previewHost.Child = null; return; }

            if (a4)
            {
                int pages = PackingCardPrintService.PageCount(_items.Count, Cols, Rows);
                txtSummary.Text = $"{_items.Count:N0} card(s)  →  {pages:N0} A4 page(s)  •  {Cols * Rows} per page";
                txtPreviewTitle.Text = pages > 1 ? $"PREVIEW • PAGE 1 / {pages}" : "PREVIEW • PAGE 1";
                previewHost.Child = _print.BuildA4Page(_items.Take(Cols * Rows).ToList(), Cols, Rows, 1, pages);
            }
            else
            {
                txtSummary.Text = $"{_items.Count:N0} label(s)  •  62 mm label, one per card";
                // ฉลาก QL-800 พิมพ์จากแม่แบบ Brother (PackingCard.lbx) แบบเดิม - ไม่วาดตัวอย่างเอง (จะไม่ตรงกับของจริง)
                txtPreviewTitle.Text = "LABEL PRINTER";
                var note = new System.Windows.Controls.StackPanel { Width = 420, Margin = new Thickness(40) };
                note.Children.Add(new System.Windows.Controls.TextBlock { Text = "🏷", FontSize = 54, HorizontalAlignment = HorizontalAlignment.Center });
                note.Children.Add(new System.Windows.Controls.TextBlock { Text = "62 MM LABEL • SAME AS BEFORE", FontSize = 18, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6) });
                note.Children.Add(new System.Windows.Controls.TextBlock { Text = "One label per card, printed with the existing Brother label template.", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
                previewHost.Child = note;
            }
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            var p = Current;
            if (p == null) return;
            SelectedQueue = p.Queue;
            IsA4 = !p.IsLabel;
            UiPrefs.Set("PickListPrinter", p.Name);
            UiPrefs.Set("PickListA4Rows", Rows);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
