using CIMS.Models;
using CIMS.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    public partial class DashboardView : Page
    {
        private DashboardViewModel _viewModel;
        public UserSession CurrentUser { get; private set; }

        public DashboardView()
        {
            InitializeComponent();
            _viewModel = new DashboardViewModel();
            this.DataContext = _viewModel;

            // ออกจากหน้า Dashboard แล้วหยุดรีเฟรช (เดิมตัวจับเวลาทำงานค้างอยู่เบื้องหลังตลอด)
            this.Loaded += (s, e) => _viewModel.StartRefresh();
            this.Unloaded += (s, e) => _viewModel.StopRefresh();

            RunEntryAnimation();
        }

        public DashboardView(UserSession session) : this()
        {
            this.CurrentUser = session;
            _viewModel.SetUser(session);
        }

        private void RunEntryAnimation()
        {
            DoubleAnimation fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(0.6) };
            this.BeginAnimation(Page.OpacityProperty, fadeIn);
        }

        #region === [ การ์ดแยกตามคลัง: คลิกขวา / ดับเบิ้ลคลิก -> เลือกคลังเข้าไปดู ] ===

        // สร้างรายการคลังในเมนูคลิกขวาตามการ์ดนั้น (Stock / Product All / Max / Min)
        private void StockMenu_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is Border border && border.DataContext is DashboardSummaryCard card && border.ContextMenu is ContextMenu menu)
                FillMenu(menu, card);
        }

        private void FillMenu(ContextMenu menu, DashboardSummaryCard card)
        {
            menu.Items.Clear();
            if (card.Rows.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "❌ ไม่พบคลังที่มีสิทธิ์ดู", IsEnabled = false });
                return;
            }

            foreach (var row in card.Rows)
            {
                string right = card.Kind == "STOCK" ? row.Name : $"{row.Count:N0} ITEMS";
                var item = new MenuItem { Header = $"🏬 {row.Code}   •   {right}", Tag = row.Stock };
                item.Click += (s, ev) => OpenStock(row.Stock, card.Kind);
                menu.Items.Add(item);
            }
        }

        // ดับเบิ้ลคลิกที่การ์ด = เปิดเมนูเลือกคลังเหมือนคลิกขวา
        private void Card_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
            if (sender is Border border && border.DataContext is DashboardSummaryCard card && border.ContextMenu is ContextMenu menu)
            {
                FillMenu(menu, card);
                menu.PlacementTarget = border;
                menu.Placement = PlacementMode.MousePoint;
                menu.IsOpen = true;
            }
        }

        // Stock / Product All = ทุกรายการของคลัง, Max = เฉพาะสถานะ MAX, Min = เฉพาะสถานะ MIN
        private void OpenStock(StockModel stock, string kind)
        {
            string filter = kind == "MAX" ? "OVER_MAX" : kind == "MIN" ? "UNDER_MIN" : "";
            var page = new StoreMaxMinView(CurrentUser, stock, filter);
            if (Window.GetWindow(this) is MainView mainWindow)
                mainWindow.NavigateToPage(page, "STORE ( MAX-MIN )");
            else
                this.NavigationService?.Navigate(page);
        }

        #endregion
    }
}
