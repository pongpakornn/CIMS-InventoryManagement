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
                () => LoadMonthsAsync(),
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

        // Dropdown 2 ตัว: TABLE (หัวตาราง) = เดือนของตาราง / TOP 5 (ในการ์ด TOP 5) = เดือนของ TOP 5 ORDER + ปีของกราฟ
        //   ทั้งสองตัวมี ALL MONTHS (Tag = null) หรือเดือนเดียว และเลือกแยกกันได้
        private List<DateTime> _months = new List<DateTime>();
        private static bool IsAll(ComboBox cb) => cb.SelectedItem is ComboBoxItem i && i.Tag == null;
        private static DateTime? MonthOf(ComboBox cb) => IsAll(cb) ? null : (cb.SelectedItem as ComboBoxItem)?.Tag as DateTime?;
        private bool TableAll => IsAll(cbTableMonth);
        private DateTime? TableMonth => MonthOf(cbTableMonth);
        private DateTime LatestMonth => _months.Count > 0 ? _months.Max() : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private static string MonText(DateTime m) => m.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

        private void Fill(ComboBox cb, DateTime? month, bool all)
        {
            cb.Items.Clear();
            cb.Items.Add(new ComboBoxItem { Content = "ALL MONTHS", Tag = null });
            foreach (var m in _months) cb.Items.Add(new ComboBoxItem { Content = MonText(m), Tag = m });
            if (_months.Count == 0) cb.Items.Add(new ComboBoxItem { Content = MonText(LatestMonth), Tag = LatestMonth });
            var items = cb.Items.OfType<ComboBoxItem>();
            cb.SelectedItem = all ? items.First(i => i.Tag == null)
                                  : items.FirstOrDefault(i => i.Tag is DateTime d && month.HasValue && d == month.Value) ?? items.First(i => i.Tag != null);
        }

        // select = เดือนที่จะเลือกทั้งสองตัว (หลัง Import) / null = คงที่เลือกอยู่
        private async Task LoadMonthsAsync(DateTime? select = null)
        {
            try
            {
                var months = await Task.Run(() => _service.GetMonths());
                bool tAll = select == null && TableAll, pAll = select == null && IsAll(cbTopMonth);
                DateTime? tKeep = select ?? TableMonth, pKeep = select ?? MonthOf(cbTopMonth);
                _ready = false;
                _months = months;
                Fill(cbTableMonth, tKeep, tAll);
                Fill(cbTopMonth, pKeep, pAll);
                _ready = true;
                await ReloadTopAsync();
                await LoadRowsAsync(false);
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดข้อมูล Forecast / Order ไม่สำเร็จ\n" + ex.Message); }
        }

        private async void cbTableMonth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) await LoadRowsAsync(false);
        }

        private async void cbTopMonth_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) await ReloadTopAsync();
        }

        // กราฟ (ปีของเดือนที่เลือก / ALL MONTHS = ปีล่าสุด) + TOP 5 ORDER
        private async Task ReloadTopAsync()
        {
            bool all = IsAll(cbTopMonth);
            var month = MonthOf(cbTopMonth);
            if (!all && month == null) return;
            int year = (month ?? LatestMonth).Year;
            txtChartTitle.Text = $"Forecast, Order & Delivery  •  {year}";
            txtTopTitle.Text = "TOP 5 ORDER";
            try
            {
                var yearData = await Task.Run(() => _service.GetYear(year));
                var top = await Task.Run(() => _service.GetTop5(all ? null : month));
                BuildChart(yearData);
                icTop.ItemsSource = top;
                txtTopEmpty.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดกราฟไม่สำเร็จ\n" + ex.Message); }
        }

        // กราฟแบบเดียวกับ Dashboard: แท่ง Order + Delivery และเส้น + จุด Forecast
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
                    Title = "Forecast", Values = f, Fill = Brushes.Transparent, StrokeThickness = 2,   // จุด Forecast ต่อกันเป็นเส้นทุกเดือน
                    Stroke = (Brush)new BrushConverter().ConvertFromString("#1E88E5"),
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
            bool all = TableAll;
            var month = TableMonth;
            if ((month == null && !all) || (more && (_loading || !_hasMore))) return;
            int ver = more ? _version : ++_version;
            int skip = more ? _offset : 0, take = more ? MorePage : FirstPage;
            string key = txtSearch.Text;
            _loading = true;
            try
            {
                var data = await Task.Run(() => _service.GetRows(all ? null : month, key, skip, take));
                if (ver != _version) return;
                if (!more) { _rows.Clear(); _offset = 0; }
                foreach (var r in data) _rows.Add(r);
                _offset += data.Count;
                _hasMore = data.Count == take;
                Renumber();
                txtEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (!more && (colMonth.Visibility == Visibility.Visible) != all) { colMonth.Visibility = all ? Visibility.Visible : Visibility.Collapsed; RefitColumns(); }
                txtSub.Text = $"{(all ? "All months" : month.Value.ToString("MMMM yyyy", CultureInfo.InvariantCulture))}  •  showing {_rows.Count:N0} item(s){(_hasMore ? " - scroll down for more" : "")}";
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
                // แบ่งความกว้างจริงตามสัดส่วน (NO. 70 / MONTH 0.7 (ALL MONTHS) / PART A 1 / PART NO 1.2 / NAME 2 / ตัวเลข 0.9 x 3)
                double avail = dgRows.ActualWidth - 24;
                if (avail <= 300) return;
                var weight = new Dictionary<string, double> { ["MONTH"] = 0.7, ["PART A"] = 1, ["PART NO"] = 1.2, ["PRODUCT NAME"] = 2, ["FORECAST"] = 0.9, ["ORDER"] = 0.9, ["DELIVERY"] = 0.9 };
                var cols = dgRows.Columns.Where(c => c.Visibility == Visibility.Visible && c.Header is string h && weight.ContainsKey(h)).ToList();
                double rest = avail - 70, sum = cols.Sum(c => weight[(string)c.Header]);
                dgRows.Columns[0].Width = new DataGridLength(70);
                foreach (var c in cols) c.Width = new DataGridLength(Math.Max(c.MinWidth, rest * weight[(string)c.Header] / sum));
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

        // 📤 Export: เลือก ALL MONTHS หรือเฉพาะเดือนที่ต้องการ -> 1 เดือน = 1 ชีท (ตามคำค้นหา) -> Desktop\CIMS_Export แล้วเปิดไฟล์
        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var all = _months.ToList();
            if (all.Count == 0) { DialogHelper.ShowWarning("ยังไม่มีข้อมูล Forecast / Order / Delivery ให้ Export"); return; }
            var pre = TableAll ? all : new List<DateTime> { TableMonth ?? all[0] };
            string key = txtSearch.Text;
            var months = MonthPickWindow.Choose("📤 EXPORT FORECAST / ORDER / DELIVERY",
                "Tick ALL MONTHS or the months you want  •  one sheet per month" + (string.IsNullOrWhiteSpace(key) ? "" : $"  •  search: {key.Trim()}"),
                "📤 EXPORT", all, pre);
            if (months == null) return;
            btnExport.IsEnabled = false;
            try
            {
                int count = 0;
                string path = await Task.Run(() => _service.Export(months, key, out count));
                LogService.WriteLog(_session?.UserId, "FORECAST_EXPORT", $"Months: {string.Join(", ", months.Select(m => m.ToString("yyyy-MM", CultureInfo.InvariantCulture)))} | Sheets: {months.Count} | Rows: {count} | File: {System.IO.Path.GetFileName(path)}", "");
                NotificationManager.Show("Export", $"Export แล้ว {count:N0} รายการ  •  {months.Count:N0} ชีท\n{ImportTemplateService.ShortPath(path)}", true);
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
            var month = TableMonth ?? LatestMonth;
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

        // 📥 Import: เดือน / ลูกค้า / สินค้าเดิมถูกแทนที่ - กดแล้วเลือก 2 แบบ
        //   1) IMPORT + คำนวณ MAX / MIN AUTO = แบบเดิม (ต้องมี CUSTOMER ทุกแถว แล้วคำนวณคลังที่เปิด AUTO CALC)
        //   2) IMPORT อย่างเดียว = ไม่คำนวณ / CUSTOMER ว่างได้ (ระบบหาจาก PART NO หรือ PRODUCT NAME ของสินค้าในระบบ)
        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.CanImportForecastOrder != true) return;
            int mode = DialogHelper.ShowChoice(MaxMinCalcService.ImportChoiceText, MaxMinCalcService.ImportChoiceTitle,
                                               MaxMinCalcService.ImportChoiceCalc, MaxMinCalcService.ImportChoiceOnly);
            if (mode == 0) return;
            bool withCalc = mode == 1;
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import Forecast & Order & Delivery", Filter = "Excel Files (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;
            var calc = new MaxMinCalcService();
            List<CalcImportRow> rows;
            try { rows = await Task.Run(() => calc.ReadForecastExcel(dlg.FileName, findCustomer: !withCalc)); }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }

            var valid = rows.Where(r => r.IsValid).ToList();
            var bad = rows.Where(r => !r.IsValid).ToList();
            string errs = string.Join("\n", bad.Take(8).Select(r => $"• แถว {r.RowNumber}: {r.Error}")) + (bad.Count > 8 ? $"\n• ... และอีก {bad.Count - 8:N0} แถว" : "");
            if (valid.Count == 0) { DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + errs); return; }
            var months = valid.Select(v => v.Month).Distinct().OrderBy(m => m).ToList();
            int auto = valid.Count(v => !string.IsNullOrEmpty(v.Note));
            string monthText = string.Join(", ", months.Select(m => m.ToString("MMM yyyy", CultureInfo.InvariantCulture)));
            if (!DialogHelper.ShowConfirm($"ไฟล์: {System.IO.Path.GetFileName(dlg.FileName)}\n\nนำเข้า {valid.Count:N0} แถว  (เดือน: {monthText})\n" +
                                          $"Forecast {valid.Sum(v => v.Forecast):N0}  •  Order {valid.Sum(v => v.Order):N0}  •  Delivery {valid.Sum(v => v.Delivery):N0}\n" +
                                          "ลูกค้า / สินค้า / เดือนเดียวกันที่เคยนำเข้าจะถูกแทนที่" +
                                          (withCalc ? "\n\nแล้วคำนวณ MAX / MIN ของคลังที่เปิด AUTO CALC" : "\n\nนำเข้าอย่างเดียว ไม่คำนวณ MAX / MIN") +
                                          (auto > 0 ? $"\n\nℹ ระบบหา CUSTOMER / PART NO จากสินค้าในระบบให้ {auto:N0} แถว (แถวที่ไม่ได้กรอก)" : "") +
                                          (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{errs}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?", withCalc ? "IMPORT + คำนวณ MAX / MIN AUTO" : "IMPORT อย่างเดียว")) return;
            try
            {
                string uid = _session?.UserId;
                int n = await Task.Run(() => calc.ApplyForecast(rows, uid));
                LogService.WriteLog(uid, "FORECAST_IMPORT", $"File: {System.IO.Path.GetFileName(dlg.FileName)} | Rows: {n} | Months: {monthText} | Skipped rows: {bad.Count}" +
                                                        (withCalc ? "" : $" | Import only (no MAX / MIN calculate) | Customer / part found by system: {auto}"), "");
                if (!withCalc)
                {
                    NotificationManager.Show("Forecast imported", $"นำเข้าแล้ว {n:N0} แถว (ไม่คำนวณ MAX / MIN)", true);
                    await LoadMonthsAsync(months.Last());
                    return;
                }
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
