using CIMS.Helpers;
using CIMS.Models;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.Views
{
    // หน้าต่างการ์ดคลัง (ดูคำอธิบาย 3 แบบใน StockPickerWindow.xaml)
    public partial class StockPickerWindow : Window
    {
        public enum PickMode { ScanStocks, Choose, PrSettings }

        private readonly PickMode _mode;
        private readonly List<PickItem> _items;
        private bool _auto;

        // ผลลัพธ์
        public bool IsAuto => _auto;
        public List<int> CheckedIds => _items.Where(i => i.IsChecked).Select(i => i.Stock.StkId).ToList();
        public int? PriorityId => _items.FirstOrDefault(i => i.IsPriority)?.Stock.StkId;
        public StockModel Chosen { get; private set; }

        private StockPickerWindow(PickMode mode, IEnumerable<StockModel> stocks)
        {
            InitializeComponent();
            _mode = mode;
            _items = stocks.Select(s => new PickItem(s, mode)).ToList();
            icStocks.ItemsSource = _items;

            var owner = Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible && owner != this) Owner = owner;
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        #region === [ เปิดหน้าต่างแต่ละแบบ ] ===

        // เลือกคลังที่สแกนร่วมกัน: คืน true ถ้ากด SAVE
        public static StockPickerWindow ShowScanStocks(IEnumerable<StockModel> stocks, bool auto, ICollection<int> checkedIds, int? priorityId)
        {
            var w = new StockPickerWindow(PickMode.ScanStocks, stocks);
            w.txtTitle.Text = "SCAN STOCKS";
            w.txtSubtitle.Text = "Select one or more stocks to scan together, or AUTO for every stock.  ★ = stock used first for SCAN OUT";
            w.cardAuto.Visibility = Visibility.Visible;
            w._auto = auto;
            foreach (var i in w._items)
            {
                i.IsChecked = !auto && checkedIds != null && checkedIds.Contains(i.Stock.StkId);
                i.IsPriority = priorityId.HasValue && i.Stock.StkId == priorityId.Value;
            }
            w.txtHint.Text = "Several stocks: the system finds the stock of each scanned item. If an item is in more than one of them, " +
                             "SCAN IN asks which stock, SCAN OUT uses the ★ stock (or asks when the ★ stock does not have it).";
            w.txtHint.Visibility = Visibility.Visible;
            w.RefreshAuto();
            return w.ShowDialog() == true ? w : null;
        }

        // สินค้าอยู่หลายคลัง -> ให้เลือก 1 คลัง (null = ยกเลิก)
        public static StockModel ShowChoose(IEnumerable<StockModel> candidates, string title, string message)
        {
            var w = new StockPickerWindow(PickMode.Choose, candidates);
            w.txtTitle.Text = title;
            w.txtSubtitle.Text = message;
            w.btnSave.Visibility = Visibility.Collapsed;
            return w.ShowDialog() == true ? w.Chosen : null;
        }

        // PR SETTINGS: คืน map STK_ID -> เปิด/ปิด (null = ยกเลิก)
        public static Dictionary<int, bool> ShowPrSettings(IEnumerable<StockModel> stocks, IDictionary<int, bool> current)
        {
            var w = new StockPickerWindow(PickMode.PrSettings, stocks);
            w.txtTitle.Text = "PR SETTINGS";
            w.txtSubtitle.Text = "Tick the stocks that create a PR automatically when an item is scanned OUT";
            foreach (var i in w._items)
                i.IsChecked = current != null && current.TryGetValue(i.Stock.StkId, out bool on) && on;
            w.txtHint.Text = "Stocks not ticked can still scan OUT normally - they just do not create a PR. Manual PRs are not affected.";
            w.txtHint.Visibility = Visibility.Visible;
            return w.ShowDialog() == true ? w._items.ToDictionary(i => i.Stock.StkId, i => i.IsChecked) : null;
        }

        #endregion

        #region === [ Events ] ===

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is PickItem item)) return;

            if (_mode == PickMode.Choose)
            {
                Chosen = item.Stock;
                DialogResult = true;
                Close();
                return;
            }

            if (_mode == PickMode.ScanStocks && _auto)
            {
                // กดการ์ดคลังตอนเปิด AUTO อยู่ = เปลี่ยนมาเลือกเองทีละคลัง
                _auto = false;
                foreach (var i in _items) i.IsChecked = false;
                RefreshAuto();
            }
            item.IsChecked = !item.IsChecked;
        }

        private void CardAuto_Click(object sender, MouseButtonEventArgs e)
        {
            _auto = !_auto;
            if (_auto) foreach (var i in _items) i.IsChecked = false;
            RefreshAuto();
        }

        private void Priority_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is PickItem item)) return;
            bool on = !item.IsPriority;
            foreach (var i in _items) i.IsPriority = false;
            item.IsPriority = on;
            e.Handled = true;
        }

        private void RefreshAuto()
        {
            cardAuto.BorderBrush = _auto ? (Brush)FindResource("AccentPurple") : new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xF1));
            autoCheck.Background = _auto ? (Brush)FindResource("AccentPurple") : new SolidColorBrush(Color.FromRgb(0xE4, 0xDD, 0xF0));
            autoCheckText.Text = _auto ? "✓" : "";
            foreach (var i in _items) i.Dimmed = _auto;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == PickMode.ScanStocks && !_auto && !_items.Any(i => i.IsChecked))
            {
                DialogHelper.ShowWarning("กรุณาเลือกคลังอย่างน้อย 1 คลัง หรือเลือก AUTO (ALL STOCKS)");
                return;
            }
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        #endregion

        // ข้อมูลการ์ด 1 ใบ
        public class PickItem : INotifyPropertyChanged
        {
            private readonly PickMode _mode;
            public StockModel Stock { get; }

            public PickItem(StockModel stock, PickMode mode) { Stock = stock; _mode = mode; }

            private bool _isChecked;
            public bool IsChecked { get => _isChecked; set { _isChecked = value; Notify(); Notify(nameof(CheckBrush)); Notify(nameof(CheckText)); } }

            private bool _isPriority;
            public bool IsPriority { get => _isPriority; set { _isPriority = value; Notify(); Notify(nameof(PriorityText)); Notify(nameof(PriorityForeground)); } }

            private bool _dimmed;
            public bool Dimmed { get => _dimmed; set { _dimmed = value; Notify(); Notify(nameof(CardOpacity)); } }

            public double CardOpacity => Dimmed ? 0.55 : 1;
            public Brush AccentBrush => Stock.IsMain ? (Brush)Application.Current.FindResource("MainPurple") : (Brush)Application.Current.FindResource("AccentPurple");
            public Brush CheckBrush => IsChecked ? (Brush)Application.Current.FindResource("AccentPurple") : new SolidColorBrush(Color.FromRgb(0xE4, 0xDD, 0xF0));
            public string CheckText => IsChecked ? "✓" : "";
            public Visibility CheckVisibility => _mode == PickMode.Choose ? Visibility.Collapsed : Visibility.Visible;
            public Visibility MainVisibility => Stock.IsMain ? Visibility.Visible : Visibility.Collapsed;

            public Visibility PriorityButtonVisibility => _mode == PickMode.ScanStocks && Stock.CanScanOut ? Visibility.Visible : Visibility.Collapsed;
            public string PriorityText => IsPriority ? "★ OUT FIRST" : "☆ OUT FIRST";
            public Brush PriorityForeground => IsPriority ? new SolidColorBrush(Color.FromRgb(0xE6, 0x8A, 0x00)) : new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x90));

            public event PropertyChangedEventHandler PropertyChanged;
            private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
