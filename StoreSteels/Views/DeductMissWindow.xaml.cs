using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CIMS.Views
{
    // ⚠ NOT DEDUCTED - ป้ายที่แสกนรับเข้าแล้วแต่ตัด Coil แม่ในคลังต้นทางไม่ได้ (เก็บให้เองตอนแสกน)
    //    ค้นหาเบื้องหลัง (ผลเก่าที่มาช้าถูกทิ้ง) / ช่วงวันที่ / ติ๊กเลือกแล้ว EXPORT / DELETE (Admin Level 1 เท่านั้น)
    public partial class DeductMissWindow : Window
    {
        private readonly DeductMissService _service = new DeductMissService();
        private readonly UserSession _session;
        private readonly ObservableCollection<DeductMissRow> _rows = new ObservableCollection<DeductMissRow>();
        private DispatcherTimer _searchTimer;
        private int _version;
        private bool _ready;
        public bool Changed { get; private set; }

        // deducted = true -> ✔ DEDUCTED: ป้ายที่แสกนรับเข้าแล้วตัดคลังต้นทาง (STOCK-PANTA) ได้ (ดูอย่างเดียว ไม่มีปุ่มลบ)
        private readonly bool _deducted;

        public DeductMissWindow(UserSession session, bool deducted = false)
        {
            _session = session;
            _deducted = deducted;
            InitializeComponent();
            btnDelete.Visibility = !deducted && _session?.UserLevel == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (deducted)
            {
                Title = "Deducted";
                hdrBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
                txtTitle.Text = "✔ DEDUCTED";
                txtSub.Text = "Labels received with SCAN IN that cut the source stock (e.g. STOCK-PANTA) - what was cut, from which mother coil / product, and what is left";
                txtSub.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xE6, 0xC9));
                txtEmpty.Text = "NO DEDUCTION IN THIS RANGE";
                colCut.Header = "CUT FROM SOURCE";
                colMissing.Visibility = colReason.Visibility = colMotherNow.Visibility = Visibility.Collapsed;
                colSrcPart.Visibility = colSrcAfter.Visibility = colHow.Visibility = colMotherDone.Visibility = Visibility.Visible;
            }
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            // จอเล็ก (1366 x 768): ไม่ให้หน้าต่างล้นจอ
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width, area.Width);
            Height = Math.Min(Height, area.Height);
            dgMiss.ItemsSource = _rows;
            dpFrom.SelectedDate = _deducted ? DateTime.Today : DateTime.Today.AddDays(-30);
            dpTo.SelectedDate = DateTime.Today;
            Loaded += async (s, e) => { _ready = true; await LoadAsync(); txtSearch.Focus(); };
        }

        private async Task LoadAsync()
        {
            if (!_ready) return;
            int ver = ++_version;
            string key = txtSearch.Text;
            DateTime from = dpFrom.SelectedDate ?? DateTime.Today, to = dpTo.SelectedDate ?? DateTime.Today;
            if (to < from) { var t = from; from = to; to = t; }
            txtStatus.Text = "⏳ LOADING...";
            try
            {
                var list = await Task.Run(() => _deducted ? _service.GetDeducted(key, from, to) : _service.GetRows(key, from, to));
                if (ver != _version) return;
                var picked = new HashSet<int>(_rows.Where(r => r.IsSelected).Select(r => r.MissId));
                _rows.Clear();
                foreach (var r in list) { r.IsSelected = picked.Contains(r.MissId); _rows.Add(r); }
                UpdateStatus();
            }
            catch (Exception ex)
            {
                if (ver != _version) return;
                txtStatus.Text = "";
                NotificationManager.Show("Not deducted", "โหลดรายการไม่สำเร็จ: " + ex.Message, false);
            }
        }

        private void UpdateStatus()
        {
            int sel = _rows.Count(r => r.IsSelected);
            decimal kg = _rows.Sum(r => r.Missing);
            int foundNow = _rows.Count(r => r.MotherNow.HasValue);
            txtStatus.Text = _deducted
                ? $"{_rows.Count:N0} LABELS  •  CUT FROM SOURCE {Qty.Plain(_rows.Sum(r => r.Deducted))} KG  •  BY MOTHER COIL {_rows.Count(r => r.How == "MOTHER COIL"):N0}  •  COIL MOVED {_rows.Count(r => r.How == "COIL"):N0}  •  {sel:N0} SELECTED"
                : $"{_rows.Count:N0} LABELS  •  NOT DEDUCTED {Qty.Plain(kg)} KG  •  MOTHER COIL NOW IN SOURCE: {foundNow:N0}  •  {sel:N0} SELECTED";
            txtEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            chkAll.IsChecked = _rows.Count > 0 && sel == _rows.Count;
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchTimer.Tick += async (s, ev) => { _searchTimer.Stop(); await LoadAsync(); };
            _searchTimer.Start();
        }

        private async void Date_Changed(object sender, SelectionChangedEventArgs e) => await LoadAsync();

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool on = chkAll.IsChecked == true;
            foreach (var r in _rows) r.IsSelected = on;
            UpdateStatus();
        }

        private void Row_Checked(object sender, RoutedEventArgs e) => UpdateStatus();

        // ติ๊กไว้ = Export เฉพาะที่ติ๊ก / ไม่ได้ติ๊ก = ทุกแถวที่แสดงอยู่
        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var rows = _rows.Where(r => r.IsSelected).ToList();
            if (rows.Count == 0) rows = _rows.ToList();
            if (rows.Count == 0) { DialogHelper.ShowWarning("ไม่มีรายการให้ Export"); return; }
            btnExport.IsEnabled = false;
            try
            {
                string path = await Task.Run(() => _deducted ? _service.ExportDeducted(rows) : _service.Export(rows));
                LogService.WriteLog(_session?.UserId, _deducted ? "EXPORT_DEDUCTED" : "EXPORT_NOT_DEDUCTED", $"Exported {rows.Count} {(_deducted ? "deducted" : "not-deducted")} labels", _deducted ? "DEDUCTED" : "NOT_DEDUCTED");
                ImportTemplateService.OpenFile(path);
            }
            catch (Exception ex)
            {
                string msg = ex.Message.Contains("being used") ? "กรุณาปิดไฟล์ Excel ก่อน Export" : ex.Message;
                DialogHelper.ShowError("Export ไม่สำเร็จ: " + msg);
            }
            finally { btnExport.IsEnabled = true; }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.UserLevel != 1) return;
            var rows = _rows.Where(r => r.IsSelected).ToList();
            if (rows.Count == 0) { DialogHelper.ShowWarning("กรุณาติ๊กเลือกรายการที่ต้องการลบก่อนครับ"); return; }
            if (!DialogHelper.ShowConfirm($"ลบ {rows.Count:N0} รายการออกจากรายการ NOT DEDUCTED\n\n(ไม่เปลี่ยนยอดสต็อก / รายการสแกน)\nลบแล้วกู้คืนไม่ได้ ยืนยันหรือไม่?", "DELETE")) return;
            try
            {
                var ids = rows.Select(r => r.MissId).ToList();
                int n = await Task.Run(() => _service.Delete(ids));
                LogService.WriteLog(_session?.UserId, "DELETE_NOT_DEDUCTED", $"Deleted {n} not-deducted labels: {string.Join(", ", rows.Take(20).Select(r => r.CoilNo))}{(rows.Count > 20 ? " ..." : "")}", "NOT_DEDUCTED");
                NotificationManager.Show("Deleted", $"ลบแล้ว {n:N0} รายการ", true);
                Changed = true;
                await LoadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("ลบไม่สำเร็จ\n" + ex.Message); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
