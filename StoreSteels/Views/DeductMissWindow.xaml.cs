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
    // ⚠ NOT DEDUCTED - ป้ายที่ยังไม่รับเข้าคลัง (รอดำเนินการ) / ✔ DEDUCTED - เพิ่มอะไรเข้ามาแล้วตัดอะไรจากคลังต้นทาง
    //    ค้นหาเบื้องหลัง (ผลเก่าที่มาช้าถูกทิ้ง) / ช่วงวันที่ / ติ๊กเลือกแล้ว EXPORT
    //    ACTION (APPROVE ของ Multi-Scanner): ✔ APPROVE รับเข้า / ＋ PRODUCT ลงทะเบียนแล้วรับเข้า / 🗑 DEL ลบ (เก็บ Log)
    public partial class DeductMissWindow : Window
    {
        private readonly DeductMissService _service = new DeductMissService();
        private readonly UserSession _session;
        private readonly ObservableCollection<DeductMissRow> _rows = new ObservableCollection<DeductMissRow>();
        private DispatcherTimer _searchTimer;
        private int _version;
        private bool _ready, _busy;
        public bool Changed { get; private set; }

        private readonly bool _deducted;
        private readonly List<int> _stockIds;   // คลังที่ผู้ใช้เห็นปุ่มนี้ (null = ทุกคลัง)
        private readonly bool _canAct;           // ช่อง ACTION

        public DeductMissWindow(UserSession session, bool deducted = false, IEnumerable<int> stockIds = null)
        {
            _session = session;
            _deducted = deducted;
            _stockIds = stockIds?.ToList();
            _canAct = !deducted && session?.CanApproveScan == true && DbSchema.HasMissStatus;
            InitializeComponent();
            if (deducted)
            {
                Title = "Deducted";
                hdrBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
                txtTitle.Text = "✔ DEDUCTED";
                txtSub.Text = "What came in (PD CODE / coil) and what was cut from the source stock (source product / mother coil before / source balance)";
                txtSub.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xE6, 0xC9));
                txtEmpty.Text = "NO DEDUCTION IN THIS RANGE";
                colCut.Visibility = colSrcPart.Visibility = colSrcName.Visibility = colMotherBefore.Visibility = colSrcAfter.Visibility = Visibility.Visible;
                colScanned.Visibility = colStatus.Visibility = Visibility.Collapsed;
            }
            colAction.Visibility = _canAct ? Visibility.Visible : Visibility.Collapsed;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            // ทุกขนาดจอ: ไม่ล้นจอ (จอเล็กตารางเลื่อนซ้าย-ขวาได้)
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width, area.Width - 10);
            Height = Math.Min(Height, area.Height - 10);
            dgMiss.ItemsSource = _rows;
            dpFrom.SelectedDate = _deducted ? DateTime.Today : DateTime.Today.AddDays(-60);
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
                var list = await Task.Run(() => _deducted ? _service.GetDeducted(key, from, to, _stockIds) : _service.GetRows(key, from, to, _stockIds));
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
                NotificationManager.Show(_deducted ? "Deducted" : "Not deducted", "โหลดรายการไม่สำเร็จ: " + ex.Message, false);
            }
        }

        private void UpdateStatus()
        {
            int sel = _rows.Count(r => r.IsSelected);
            txtStatus.Text = _deducted
                ? $"{_rows.Count:N0} LABELS  •  CUT FROM SOURCE {Qty.Plain(_rows.Sum(r => r.Deducted))} KG  •  BY MOTHER COIL {_rows.Count(r => r.How == "MOTHER COIL"):N0}  •  NOT CUT YET {_rows.Count(r => r.How == "NONE"):N0}  •  {sel:N0} SELECTED"
                : $"{_rows.Count:N0} LABELS  •  WAITING {_rows.Count(r => r.IsPending):N0} ({Qty.Plain(_rows.Where(r => r.IsPending).Sum(r => r.LabelQty))} KG NOT IN STOCK)  •  NO PD CODE {_rows.Count(r => !r.HasProduct):N0}  •  MOTHER COIL NOW IN SOURCE {_rows.Count(r => r.MotherNow.HasValue):N0}  •  {sel:N0} SELECTED" +
                  (_canAct ? "" : "  •  ACTION = APPROVE of Multi-Scanner");
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

        // ===== ACTION (APPROVE ของ Multi-Scanner) =====

        // ✔ APPROVE: รับเข้าคลังตามป้าย + ตัด Coil แม่ในคลังต้นทางเท่าที่ตัดได้ตอนนี้ -> ไปแสดงที่ DEDUCTED
        private async void Approve_Click(object sender, RoutedEventArgs e)
        {
            if (!_canAct || _busy || !((sender as FrameworkElement)?.Tag is DeductMissRow r)) return;
            if (!r.HasProduct) { DialogHelper.ShowWarning("ยังไม่มี PD CODE ของป้ายนี้ในระบบ\nกด ＋ PRODUCT เพื่อลงทะเบียนสินค้าก่อน"); return; }
            string what = r.Status == "RECEIVED"
                ? $"รายการเดิม: รับเข้า {r.StockCode} ไปแล้ว\nAPPROVE = ตัด {r.SourceCode} ตามหลัง"
                : $"รับเข้า {r.StockCode}  {r.PartCode}  {r.LabelQtyText}";
            string cut = r.MotherNow.HasValue ? $"ตัด Coil แม่ {r.MotherCoil} ใน {r.SourceCode} (มี {Qty.Plain(r.MotherNow.Value)} KG)"
                                              : $"Coil แม่ {r.MotherCoil} ยังไม่มีใน {r.SourceCode} • ยังไม่ตัด";
            if (!DialogHelper.ShowConfirm($"{what}\nCoil {r.CoilNo}\n\n{cut}\n\nยืนยัน APPROVE หรือไม่?", "APPROVE")) return;
            _busy = true;
            try
            {
                string uid = _session?.UserId;
                var res = await Task.Run(() => _service.Receive(r, r.PartId.Value, r.LabelQty, uid, "APPROVED"));
                if (!res.Ok) { DialogHelper.ShowError(res.Message); return; }
                var d = res.Deduct;
                NotificationManager.Show("Approved", $"{r.PartCode}  {r.LabelQtyText} → {r.StockCode}\n" +
                    (d.Deducted > 0 ? $"ตัด {d.SourceCode} {Qty.Plain(d.Deducted)} ({d.SourcePartCode} เหลือ {Qty.Plain(d.SourceAfter)})" : $"ยังไม่ได้ตัด {r.SourceCode}") + "\nย้ายไปที่ DEDUCTED แล้ว", true);
                Changed = true;
                await LoadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("APPROVE ไม่สำเร็จ\n" + ex.Message); }
            finally { _busy = false; }
        }

        // ＋ PRODUCT: หน้าต่าง Inventory Registration (ข้อมูลจากป้าย) -> ลงทะเบียน / เลือกสินค้าที่มี -> รับเข้า -> ไปแสดงที่ DEDUCTED
        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            if (!_canAct || _busy || !((sender as FrameworkElement)?.Tag is DeductMissRow r)) return;
            if (r.Status == "RECEIVED") { DialogHelper.ShowWarning($"รายการเดิมนี้รับเข้า {r.StockCode} ไปแล้ว (มี PD CODE แล้ว)\nใช้ ✔ APPROVE เพื่อตัด {r.SourceCode} ตามหลัง"); return; }
            var w = new NotDeductedRegisterWindow(_session, r);
            w.ShowDialog();
            if (w.Done) { Changed = true; await LoadAsync(); }
        }

        // 🗑 DEL: ยืนยันก่อน -> ลบรายการ (ไม่บันทึกเข้าคลัง) / รายการเดิมที่รับเข้าไปแล้ว = ยกเลิกรายการสแกนนั้น (คืนยอด) - เก็บ Log
        private async void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (!_canAct || _busy || !((sender as FrameworkElement)?.Tag is DeductMissRow r)) return;
            string extra = r.Status == "RECEIVED"
                ? $"\n\nรายการเดิมนี้รับเข้า {r.StockCode} ไปแล้ว {r.LabelQtyText}\nลบ = ยกเลิกรายการสแกนนั้น ยอด {r.StockCode} ลดลงตามป้าย"
                : "\n\nป้ายนี้ยังไม่ได้เข้าคลัง - ลบแล้วไม่มีการบันทึกเข้าคลังใดๆ";
            if (!DialogHelper.ShowConfirm($"ต้องการลบรายการนี้จริงๆ ใช่หรือไม่?\n\n{r.PartCodeText}  {r.PartNameText}\nCoil {r.CoilNo}  •  Mother {r.MotherCoil}  •  {r.LabelQtyText}{extra}\n\nลบแล้วกู้คืนไม่ได้", "DELETE")) return;
            _busy = true;
            try
            {
                string uid = _session?.UserId;
                var res = await Task.Run(() => _service.Delete(r, uid));
                if (!res.Ok) { DialogHelper.ShowError(res.Message); return; }
                NotificationManager.Show("Deleted", $"ลบแล้ว  Coil {r.CoilNo}", true);
                Changed = true;
                await LoadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("ลบไม่สำเร็จ\n" + ex.Message); }
            finally { _busy = false; }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
