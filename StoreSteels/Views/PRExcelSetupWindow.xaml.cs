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

namespace CIMS.Views
{
    // 📊 SETUP EXCEL PATH (User Management - Admin Level 1)
    //   ต่อคลัง: ไฟล์ Excel + แถวเริ่มตาราง + ข้อมูล PR แต่ละช่องลงเซลล์ / คอลัมน์ไหน (เพิ่มช่องใหม่ได้ เช่น MOTHER COIL / COIL NO / BIN)
    //   แก้ได้หลายคลังแล้วกด SAVE ครั้งเดียว -> CIMS.Stocks.PRExportPath / PRExcelMap / PRExcelStartRow
    public partial class PRExcelSetupWindow : Window
    {
        public class EntryRow : INotifyPropertyChanged
        {
            public static readonly List<PRExcelFieldOption> FieldList = PRExcelFields.All.Select(f => new PRExcelFieldOption { Key = f.Key, Label = f.Label }).ToList();
            public List<PRExcelFieldOption> Fields => FieldList;
            private string _field = "DATE";
            public string Field { get => _field; set { _field = value; Notify(); } }
            private int _place;
            public int PlaceIndex { get => _place; set { _place = value; Notify(); Notify(nameof(Hint)); } }
            private string _target = "";
            public string Target { get => _target; set { _target = value; Notify(); } }
            public string Hint => PlaceIndex == 1 ? "column e.g. B" : "cell e.g. C1";
            public event PropertyChangedEventHandler PropertyChanged;
            private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class StockRow
        {
            public StockModel Stock { get; set; }
            public string Code => Stock.Code;
            public bool KeepPr { get; set; }
            public string Path { get; set; }
            public int StartRow { get; set; }
            public ObservableCollection<EntryRow> Entries { get; set; } = new ObservableCollection<EntryRow>();
            public string Original { get; set; }
            public string Key => $"{(Path ?? "").Trim()}|{StartRow}|{ToLayout().MapText}";
            public bool Changed => Key != Original;
            public Visibility ChangedVisibility => Changed ? Visibility.Visible : Visibility.Collapsed;
            public string Summary => (KeepPr ? "KEEP PR · " : "") + (string.IsNullOrWhiteSpace(Path) ? "no Excel file" : System.IO.Path.GetFileName(Path.Trim()));

            public PRExcelLayout ToLayout() => new PRExcelLayout
            {
                StockId = Stock.StkId, Path = (Path ?? "").Trim(), StartRow = StartRow,
                Entries = Entries.Select(e => new PRExcelEntry { Field = e.Field, IsTable = e.PlaceIndex == 1, Target = (e.Target ?? "").Trim().ToUpperInvariant() }).ToList()
            };
        }

        private readonly UserSession _session;
        private readonly List<StockRow> _rows = new List<StockRow>();
        private StockRow _cur;
        private bool _loading;

        public PRExcelSetupWindow(UserSession session)
        {
            InitializeComponent();
            _session = session;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var svc = new StockService();
            try
            {
                var auto = svc.GetPrAutoMap();
                var layouts = svc.GetPrExcelLayouts();
                foreach (var s in svc.GetStocks().OrderByDescending(s => auto.TryGetValue(s.StkId, out bool k) && k))
                {
                    var l = layouts.TryGetValue(s.StkId, out var x) ? x : new PRExcelLayout { StockId = s.StkId, Entries = PRExcelLayout.DefaultEntries() };
                    var r = new StockRow { Stock = s, KeepPr = auto.TryGetValue(s.StkId, out bool on) && on, Path = l.Path, StartRow = l.StartRow };
                    foreach (var e in l.Entries) r.Entries.Add(new EntryRow { Field = e.Field, PlaceIndex = e.IsTable ? 1 : 0, Target = e.Target });
                    r.Original = r.Key;
                    _rows.Add(r);
                }
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลด SETUP EXCEL PATH ไม่สำเร็จ\n" + ex.Message); }
            lbStocks.ItemsSource = _rows;
            if (!DbSchema.HasPrStock) txtStatus.Text = "⚠ Run Database/Update_20261017.sql first - cannot save yet";
            lbStocks.SelectedIndex = _rows.Count > 0 ? 0 : -1;
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => Motion.Stagger(lbStocks, 30)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Stocks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _cur = lbStocks.SelectedItem as StockRow;
            spEditor.IsEnabled = _cur != null;
            if (_cur == null) return;
            _loading = true;
            txtPath.Text = _cur.Path ?? "";
            txtStartRow.Text = _cur.StartRow.ToString();
            icEntries.ItemsSource = _cur.Entries;
            _loading = false;
            Motion.SlideIn(spEditor, 0, 260, true, 18);
            Refresh();
        }

        private void Any_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading || _cur == null) return;
            _cur.Path = txtPath.Text;
            if (int.TryParse(txtStartRow.Text, out int r) && r > 0) _cur.StartRow = r;
            Refresh();
        }

