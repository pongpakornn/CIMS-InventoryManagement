using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace CIMS.Views
{
    // 🧾 PR SETTINGS - ย้ายจากหน้า PR มาอยู่ที่ User Management (Admin Level 1 เท่านั้น)
    //   KEEP PR (CIMS.Stocks.AutoPR) = แสกนออกแล้วสร้าง PR + คลังนี้อยู่ใน Dropdown หน้า PR
    //   ไฟล์ Excel ของ EXPORT PATH ตั้งที่ปุ่ม 📊 SETUP EXCEL PATH (แสดงไว้ให้ดูเฉย ๆ)
    public partial class PRSettingsWindow : Window
    {
        public class Row : INotifyPropertyChanged
        {
            public StockModel Stock { get; set; }
            public string Code => Stock.Code;
            public string Name => Stock.Name;
            public string PathText { get; set; }
            public bool OrigKeep { get; set; }
            private bool _keep;
            public bool KeepPr { get => _keep; set { _keep = value; Notify(); } }
            public bool KeepChanged => KeepPr != OrigKeep;
            public event PropertyChangedEventHandler PropertyChanged;
            private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly UserSession _session;
        private readonly List<Row> _rows;
        public bool Saved { get; private set; }

        public PRSettingsWindow(UserSession session)
        {
            InitializeComponent();
            _session = session;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var svc = new StockService();
            List<StockModel> stocks; Dictionary<int, bool> auto; Dictionary<int, PRExcelLayout> layouts;
            try { stocks = svc.GetStocks(); auto = svc.GetPrAutoMap(); layouts = svc.GetPrExcelLayouts(); }
            catch (Exception ex)
            {
                DialogHelper.ShowError("โหลดการตั้งค่า PR ไม่สำเร็จ\n" + ex.Message);
                stocks = new List<StockModel>(); auto = new Dictionary<int, bool>(); layouts = new Dictionary<int, PRExcelLayout>();
            }
            _rows = stocks.Select(s =>
            {
                bool k = auto.TryGetValue(s.StkId, out bool on) && on;
                string p = layouts.TryGetValue(s.StkId, out var l) && l.HasPath ? "📊 " + l.Path : "📊 no Excel file (SETUP EXCEL PATH)";
                return new Row { Stock = s, OrigKeep = k, KeepPr = k, PathText = p };
            }).ToList();
            icRows.ItemsSource = _rows;
            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => Motion.Stagger(icRows, 35)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void Changed_Click(object sender, RoutedEventArgs e)
        {
            int n = _rows.Count(r => r.KeepChanged);
            btnSave.IsEnabled = n > 0;
            txtStatus.Text = n > 0 ? $"{n} stock(s) changed - press SAVE" : "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_session?.UserLevel != 1) { DialogHelper.ShowWarning("เฉพาะ Admin (Level 1) เท่านั้น", "ACCESS DENIED"); return; }
            var keep = _rows.Where(r => r.KeepChanged).ToDictionary(r => r.Stock.StkId, r => r.KeepPr);
            if (keep.Count == 0) return;
            try
            {
                new StockService().SetPrAuto(keep, _session.UserId);
                string on = string.Join(", ", _rows.Where(r => r.KeepPr).Select(r => r.Code));
                LogService.WritePRLog(_session.UserId, "PR_SETTINGS", $"KEEP PR stocks: {(on.Length == 0 ? "-" : on)}", "PR_SETTINGS");
                Saved = true;
                DialogHelper.ShowSuccess("บันทึก PR SETTINGS เรียบร้อยแล้ว\n\nคลังที่เก็บ PR: " + (on.Length == 0 ? "ไม่มี" : on));
                Close();
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
