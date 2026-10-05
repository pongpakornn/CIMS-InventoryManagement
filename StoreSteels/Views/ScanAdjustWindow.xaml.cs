using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CIMS.Views
{
    // ✏️ ADJUST SCAN (Level 1 admin เท่านั้น): แก้จำนวน / ลบ (ยกเลิก) รายการสแกน ยอดคลังปรับย้อนให้อัตโนมัติ
    // ทุกครั้งต้องกรอกหมายเหตุ - รายการที่ลบยังเก็บไว้ (CANCELLED) และบันทึกลง CIMS.ScanAdjustments + Activity Log
    public partial class ScanAdjustWindow : Window
    {
        private readonly ScanAdjustService _service = new ScanAdjustService();
        private readonly UserSession _session;
        private readonly ObservableCollection<ScanAdjustRow> _rows = new ObservableCollection<ScanAdjustRow>();
        private DispatcherTimer _searchTimer;
        private int _version;
        private bool _ready;
        public bool Changed { get; private set; }

        public ScanAdjustWindow(UserSession session, IEnumerable<StockModel> stocks)
        {
            InitializeComponent();
            _session = session;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dgScan.ItemsSource = _rows;
            cbStock.Items.Add(new ComboBoxItem { Content = "ALL STOCKS", Tag = null });
            foreach (var s in stocks) cbStock.Items.Add(new ComboBoxItem { Content = s.Code, Tag = s, ToolTip = s.Name });
            cbStock.SelectedIndex = 0;
            cbType.SelectedIndex = 0;
            dpFrom.SelectedDate = DateTime.Today.AddDays(-7);
            dpTo.SelectedDate = DateTime.Today;
            Loaded += async (s, e) => { _ready = true; await LoadAsync(); txtSearch.Focus(); };
        }

        private async Task LoadAsync()
        {
            if (!_ready) return;
            int ver = ++_version;
            var stock = (cbStock.SelectedItem as ComboBoxItem)?.Tag as StockModel;
            string type = (cbType.SelectedItem as ComboBoxItem)?.Tag as string;
            if (string.IsNullOrEmpty(type)) type = null;
            DateTime from = dpFrom.SelectedDate ?? DateTime.Today, to = dpTo.SelectedDate ?? DateTime.Today;
            if (to < from) { var t = from; from = to; to = t; }
            string key = txtSearch.Text;
            txtStatus.Text = "⏳ LOADING...";
            try
            {
                var list = await Task.Run(() => _service.GetRows(key, from, to, stock?.StkId, stock?.IsMain ?? false, type));
                if (ver != _version) return;
                _rows.Clear();
                foreach (var r in list) _rows.Add(r);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                if (ver != _version) return;
                txtStatus.Text = "";
                DialogHelper.ShowError("โหลดรายการสแกนไม่สำเร็จ\n" + ex.Message);
            }
        }

        private void UpdateStatus()
        {
            int sel = _rows.Count(r => r.IsSelected);
            txtStatus.Text = $"{_rows.Count:N0} SCAN(S){(_rows.Count >= 500 ? " (showing the latest 500 - narrow the filter)" : "")}  •  {sel:N0} SELECTED";
            txtEmpty.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var selectable = _rows.Where(r => r.CanSelect).ToList();
            chkAll.IsChecked = selectable.Count > 0 && selectable.All(r => r.IsSelected);
        }

        private void Filter_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchTimer.Tick += async (s, ev) => { _searchTimer.Stop(); await LoadAsync(); };
            _searchTimer.Start();
        }

        private async void Date_Changed(object sender, SelectionChangedEventArgs e) => await LoadAsync();
        private async void Combo_Changed(object sender, SelectionChangedEventArgs e) => await LoadAsync();

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool on = chkAll.IsChecked == true;
            foreach (var r in _rows.Where(x => x.CanSelect)) r.IsSelected = on;
            UpdateStatus();
        }

        private void Row_Checked(object sender, RoutedEventArgs e) => UpdateStatus();

        private List<ScanAdjustRow> Selected() => _rows.Where(r => r.IsSelected && r.CanSelect).ToList();

        // ✏️ แก้จำนวน: ทีละ 1 รายการ -> กรอกจำนวนใหม่ -> กรอกหมายเหตุ
        private async void EditQty_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.UserLevel != 1) return;
            var sel = Selected();
            if (sel.Count != 1) { DialogHelper.ShowWarning("กรุณาติ๊กเลือก 1 รายการที่ต้องการแก้ไขจำนวน"); return; }
            var row = sel[0];

            int? qty = DialogHelper.ShowQuantityInput(
                $"{row.TxType}  •  {row.StockCode}\n{row.PartCode}  {row.PartName}\n\nจำนวนเดิม {row.Qty:N0}  กรอกจำนวนที่ถูกต้อง", "EDIT SCAN QTY");
            if (qty == null) return;
            if (qty.Value == row.Qty) { DialogHelper.ShowWarning("จำนวนใหม่เท่ากับจำนวนเดิม"); return; }

            string reason = AskReason($"แก้จำนวน {row.PartCode} ({row.TxType} {row.StockCode})\nจาก {row.Qty:N0} เป็น {qty.Value:N0}", "EDIT SCAN QTY");
            if (reason == null) return;

            try
            {
                var res = await Task.Run(() => _service.EditQty(row.TxId, qty.Value, reason, _session.UserId));
                LogService.WriteLog(_session.UserId, "SCAN_ADJUST",
                    $"Edit scan #{row.TxId} {row.TxType} {row.StockCode} {row.PartCode}: {row.Qty} -> {qty.Value} | Balance: {res.BalanceAfter} | Reason: {reason}", row.TxId.ToString());
                Changed = true;
                var msg = new StringBuilder($"แก้ไขจำนวนเรียบร้อย\nยอดคงเหลือ {res.StockCode} : {res.BalanceAfter:N0}");
                if (!string.IsNullOrEmpty(res.PrNo)) msg.Append($"\n\n⚠ รายการนี้มี PR {res.PrNo} ({res.PrStatus}) ผู้มีสิทธิ์ PR ต้องแก้ไขจำนวนใน PR เอง");
                DialogHelper.ShowSuccess(msg.ToString());
                await LoadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("แก้ไขไม่สำเร็จ (ยอด Stock ไม่ถูกเปลี่ยน)\n" + ex.Message); }
        }

        // 🗑 ลบ (ยกเลิก) หลายรายการ: หมายเหตุเดียวใช้กับทุกรายการที่เลือก - รายการที่ปรับไม่ได้ (เช่น ยอดไม่พอ) ข้ามแล้วแจ้ง
        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.UserLevel != 1) return;
            var sel = Selected();
            if (sel.Count == 0) { DialogHelper.ShowWarning("กรุณาติ๊กเลือกรายการที่ต้องการลบ"); return; }

            string list = string.Join("\n", sel.Take(8).Select(r => $"• {r.TxType} {r.StockCode}  {r.PartCode}  x{r.Qty:N0}")) + (sel.Count > 8 ? $"\n... และอีก {sel.Count - 8} รายการ" : "");
            string reason = AskReason($"ลบรายการสแกน {sel.Count:N0} รายการ ยอด Stock จะถูกปรับย้อนกลับ\n\n{list}", "DELETE SCAN");
            if (reason == null) return;

            var ok = new List<ScanAdjustResult>();
            var fails = new List<string>();
            foreach (var row in sel)
            {
                try
                {
                    var res = await Task.Run(() => _service.CancelScan(row.TxId, reason, _session.UserId));
                    ok.Add(res);
                    LogService.WriteLog(_session.UserId, "SCAN_ADJUST",
                        $"Delete scan #{row.TxId} {row.TxType} {row.StockCode} {row.PartCode} x{row.Qty} | Balance: {res.BalanceAfter}{(res.SourceNote != null ? " | " + res.SourceNote : "")} | Reason: {reason}", row.TxId.ToString());
                }
                catch (Exception ex) { fails.Add($"• {row.PartCode} ({row.TxType} {row.StockCode}): {ex.Message}"); }
            }
            if (ok.Count > 0) Changed = true;

            var msg = new StringBuilder($"ลบรายการสแกนแล้ว {ok.Count:N0} รายการ ยอด Stock ปรับย้อนกลับเรียบร้อย");
            var src = ok.Where(r => r.SourceNote != null).Select(r => r.SourceNote).ToList();
            if (src.Count > 0) msg.Append("\n" + string.Join("\n", src));
            var prs = ok.Where(r => !string.IsNullOrEmpty(r.PrNo)).Select(r => $"{r.PrNo} ({r.PrStatus})").ToList();
            if (prs.Count > 0) msg.Append($"\n\n⚠ มี PR ที่สร้างจากการสแกนออกนี้: {string.Join(", ", prs)}\nผู้มีสิทธิ์ PR ต้องไป Reject / แก้ไขเองที่หน้า PR");
            if (fails.Count > 0) msg.Append($"\n\n❌ ลบไม่ได้ {fails.Count:N0} รายการ:\n" + string.Join("\n", fails));
            if (fails.Count > 0 && ok.Count == 0) DialogHelper.ShowError(msg.ToString());
            else if (fails.Count > 0) DialogHelper.ShowWarning(msg.ToString());
            else DialogHelper.ShowSuccess(msg.ToString());
            await LoadAsync();
        }

        // หมายเหตุบังคับกรอก (กดยกเลิก = null)
        private static string AskReason(string what, string title)
        {
            while (true)
            {
                string reason = DialogHelper.ShowTextInput(what + "\n\nกรุณาระบุหมายเหตุ / สาเหตุที่แก้ไข Stock", title, "CONFIRM", isDanger: true);
                if (reason == null) return null;
                if (!string.IsNullOrWhiteSpace(reason)) return reason.Trim();
                DialogHelper.ShowWarning("กรุณาระบุหมายเหตุ / สาเหตุที่แก้ไข Stock ก่อนยืนยัน");
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
