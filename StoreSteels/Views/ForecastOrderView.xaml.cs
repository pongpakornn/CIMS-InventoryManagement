using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using LiveCharts;
using LiveCharts.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace CIMS.Views
{
    // 📊 Forecast / Order / Delivery (เมนูของตัวเอง)
    //   บน: กราฟแท่งทั้งปีของเดือนที่เลือก (เหมือน Dashboard) + หลอด TOP 5 สินค้าที่ลูกค้าเรียก Order มากที่สุด
    //   ล่าง: Import / ตารางตามลูกค้า (แสดง 50 แถวแรก เลื่อนลงโหลดเพิ่มทีละ 30) / Export ตามตารางไป Desktop\CIMS_Export
    //   สิทธิ์ ForecastOrder: VIEW = ดู + Export / ADD = Import
    public partial class ForecastOrderView : Page, INotifyPropertyChanged
    {
        private readonly UserSession _session;
        private readonly ForecastOrderService _service = new ForecastOrderService();
        private readonly ObservableCollection<ForecastOrderRow> _rows = new ObservableCollection<ForecastOrderRow>();
        private readonly ListCollectionView _view;
        private const int FirstPage = 50, MorePage = 30;
        private int _offset, _version;
        private bool _hasMore, _loading, _ready;
        private DispatcherTimer _searchTimer;

        public SeriesCollection BarSeries { get; private set; } = new SeriesCollection();
        public string[] ChartLabels { get; private set; } = new string[0];
        public Func<double, string> ValuesFormatter { get; } = v => v.ToString("N0");
        public double? AxisMax { get; private set; }

        public ForecastOrderView(UserSession session)
        {
            InitializeComponent();
            _session = session;
            DataContext = this;
            _view = new ListCollectionView(_rows);
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ForecastOrderRow.GroupKey)));
            dgRows.ItemsSource = _view;
            btnImport.Visibility = session?.CanImportForecastOrder == true ? Visibility.Visible : Visibility.Collapsed;
            btnTemplate.Visibility = btnImport.Visibility;   // Template ใช้คู่กับปุ่ม Import
            Loaded += async (s, e) => { if (!_ready) await LoadMonthsAsync(); };

            // ⚡ เรียลไทม์: เครื่องอื่น Import Forecast / Order / Delivery / วันทำงาน -> เดือน / กราฟ / ตารางอัพเดทเอง (คงเดือนที่เลือก + คำค้น)
            //    กำลังเลื่อนอ่านตารางอยู่ = รอจนกลับขึ้นบนสุด
            LiveRefresh.Attach(this, TimeSpan.FromSeconds(8),
                () => LiveRefresh.DbToken("SELECT MAX(ImportID), COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM CIMS.ForecastOrderImports",
                                          "SELECT COUNT(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM CIMS.CustomerWorkdays"),
                () => LoadMonthsAsync(SelectedMonth),
                () => !_ready || _loading || ScrollY(dgRows) > 0.5);
        }

        private static double ScrollY(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is ScrollViewer sv) return sv.VerticalOffset;
                double v = ScrollY(c);
                if (v >= 0) return v;
            }
            return -1;
        }

        private DateTime? SelectedMonth => (cbMonth.SelectedItem as ComboBoxItem)?.Tag as DateTime?;

        private async Task LoadMonthsAsync(DateTime? select = null)
        {
            try
            {
                var months = await Task.Run(() => _service.GetMonths());
                _ready = false;
                cbMonth.Items.Clear();
                foreach (var m in months)
                    cbMonth.Items.Add(new ComboBoxItem { Content = m.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant(), Tag = m });
                if (months.Count == 0)
                {
                    var now = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                    cbMonth.Items.Add(new ComboBoxItem { Content = now.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant(), Tag = now });
                }
                var pick = cbMonth.Items.OfType<ComboBoxItem>().FirstOrDefault(i => select.HasValue && (DateTime)i.Tag == select.Value) ?? cbMonth.Items.OfType<ComboBoxItem>().First();
                cbMonth.SelectedItem = pick;
                _ready = true;
                await ReloadAllAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดข้อมูล Forecast / Order ไม่สำเร็จ\n" + ex.Message); }
        }

        private async void cbMonth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) await ReloadAllAsync();
        }

        private async Task ReloadAllAsync()
        {
            var month = SelectedMonth;
            if (month == null) return;
            txtChartTitle.Text = $"Forecast, Order & Delivery  •  {month.Value.Year}";
            txtTopTitle.Text = $"TOP 5 ORDER  •  {month.Value.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()}";
            try
            {
                var year = await Task.Run(() => _service.GetYear(month.Value.Year));
                var top = await Task.Run(() => _service.GetTop5(month.Value));
                BuildChart(year);
                icTop.ItemsSource = top;
                txtTopEmpty.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดกราฟไม่สำเร็จ\n" + ex.Message); }
            await LoadRowsAsync(false);
        }

        // กราฟแบบเดียวกับ Dashboard: แท่ง Order + Delivery และจุด Forecast
        private void BuildChart(List<ForecastMonthTotal> data)
        {
            var f = new ChartValues<double>(); var o = new ChartValues<double>(); var d = new ChartValues<double>();
            var labels = new string[12];
            for (int m = 1; m <= 12; m++)
            {
                var row = data.FirstOrDefault(x => x.Month == m);
                f.Add(row?.Forecast ?? 0); o.Add(row?.Order ?? 0); d.Add(row?.Delivery ?? 0);
                labels[m - 1] = new DateTime(2000, m, 1).ToString("MMM", CultureInfo.InvariantCulture);
            }
            BarSeries = new SeriesCollection
            {
                new ColumnSeries { Title = "Order", Values = o, Fill = (Brush)new BrushConverter().ConvertFromString("#6A1B9A"), MaxColumnWidth = 22 },
                new ColumnSeries { Title = "Delivery", Values = d, Fill = (Brush)new BrushConverter().ConvertFromString("#B39DDB"), MaxColumnWidth = 22 },
                new LineSeries
                {
                    Title = "Forecast", Values = f, Stroke = Brushes.Transparent, Fill = Brushes.Transparent,
                    PointGeometry = DefaultGeometries.Circle, PointGeometrySize = 11, LineSmoothness = 0,
                    PointForeground = (Brush)new BrushConverter().ConvertFromString("#1E88E5")
                }
            };
            double topV = new[] { f.DefaultIfEmpty(0).Max(), o.DefaultIfEmpty(0).Max(), d.DefaultIfEmpty(0).Max() }.Max();
            AxisMax = topV <= 0 ? (double?)null : Math.Ceiling(topV * 1.1 / Math.Pow(10, Math.Floor(Math.Log10(topV * 1.1)) - 1)) * Math.Pow(10, Math.Floor(Math.Log10(topV * 1.1)) - 1);
            ChartLabels = labels;
            OnPropertyChanged(nameof(BarSeries)); OnPropertyChanged(nameof(ChartLabels)); OnPropertyChanged(nameof(AxisMax));
        }

        // ตาราง: หน้าแรก 50 แถว / เลื่อนใกล้ล่างสุดโหลดเพิ่มทีละ 30 (เหมือน Store Max-Min) - ค้นหาซ้อนกันใช้ผลล่าสุด
        private async Task LoadRowsAsync(bool more)
        {
            var month = SelectedMonth;
            if (month == null || (more && (_loading || !_hasMore))) return;
            int ver = more ? _version : ++_version;
            int skip = more ? _offset : 0, take = more ? MorePage : FirstPage;
            string key = txtSearch.Text;
            _loading = true;
            try
            {
                var data = await Task.Run(() => _service.GetRows(month.Value, key, skip, take));
                if (ver != _version) return;
                if (!more) { _rows.Clear(); _offset = 0; }
                foreach (var r in data) _rows.Add(r);
                _offset += data.Count;
                _hasMore = data.Count == take;
                Renumber();
                txtEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                txtSub.Text = $"{month.Value.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}  •  showing {_rows.Count:N0} item(s){(_hasMore ? " - scroll down for more" : "")}";
            }
            catch (Exception ex) { if (ver == _version) DialogHelper.ShowError("โหลดรายการไม่สำเร็จ\n" + ex.Message); }
            finally { if (ver == _version) _loading = false; }
        }

        private void Renumber()
        {
            var counters = new Dictionary<string, int>();
            foreach (var r in _rows)
            {
                counters.TryGetValue(r.GroupKey, out int n);
                counters[r.GroupKey] = ++n;
                r.No = n;
            }
            _view.Refresh();
        }

        // คอลัมน์แบบ * วัดตอนตารางยังไม่มีขนาดจริง -> คำนวณความกว้างใหม่หลังข้อมูลมา / ขนาดเปลี่ยน
        private void RefitColumns()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                // แบ่งความกว้างจริงตามสัดส่วน (NO. 70 / PART A 1 / PART NO 1.2 / NAME 2 / ตัวเลข 0.9 x 3)
                double avail = dgRows.ActualWidth - 24;
                if (avail <= 300) return;
                double[] weight = { 0, 1, 1.2, 2, 0.9, 0.9, 0.9 };
                double rest = avail - 70, sum = weight.Sum();
                for (int i = 0; i < dgRows.Columns.Count && i < weight.Length; i++)
                    dgRows.Columns[i].Width = new DataGridLength(i == 0 ? 70 : Math.Max(dgRows.Columns[i].MinWidth, rest * weight[i] / sum));
            }));
        }

        private void dgRows_SizeChanged(object sender, SizeChangedEventArgs e) { if (e.WidthChanged) RefitColumns(); }

        private async void dgRows_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange > 0 && e.ExtentHeight > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 120)
                await LoadRowsAsync(true);
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _searchTimer.Tick += async (s, ev) => { _searchTimer.Stop(); await LoadRowsAsync(false); };
            _searchTimer.Start();
        }

        // 📤 Export ตามตาราง (ทุกแถวของเดือน + คำค้นหา) -> Desktop\CIMS_Export แล้วเปิดไฟล์
        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var month = SelectedMonth;
            if (month == null) return;
            string key = txtSearch.Text;
            btnExport.IsEnabled = false;
            try
            {
                int count = 0;
                string path = await Task.Run(() => _service.Export(month.Value, key, out count));
                LogService.WriteLog(_session?.UserId, "FORECAST_EXPORT", $"Month: {month.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture)} | Rows: {count} | File: {System.IO.Path.GetFileName(path)}", "");
                NotificationManager.Show("Export", $"Export แล้ว {count:N0} รายการ\n{ImportTemplateService.ShortPath(path)}", true);
                ImportTemplateService.OpenFile(path);   // เปิดไฟล์ขึ้นมาเลย (เหมือนทุกหน้า)
            }
            catch (Exception ex)
            {
                string msg = ex.Message.Contains("being used") ? "กรุณาปิดไฟล์ Excel ก่อน Export" : ex.Message;
                DialogHelper.ShowError("Export ไม่สำเร็จ\n" + msg);
            }
            finally { btnExport.IsEnabled = true; }
        }

        // 📄 Template สำหรับ IMPORT FORECAST / ORDER / DELIVERY: ใส่ข้อมูลของเดือนที่เลือกไว้ให้ (แก้ตัวเลขแล้ว Import กลับได้เลย)
        private async void Template_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.CanImportForecastOrder != true) return;
            var month = SelectedMonth ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            string mon = month.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            if (!DialogHelper.ShowConfirm(
                    $"ต้องการสร้างไฟล์ Template สำหรับนำเข้า Forecast / Order / Delivery ใช่หรือไม่?\n\n" +
                    $"• ไฟล์จะมีรายการของเดือน {mon} ที่มีอยู่แล้วให้ (ไม่มีจะเป็นตารางว่าง)\n" +
                    "• กรอกหรือแก้ตัวเลขในไฟล์ แล้วนำเข้ากลับด้วยปุ่ม IMPORT FORECAST / ORDER / DELIVERY\n\n" +
                    "กด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก", "TEMPLATE EXCEL")) return;
            btnTemplate.IsEnabled = false;
            try
            {
                string path = ImportTemplateService.NewPath(ImportTemplateService.Systems.Forecast, "ForecastOrder_" + month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + "_Template");
                int count = 0;
                await Task.Run(() =>
                {
                    var rows = _service.GetRows(month, "", 0, 1000000);
                    count = rows.Count;
                    new ImportTemplateService().CreateForecastTemplate(path, month, rows);
                });
                LogService.WriteLog(_session?.UserId, "FORECAST_TEMPLATE", $"Month: {month.ToString("yyyy-MM", CultureInfo.InvariantCulture)} | Rows: {count} | File: {System.IO.Path.GetFileName(path)}", "");
                NotificationManager.Show("Template", $"สร้างไฟล์ Template แล้ว ({count:N0} รายการ)\n{ImportTemplateService.ShortPath(path)}", true);
                ImportTemplateService.OpenFile(path);   // เปิดไฟล์ Template ขึ้นมาเลย
            }
            catch (Exception ex)
            {
                string msg = ex.Message.Contains("being used") ? "กรุณาปิดไฟล์ Excel ก่อน" : ex.Message;
                DialogHelper.ShowError("สร้างไฟล์ Template ไม่สำเร็จ\n" + msg);
            }
            finally { btnTemplate.IsEnabled = true; }
        }

        // 📥 Import (แบบเดียวกับหน้า Max-Min Calculator): เดือน / ลูกค้า / สินค้าเดิมถูกแทนที่ แล้วคำนวณคลังที่เปิด AUTO CALC
        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.CanImportForecastOrder != true) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import Forecast & Order & Delivery", Filter = "Excel Files (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;
            var calc = new MaxMinCalcService();
            List<CalcImportRow> rows;
            try { rows = await Task.Run(() => calc.ReadForecastExcel(dlg.FileName)); }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }

            var valid = rows.Where(r => r.IsValid).ToList();
            var bad = rows.Where(r => !r.IsValid).ToList();
            string errs = string.Join("\n", bad.Take(8).Select(r => $"• แถว {r.RowNumber}: {r.Error}")) + (bad.Count > 8 ? $"\n• ... และอีก {bad.Count - 8:N0} แถว" : "");
            if (valid.Count == 0) { DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + errs); return; }
            var months = valid.Select(v => v.Month).Distinct().OrderBy(m => m).ToList();
            string monthText = string.Join(", ", months.Select(m => m.ToString("MMM yyyy", CultureInfo.InvariantCulture)));
            if (!DialogHelper.ShowConfirm($"ไฟล์: {System.IO.Path.GetFileName(dlg.FileName)}\n\nนำเข้า {valid.Count:N0} แถว  (เดือน: {monthText})\n" +
                                          $"Forecast {valid.Sum(v => v.Forecast):N0}  •  Order {valid.Sum(v => v.Order):N0}  •  Delivery {valid.Sum(v => v.Delivery):N0}\n" +
                                          "ลูกค้า / สินค้า / เดือนเดียวกันที่เคยนำเข้าจะถูกแทนที่" +
                                          (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{errs}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?", "IMPORT FORECAST / ORDER / DELIVERY")) return;
            try
            {
                string uid = _session?.UserId;
                int n = await Task.Run(() => calc.ApplyForecast(rows, uid));
                LogService.WriteLog(uid, "FORECAST_IMPORT", $"File: {System.IO.Path.GetFileName(dlg.FileName)} | Rows: {n} | Months: {monthText} | Skipped rows: {bad.Count}", "");
                var r = await Task.Run(() => calc.Calculate(null, null, uid));
                if (r.Stocks > 0)
                    LogService.WriteLog(uid, "MAXMIN_CALCULATE", $"Auto calculate after forecast import | Stocks: {r.Stocks} | Updated: {r.Updated} | Skipped (no order): {r.Skipped}", "");
                NotificationManager.Show("Forecast imported", $"นำเข้าแล้ว {n:N0} แถว" + (r.Stocks > 0 ? $"\nคำนวณ MAX / MIN อัตโนมัติ {r.Updated:N0} รายการ" : ""), true);
                await LoadMonthsAsync(months.Last());
            }
            catch (Exception ex) { DialogHelper.ShowError("นำเข้าไม่สำเร็จ (ยกเลิกทั้งไฟล์)\n" + ex.Message); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
