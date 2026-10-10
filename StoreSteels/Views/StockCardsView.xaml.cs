using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    // หน้าแรกของ Store ( Max - Min ): การ์ดคลังทั้งหมด (จำนวนรายการสินค้า + หน่วย) กดเพื่อเข้าตารางของคลังนั้น
    public partial class StockCardsView : Page
    {
        private readonly UserSession _session;
        private readonly StockService _stockService = new StockService();

        public StockCardsView(UserSession session)
        {
            InitializeComponent();
            _session = session;

            btnAddStock.Visibility = _session.CanCreateStock ? Visibility.Visible : Visibility.Collapsed;
            btnFormats.Visibility = _session.CanManageFormats ? Visibility.Visible : Visibility.Collapsed;

            Loaded += async (s, e) => await LoadStocksAsync();
            RunEntryAnimation();

            // ⚡ เรียลไทม์: เครื่องอื่นเพิ่ม / แก้ / ลบคลัง -> การ์ดอัพเดทเอง
            LiveRefresh.Attach(this, TimeSpan.FromSeconds(3),
                () => LiveRefresh.DbToken("SELECT COUNT(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM CIMS.Stocks"),
                () => LoadStocksAsync(true));
        }

        // ข้อมูลที่การ์ด 1 ใบใช้ (คลัง + ปุ่มที่ผู้ใช้คนนี้มีสิทธิ์)
        public class StockCard
        {
            public StockModel Stock { get; set; }
            public Visibility EditVisibility { get; set; }
            public Visibility DeleteVisibility { get; set; }
            public Visibility MainBadgeVisibility => Stock.IsMain ? Visibility.Visible : Visibility.Collapsed;
            public Brush AccentBrush => (Brush)Application.Current.FindResource(Stock.IsMain ? "MainPurple" : "AccentPurple");
        }

        private async Task LoadStocksAsync(bool live = false)
        {
            try
            {
                var stocks = await Task.Run(() => _stockService.GetStocks());
                var visible = stocks.Where(s => _session.CanViewStock(s)).ToList();

                icStocks.ItemsSource = visible.Select(s => new StockCard
                {
                    Stock = s,
                    EditVisibility = _session.CanEditStore ? Visibility.Visible : Visibility.Collapsed,
                    DeleteVisibility = _session.CanDeleteStock(s) ? Visibility.Visible : Visibility.Collapsed
                }).ToList();

                txtSubtitle.Text = $"SELECT A STOCK  •  {visible.Count} STOCK(S)";
                txtEmpty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (live) throw;   // รอบเรียลไทม์: ไม่ขึ้นหน้าต่าง Error (ลองใหม่รอบหน้า)
                DialogHelper.ShowError("ไม่สามารถโหลดรายชื่อคลังได้\n" + ex.Message);
            }
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockCard card)
            {
                var main = Window.GetWindow(this) as MainView;
                main?.NavigateToPage(new StoreMaxMinView(_session, card.Stock), "STORE ( MAX-MIN )");
            }
        }

        private async void AddStock_Click(object sender, RoutedEventArgs e)
        {
            if (!_session.CanCreateStock)
            {
                DialogHelper.ShowWarning("คุณไม่มีสิทธิ์สร้างคลังใหม่", "ACCESS DENIED");
                return;
            }

            var editor = new StockEditorWindow(_session, null) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
            {
                _session.GrantViewInSession(editor.Result);
                NotificationManager.Show("Stock", $"สร้างคลัง {editor.Result.Code} สำเร็จ", true);
                await LoadStocksAsync();
            }
        }

        private async void EditStock_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is StockCard card)) return;
            if (!_session.CanEditStore)
            {
                DialogHelper.ShowWarning("คุณไม่มีสิทธิ์แก้ไขคลังนี้", "ACCESS DENIED");
                return;
            }

            var editor = new StockEditorWindow(_session, card.Stock) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
            {
                NotificationManager.Show("Stock", $"บันทึกการตั้งค่าคลัง {editor.Result.Code} สำเร็จ", true);
                await LoadStocksAsync();
            }
        }

        private async void DeleteStock_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is StockCard card)) return;
            var stock = card.Stock;

            if (!_session.CanDeleteStock(stock))
            {
                DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ลบคลังนี้", "ACCESS DENIED");
                return;
            }

            string reason = DialogHelper.ShowTextInput(
                $"คลัง {stock.Code} ({stock.Name}) มีสินค้า {stock.ItemCount:N0} รายการ\n" +
                "ข้อมูลยอดคงเหลือและประวัติแสกนของคลังนี้จะถูกลบทั้งหมด\n\nกรุณาระบุเหตุผลในการลบคลัง",
                "DELETE STOCK", "DELETE", isDanger: true);
            if (reason == null) return;

            if (!DialogHelper.ShowConfirm($"ยืนยันการลบคลัง {stock.Code} ใช่หรือไม่?\nไม่สามารถกู้คืนข้อมูลได้\n\nเหตุผล: {reason}", "CONFIRM DELETE"))
                return;

            try
            {
                await Task.Run(() => _stockService.DeleteStock(stock.StkId, reason, _session.UserId));
                LogService.WriteLog(_session.UserId, "STOCK_DELETE", $"Deleted stock {stock.Code} ({stock.Name}) | Reason: {reason}", stock.Code);
                NotificationManager.Show("Stock", $"ลบคลัง {stock.Code} เรียบร้อยแล้ว", true);
                await LoadStocksAsync();
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ลบคลังไม่สำเร็จ\n" + ex.Message);
            }
        }

        private void Formats_Click(object sender, RoutedEventArgs e)
        {
            new BarcodeFormatWindow(_session) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void RunEntryAnimation()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5)));
            PageTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(30, 0, TimeSpan.FromSeconds(0.5)) { EasingFunction = ease });
        }
    }
}
