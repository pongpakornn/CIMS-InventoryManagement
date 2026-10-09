using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CIMS.Views
{
    // 🕘 ACTIVITY LOG - โหลดเบื้องหลัง ยกเลิกคำค้นเก่าได้ (ไม่ค้าง) / 50 รายการต่อหน้า แสดง 20 แล้วเลื่อนเพิ่มทีละ 15
    public partial class ActivityLogView : Page
    {
        private readonly ActivityLogService _service = new ActivityLogService();
        private readonly UserSession _session;
        private readonly ObservableCollection<ActivityLogRow> _shown = new ObservableCollection<ActivityLogRow>();
        private List<ActivityLogRow> _pageRows = new List<ActivityLogRow>();
        private const int FirstRows = 20, MoreRows = 15;
        private int _page, _total;
        private CancellationTokenSource _cts;
        private DispatcherTimer _searchTimer;
        private bool _ready;

        public ActivityLogView(UserSession session)
        {
            InitializeComponent();
            _session = session;
            dgLog.ItemsSource = _shown;
            dpFrom.SelectedDate = DateTime.Today.AddDays(-6);
            dpTo.SelectedDate = DateTime.Today;
            cbUser.Items.Add(new ComboBoxItem { Content = "ALL USERS", Tag = null });
            cbAction.Items.Add(new ComboBoxItem { Content = "ALL ACTIONS", Tag = null });
            cbUser.SelectedIndex = cbAction.SelectedIndex = 0;

            Loaded += async (s, e) =>
            {
                try
                {
                    var (users, acts) = await Task.Run(() => _service.GetLookups());
                    foreach (var u in users) cbUser.Items.Add(new ComboBoxItem { Content = string.IsNullOrWhiteSpace(u.Name) ? u.Id : $"{u.Id} - {u.Name}", Tag = u.Id });
                    foreach (var a in acts) cbAction.Items.Add(new ComboBoxItem { Content = a, Tag = a });
                }
                catch (Exception ex) { NotificationManager.Show("Activity Log", "โหลดตัวกรองไม่สำเร็จ: " + ex.Message, false); }
                _ready = true;
                await LoadPage(0);
            };
            Unloaded += (s, e) => { _cts?.Cancel(); _searchTimer?.Stop(); };

            // ⚡ เรียลไทม์: Log ใหม่จากทุกเครื่องขึ้นเอง - เฉพาะตอนดูหน้าแรกอยู่บนสุด (กำลังเลื่อนอ่าน / หน้าอื่น = ไม่ดึงทับ)
            LiveRefresh.Attach(this, TimeSpan.FromSeconds(3),
                () => LiveRefresh.DbToken("SELECT MAX(LogID), COUNT_BIG(*) FROM CIMS.SystemLogs"),
                () => LoadPage(0),
                () => !_ready || _page != 0 || ScrollOffset(dgLog) > 0.5);
        }

        private static double ScrollOffset(DependencyObject root)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is ScrollViewer sv) return sv.VerticalOffset;
                double v = ScrollOffset(c);
                if (v >= 0) return v;
            }
            return -1;
        }

        private ActivityLogFilter BuildFilter()
        {
            DateTime from = dpFrom.SelectedDate ?? DateTime.Today, to = dpTo.SelectedDate ?? DateTime.Today;
            if (to < from) { var t = from; from = to; to = t; }
            return new ActivityLogFilter
            {
                Keyword = txtSearch.Text,
                From = from, To = to,
                UserId = (cbUser.SelectedItem as ComboBoxItem)?.Tag as string,
                Action = (cbAction.SelectedItem as ComboBoxItem)?.Tag as string
            };
        }

        private async Task LoadPage(int page)
        {
            if (!_ready) return;
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            var filter = BuildFilter();
            txtStatus.Text = "⏳ LOADING...";
            btnPrev.IsEnabled = btnNext.IsEnabled = false;
            try
            {
                var (rows, total) = await _service.QueryAsync(filter, page, cts.Token);
                if (cts.IsCancellationRequested) return;
                _page = page; _total = total; _pageRows = rows;
                _shown.Clear();
                foreach (var r in rows.Take(FirstRows)) _shown.Add(r);
                if (dgLog.Items.Count > 0) dgLog.ScrollIntoView(dgLog.Items[0]);
                UpdateFooter();
            }
            catch (OperationCanceledException) { }
            catch (Exception) when (cts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                txtStatus.Text = "";
                NotificationManager.Show("Activity Log", "โหลดข้อมูลไม่สำเร็จ: " + ex.Message, false);
            }
        }

        private void UpdateFooter()
        {
            int pages = Math.Max(1, (int)Math.Ceiling(_total / (double)ActivityLogService.PageSize));
            int first = _total == 0 ? 0 : _page * ActivityLogService.PageSize + 1;
            int last = _page * ActivityLogService.PageSize + _shown.Count;
            txtPage.Text = $"PAGE {_page + 1:N0} / {pages:N0}";
            txtStatus.Text = $"{first:N0} - {last:N0} OF {_total:N0} RECORDS" + (_shown.Count < _pageRows.Count ? "   •   scroll down for more" : "");
            btnPrev.IsEnabled = _page > 0;
            btnNext.IsEnabled = (_page + 1) * ActivityLogService.PageSize < _total;
            txtEmpty.Visibility = _total == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // เลื่อนลงจนสุด -> แสดงเพิ่มอีก 15 แถวจนครบหน้า (ไม่นับตอนตารางขยายเพราะเพิ่มแถว)
        private void dgLog_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange <= 0 || e.ExtentHeightChange != 0 || e.ViewportHeight <= 0) return;
            if (e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight) <= 1) ShowMore();
        }

        private void dgLog_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta >= 0) return;
            var sv = FindScrollViewer(dgLog);
            if (sv != null && sv.ScrollableHeight - sv.VerticalOffset <= 1) ShowMore();
        }

        private void ShowMore()
        {
            if (_shown.Count >= _pageRows.Count) return;
            foreach (var r in _pageRows.Skip(_shown.Count).Take(MoreRows).ToList()) _shown.Add(r);
            UpdateFooter();
        }

        private static ScrollViewer FindScrollViewer(DependencyObject d)
        {
            if (d is ScrollViewer sv) return sv;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var found = FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
                if (found != null) return found;
            }
            return null;
        }

        // พิมพ์ค้นหา: รอหยุดพิมพ์ 350 ms แล้วค้น (คำค้นเก่าถูกยกเลิก)
        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _searchTimer.Tick += async (s, ev) => { _searchTimer.Stop(); await LoadPage(0); };
            _searchTimer.Start();
        }

        private async void Filter_Changed(object sender, SelectionChangedEventArgs e) => await LoadPage(0);
        private async void Prev_Click(object sender, RoutedEventArgs e) => await LoadPage(Math.Max(0, _page - 1));
        private async void Next_Click(object sender, RoutedEventArgs e) => await LoadPage(_page + 1);

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var filter = BuildFilter();
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CIMS_Export");
            string file = $"ActivityLog_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.xlsx";
            btnExport.IsEnabled = false;
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, file);
                int n = await Task.Run(() => _service.Export(filter, path));
                LogService.WriteLog(_session?.UserId, "ACTIVITY_LOG_EXPORT", $"Rows: {n} | {filter.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} - {filter.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} | Key: {filter.Keyword} | File: {file}", "");
                NotificationManager.Show("Export complete", $"Export Activity Log {n:N0} รายการ\nDesktop\\CIMS_Export\\{file}", true);
                CIMS.Services.ImportTemplateService.OpenFile(path);   // เปิดไฟล์ขึ้นมาเลย (เหมือนทุกหน้า)
            }
            catch (System.IO.IOException) { DialogHelper.ShowError("บันทึกไฟล์ไม่สำเร็จ กรุณาปิดไฟล์ Excel ที่เปิดอยู่ก่อนแล้วลองใหม่"); }
            catch (Exception ex) { DialogHelper.ShowError("Export ไม่สำเร็จ\n" + ex.Message); }
            finally { btnExport.IsEnabled = true; }
        }
    }
}
