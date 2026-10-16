using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CIMS.Views
{
    // ▦ TABLE COLUMNS (หน้าต่าง ADMIN ของ Inventory Registration - Level 1 เท่านั้น)
    //   ต่อคลัง (ALL STOCKS = ตอนช่องกรองเลือก ALL STOCKS): จัดกลุ่ม CATEGORY / CUSTOMER / SUPPLIER / NONE + คอลัมน์ที่แสดงตามลำดับที่เลือก
    //   แก้ได้หลายคลังแล้วกด SAVE ครั้งเดียว -> CIMS.ProductControlSettings
    public partial class TableLayoutPanel : UserControl
    {
        public class StockRow
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public ProductControlLayout Layout { get; set; }
            public string Original { get; set; }
            public string Key => Layout.GroupBy + "|" + string.Join(",", Layout.Columns);
            public bool Changed => Key != Original;
            public Visibility ChangedVisibility => Changed ? Visibility.Visible : Visibility.Collapsed;
            public string Summary => $"{Layout.GroupBy} · {Layout.Columns.Count} COLUMNS";
        }

        private readonly UserSession _session;
        private readonly List<StockRow> _rows = new List<StockRow>();
        private StockRow _cur;
        private bool _building;
        public bool Saved { get; private set; }

        public TableLayoutPanel(UserSession session, List<StockModel> stocks)
        {
            InitializeComponent();
            _session = session;

            Dictionary<int, ProductControlLayout> map;
            try { map = new ProductControlLayoutService().GetAll(); }
            catch (Exception ex) { map = new Dictionary<int, ProductControlLayout>(); DialogHelper.ShowError("โหลดการตั้งค่าตารางไม่สำเร็จ\n" + ex.Message); }

            void Add(int id, string code)
            {
                var l = map.TryGetValue(id, out var x) ? x.Clone() : ProductControlColumns.Default(id);
                var r = new StockRow { Id = id, Code = code, Layout = l };
                r.Original = r.Key;
                _rows.Add(r);
            }
            Add(0, "ALL STOCKS");
            foreach (var s in stocks) Add(s.StkId, s.Code);
            lbStocks.ItemsSource = _rows;

            if (!DbSchema.HasProductControlSettings)
                txtStatus.Text = "⚠ Run Database/Update_20261016c.sql first - settings cannot be saved yet.";
            lbStocks.SelectedIndex = 0;
        }

        private void Stocks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _cur = lbStocks.SelectedItem as StockRow;
            Build();
        }

        private void Build()
        {
            _building = true;
            wpGroup.Children.Clear(); wpColumns.Children.Clear();
            if (_cur == null) { _building = false; return; }
            var l = _cur.Layout;

            foreach (var g in ProductControlColumns.Groups)
            {
                var chip = new CheckBox { Content = g, Tag = g, IsChecked = l.GroupBy == g, Style = (Style)FindResource("StkChip") };
                chip.Click += (o, e) => { l.GroupBy = (string)((CheckBox)o).Tag; Changed(); };
                wpGroup.Children.Add(chip);
            }
            foreach (var c in ProductControlColumns.All)
            {
                int pos = l.Columns.IndexOf(c.Key);
                var chip = new CheckBox
                {
                    Content = pos >= 0 ? $"{pos + 1}. {c.Label}" : c.Label,
                    Tag = c.Key,
                    IsChecked = pos >= 0,
                    Style = (Style)FindResource("StkChip")
                };
                chip.Click += (o, e) =>
                {
                    string key = (string)((CheckBox)o).Tag;
                    if (((CheckBox)o).IsChecked == true) { if (!l.Columns.Contains(key)) l.Columns.Add(key); }   // ติ๊กทีหลัง = ต่อท้าย
                    else l.Columns.Remove(key);
                    Changed();
                };
                wpColumns.Children.Add(chip);
            }
            BuildOrder();
            _building = false;
        }

        // ลำดับคอลัมน์: ◀ ▶ เลื่อน / ✕ ซ่อน
        private void BuildOrder()
        {
            wpOrder.Children.Clear(); spPreviewHead.Children.Clear();
            var cols = _cur.Layout.Columns;
            for (int i = 0; i < cols.Count; i++)
            {
                int idx = i;
                var left = new Button { Content = "◀", Style = (Style)FindResource("SmallBtn"), IsEnabled = i > 0, ToolTip = "Move left" };
                var right = new Button { Content = "▶", Style = (Style)FindResource("SmallBtn"), IsEnabled = i < cols.Count - 1, ToolTip = "Move right" };
                var del = new Button { Content = "✕", Style = (Style)FindResource("SmallBtn"), ToolTip = "Hide this column" };
                left.Click += (o, e) => { Swap(idx, idx - 1); };
                right.Click += (o, e) => { Swap(idx, idx + 1); };
                del.Click += (o, e) => { cols.RemoveAt(idx); Changed(); };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(left);
                sp.Children.Add(new TextBlock { Text = $"{i + 1}. {ProductControlColumns.Label(cols[i])}", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                sp.Children.Add(right);
                sp.Children.Add(del);
                wpOrder.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x67, 0x50, 0xA4)), CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 8, 8), Child = sp });
            }
            if (cols.Count == 0)
                wpOrder.Children.Add(new TextBlock { Text = "No columns - tick at least 1 column.", Foreground = Brushes.IndianRed, FontWeight = FontWeights.Bold });

            // ตัวอย่างหัวตาราง
            void Head(string t, double w) => spPreviewHead.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x14, 0x8C)), Width = w, Padding = new Thickness(6, 8, 6, 8),
                BorderBrush = Brushes.White, BorderThickness = new Thickness(0, 0, 1, 0),
                Child = new TextBlock { Text = t, Foreground = Brushes.White, FontWeight = FontWeights.ExtraBold, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
            });
            Head("☐", 36);
            foreach (var k in cols) Head(ProductControlColumns.Label(k), k == "NAME" ? 130 : 92);
            Head("ACTION", 110);
            bool grouped = _cur.Layout.GroupBy != "NONE";
            bdPreviewGroup.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
            txtPreviewGroup.Text = grouped ? $"{_cur.Layout.GroupBy} : (group name)   •   N ITEMS" : "";
        }

        private void Swap(int a, int b)
        {
            var cols = _cur.Layout.Columns;
            if (a < 0 || b < 0 || a >= cols.Count || b >= cols.Count) return;
            (cols[a], cols[b]) = (cols[b], cols[a]);
            Changed();
        }

        private void Changed()
        {
            if (_building) return;
            Build();
            lbStocks.Items.Refresh();
            int n = _rows.Count(r => r.Changed);
            btnSave.IsEnabled = n > 0 && DbSchema.HasProductControlSettings;
            txtStatus.Text = n > 0 ? $"{n} stock(s) changed - press SAVE" : "";
        }

        private void Default_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            _cur.Layout.Columns = ProductControlColumns.DefaultOrder.ToList();
            _cur.Layout.GroupBy = "CATEGORY";
            Changed();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var changed = _rows.Where(r => r.Changed).ToList();
            if (changed.Count == 0) return;
            var empty = changed.FirstOrDefault(r => r.Layout.Columns.Count == 0);
            if (empty != null) { DialogHelper.ShowWarning($"{empty.Code}: กรุณาเลือกคอลัมน์อย่างน้อย 1 คอลัมน์"); lbStocks.SelectedItem = empty; return; }
            try
            {
                // QR ของแถวที่บันทึก = ค่าล่าสุดในฐานข้อมูล (ไม่ทับค่าที่ตั้งในหน้า QR CODE)
                var now = new ProductControlLayoutService().GetAll();
                var save = changed.Select(r =>
                {
                    var l = r.Layout.Clone();
                    l.QrContent = now.TryGetValue(r.Id, out var x) ? x.QrContent : l.QrContent;
                    return l;
                }).ToList();
                new ProductControlLayoutService().Save(save, _session?.UserId);
                LogService.WriteLog(_session?.UserId, "PRODUCT_CONTROL_LAYOUT",
                    string.Join(" | ", changed.Select(r => $"{r.Code}: {r.Original} -> {r.Key}")), string.Join(",", changed.Select(r => r.Code)));
                foreach (var r in changed) r.Original = r.Key;
                Saved = true;
                Changed();
                NotificationManager.Show("Inventory Registration", $"บันทึกตารางของ {string.Join(", ", changed.Select(r => r.Code))} แล้ว", true);
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
        }

        public bool HasUnsaved => _rows.Any(r => r.Changed);
    }
}
