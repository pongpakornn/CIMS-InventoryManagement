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
    // 📋 รายการ PR ที่ Approve แล้ว - ค้นหาเบื้องหลัง (ผลเก่าที่มาช้าถูกทิ้ง ไม่ค้าง) / ติ๊กเลือกแล้ว EXPORT / EDIT / DEL
    public partial class PRApprovedWindow : Window
    {
        private readonly PRService _service = new PRService();
        private readonly ExportService _export = new ExportService();
        private readonly UserSession _session;
        private readonly ObservableCollection<PRModel> _rows = new ObservableCollection<PRModel>();
        private DispatcherTimer _searchTimer;
        private int _version;
        private bool _ready;
        public bool Changed { get; private set; }

        // 🔐 สิทธิ์ PR: EDIT = ปุ่ม EDIT / DEL = ปุ่ม DEL / APPROVE = EXPORT
        public Visibility EditVisibility => _session?.CanEditPR == true ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DeleteVisibility => _session?.CanDeletePR == true ? Visibility.Visible : Visibility.Collapsed;

        public PRApprovedWindow(UserSession session)
        {
            _session = session;
            InitializeComponent();
            btnExport.Visibility = _session?.CanApprovePR == true ? Visibility.Visible : Visibility.Collapsed;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            dgApproved.ItemsSource = _rows;
            dpFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
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
                var list = await Task.Run(() => _service.GetApprovedList(key, from, to));
                if (ver != _version) return;   // พิมพ์ต่อแล้ว -> ผลนี้เก่า ทิ้งไป
                var picked = new HashSet<string>(_rows.Where(r => r.IsSelected).Select(r => r.PRNumber));
                _rows.Clear();
                foreach (var r in list) { r.IsSelected = picked.Contains(r.PRNumber); _rows.Add(r); }
                UpdateStatus();
            }
            catch (Exception ex)
            {
                if (ver != _version) return;
                txtStatus.Text = "";
                NotificationManager.Show("Approved PR", "โหลดรายการไม่สำเร็จ: " + ex.Message, false);
            }
        }

        private void UpdateStatus()
        {
            int sel = _rows.Count(r => r.IsSelected);
            txtStatus.Text = $"{_rows.Count:N0} APPROVED PR  •  {_rows.Count(r => !r.IsExported):N0} NOT EXPORTED YET  •  {sel:N0} SELECTED";
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

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.CanApprovePR != true) return;
            var selected = _rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0) { DialogHelper.ShowWarning("กรุณาติ๊กเลือกรายการที่ต้องการ Export ก่อนครับ"); return; }
            int again = selected.Count(r => r.IsExported);
            if (!DialogHelper.ShowConfirm($"Export PR ที่เลือก {selected.Count:N0} รายการ" + (again > 0 ? $"\n(เคย Export แล้ว {again:N0} รายการ จะถูก Export ซ้ำ)" : "") + "\n\nยืนยันหรือไม่?", "EXPORT PR")) return;

            btnExport.IsEnabled = false;
            try
            {
                string uid = _session?.UserId;
                await Task.Run(() => _export.ExportPRToExcel(selected));
                await Task.Run(() =>
                {
                    foreach (var item in selected)
                        if (_service.UpdateAfterExport(item.PRNumber, uid))
                            LogService.WriteLog(uid, "EXPORT_PR", $"Exported PR No: {item.PRNumber}", item.PRNumber);
                });
                NotificationManager.Show("Export complete", $"Export PR แล้ว {selected.Count:N0} รายการ", true);
                foreach (var r in _rows) r.IsSelected = false;
                Changed = true;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                string msg = ex.Message.Contains("being used") ? "กรุณาปิดไฟล์ Excel ก่อน Export รายการ" : ex.Message;
                DialogHelper.ShowError("Export ไม่สำเร็จ: " + msg);
            }
            finally { btnExport.IsEnabled = true; }
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is PRModel pr) || _session?.CanEditPR != true) return;
            var w = new PREditWindow(pr) { Owner = this };
            if (w.ShowDialog() != true) return;
            try
            {
                int q = w.Qty; string d = w.Dept, t = w.Target, r = w.Remark;
                await Task.Run(() => _service.UpdatePR(pr.PRNumber, q, d, t, r));
                var changes = new List<string>();
                if (q != pr.QTY) changes.Add($"QTY: {pr.QTY}->{q}");
                if (d != (pr.Department ?? "")) changes.Add($"DEPT: {pr.Department}->{d}");
                if (t != (pr.TargetDept ?? "")) changes.Add($"TARGET: {pr.TargetDept}->{t}");
                if (r != (pr.Remark ?? "")) changes.Add($"REMARK: {pr.Remark}->{r}");
                LogService.WritePRLog(_session?.UserId, "EDIT_PR", $"Edited PR No: {pr.PRNumber} | {(changes.Count == 0 ? "no change" : string.Join(", ", changes))}", pr.PRNumber);
                NotificationManager.Show("Saved", $"แก้ไข {pr.PRNumber} แล้ว", true);
                Changed = true;
                await LoadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("แก้ไขไม่สำเร็จ\n" + ex.Message); }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is PRModel pr) || _session?.CanDeletePR != true) return;
            if (!DialogHelper.ShowConfirm($"ลบ {pr.PRNumber}\n{pr.PartName}  จำนวน {pr.QTY:N0}\n\nลบแล้วกู้คืนไม่ได้ ยืนยันหรือไม่?", "DELETE PR")) return;
            try
            {
                await Task.Run(() => _service.DeletePR(pr.PRNumber));
                LogService.WritePRLog(_session?.UserId, "DELETE_PR", $"Deleted approved PR No: {pr.PRNumber} | {pr.PartName} | QTY: {pr.QTY}", pr.PRNumber);
                NotificationManager.Show("Deleted", $"ลบ {pr.PRNumber} แล้ว", true);
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
