using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.Views
{
    // 📅 วันทำงานของลูกค้า (CIMS.CustomerWorkdays) - ใช้คำนวณ Order / Workdays ในหน้า Max-Min Calculator
    public partial class WorkdayWindow : Window
    {
        private readonly MaxMinCalcService _service = new MaxMinCalcService();
        private readonly UserSession _session;
        private readonly bool _canEdit;
        private int _year = DateTime.Today.Year;
        private string _customer;
        private HashSet<DateTime> _days = new HashSet<DateTime>();
        private readonly Dictionary<int, TextBlock> _monthBadges = new Dictionary<int, TextBlock>();

        public bool Changed { get; private set; }

        private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
        private Brush Purple => (Brush)FindResource("AccentPurple");
        private Brush Dark => (Brush)FindResource("MainPurple");

        public WorkdayWindow(UserSession session, bool canEdit)
        {
            InitializeComponent();
            _session = session;
            _canEdit = canEdit;
            // เจ้าของหน้าต่าง = หน้าจอหลัก (ถ้าไม่มี แสดงกลางจอ)
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            btnImport.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
            btnTemplate.Visibility = btnImport.Visibility;
            Loaded += async (s, e) => await ShowCustomers();
        }

        #region === [ การ์ดลูกค้า ] ===

        private async Task ShowCustomers()
        {
            _customer = null;
            txtYear.Text = _year.ToString();
            txtTitle.Text = "📅 CUSTOMER WORKDAYS";
            txtSubtitle.Text = "Select a customer to set the working days of each month";
            btnBack.Visibility = Visibility.Collapsed;
            pnlCalendar.Visibility = Visibility.Collapsed;
            pnlCustomers.Visibility = Visibility.Visible;
            txtTotal.Text = "";

            List<WorkdayCustomer> list;
            int y = _year;
            try { list = await Task.Run(() => _service.GetWorkdayCustomers(y)); }
            catch (Exception ex) { DialogHelper.ShowError("โหลดรายชื่อลูกค้าไม่สำเร็จ\n" + ex.Message); return; }

            wpCustomers.Children.Clear();
            if (list.Count == 0)
            {
                wpCustomers.Children.Add(new TextBlock { Text = "NO CUSTOMER - add CUSTOMER to the products in Inventory Registration first.", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, Margin = new Thickness(4, 20, 0, 0) });
                return;
            }
            foreach (var c in list) wpCustomers.Children.Add(CustomerCard(c));
        }

        private FrameworkElement CustomerCard(WorkdayCustomer c)
        {
            var card = new Border
            {
                Width = 250, Height = 128, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 14, 14),
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xF1)), BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand, Tag = c.Customer,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.08 }
            };
            card.MouseEnter += (s, e) => card.BorderBrush = Purple;
            card.MouseLeave += (s, e) => card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xF1));
            card.MouseLeftButtonUp += async (s, e) => await ShowCalendar(c.Customer);

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new Border { CornerRadius = new CornerRadius(16, 16, 0, 0), Background = c.MonthsSet >= 12 ? Dark : Purple });

            var sp = new StackPanel { Margin = new Thickness(16, 10, 16, 10) };
            Grid.SetRow(sp, 1);
            sp.Children.Add(new TextBlock { Text = "👥 " + c.Customer, FontSize = 17, FontWeight = FontWeights.Black, Foreground = Dark, TextTrimming = TextTrimming.CharacterEllipsis });
            var nums = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            nums.Children.Add(new TextBlock { Text = c.YearDays.ToString("N0"), FontSize = 30, FontWeight = FontWeights.Black, Foreground = Purple });
            nums.Children.Add(new TextBlock { Text = $"WORKDAYS {_year}", FontSize = 11, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 7) });
            sp.Children.Add(nums);
            sp.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xEE, 0xF6)),
                Child = new TextBlock { Text = $"{c.MonthsSet} / 12 MONTHS SET", FontSize = 10, FontWeight = FontWeights.Black, Foreground = Dark }
            });
            g.Children.Add(sp);
            card.Child = g;
            return card;
        }

        #endregion

        #region === [ ปฏิทิน 12 เดือน ] ===

        private async Task ShowCalendar(string customer)
        {
            _customer = customer;
            txtYear.Text = _year.ToString();
            txtTitle.Text = "📅 " + customer;
            txtSubtitle.Text = _canEdit ? "Click a day = working day / click again = not working (saved automatically)" : "View only";
            btnBack.Visibility = Visibility.Visible;
            pnlCustomers.Visibility = Visibility.Collapsed;
            pnlCalendar.Visibility = Visibility.Visible;

            int y = _year;
            try { _days = await Task.Run(() => _service.GetWorkdays(customer, y)); }
            catch (Exception ex) { DialogHelper.ShowError("โหลดวันทำงานไม่สำเร็จ\n" + ex.Message); return; }

            ugMonths.Children.Clear();
            _monthBadges.Clear();
            for (int m = 1; m <= 12; m++) ugMonths.Children.Add(MonthCard(m));
            UpdateTotals();

            // ปีปัจจุบัน -> เลื่อนไปที่เดือนนี้ให้เลย
            if (_year == DateTime.Today.Year)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                    ((FrameworkElement)ugMonths.Children[DateTime.Today.Month - 1]).BringIntoView()));
            else pnlCalendar.ScrollToTop();
        }

        private FrameworkElement MonthCard(int month)
        {
            var first = new DateTime(_year, month, 1);
            var card = new Border
            {
                CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(12, 10, 12, 10),
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xF1)), BorderThickness = new Thickness(1.5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.07 }
            };
            var root = new StackPanel();
            card.Child = root;

            // หัวการ์ด: ชื่อเดือน + จำนวนวันทำงาน (มุมขวาบน) / แถวล่าง: ปุ่มลัด
            var head = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            head.Children.Add(new TextBlock { Text = first.ToString("MMMM", En).ToUpperInvariant(), FontSize = 14, FontWeight = FontWeights.Black, Foreground = Dark, VerticalAlignment = VerticalAlignment.Center });
            var badge = new TextBlock { FontSize = 11, FontWeight = FontWeights.Black, Foreground = Brushes.White };
            _monthBadges[month] = badge;
            head.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Purple, Padding = new Thickness(9, 3, 9, 3), Child = badge, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(head);
            if (_canEdit)
            {
                var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 0, 0, 6) };
                tools.Children.Add(SmallButton("MON-FRI", "Set Monday - Friday as working days", async () => await SetMonth(month, weekdaysOnly: true)));
                tools.Children.Add(SmallButton("CLEAR", "Clear this month", async () => await SetMonth(month, weekdaysOnly: false)));
                root.Children.Add(tools);
            }

            // วันในสัปดาห์ (อาทิตย์เริ่มต้น)
            var grid = new UniformGrid { Columns = 7 };
            foreach (var d in new[] { "SU", "MO", "TU", "WE", "TH", "FR", "SA" })
                grid.Children.Add(new TextBlock { Text = d, FontSize = 10, FontWeight = FontWeights.Black, Foreground = d == "SU" || d == "SA" ? Brushes.IndianRed : Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3) });

            for (int i = 0; i < (int)first.DayOfWeek; i++) grid.Children.Add(new Border());
            for (int day = 1; day <= DateTime.DaysInMonth(_year, month); day++) grid.Children.Add(DayCell(new DateTime(_year, month, day)));
            root.Children.Add(grid);
            return card;
        }

        private Button SmallButton(string text, string tip, Func<Task> action)
        {
            var b = new Button { Content = text, ToolTip = tip, Style = (Style)FindResource("StkCardButton"), Margin = new Thickness(4, 0, 0, 0), FontSize = 9, Focusable = false };
            b.Click += async (s, e) => await action();
            return b;
        }

        private FrameworkElement DayCell(DateTime date)
        {
            var text = new TextBlock { Text = date.Day.ToString(), FontSize = 12, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Height = 28, Margin = new Thickness(1.5), CornerRadius = new CornerRadius(8), Child = text, Tag = date, Cursor = _canEdit ? Cursors.Hand : Cursors.Arrow, ToolTip = date.ToString("dddd d MMMM yyyy", En) };
            if (date == DateTime.Today) { cell.BorderBrush = Dark; cell.BorderThickness = new Thickness(1.5); }
            Paint(cell);
            if (_canEdit) cell.MouseLeftButtonUp += async (s, e) => await ToggleDay(cell);
            return cell;
        }

        private void Paint(Border cell)
        {
            var date = (DateTime)cell.Tag;
            bool on = _days.Contains(date);
            bool weekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
            cell.Background = on ? Purple : new SolidColorBrush(Color.FromRgb(0xF7, 0xF5, 0xFA));
            ((TextBlock)cell.Child).Foreground = on ? Brushes.White : weekend ? Brushes.IndianRed : new SolidColorBrush(Color.FromRgb(0x4A, 0x45, 0x50));
        }

        // กดครั้งที่ 1 = วันทำงาน / ครั้งที่ 2 = ยกเลิก -> บันทึกทันที (ผิดพลาดคืนค่าเดิม + แจ้ง Toast)
        private async Task ToggleDay(Border cell)
        {
            var date = (DateTime)cell.Tag;
            bool on = !_days.Contains(date);
            if (on) _days.Add(date); else _days.Remove(date);
            Paint(cell);
            UpdateTotals();
            await Save(new[] { date }, on, () => { if (on) _days.Remove(date); else _days.Add(date); Paint(cell); UpdateTotals(); });
        }

        private async Task SetMonth(int month, bool weekdaysOnly)
        {
            var all = Enumerable.Range(1, DateTime.DaysInMonth(_year, month)).Select(d => new DateTime(_year, month, d)).ToList();
            var before = new HashSet<DateTime>(_days);
            var toClear = all.Where(_days.Contains).ToList();
            var toSet = weekdaysOnly ? all.Where(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday).ToList() : new List<DateTime>();
            foreach (var d in toClear) _days.Remove(d);
            foreach (var d in toSet) _days.Add(d);
            RepaintMonth(month);
            UpdateTotals();

            bool ok = await Save(toClear, false, null);
            if (ok && toSet.Count > 0) ok = await Save(toSet, true, null);
            if (!ok) { _days = before; RepaintMonth(month); UpdateTotals(); }
        }

        private void RepaintMonth(int month)
        {
            if (!(ugMonths.Children[month - 1] is Border card) || !(card.Child is StackPanel sp)) return;
            var g = sp.Children.OfType<UniformGrid>().FirstOrDefault();
            if (g == null) return;
            foreach (var cell in g.Children.OfType<Border>().Where(b => b.Tag is DateTime)) Paint(cell);
        }

        private async Task<bool> Save(IEnumerable<DateTime> dates, bool working, Action revert)
        {
            var list = dates.ToList();
            if (list.Count == 0) return true;
            string cust = _customer, uid = _session?.UserId;
            try
            {
                await Task.Run(() => _service.SetWorkdays(cust, list, working, uid));
                Changed = true;
                txtSaved.Text = "✓ SAVED " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                revert?.Invoke();
                NotificationManager.Show("Save failed", "บันทึกวันทำงานไม่สำเร็จ: " + ex.Message, false);
                return false;
            }
        }

        private void UpdateTotals()
        {
            for (int m = 1; m <= 12; m++)
                if (_monthBadges.TryGetValue(m, out var b)) b.Text = $"{_days.Count(d => d.Month == m && d.Year == _year)} DAYS";
            txtTotal.Text = $"{_days.Count(d => d.Year == _year)} WORKDAYS IN {_year}";
        }

        #endregion

        #region === [ Events ] ===

        // 📄 TEMPLATE: ลูกค้าที่เปิดอยู่ (หรือทุกลูกค้า) + วันทำงานของปีที่เลือกใส่ไว้ให้ -> Desktop\CIMS_Export\Max-Min Calculator
        private async void Template_Click(object sender, RoutedEventArgs e)
        {
            string who = _customer ?? "ทุกลูกค้า";
            if (!DialogHelper.ShowConfirm($"ต้องการสร้างไฟล์ Template สำหรับ IMPORT WORKDAYS ({who} ปี {_year}) ใช่หรือไม่?\n\n" +
                    "• 1 แถว = 1 วันทำงาน (CUSTOMER / DATE) - วันทำงานที่มีอยู่แล้วใส่ไว้ให้\n" +
                    "• แก้แล้วนำเข้ากลับด้วยปุ่ม IMPORT WORKDAYS\n\nกด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก", "TEMPLATE EXCEL")) return;
            btnTemplate.IsEnabled = false;
            try
            {
                int y = _year; string c = _customer;
                string path = await Task.Run(() => _service.WriteWorkdayTemplate(y, c));
                LogService.WriteLog(_session?.UserId, "WORKDAY_TEMPLATE", $"Customer: {c ?? "ALL"} | Year: {y} | File: {System.IO.Path.GetFileName(path)}", "");
                NotificationManager.Show("Template", $"สร้างไฟล์ Template แล้ว\n{ImportTemplateService.ShortPath(path)}", true);
                ImportTemplateService.OpenFile(path);
            }
            catch (Exception ex) { DialogHelper.ShowError("สร้างไฟล์ Template ไม่สำเร็จ (ปิดไฟล์ Excel เดิมก่อน)\n" + ex.Message); }
            finally { btnTemplate.IsEnabled = true; }
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import Customer Workdays", Filter = "Excel Files (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;

            List<CalcImportRow> rows;
            try { rows = await Task.Run(() => _service.ReadWorkdayExcel(dlg.FileName)); }
            catch (Exception ex) { NotificationManager.Show("Import failed", "อ่านไฟล์ไม่สำเร็จ: " + ex.Message, false); return; }

            var valid = rows.Where(r => r.IsValid).ToList();
            int bad = rows.Count - valid.Count;
            if (valid.Count == 0) { NotificationManager.Show("Import failed", "ไม่พบวันทำงานที่นำเข้าได้ในไฟล์นี้" + (bad > 0 ? $" (ข้าม {bad} แถว)" : ""), false); return; }

            try
            {
                string uid = _session?.UserId;
                var result = await Task.Run(() => _service.ApplyWorkdays(rows, uid));
                Changed = true;
                LogService.WriteLog(uid, "WORKDAY_IMPORT", $"Import workdays: {System.IO.Path.GetFileName(dlg.FileName)} | Days: {result.Days} | Months: {result.Months} | Skipped rows: {bad}", "");
                NotificationManager.Show("Workdays imported", $"นำเข้าวันทำงาน {result.Days:N0} วัน ({result.Months:N0} เดือน-ลูกค้า) แทนที่ของเดิมแล้ว" + (bad > 0 ? $"\nข้ามแถวที่มีปัญหา {bad} แถว" : ""), true);
                if (_customer != null) await ShowCalendar(_customer); else await ShowCustomers();
            }
            catch (Exception ex) { NotificationManager.Show("Import failed", "นำเข้าวันทำงานไม่สำเร็จ: " + ex.Message, false); }
        }

        private async void PrevYear_Click(object sender, RoutedEventArgs e) { _year--; await Reload(); }
        private async void NextYear_Click(object sender, RoutedEventArgs e) { _year++; await Reload(); }
        private async Task Reload() { if (_customer != null) await ShowCalendar(_customer); else await ShowCustomers(); }
        private async void Back_Click(object sender, RoutedEventArgs e) => await ShowCustomers();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

        #endregion
    }
}
