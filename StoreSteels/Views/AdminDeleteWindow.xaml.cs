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
using System.Windows.Input;

namespace CIMS.Views
{
    // 🗑 ADMIN DELETE (Level 1 เท่านั้น) หน้า Product Control
    //   เลือกคลัง -> ลบรายการที่ติ๊กในตาราง หรือลบสินค้าทั้งหมดในคลังที่เลือก
    //   ลบสินค้าออกจากระบบทั้งตัว (ทุกคลัง / ประวัติสแกน / Max-Min) แม้ยังมียอดคงเหลือ - ต้องใส่เหตุผล + ยืนยันอีกครั้ง
    public partial class AdminDeleteWindow : Window
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

        public AdminDeleteWindow(UserSession session, List<StockModel> stocks, List<int> tickedPartIds)
        {
            InitializeComponent();
            _session = session;
            _tickedIds = tickedPartIds ?? new List<int>();
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;

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

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockCard c) { c.IsChecked = !c.IsChecked; UpdateSummary(); }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e) => UpdateSummary();
        private void Reason_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateSummary();

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
            if (!DialogHelper.ShowConfirm($"ยืนยันการลบ{what}\n\nสินค้าจะถูกลบออกจากระบบทั้งหมด (ทุกคลัง ประวัติสแกน และค่า Max-Min) แม้ยังมียอดคงเหลือ\nลบแล้วกู้คืนไม่ได้\n\nเหตุผล: {reason}", "CONFIRM ADMIN DELETE"))
                return;

            btnDelete.IsEnabled = false;
            try
            {
                var stocks = chosen.Select(c => c.Stock).ToList();
                var ids = all ? null : _tickedIds.ToList();
                var deleted = await Task.Run(() => _service.AdminDeleteParts(stocks, ids));
                DeletedCount = deleted.Count;
                LogService.WriteLog(_session.UserId, "ADMIN_DELETE_PART",
                    $"{(all ? "Delete ALL" : "Delete ticked")} in {string.Join(", ", stocks.Select(s => s.Code))} | Deleted: {deleted.Count} | Reason: {reason} | Codes: {string.Join(", ", deleted.Take(100))}{(deleted.Count > 100 ? " ..." : "")}",
                    string.Join(",", stocks.Select(s => s.Code)));
                if (deleted.Count == 0) DialogHelper.ShowWarning("ไม่พบสินค้าที่ตรงกับคลังที่เลือก ไม่มีรายการใดถูกลบ");
                else DialogHelper.ShowSuccess($"ลบสินค้าออกจากระบบแล้ว {deleted.Count:N0} รายการ");
                DialogResult = deleted.Count > 0;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ลบไม่สำเร็จ (ยกเลิกทั้งหมด ไม่มีรายการใดถูกลบ)\n" + ex.Message);
                btnDelete.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
