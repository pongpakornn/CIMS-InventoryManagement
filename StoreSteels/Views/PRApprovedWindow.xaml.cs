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

        // คลังที่เลือกในหน้า PR (null = ทุกคลัง) + คลังที่เปิดเก็บ PR (ตัวเลือกของ EXPORT PATH EXCEL)
        private readonly StockModel _stock;
        private readonly List<StockModel> _prStocks;

        public PRApprovedWindow(UserSession session, StockModel stock = null, List<StockModel> prStocks = null)
        {
            _session = session;
            _stock = stock;
            _prStocks = prStocks ?? new List<StockModel>();
            InitializeComponent();
            btnExport.Visibility = _session?.CanApprovePR == true ? Visibility.Visible : Visibility.Collapsed;
            if (_stock != null) txtSubtitle.Text = $"STOCK : {_stock.Code}  •  Search, pick a date range, tick the items and export / edit / delete";
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
                int? stk = _stock?.StkId;
                var list = await Task.Run(() => _service.GetApprovedList(key, from, to, stk));
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

        // 📤 EXPORT: เลือกแบบ (EXPORT = ไฟล์ใหม่ / EXPORT PATH = ต่อข้อมูลลงไฟล์ Excel ของคลัง) -> เลือกคลัง -> บันทึกแล้วเปิดไฟล์
        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.CanApprovePR != true) return;
            var selected = _rows.Where(r => r.IsSelected).ToList();
            if (selected.Count == 0) { DialogHelper.ShowWarning("กรุณาติ๊กเลือกรายการที่ต้องการ Export ก่อนครับ"); return; }

            Dictionary<int, PRExcelLayout> layouts = new Dictionary<int, PRExcelLayout>();
            try { layouts = new StockService().GetPrExcelLayouts(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PR layouts: {ex.Message}"); }
            var pathStocks = _prStocks.Where(s => layouts.TryGetValue(s.StkId, out var l) && l.HasPath).ToList();

            string mode = TemplateChoiceWindow.ChooseWithTitle("📤 EXPORT PR", "เลือกแบบการ Export PR ที่ติ๊กไว้ " + selected.Count.ToString("N0") + " รายการ", new[]
            {
                new TemplateChoiceWindow.Option { Key = "FILE", Title = "📤 EXPORT", Detail = "สร้างไฟล์ใหม่ PR-" + DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".xlsx", UseWith = "Desktop\\CIMS_Export\\PR Management\\<คลัง>\\" },
                new TemplateChoiceWindow.Option { Key = "PATH", Title = "📥 EXPORT PATH", Detail = pathStocks.Count == 0 ? "ยังไม่มีคลังที่ตั้งไฟล์ Excel (User Management -> SETUP EXCEL PATH)" : "ต่อข้อมูลลงไฟล์ Excel ของคลัง ตามช่องที่ตั้งไว้ แล้วเปิดไฟล์ให้ตรวจ", UseWith = string.Join("  •  ", pathStocks.Select(s => s.Code)) }
            });
            if (mode == null) return;
            if (mode == "PATH" && pathStocks.Count == 0) { DialogHelper.ShowWarning("ยังไม่มีคลังที่ตั้งไฟล์ Excel\nตั้งได้ที่ User Management -> 📊 SETUP EXCEL PATH (Admin)"); return; }

            // คลังของรายการ: EXPORT = คลังที่เปิดเก็บ PR / EXPORT PATH = คลังที่ตั้งไฟล์ไว้
            var candidates = mode == "PATH" ? pathStocks : (_prStocks.Count > 0 ? _prStocks : new List<StockModel>());
            StockModel stock = null;
            if (candidates.Count > 0)
            {
                stock = StockPickerWindow.ShowChoose(candidates, mode == "PATH" ? "EXPORT PATH" : "EXPORT",
                                                     mode == "PATH" ? "เลือกคลัง: ระบบต่อรายการที่ติ๊กลงไฟล์ Excel ของคลังนั้น" : "เลือกคลัง: ระบบสร้างไฟล์ในโฟลเดอร์ของคลังนั้น");
                if (stock == null) return;
            }
            var items = stock == null ? selected : selected.Where(r => r.StockID == null || r.StockID == stock.StkId).ToList();
            if (items.Count == 0) { DialogHelper.ShowWarning($"ไม่มีรายการที่ติ๊กของคลัง {stock?.Code}"); return; }

            int again = items.Count(r => r.IsExported);
            string uid = _session?.UserId;
            btnExport.IsEnabled = false;
            try
            {
                if (mode == "FILE")
                {
                    if (!DialogHelper.ShowConfirm($"Export PR {items.Count:N0} รายการ ของ {stock?.Code ?? "ทุกคลัง"}" + (again > 0 ? $"\n(เคย Export แล้ว {again:N0} รายการ จะถูก Export ซ้ำ)" : "") + "\n\nยืนยันหรือไม่?", "EXPORT PR")) return;
                    string code = stock?.Code;
                    string path = await Task.Run(() => _export.ExportPRFile(items, code));
                    await MarkExported(items, uid, "EXPORT_PR", System.IO.Path.GetFileName(path));
                    NotificationManager.Show("Export complete", $"Export PR แล้ว {items.Count:N0} รายการ\n{ImportTemplateService.ShortPath(path)}", true);
                    ImportTemplateService.OpenFile(path);
                }
                else
                {
                    var layout = layouts[stock.StkId];
                    string fields = string.Join("  •  ", layout.Entries.Select(x => $"{PRExcelFields.Label(x.Field)} = {x.Target}"));
                    if (!DialogHelper.ShowConfirm($"ต่อข้อมูล PR {items.Count:N0} รายการ ลงไฟล์ของ {stock.Code}" + (again > 0 ? $"\n(เคย Export แล้ว {again:N0} รายการ)" : "") +
                                                  $"\n\n{layout.Path}\n\n{fields}\nเริ่มแถว {layout.StartRow} (มีข้อมูลแล้ว = ต่อจากแถวสุดท้าย)\n\nยืนยันหรือไม่?", "EXPORT PATH")) return;
                    var rows = await Task.Run(() => _export.ExportPRToPath(items, layout));
                    await MarkExported(items, uid, "EXPORT_PR_PATH", $"{stock.Code} -> {System.IO.Path.GetFileName(layout.Path)}" + (rows.First > 0 ? $" rows {rows.First}-{rows.Last}" : ""));
                    NotificationManager.Show("Export complete", $"ต่อข้อมูลลงไฟล์ {stock.Code} แล้ว {items.Count:N0} รายการ" + (rows.First > 0 ? $" (แถว {rows.First} - {rows.Last})" : ""), true);
                    ImportTemplateService.OpenFile(layout.Path);   // เปิดไฟล์ให้ตรวจข้อมูล
                }
                foreach (var r in _rows) r.IsSelected = false;
                Changed = true;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                string msg = ex.Message.Contains("being used") ? "ไฟล์ Excel ถูกเปิดค้างอยู่ กรุณาปิดไฟล์ก่อน Export" : ex.Message;
                DialogHelper.ShowError("Export ไม่สำเร็จ: " + msg);
            }
            finally { btnExport.IsEnabled = true; }
        }

        private async Task MarkExported(List<PRModel> items, string uid, string action, string target)
        {
            await Task.Run(() =>
            {
                foreach (var item in items)
                    if (_service.UpdateAfterExport(item.PRNumber, uid))
                        LogService.WriteLog(uid, action, $"Exported PR No: {item.PRNumber} | {target}", item.PRNumber);
            });
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
