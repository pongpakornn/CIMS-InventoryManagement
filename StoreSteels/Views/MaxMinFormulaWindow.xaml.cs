using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CIMS.Views
{
    // ⚙ รายการสูตรคำนวณ MAX / MIN (CIMS.MaxMinFormulas) - เพิ่ม / แก้ไข / ลบ เหมือน Barcode Formats
    public partial class MaxMinFormulaWindow : Window
    {
        private readonly MaxMinCalcService _service = new MaxMinCalcService();
        private readonly UserSession _session;
        private readonly StockModel _stock;   // ใช้แสดงตัวอย่างตามหน่วย MAX / MIN ของคลังที่เปิดอยู่
        private readonly bool _inBox;
        private MaxMinFormula _current;
        private bool _ready;

        public bool Changed { get; private set; }

        private static readonly string[] Sources = { "ORDER", "FORECAST" };
        private static readonly string[] Rounds = { "UP", "NEAREST", "DOWN" };

        public MaxMinFormulaWindow(UserSession session, StockModel stock, int? selectId)
        {
            InitializeComponent();
            _session = session;
            _stock = stock;
            _inBox = stock == null || stock.MaxMinInBox;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            txtExPack.IsEnabled = _inBox;
            Reload(selectId);
        }

        private void Reload(int? selectId)
        {
            try
            {
                var list = _service.GetFormulas();
                lstFormulas.ItemsSource = list;
                lstFormulas.SelectedItem = list.FirstOrDefault(f => f.FormulaId == selectId) ?? list.FirstOrDefault();
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดรายการสูตรไม่สำเร็จ\n" + ex.Message); }
        }

        private void lstFormulas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstFormulas.SelectedItem is MaxMinFormula f) Show(f);
        }

        private void Show(MaxMinFormula f)
        {
            _ready = false;
            _current = f;
            txtName.Text = f.Name;
            txtName.IsReadOnly = f.IsDefault && f.FormulaId > 0;   // ชื่อสูตร DEFAULT คงไว้
            cbSource.SelectedIndex = Math.Max(0, Array.IndexOf(Sources, f.QtySource));
            cbRound.SelectedIndex = Math.Max(0, Array.IndexOf(Rounds, f.RoundMode));
            txtDefMax.Text = f.DefDayMax.ToString();
            txtDefMin.Text = f.DefDayMin.ToString();
            txtDefWd.Text = f.DefWorkdays.ToString();
            btnDelete.IsEnabled = f.FormulaId > 0 && !f.IsDefault;
            _ready = true;
            UpdateExample();
        }

        private MaxMinFormula Read() => new MaxMinFormula
        {
            FormulaId = _current?.FormulaId ?? 0,
            IsDefault = _current?.IsDefault ?? false,
            Name = (txtName.Text ?? "").Trim(),
            QtySource = Sources[Math.Max(0, cbSource.SelectedIndex)],
            RoundMode = Rounds[Math.Max(0, cbRound.SelectedIndex)],
            DefDayMax = int.TryParse(txtDefMax.Text, out int a) ? a : 0,
            DefDayMin = int.TryParse(txtDefMin.Text, out int b) ? b : 0,
            DefWorkdays = int.TryParse(txtDefWd.Text, out int c) ? c : 0
        };

        private void Input_Changed(object sender, EventArgs e) { if (_ready) UpdateExample(); }

        // ตัวอย่างสด (ไม่ได้บันทึก): 20,000 / 21 วัน = 952.38 ต่อวัน -> / 50 = 19.05 -> ปัดขึ้น 20 กล่อง -> x3 = 60 / x1 = 20
        private void UpdateExample()
        {
            var f = Read();
            decimal.TryParse(txtExQty.Text, out decimal qty);
            int.TryParse(txtExWd.Text, out int wd);
            int.TryParse(txtExPack.Text, out int pack);
            string unit = _inBox ? "BOX" : _stock.MaxMinUnit;
            lblExQty.Text = f.QtySource == "FORECAST" ? "FORECAST" : "ORDER";
            lblExMax.Text = $"MAX ({unit}) = x {f.DefDayMax} DAYS";
            lblExMin.Text = $"MIN ({unit}) = x {f.DefDayMin} DAYS";

            if (wd <= 0 || (_inBox && pack <= 0))
            {
                txtExSteps.Text = "กรอก WORKDAYS" + (_inBox ? " และ PACKSIZE" : "") + " ให้มากกว่า 0";
                txtExMax.Text = txtExMin.Text = "-";
            }
            else
            {
                var ex = f.Example(qty, wd, pack, _inBox, f.DefDayMax, f.DefDayMin);
                txtExSteps.Text = _inBox
                    ? $"{qty:N0} / {wd} days = {ex.perDay:N2} per day   →   / {pack} = {ex.perUnit:N2}   →   {ex.rounded:N0} box per day"
                    : $"{qty:N0} / {wd} days = {ex.perDay:N2}   →   {ex.rounded:N0} {unit} per day";
                txtExMax.Text = ex.max.ToString("N0");
                txtExMin.Text = ex.min.ToString("N0");
            }
            txtFormula.Text = f.Describe(_inBox);
        }

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

        private void New_Click(object sender, RoutedEventArgs e)
        {
            lstFormulas.SelectedItem = null;
            Show(new MaxMinFormula { Name = "" });
            txtName.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var f = Read();
            if (string.IsNullOrEmpty(f.Name)) { DialogHelper.ShowWarning("กรุณากรอกชื่อสูตร (FORMULA NAME)"); txtName.Focus(); return; }
            if (f.DefDayMax <= 0 || f.DefDayMin < 0) { DialogHelper.ShowWarning("DEFAULT MAX (DAYS) ต้องมากกว่า 0 และ MIN (DAYS) ต้องไม่ติดลบ"); return; }
            if (f.DefDayMin > f.DefDayMax) { DialogHelper.ShowWarning("DEFAULT MIN (DAYS) ต้องไม่มากกว่า MAX (DAYS)"); return; }
            if (f.DefWorkdays <= 0 || f.DefWorkdays > 31) { DialogHelper.ShowWarning("DEFAULT WORKDAYS ต้องอยู่ระหว่าง 1 - 31 วัน"); return; }
            try
            {
                if (_service.IsFormulaNameTaken(f.Name, f.FormulaId)) { DialogHelper.ShowWarning($"ชื่อสูตร \"{f.Name}\" มีอยู่แล้ว กรุณาใช้ชื่ออื่น"); return; }
                int id = _service.SaveFormula(f, _session?.UserId);
                LogService.WriteLog(_session?.UserId, "MAXMIN_FORMULA",
                    $"{(f.FormulaId == 0 ? "Created" : "Updated")} formula {f.Name} | Source: {f.QtySource} | Round: {f.RoundMode} | Default MAX {f.DefDayMax} MIN {f.DefDayMin} WD {f.DefWorkdays}", f.Name);
                Changed = true;
                NotificationManager.Show("Formula saved", $"บันทึกสูตร {f.Name} แล้ว", true);
                Reload(id);
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกสูตรไม่สำเร็จ\n" + ex.Message); }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var f = _current;
            if (f == null || f.FormulaId == 0 || f.IsDefault) return;
            if (!DialogHelper.ShowConfirm($"ลบสูตร \"{f.Name}\" ?", "DELETE FORMULA")) return;
            try
            {
                var used = _service.DeleteFormula(f.FormulaId);
                if (used.Count > 0)
                {
                    DialogHelper.ShowWarning($"ลบสูตร \"{f.Name}\" ไม่ได้ เพราะคลังต่อไปนี้ใช้อยู่\n\n• {string.Join("\n• ", used)}\n\nกรุณาเปลี่ยนสูตรของคลังเหล่านี้ก่อน");
                    return;
                }
                LogService.WriteLog(_session?.UserId, "MAXMIN_FORMULA", $"Deleted formula {f.Name}", f.Name);
                Changed = true;
                NotificationManager.Show("Formula deleted", $"ลบสูตร {f.Name} แล้ว", true);
                Reload(null);
            }
            catch (Exception ex) { DialogHelper.ShowError("ลบสูตรไม่สำเร็จ\n" + ex.Message); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