        private void Entry_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) Refresh(); }

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = $"EXCEL FILE - {_cur.Code}", Filter = "Excel Files (*.xlsx)|*.xlsx" };
            try { if (!string.IsNullOrWhiteSpace(_cur.Path)) dlg.InitialDirectory = System.IO.Path.GetDirectoryName(_cur.Path.Trim()); } catch { }
            if (dlg.ShowDialog(this) == true) txtPath.Text = dlg.FileName;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            var used = new HashSet<string>(_cur.Entries.Select(x => x.Field));
            string next = PRExcelFields.All.Select(f => f.Key).FirstOrDefault(k => !used.Contains(k)) ?? "BIN";
            _cur.Entries.Add(new EntryRow { Field = next, PlaceIndex = 1, Target = "" });
            Refresh();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null || !((sender as FrameworkElement)?.DataContext is EntryRow row)) return;
            _cur.Entries.Remove(row);
            Refresh();
        }

        private void Default_Click(object sender, RoutedEventArgs e)
        {
            if (_cur == null) return;
            _cur.Entries.Clear();
            foreach (var x in PRExcelLayout.DefaultEntries()) _cur.Entries.Add(new EntryRow { Field = x.Field, PlaceIndex = x.IsTable ? 1 : 0, Target = x.Target });
            txtStartRow.Text = "4";
            Refresh();
        }

        // ตัวอย่างที่จะลง + ปุ่ม SAVE / จำนวนคลังที่แก้
        private void Refresh()
        {
            if (_cur != null)
            {
                var l = _cur.ToLayout();
                var heads = l.Entries.Where(x => !x.IsTable).Select(x => $"{x.Target} = {PRExcelFields.Label(x.Field)}");
                var cols = l.Entries.Where(x => x.IsTable).OrderBy(x => x.Target.Length).ThenBy(x => x.Target).Select(x => $"{x.Target} = {PRExcelFields.Label(x.Field)}");
                txtPreview.Text = "ONE CELL  :  " + (heads.Any() ? string.Join("   ", heads) : "-") +
                                  $"\nTABLE (from row {l.StartRow}, next export continues below the last row)  :  " + (cols.Any() ? string.Join("   ", cols) : "-");
            }
            lbStocks.Items.Refresh();
            int n = _rows.Count(r => r.Changed);
            btnSave.IsEnabled = n > 0 && DbSchema.HasPrStock;
            if (DbSchema.HasPrStock) txtStatus.Text = n > 0 ? $"{n} stock(s) changed - press SAVE" : "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.UserLevel != 1) { DialogHelper.ShowWarning("เฉพาะ Admin (Level 1) เท่านั้น", "ACCESS DENIED"); return; }
            var changed = _rows.Where(r => r.Changed).ToList();
            foreach (var r in changed)
            {
                var l = r.ToLayout();
                var bad = l.Entries.FirstOrDefault(x => x.IsTable ? !PRExcelLayout.ValidColumn(x.Target) : !PRExcelLayout.ValidCell(x.Target));
                if (bad != null)
                {
                    lbStocks.SelectedItem = r;
                    DialogHelper.ShowWarning($"{r.Code}: {PRExcelFields.Label(bad.Field)} ช่อง \"{bad.Target}\" ไม่ถูกต้อง\n" +
                                             (bad.IsTable ? "TABLE COLUMN ใส่ตัวอักษรคอลัมน์ เช่น B" : "ONE CELL ใส่เซลล์ เช่น C1"));
                    return;
                }
                var dup = l.Entries.GroupBy(x => x.Target).FirstOrDefault(g => g.Count() > 1);
                if (dup != null) { lbStocks.SelectedItem = r; DialogHelper.ShowWarning($"{r.Code}: ช่อง {dup.Key} ถูกใช้ซ้ำ ({string.Join(", ", dup.Select(x => PRExcelFields.Label(x.Field)))})"); return; }
                if (l.HasPath && !System.IO.File.Exists(l.Path) &&
                    !DialogHelper.ShowConfirm($"{r.Code}: ไม่พบไฟล์บนเครื่องนี้\n{l.Path}\n(ไฟล์อาจอยู่เครื่องอื่น / ยังไม่ได้สร้าง)\n\nบันทึกต่อหรือไม่?", "SETUP EXCEL PATH")) return;
            }
            try
            {
                var svc = new StockService();
                foreach (var r in changed)
                {
                    var l = r.ToLayout();
                    svc.SavePrExcelLayout(l, _session.UserId);
                    LogService.WritePRLog(_session.UserId, "PR_EXCEL_SETUP", $"{r.Code} | File: {(l.HasPath ? l.Path : "-")} | Start row: {l.StartRow} | {l.MapText}", r.Code);
                    r.Original = r.Key;
                }
                Refresh();
                DialogHelper.ShowSuccess($"บันทึก SETUP EXCEL PATH ของ {string.Join(", ", changed.Select(r => r.Code))} แล้ว");
                Close();
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_rows.Any(r => r.Changed) && !DialogHelper.ShowConfirm("มีการตั้งค่าที่ยังไม่ได้กด SAVE\nต้องการปิดหน้าต่างโดยไม่บันทึกหรือไม่?", "UNSAVED CHANGES")) return;
            Close();
        }
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Cancel_Click(sender, e); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
