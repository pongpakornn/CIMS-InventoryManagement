using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CIMS.Views
{
    // 🗑 STOCK DELETE (หน้าต่าง ADMIN ของ Inventory Registration - Level 1 เท่านั้น)
    //   เลือกคลัง -> ลบรายการที่ติ๊กในตาราง หรือลบสินค้าทั้งหมดในคลังที่เลือก
    //   ลบสินค้าออกจากระบบทั้งตัว (ทุกคลัง / ประวัติสแกน / Max-Min / Coil) แม้ยังมียอดคงเหลือ - ต้องใส่เหตุผล + ยืนยันอีกครั้ง
    public partial class StockDeletePanel : UserControl
    {
        public class StockCard : INotifyPropertyChanged
        {
            public StockModel Stock { get; set; }
            public string Code => Stock.Code;
            public string Name => Stock.Name;
            public int Count { get; set; }
            public string CountText => $"{Count:N0} PRODUCT(S)";
            private bool _isChecked;
            public bool IsChecked { get => _isChecked; set { _isChecked = value; OnPropertyChanged(); } }
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly UserSession _session;
        private readonly List<int> _tickedIds;
        private readonly List<StockCard> _cards;
        private readonly ProductControlService _service = new ProductControlService();
        public int DeletedCount { get; private set; }

        public StockDeletePanel(UserSession session, List<StockModel> stocks, List<int> tickedPartIds)
        {
            InitializeComponent();
            _session = session;
            _tickedIds = tickedPartIds ?? new List<int>();

            Dictionary<int, int> counts;
            try { counts = _service.GetStockPartCounts(stocks); }
            catch (Exception ex) { counts = new Dictionary<int, int>(); DialogHelper.ShowError("โหลดจำนวนสินค้าในคลังไม่สำเร็จ\n" + ex.Message); }
            _cards = stocks.Select(s => new StockCard { Stock = s, Count = counts.TryGetValue(s.StkId, out int n) ? n : 0 }).ToList();
            icStocks.ItemsSource = _cards;

            rbSelected.Content = $"DELETE THE TICKED PRODUCTS IN THE TABLE ({_tickedIds.Count:N0} ticked)";
            rbSelected.IsEnabled = _tickedIds.Count > 0;
            if (_tickedIds.Count > 0) rbSelected.IsChecked = true; else rbAll.IsChecked = true;
            UpdateSummary();
        }

        // ✨ เปิดแผง = การ์ดคลังไหลเข้าทีละใบ
        private void Panel_Loaded(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(() => Motion.Stagger(icStocks, 40)), System.Windows.Threading.DispatcherPriority.Loaded);

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockCard c) { c.IsChecked = !c.IsChecked; UpdateSummary(); }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e) => UpdateSummary();
        private void Reason_Changed(object sender, TextChangedEventArgs e) => UpdateSummary();

        private List<StockCard> Chosen() => _cards?.Where(c => c.IsChecked).ToList() ?? new List<StockCard>();

        private void UpdateSummary()
        {
            if (txtSummary == null || _cards == null) return;
            var chosen = Chosen();
            if (chosen.Count == 0) txtSummary.Text = "Select at least 1 stock.";
            else if (rbAll.IsChecked == true)
                txtSummary.Text = $"All products in {string.Join(", ", chosen.Select(c => c.Code))} will be deleted from the system (up to {chosen.Sum(c => c.Count):N0} product(s)).";
            else
                txtSummary.Text = $"{_tickedIds.Count:N0} ticked product(s) will be deleted if they are in {string.Join(", ", chosen.Select(c => c.Code))}.";
            btnDelete.IsEnabled = chosen.Count > 0 && !string.IsNullOrWhiteSpace(txtReason.Text) && (rbAll.IsChecked == true || _tickedIds.Count > 0);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.UserLevel != 1) return;
            var chosen = Chosen();
            string reason = (txtReason.Text ?? "").Trim();
            if (chosen.Count == 0 || reason.Length == 0) return;
            bool all = rbAll.IsChecked == true;

            string what = all ? $"สินค้าทั้งหมดในคลัง {string.Join(", ", chosen.Select(c => c.Code))}" : $"สินค้าที่ติ๊กไว้ {_tickedIds.Count:N0} รายการ (เฉพาะที่อยู่ในคลัง {string.Join(", ", chosen.Select(c => c.Code))})";
            if (!DialogHelper.ShowConfirm($"ยืนยันการลบ{what}\n\nสินค้าจะถูกลบออกจากระบบทั้งหมด (ทุกคลัง ประวัติสแกน ค่า Max-Min และ Coil) แม้ยังมียอดคงเหลือ\nลบแล้วกู้คืนไม่ได้\n\nเหตุผล: {reason}", "CONFIRM DELETE"))
                return;

            btnDelete.IsEnabled = false;
            try
            {
                var stocks = chosen.Select(c => c.Stock).ToList();
                var ids = all ? null : _tickedIds.ToList();
                var res = await Task.Run(() => _service.DeleteInStocks(stocks, ids, true));
                var deleted = res.Deleted;
                int count = deleted.Count + res.Removed.Count;
                DeletedCount += count;
                LogService.WriteLog(_session.UserId, "ADMIN_DELETE_PART",
                    $"{(all ? "Delete ALL" : "Delete ticked")} in {string.Join(", ", stocks.Select(s => s.Code))} | Deleted: {deleted.Count} | Removed from stock only: {res.Removed.Count} | Reason: {reason} | Codes: {string.Join(", ", deleted.Concat(res.Removed).Take(100))}{(count > 100 ? " ..." : "")}",
                    string.Join(",", stocks.Select(s => s.Code)));
                if (count == 0) DialogHelper.ShowWarning("ไม่พบสินค้าที่ตรงกับคลังที่เลือก ไม่มีรายการใดถูกลบ");
                else
                {
                    DialogHelper.ShowSuccess($"ลบสินค้าออกจากระบบแล้ว {deleted.Count:N0} รายการ" + (res.Removed.Count > 0 ? $"\nเอาออกเฉพาะคลังที่เลือก (ยังอยู่คลังอื่น) {res.Removed.Count:N0} รายการ" : ""));
                    // จำนวนสินค้าในการ์ดคลังใหม่ / รายการที่ติ๊กถูกลบไปแล้ว
                    var counts = _service.GetStockPartCounts(_cards.Select(c => c.Stock).ToList());
                    foreach (var c in _cards) c.Count = counts.TryGetValue(c.Stock.StkId, out int n) ? n : 0;
                    icStocks.Items.Refresh();
                    if (!all) { _tickedIds.Clear(); rbSelected.Content = "DELETE THE TICKED PRODUCTS IN THE TABLE (0 ticked)"; rbSelected.IsEnabled = false; rbAll.IsChecked = true; }
                    txtReason.Clear();
                }
                UpdateSummary();
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ลบไม่สำเร็จ (ยกเลิกทั้งหมด ไม่มีรายการใดถูกลบ)\n" + ex.Message);
                UpdateSummary();
            }
        }
    }
}
