using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using QRCoder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CIMS.Views
{
    // ▣ QR CODE (หน้าต่าง ADMIN ของ Inventory Registration - Level 1 เท่านั้น) ต่อคลัง:
    //   รูปแบบที่ 1 = ข้อความใต้ QR ใน PDF (บรรทัดละช่อง) / รูปแบบที่ 2 = สิ่งที่อยู่ใน QR คั่นด้วย | (หน้าแสกนอ่าน - ต้องมี PRODUCT CODE)
    //   แก้ได้หลายคลังแล้วกด SAVE ครั้งเดียว -> CIMS.ProductControlSettings.QrLabel / QrFields (Update_20261017b.sql)
    public partial class QrFormatPanel : UserControl
    {
        public class StockRow
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public bool? SysQr { get; set; }   // null = ALL STOCKS
            public ProductControlLayout Layout { get; set; }
            public string Original { get; set; }
            public string Key => string.Join(",", Layout.QrLabel) + "|" + string.Join(",", Layout.QrFields);
            public bool Changed => Key != Original;
            public Visibility ChangedVisibility => Changed ? Visibility.Visible : Visibility.Collapsed;
            public string Summary => $"QR {Layout.QrFields.Count} FIELD(S) · LABEL {Layout.QrLabel.Count} LINE(S)";
            public string SysQrText => SysQr == null ? "ALL STOCKS FILTER" : SysQr == true ? "SYSTEM QR ON" : "SYSTEM QR OFF";
            public Brush SysQrBrush => new SolidColorBrush(SysQr == null ? Color.FromRgb(0x8A, 0x84, 0x90) : SysQr == true ? Color.FromRgb(0x2E, 0x9E, 0x6B) : Color.FromRgb(0xBD, 0xB6, 0xC8));
        }

        // ค่าตัวอย่าง (ไม่ใช่ข้อมูลจริง) ไว้แสดงหน้าตาของ QR / ป้าย
        private static readonly Dictionary<string, string> Sample = new Dictionary<string, string>
        {
            ["SUPPLIER"] = "SUP-001", ["CUSTOMER"] = "CUSTOMER A", ["CATEGORY"] = "PACKING", ["PARTA"] = "A-100", ["PARTB"] = "B-200",
            ["MODEL"] = "MODEL-X", ["CODE"] = "I2AB-01002", ["PARTNO"] = "PN-7788", ["NAME"] = "ถุงพลาสติก 0.30 มิล x 11x15", ["PACKSIZE"] = "100",
            ["BIN"] = "A-01-02", ["MOTHER"] = "CWE0885B", ["COILNO"] = "CWE0885B-006"
        };

        private readonly UserSession _session;
        private readonly List<StockRow> _rows = new List<StockRow>();
        private StockRow _cur;
        private bool _building;

        public QrFormatPanel(UserSession session, List<StockModel> stocks)
        {
            InitializeComponent();
            _session = session;

            Dictionary<int, ProductControlLayout> map;
            try { map = new ProductControlLayoutService().GetAll(); }
            catch (Exception ex) { map = new Dictionary<int, ProductControlLayout>(); DialogHelper.ShowError("โหลดการตั้งค่า QR ไม่สำเร็จ\n" + ex.Message); }

            void Add(int id, string code, bool? sysQr)
            {
                var l = map.TryGetValue(id, out var x) ? x.Clone() : ProductControlColumns.Default(id);
                if (!l.QrFields.Contains("CODE")) l.QrFields.Insert(0, "CODE");
                var r = new StockRow { Id = id, Code = code, SysQr = sysQr, Layout = l };
                r.Original = r.Key;
                _rows.Add(r);
            }
            Add(0, "ALL STOCKS", null);
            foreach (var s in stocks) Add(s.StkId, s.Code, s.InSysQr || s.OutSysQr);
            lbStocks.ItemsSource = _rows;

            if (!DbSchema.HasProductControlSettings)
                txtStatus.Text = "⚠ Run Database/Update_20261016c.sql first - settings cannot be saved yet.";
            else if (!DbSchema.HasQrFields)
                txtStatus.Text = "⚠ Run Database/Update_20261017b.sql first - only PRODUCT CODE / CODE | NAME can be saved.";
            lbStocks.SelectedIndex = 0;
        }

        // ✨ เปิดแผง = รายชื่อคลังไหลเข้าทีละแถว
        private void Panel_Loaded(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(() => Motion.Stagger(lbStocks, 30)), System.Windows.Threading.DispatcherPriority.Loaded);

        private void Stocks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _cur = lbStocks.SelectedItem as StockRow;
            Build();
        }

        private void Build()
        {
            _building = true;
            wpLabelOrder.Children.Clear(); wpLabelAdd.Children.Clear(); wpQrOrder.Children.Clear(); wpQrAdd.Children.Clear();
            if (_cur == null) { _building = false; return; }
            BuildList(_cur.Layout.QrLabel, wpLabelOrder, wpLabelAdd, false);
            BuildList(_cur.Layout.QrFields, wpQrOrder, wpQrAdd, true);
            BuildPreview();
            _building = false;
        }

        // ลำดับ: ◀ ▶ เลื่อน / ✕ เอาออก (PRODUCT CODE ใน QR เอาออกไม่ได้) + ชิปเพิ่มช่องที่ยังไม่ได้เลือก
        private void BuildList(List<string> list, WrapPanel order, WrapPanel add, bool isQr)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                bool locked = isQr && list[i] == "CODE";
                var left = new Button { Content = "◀", Style = (Style)FindResource("SmallBtn"), IsEnabled = i > 0, ToolTip = "Move left" };
                var right = new Button { Content = "▶", Style = (Style)FindResource("SmallBtn"), IsEnabled = i < list.Count - 1, ToolTip = "Move right" };
                var del = new Button { Content = "✕", Style = (Style)FindResource("SmallBtn"), ToolTip = locked ? "PRODUCT CODE must stay in the QR" : "Remove", IsEnabled = !locked && list.Count > 1 };
                left.Click += (o, e) => Swap(list, idx, idx - 1);
                right.Click += (o, e) => Swap(list, idx, idx + 1);
                del.Click += (o, e) => { list.RemoveAt(idx); Changed(); };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(left);
                sp.Children.Add(new TextBlock { Text = $"{i + 1}. {ProductControlColumns.Label(list[i])}{(locked ? "  🔒" : "")}", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                sp.Children.Add(right);
                sp.Children.Add(del);
                var chip = new Border
                {
                    Background = new SolidColorBrush(isQr ? Color.FromRgb(0x4A, 0x14, 0x8C) : Color.FromRgb(0x67, 0x50, 0xA4)),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 3, 4, 3), Margin = new Thickness(0, 0, 8, 8), Child = sp
                };
                Motion.SetHoverLift(chip, true);
                order.Children.Add(chip);
            }

            foreach (var f in QrFormat.Fields)
            {
                if (list.Contains(f.Key)) continue;
                var chip = new Button { Content = "+ " + f.Label, Tag = f.Key, Style = (Style)FindResource("AddChip"), Margin = new Thickness(0, 0, 6, 6) };
                chip.Click += (o, e) => { list.Add((string)((Button)o).Tag); Changed(); };
                add.Children.Add(chip);
            }
            if (add.Children.Count == 0)
                add.Children.Add(new TextBlock { Text = "All fields are in.", Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x90)), FontWeight = FontWeights.Bold, FontSize = 11 });
        }

        // ตัวอย่าง: QR จริงจากค่าตัวอย่าง + ข้อความใต้ QR แบบเดียวกับ PDF
        private void BuildPreview()
        {
            var l = _cur.Layout;
            string raw = string.Join(QrFormat.Sep.ToString(), l.QrFields.Select(k => Sample.TryGetValue(k, out var v) ? v : ""));
            txtRaw.Text = raw;
            txtFields.Text = QrFormat.Sample(l.QrFields);
            try
            {
                using (var gen = new QRCodeGenerator())
                {
                    byte[] png = new PngByteQRCode(gen.CreateQrCode(raw, QRCodeGenerator.ECCLevel.M)).GetGraphic(8);
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(png))
                    {
                        bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit();
                    }
                    bmp.Freeze();
                    imgQr.Source = bmp;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"QR preview: {ex.Message}"); imgQr.Source = null; }

            spLabel.Children.Clear();
            bool first = true;
            foreach (var k in l.QrLabel)
            {
                spLabel.Children.Add(new TextBlock
                {
                    Text = Sample.TryGetValue(k, out var v) ? v : k,
                    FontSize = first ? 13 : 10.5, FontWeight = first ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = first ? Brushes.Black : new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
                });
                first = false;
            }
            txtSysQrNote.Text = _cur.SysQr == false
                ? $"{_cur.Code} does not take SYSTEM QR in Multi-Scanner yet (Store > stock settings). The PDF still uses this format."
                : "";
        }

        private void Swap(List<string> list, int a, int b)
        {
            if (a < 0 || b < 0 || a >= list.Count || b >= list.Count) return;
            (list[a], list[b]) = (list[b], list[a]);
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
            _cur.Layout.QrFields = QrFormat.DefaultFields.ToList();
            _cur.Layout.QrLabel = QrFormat.DefaultLabel.ToList();
            Changed();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var changed = _rows.Where(r => r.Changed).ToList();
            if (changed.Count == 0) return;
            // ฐานข้อมูลยังไม่รัน Update_20261017b.sql = เก็บได้แค่ PRODUCT CODE / PRODUCT CODE|PRODUCT NAME
            if (!DbSchema.HasQrFields)
            {
                var bad = changed.FirstOrDefault(r => !(r.Layout.QrFields.SequenceEqual(new[] { "CODE" }) || r.Layout.QrFields.SequenceEqual(QrFormat.DefaultFields)) || !r.Layout.QrLabel.SequenceEqual(QrFormat.DefaultLabel));
                if (bad != null) { DialogHelper.ShowWarning($"{bad.Code}: ฐานข้อมูลยังไม่รองรับรูปแบบนี้ กรุณารัน Database/Update_20261017b.sql ก่อน"); lbStocks.SelectedItem = bad; return; }
            }
            try
            {
                // คอลัมน์ / จัดกลุ่มของคลัง = ค่าล่าสุดในฐานข้อมูล (ไม่ทับค่าที่ตั้งในหน้า TABLE COLUMNS)
                var now = new ProductControlLayoutService().GetAll();
                var save = changed.Select(r =>
                {
                    var l = now.TryGetValue(r.Id, out var x) ? x.Clone() : ProductControlColumns.Default(r.Id);
                    l.QrFields = r.Layout.QrFields.ToList();
                    l.QrLabel = r.Layout.QrLabel.ToList();
                    return l;
                }).ToList();
                new ProductControlLayoutService().Save(save, _session?.UserId);
                LogService.WriteLog(_session?.UserId, "PRODUCT_CONTROL_QR",
                    string.Join(" | ", changed.Select(r => $"{r.Code}: QR {QrFormat.Sample(r.Layout.QrFields)} / LABEL {string.Join(", ", r.Layout.QrLabel.Select(ProductControlColumns.Label))}")),
                    string.Join(",", changed.Select(r => r.Code)));
                foreach (var r in changed) r.Original = r.Key;
                Changed();
                NotificationManager.Show("QR CODE", $"บันทึกรูปแบบ QR ของ {string.Join(", ", changed.Select(r => r.Code))} แล้ว", true);
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
        }

        public bool HasUnsaved => _rows.Any(r => r.Changed);
    }
}
