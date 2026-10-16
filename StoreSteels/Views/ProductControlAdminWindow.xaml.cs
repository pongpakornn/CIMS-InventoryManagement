using CIMS.Helpers;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    // 🛠 ADMIN ของหน้า Inventory Registration (Level 1 เท่านั้น) - เปิดจากปุ่ม ADMIN
    //   Sidebar: STOCK DELETE / TABLE COLUMNS (คอลัมน์ + จัดกลุ่มของตารางแต่ละคลัง) / QR CODE (รูปแบบ QR ที่ Export ต่อคลัง)
    public partial class ProductControlAdminWindow : Window
    {
        private readonly UserSession _session;
        private readonly List<StockModel> _stocks;
        private readonly List<int> _ticked;
        private StockDeletePanel _delete;
        private TableLayoutPanel _columns;
        private QrFormatPanel _qr;
        private bool _open = true;

        public int DeletedCount => _delete?.DeletedCount ?? 0;
        // หน้า QR CODE ไม่กระทบตาราง (EXPORT QR SELECT อ่านค่าล่าสุดทุกครั้ง)
        public bool LayoutChanged => _columns?.Saved == true;

        public ProductControlAdminWindow(UserSession session, List<StockModel> stocks, List<int> tickedPartIds)
        {
            InitializeComponent();
            _session = session;
            _stocks = stocks ?? new List<StockModel>();
            _ticked = tickedPartIds ?? new List<int>();
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // จอเล็ก: ไม่ให้หน้าต่างล้นจอ
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            navDelete.IsChecked = true;
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (host == null) return;
            try
            {
                if (sender == navDelete) { host.Content = _delete ??= new StockDeletePanel(_session, _stocks, _ticked); txtTitle.Text = "STOCK DELETE"; }
                else if (sender == navColumns) { host.Content = _columns ??= new TableLayoutPanel(_session, _stocks); txtTitle.Text = "TABLE COLUMNS"; }
                else if (sender == navQr) { host.Content = _qr ??= new QrFormatPanel(_session, _stocks); txtTitle.Text = "QR CODE"; }
            }
            catch (Exception ex) { DialogHelper.ShowError("เปิดหน้าไม่สำเร็จ\n" + ex.Message); }
        }

        // ☰ พับ Sidebar เหลือแต่ไอคอน / กางออก (แบบเดียวกับหน้าหลัก)
        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            _open = !_open;
            Sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(_open ? 240 : 64, TimeSpan.FromSeconds(0.25)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            var vis = _open ? Visibility.Visible : Visibility.Collapsed;
            spBrand.Visibility = vis; lblDelete.Visibility = vis; lblColumns.Visibility = vis; lblQr.Visibility = vis; txtLevel.Visibility = vis;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => TryClose();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) TryClose(); }

        private void TryClose()
        {
            if ((_columns?.HasUnsaved == true || _qr?.HasUnsaved == true) &&
                !DialogHelper.ShowConfirm("มีการตั้งค่าที่ยังไม่ได้กด SAVE\nต้องการปิดหน้าต่างโดยไม่บันทึกหรือไม่?", "UNSAVED CHANGES")) return;
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
