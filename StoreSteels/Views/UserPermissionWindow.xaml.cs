using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.Views
{
    // 🔑 สิทธิ์ของผู้ใช้ทุกระบบ + ทุกคลัง / readOnly = ปุ่ม VIEW (ดูข้อมูลผู้ใช้ + สิทธิ์ แก้ไม่ได้)
    //    แถว Multi-Scanner กดขยาย = คลังที่ UserID นี้สแกนได้ + ★ + DISPLAY (Update_20261015.sql) - คนเปิดหน้าต่างบันทึกหลังกด SAVE
    public partial class UserPermissionWindow : Window
    {
        public List<UserPermRow> Rows { get; }

        private readonly UserAccount _user;
        private readonly UserSession _session;
        private readonly ScannerSetup _scanner;   // null = ฐานยังไม่รัน Update_20261015.sql

        public bool HasScannerSetup => _scanner != null;
        public List<int> ScanStockIds => _scanner?.Items.Where(i => i.IsChecked).Select(i => i.Stock.StkId).ToList() ?? new List<int>();
        public int? ScanPriorityId => _scanner?.Items.FirstOrDefault(i => i.IsChecked && i.IsPriority)?.Stock.StkId;
        public List<int> ScanDeductIds => _scanner?.Items.Where(i => i.IsChecked && i.IsDeduct).Select(i => i.Stock.StkId).ToList() ?? new List<int>();
        public bool ScanStocksChanged => _scanner != null &&
            (!ScanStockIds.OrderBy(x => x).SequenceEqual(_scanner.OriginalIds.OrderBy(x => x)) || ScanPriorityId != _scanner.OriginalPriority ||
             !ScanDeductIds.OrderBy(x => x).SequenceEqual(_scanner.OriginalDeduct.OrderBy(x => x)));
        public Dictionary<int, string> DisplayChanges => _scanner?.Items.Where(i => i.IsChecked && i.SelectedDisplay != i.OriginalDisplay)
                                                             .ToDictionary(i => i.Stock.StkId, i => i.SelectedDisplay) ?? new Dictionary<int, string>();

        public UserPermissionWindow(UserAccount user, List<UserPermRow> rows, bool readOnly, UserSession session = null)
        {
            InitializeComponent();
            Rows = rows;
            _user = user;
            _session = session;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }

            txtTitle.Text = readOnly ? "👤 USER PROFILE" : "🔑 PERMISSION";
            txtUser.Text = $"{user.UserId}  •  {user.Name}  •  {user.LevelText}";
            pnlLevel1.Visibility = user.Level == 1 ? Visibility.Visible : Visibility.Collapsed;

            if (readOnly)
            {
                foreach (var (label, value) in new[] {
                    ("USER ID", user.UserId), ("NAME", user.Name), ("POSITION", user.Position), ("UNIT", user.Department),
                    ("SECTION", user.Section), ("DIVISION", user.Division), ("LEVEL", user.LevelText), ("STATUS", user.StatusText),
                    ("LAST LOGIN", user.LastLogin?.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "-") })
                {
                    var b = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF5, 0xFA)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8), MinWidth = 140 };
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x90)) });
                    sp.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(value) ? "-" : value, FontSize = 13, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2A, 0x32)) });
                    b.Child = sp;
                    pnlProfile.Children.Add(b);
                }
                icPerms.IsEnabled = false;
                pnlQuick.Visibility = Visibility.Collapsed;
                btnSave.Visibility = Visibility.Collapsed;
                btnCancel.Content = "CLOSE";
                btnAlert.Visibility = Visibility.Collapsed;
            }

            _scanner = LoadScannerSetup(readOnly);
            var msRow = Rows.FirstOrDefault(r => string.Equals(r.SysId, "MultiScanner", StringComparison.OrdinalIgnoreCase));
            if (msRow != null) msRow.Scanner = _scanner;
            ShowAlertContacts();
            Refresh();
        }

        private ScannerSetup LoadScannerSetup(bool readOnly)
        {
            if (!DbSchema.HasUserScanStocks) return null;
            try
            {
                // คลังที่เปิดช่องทางสแกน (รับเข้า / จ่ายออก) อย่างน้อย 1 ช่องทาง
                var all = new StockService().GetStocks();
                var stocks = all.Where(s => s.CanScanIn || s.CanScanOut).ToList();
                var a = new ScannerSetupService().GetAssignment(_user.UserId);
                var map = new ScanService().GetScanDisplayMap(_user.UserId);
                var formats = new StockService().GetFormats();
                var eligible = DbSchema.HasScanDeduct ? ScannerSetupService.DeductStockIds(all, formats) : new HashSet<int>();
                var setup = new ScannerSetup
                {
                    OriginalIds = a.StockIds.Where(id => stocks.Any(s => s.StkId == id)).ToList(),
                    OriginalPriority = a.PriorityId,
                    OriginalDeduct = a.DeductIds.Where(id => eligible.Contains(id) && stocks.Any(s => s.StkId == id)).ToList(),
                    IsExpanded = readOnly
                };
                foreach (var s in stocks)
                {
                    string cur = map.TryGetValue(s.StkId, out string v) ? v : ScanDisplay.Default;
                    var item = new ScanStockChoice(setup, s)
                    {
                        Options = ScanDisplayWindow.OptionsFor(s, formats, cur),
                        SelectedDisplay = cur,
                        OriginalDisplay = cur,
                        IsDeductEligible = eligible.Contains(s.StkId)
                    };
                    item.SetInitial(setup.OriginalIds.Contains(s.StkId), a.PriorityId == s.StkId, setup.OriginalDeduct.Contains(s.StkId));
                    setup.Items.Add(item);
                }
                setup.Refresh();
                return setup;
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดการตั้งค่า Multi-Scanner ไม่สำเร็จ\n" + ex.Message); return null; }
        }

        // 🔔 ผู้ติดต่อ Admin ที่หน้าสแกนแสดง (แก้ได้ที่ปุ่ม SET ALERT CONTACT)
        private void ShowAlertContacts()
        {
            if (!DbSchema.HasAdminContacts) return;
            try { txtAlert.Text = ScannerSetupService.ContactText(new ScannerSetupService().GetAdminContacts()); }
            catch (Exception ex) { txtAlert.Text = ex.Message; }
            pnlAlert.Visibility = Visibility.Visible;
        }

        private void Alert_Click(object sender, RoutedEventArgs e)
        {
            if (AdminContactWindow.EditAndSave(_session?.UserId)) ShowAlertContacts();
        }

        private void ScannerToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ScannerSetup s) s.IsExpanded = !s.IsExpanded;
        }

        private void ScanPriority_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ScanStockChoice c) c.Owner.SetPriority(c);
        }

        // วาดแถวใหม่หลังกด VIEW ALL / SELECT ALL / CLEAR (หัวกลุ่ม SYSTEMS / STOCKS มาจาก GroupTitle)
        private void Refresh()
        {
            icPerms.ItemsSource = null;
            icPerms.ItemsSource = Rows;
        }

        private void ViewAll_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.View = true; Refresh(); }
        private void All_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.View = r.Add = r.Edit = r.Delete = r.Approve = true; Refresh(); }
        private void Clear_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.View = r.Add = r.Edit = r.Delete = r.Approve = false; Refresh(); }

        // บันทึก: เลือกคลังสแกนไว้ -> ติ๊ก VIEW ของ Multi-Scanner + คลังที่เลือกให้เอง (ไม่งั้นผู้ใช้เข้าหน้าสแกน / มองไม่เห็นคลังนั้น)
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var ids = ScanStockIds;
            if (ids.Count > 0)
            {
                var ticked = new List<string>();
                foreach (var r in Rows)
                {
                    bool need = string.Equals(r.SysId, "MultiScanner", StringComparison.OrdinalIgnoreCase) ||
                                _scanner.Items.Any(i => i.IsChecked && string.Equals(i.Stock.PermSysId, r.SysId, StringComparison.OrdinalIgnoreCase));
                    if (need && !r.View) { r.View = true; ticked.Add(r.Title); }
                }
                if (ticked.Count > 0) NotificationManager.Show("VIEW ticked", "ติ๊กสิทธิ์ VIEW ให้แล้ว: " + string.Join(", ", ticked), true);
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

        // ===== รายการย่อยของแถว Multi-Scanner =====
        public class ScannerSetup : INotifyPropertyChanged
        {
            public ObservableCollection<ScanStockChoice> Items { get; } = new ObservableCollection<ScanStockChoice>();
            public List<int> OriginalIds { get; set; } = new List<int>();
            public int? OriginalPriority { get; set; }
            public List<int> OriginalDeduct { get; set; } = new List<int>();

            private bool _isExpanded;
            public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; N(); N(nameof(ItemsVisibility)); N(nameof(ExpandText)); } }
            public Visibility ItemsVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            public string ExpandText => (IsExpanded ? "▾" : "▸") + " SCAN STOCKS + DISPLAY";

            public string Summary
            {
                get
                {
                    var on = Items.Where(i => i.IsChecked).ToList();
                    return on.Count == 0 ? "⛔ NOT SET - this user cannot scan (the scan page asks them to contact the Admin)"
                         : "STOCKS : " + string.Join(",  ", on.Select(i => i.Code + (i.IsPriority ? " ★" : "") + (i.IsDeduct ? " (DEDUCTED)" : ""))) + (on.Count > 1 ? "   (scan together)" : "");
                }
            }
            public Brush SummaryBrush => Items.Any(i => i.IsChecked) ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)) : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

            public void SetPriority(ScanStockChoice c)
            {
                bool on = !c.IsPriority;
                foreach (var i in Items) i.IsPriority = false;
                c.IsPriority = on && c.IsChecked;
                Refresh();
            }

            public void Refresh() { N(nameof(Summary)); N(nameof(SummaryBrush)); }

            public event PropertyChangedEventHandler PropertyChanged;
            private void N([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        public class ScanStockChoice : INotifyPropertyChanged
        {
            public ScannerSetup Owner { get; }
            public StockModel Stock { get; }
            public string Code => Stock.Code;
            public string Name => Stock.Name;
            public List<ScanDisplayWindow.Option> Options { get; set; }
            public string OriginalDisplay { get; set; }

            public ScanStockChoice(ScannerSetup owner, StockModel stock) { Owner = owner; Stock = stock; }
            public void SetInitial(bool on, bool priority, bool deduct) { _isChecked = on; _isPriority = on && priority; _isDeduct = on && deduct && IsDeductEligible; }

            // 🔁 ปุ่ม DEDUCTED / NOT DEDUCTED ของคลังนี้ (แสดงเฉพาะคลังที่ตัดสต็อกข้ามคลัง)
            public bool IsDeductEligible { get; set; }
            public Visibility DeductVisibility => IsDeductEligible ? Visibility.Visible : Visibility.Hidden;
            private bool _isDeduct;
            public bool IsDeduct { get => _isDeduct; set { _isDeduct = value && IsDeductEligible; N(); Owner?.Refresh(); } }

            private bool _isChecked;
            public bool IsChecked
            {
                get => _isChecked;
                set
                {
                    _isChecked = value;
                    if (!value) { IsPriority = false; IsDeduct = false; }
                    else if (IsDeductEligible && !_isDeduct) IsDeduct = true;   // เลือกคลังกลุ่มตัดข้ามคลัง = เปิด DEDUCTED ให้ก่อน (ปิดเองได้)
                    N(); N(nameof(CanPriority)); Owner?.Refresh();
                }
            }

            private bool _isPriority;
            public bool IsPriority { get => _isPriority; set { _isPriority = value; N(); N(nameof(PriorityText)); N(nameof(PriorityBrush)); } }
            public bool CanPriority => IsChecked && Stock.CanScanOut;
            public string PriorityText => IsPriority ? "★ OUT FIRST" : "☆ OUT FIRST";
            public Brush PriorityBrush => IsPriority ? new SolidColorBrush(Color.FromRgb(0xE6, 0x8A, 0x00)) : new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x90));

            private string _selectedDisplay;
            public string SelectedDisplay { get => _selectedDisplay; set { _selectedDisplay = value; N(); } }

            public event PropertyChangedEventHandler PropertyChanged;
            private void N([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }
}
