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
    // 🕘 ประวัติการสแกน - โหลดข้อมูลเบื้องหลัง (ยกเลิกคำค้นเก่าได้ ไม่ให้หน้าจอค้าง)
    // 1 หน้า = 50 รายการ: แสดง 20 แถวแรก เลื่อนลงเพิ่มทีละ 15 จนครบ 50 แล้วกด NEXT / PREV เปลี่ยนหน้า
    public partial class ScanHistoryWindow : Window
    {
        private readonly ScanHistoryService _service = new ScanHistoryService();
        private readonly UserSession _session;
        private readonly ObservableCollection<ScanHistoryRow> _shown = new ObservableCollection<ScanHistoryRow>();
        private List<ScanHistoryRow> _pageRows = new List<ScanHistoryRow>();
        private const int FirstRows = 20, MoreRows = 15;
        private int _page, _total;
        private CancellationTokenSource _cts;
        private DispatcherTimer _searchTimer;
        private bool _ready;

        public ScanHistoryWindow(UserSession session, IEnumerable<StockModel> stocks)
        {
            InitializeComponent();
            _session = session;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dgHistory.ItemsSource = _shown;
            cbStock.Items.Add(new ComboBoxItem { Content = "ALL STOCKS", Tag = null });
            foreach (var s in stocks) cbStock.Items.Add(new ComboBoxItem { Content = s.Code, Tag = s, ToolTip = s.Name });
            cbStock.SelectedIndex = 0;
            cbType.SelectedIndex = 0;
            dpFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            dpTo.SelectedDate = DateTime.Today;

            Loaded += async (s, e) => { _ready = true; await LoadPage(0); txtSearch.Focus(); };
            Closed += (s, e) => _cts?.Cancel();
        }

        private ScanHistoryFilter BuildFilter()
        {
            var stock = (cbStock.SelectedItem as ComboBoxItem)?.Tag as StockModel;
            string type = (cbType.SelectedItem as ComboBoxItem)?.Tag as string;
            DateTime from = dpFrom.SelectedDate ?? DateTime.Today, to = dpTo.SelectedDate ?? DateTime.Today;
            if (to < from) { var t = from; from = to; to = t; }
            return new ScanHistoryFilter
            {
                Keyword = txtSearch.Text,
                From = from, To = to,
                StkId = stock?.StkId,
                StockIsMain = stock?.IsMain ?? false,
                TxType = string.IsNullOrEmpty(type) ? null : type
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
                if (dgHistory.Items.Count > 0) dgHistory.ScrollIntoView(dgHistory.Items[0]);
                UpdateFooter();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (cts.IsCancellationRequested && ex is Microsoft.Data.SqlClient.SqlException) { }
            catch (Exception ex)
            {
                txtStatus.Text = "";
                NotificationManager.Show("History", "โหลดประวัติการสแกนไม่สำเร็จ: " + ex.Message, false);
            }
        }

        private void UpdateFooter()
        {
            int pages = Math.Max(1, (int)Math.Ceiling(_total / (double)ScanHistoryService.PageSize));
            int first = _total == 0 ? 0 : _page * ScanHistoryService.PageSize + 1;
            int last = _page * ScanHistoryService.PageSize + _shown.Count;
            txtPage.Text = $"PAGE {_page + 1:N0} / {pages:N0}";
            txtStatus.Text = $"{first:N0} - {last:N0} OF {_total:N0} RECORDS" + (_shown.Count < _pageRows.Count ? "   •   scroll down for more" : "");
            btnPrev.IsEnabled = _page > 0;
            btnNext.IsEnabled = (_page + 1) * ScanHistoryService.PageSize < _total;
            txtEmpty.Visibility = _total == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // เลื่อนใกล้ล่างสุด -> แสดงเพิ่มอีก 15 แถวจนครบหน้า (50)
        private void dgHistory_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // เฉพาะตอนผู้ใช้เลื่อนลงจริง -> เพิ่มครั้งละ 15 แถว
            // (ตอนเพิ่มแถว ตารางที่อยู่ล่างสุดจะเลื่อนตามเอง = ExtentHeightChange > 0 ไม่นับ ไม่งั้นจะโหลดต่อกันจนครบ 50 ทีเดียว)
            if (e.VerticalChange <= 0 || e.ExtentHeightChange != 0 || _shown.Count >= _pageRows.Count || e.ExtentHeight <= 0 || e.ViewportHeight <= 0) return;
            // DataGrid เลื่อนเป็น "แถว" (ไม่ใช่ pixel) -> ใกล้ล่างสุด = เหลือไม่ถึง 1 แถว
            bool rowUnits = (e.OriginalSource as ScrollViewer)?.CanContentScroll ?? true;
            double left = e.ExtentHeight - (e.VerticalOffset + e.ViewportHeight);
            if (left <= (rowUnits ? 1 : 30)) ShowMore();
        }

        // อยู่ล่างสุดแล้ว (ตารางเลื่อนตามแถวที่เพิ่ม) เลื่อนล้อเมาส์ / กดลง ต่อ -> แสดงชุดถัดไป
        private void dgHistory_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta < 0 && AtBottom()) ShowMore();
        }

        private void dgHistory_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Down || e.Key == Key.PageDown || e.Key == Key.End) && AtBottom()) ShowMore();
        }

        private bool AtBottom()
        {
            var sv = FindScrollViewer(dgHistory);
            return sv != null && sv.ScrollableHeight - sv.VerticalOffset <= (sv.CanContentScroll ? 1 : 30);
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

        // พิมพ์ค้นหา: รอให้หยุดพิมพ์ 350 ms แล้วค้น (คำค้นเก่าที่ยังไม่เสร็จถูกยกเลิก)
        private void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _searchTimer.Tick += async (s, ev) => { _searchTimer.Stop(); await LoadPage(0); };
            _searchTimer.Start();
        }

        private async void Date_Changed(object sender, SelectionChangedEventArgs e) => await LoadPage(0);
        private async void Combo_Changed(object sender, SelectionChangedEventArgs e) => await LoadPage(0);
        private async void Prev_Click(object sender, RoutedEventArgs e) => await LoadPage(Math.Max(0, _page - 1));
        private async void Next_Click(object sender, RoutedEventArgs e) => await LoadPage(_page + 1);

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var filter = BuildFilter();
            string path = CIMS.Services.ImportTemplateService.NewPath(CIMS.Services.ImportTemplateService.Systems.Scanner, "ScanHistory");
            string file = System.IO.Path.GetFileName(path);
            btnExport.IsEnabled = false;
            try
            {
                
                int n = await Task.Run(() => _service.Export(filter, path));
                LogService.WriteLog(_session?.UserId, "SCAN_HISTORY_EXPORT", $"Rows: {n} | {filter.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} - {filter.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} | Key: {filter.Keyword} | File: {file}", "");
                NotificationManager.Show("Export complete", $"Export ประวัติการสแกน {n:N0} รายการ\n{CIMS.Services.ImportTemplateService.ShortPath(path)}", true);
                CIMS.Services.ImportTemplateService.OpenFile(path);   // เปิดไฟล์ขึ้นมาเลย (เหมือนทุกหน้า)
            }
            catch (System.IO.IOException) { DialogHelper.ShowError("บันทึกไฟล์ไม่สำเร็จ กรุณาปิดไฟล์ Excel ที่เปิดอยู่ก่อนแล้วลองใหม่"); }
            catch (Exception ex) { DialogHelper.ShowError("Export ไม่สำเร็จ\n" + ex.Message); }
            finally { btnExport.IsEnabled = true; }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
