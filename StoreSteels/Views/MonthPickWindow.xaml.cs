using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace CIMS.Views
{
    // 📤 หน้าต่างเลือกเดือน: ALL MONTHS หรือติ๊กเฉพาะเดือนที่ต้องการ (เช่น Export Forecast / Order / Delivery แยกชีทตามเดือน)
    public partial class MonthPickWindow : Window
    {
        public class Item
        {
            public DateTime Month { get; set; }
            public bool IsChecked { get; set; }
            public string Text => Month.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        }

        private readonly List<Item> _items;
        public List<DateTime> Chosen { get; private set; }

        private MonthPickWindow(string title, string subtitle, string okText, IEnumerable<DateTime> months, IEnumerable<DateTime> preselect)
        {
            InitializeComponent();
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            txtTitle.Text = title;
            if (!string.IsNullOrWhiteSpace(subtitle)) txtSub.Text = subtitle;
            if (!string.IsNullOrWhiteSpace(okText)) btnOk.Content = okText;
            var pre = new HashSet<DateTime>(preselect ?? Enumerable.Empty<DateTime>());
            _items = months.Distinct().OrderByDescending(m => m).Select(m => new Item { Month = m, IsChecked = pre.Contains(m) }).ToList();
            icMonths.ItemsSource = _items;
            Paint();
        }

        // คืนเดือนที่เลือก (เก่า -> ใหม่) หรือ null ถ้ากดยกเลิก
        public static List<DateTime> Choose(string title, string subtitle, string okText, IEnumerable<DateTime> months, IEnumerable<DateTime> preselect)
        {
            var w = new MonthPickWindow(title, subtitle, okText, months, preselect);
            return w.ShowDialog() == true ? w.Chosen : null;
        }

        private void Paint()
        {
            int n = _items.Count(i => i.IsChecked);
            chkAll.IsChecked = n == _items.Count && n > 0;
            btnOk.IsEnabled = n > 0;
            txtCount.Text = n == 0 ? "ยังไม่ได้เลือกเดือน" : $"เลือก {n:N0} เดือน  •  1 เดือน = 1 ชีท";
        }

        private void All_Click(object sender, RoutedEventArgs e)
        {
            bool on = chkAll.IsChecked == true;
            foreach (var i in _items) i.IsChecked = on;
            icMonths.ItemsSource = null; icMonths.ItemsSource = _items;
            Paint();
        }

        private void Month_Click(object sender, RoutedEventArgs e) => Paint();

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Chosen = _items.Where(i => i.IsChecked).Select(i => i.Month).OrderBy(m => m).ToList();
            DialogResult = Chosen.Count > 0;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
