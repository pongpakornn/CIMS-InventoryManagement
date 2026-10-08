using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.Views
{
    // จัดการรูปแบบป้ายบาร์โค้ด Supplier (CIMS.BarcodeFormats) - ใช้ร่วมกันได้หลายคลัง
    public partial class BarcodeFormatWindow : Window
    {
        private static readonly Regex DigitsOnly = new Regex("^[0-9]+$");
        private readonly UserSession _session;
        private readonly StockService _stockService = new StockService();
        private BarcodeFormatModel _current;

        public BarcodeFormatWindow(UserSession session)
        {
            InitializeComponent();
            _session = session;
            // ADD = เพิ่มรูปแบบใหม่ / EDIT = แก้ไขรูปแบบเดิม / DEL = ลบรูปแบบ (สิทธิ์หน้า Store)
            btnNew.Visibility = _session.CanAddStore ? Visibility.Visible : Visibility.Collapsed;
            Loaded += (s, e) => { LoadSourceStocks(); Reload(null); };
        }

        // ตัวเลือก "ตัดยอดจากคลัง": ไม่ตัด + คลังทุกคลังที่ไม่ใช่คลังหลัก
        private void LoadSourceStocks()
        {
            cbSourceStock.Items.Clear();
            cbSourceStock.Items.Add(new ComboBoxItem { Content = "(NONE) - do not deduct", Tag = null });
            try
            {
                foreach (var s in _stockService.GetStocks().Where(x => !x.IsMain))
                    cbSourceStock.Items.Add(new ComboBoxItem { Content = s.DisplayName, Tag = s.StkId });
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ไม่สามารถโหลดรายชื่อคลังได้\n" + ex.Message);
            }
            cbSourceStock.SelectedIndex = 0;
        }

        private void SelectSourceStock(int? stkId)
        {
            var match = cbSourceStock.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as int?) == stkId);
            cbSourceStock.SelectedItem = match ?? cbSourceStock.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }

        private int? SelectedSourceStock() => (cbSourceStock.SelectedItem as ComboBoxItem)?.Tag as int?;

        // ช่องที่แสดงในผลทดสอบ (ไฮไลต์ช่องที่ถูกเลือกเป็น รหัส / รหัสสำรอง / จำนวน)
        public class FieldChip
        {
            public int Number { get; set; }
            public string Text { get; set; }
            public Brush Background { get; set; }
            public Brush Foreground { get; set; }
        }

        private void Reload(int? selectId)
        {
            try
            {
                var list = _stockService.GetFormats();
                lstFormats.ItemsSource = list;
                var pick = list.FirstOrDefault(f => f.FmtId == selectId) ?? list.FirstOrDefault();
                if (pick != null) lstFormats.SelectedItem = pick;
                else ShowFormat(new BarcodeFormatModel { Delimiter = ";", CodePos = 1 });
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ไม่สามารถโหลดรูปแบบบาร์โค้ดได้\n" + ex.Message);
            }
        }

        private void lstFormats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstFormats.SelectedItem is BarcodeFormatModel f) ShowFormat(f);
        }

        private void ShowFormat(BarcodeFormatModel f)
        {
            _current = f;
            txtName.Text = f.Name ?? "";
            txtDelimiter.Text = f.Delimiter ?? ";";
            txtCodePos.Text = f.CodePos > 0 ? f.CodePos.ToString() : "";
            txtAltPos.Text = f.AltCodePos?.ToString() ?? "";
            txtQtyPos.Text = f.QtyPos?.ToString() ?? "";
            txtMinFields.Text = f.FmtId == 0 ? "" : f.MinFields.ToString();
            chkActive.IsChecked = f.IsActive;
            txtSample.Text = f.SampleText ?? "";
            cbDelimMode.SelectedIndex = f.SplitBySpaces ? 1 : 0;
            txtTrimChars.Text = f.TrimChars ?? "";
            cbCodePrefix.SelectedIndex = string.Equals(f.CodePrefix, "DIGIT", StringComparison.OrdinalIgnoreCase) ? 1
                                       : string.Equals(f.CodePrefix, "CUT", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            txtCodeCut.Text = f.CodeCut?.ToString() ?? "";
            txtMatchStart.Text = f.MatchStart ?? "";
            txtMatchEnd.Text = f.MatchEnd ?? "";
            cbMatchBy.SelectedIndex = f.MatchByName ? 1 : 0;
            txtNameFields.Text = f.NameFields ?? "";
            txtCoilNoPos.Text = f.CoilNoPos?.ToString() ?? "";
            txtMotherPos.Text = f.MotherCoilPos?.ToString() ?? "";
            SelectSourceStock(f.SourceStkId);
            btnDelete.IsEnabled = f.FmtId > 0;
            btnDelete.Visibility = _session.CanDeleteStore ? Visibility.Visible : Visibility.Collapsed;
            btnSave.Visibility = (f.FmtId == 0 ? _session.CanAddStore : _session.CanEditStore) ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreview();
        }

        private static int? ParsePos(string text) => int.TryParse((text ?? "").Trim(), out int v) && v > 0 ? v : (int?)null;

        // สร้างรูปแบบจากค่าที่กรอกอยู่ตอนนี้ (ใช้ทั้งพรีวิวและบันทึก)
        private BarcodeFormatModel BuildFromInputs()
        {
            int codePos = ParsePos(txtCodePos.Text) ?? 0;
            int? alt = ParsePos(txtAltPos.Text);
            int? qty = ParsePos(txtQtyPos.Text);
            var f = new BarcodeFormatModel
            {
                FmtId = _current?.FmtId ?? 0,
                Name = (txtName.Text ?? "").Trim(),
                Delimiter = cbDelimMode.SelectedIndex == 1 && string.IsNullOrEmpty(txtDelimiter.Text) ? " " : (txtDelimiter.Text ?? ""),
                DelimMode = cbDelimMode.SelectedIndex == 1 ? "SPACES" : "CHAR",
                TrimChars = string.IsNullOrEmpty(txtTrimChars.Text) ? null : txtTrimChars.Text.Trim(),
                CodePrefix = cbCodePrefix.SelectedIndex == 1 ? "DIGIT" : cbCodePrefix.SelectedIndex == 2 ? "CUT" : "NONE",
                CodeCut = ParsePos(txtCodeCut.Text),
                MatchStart = string.IsNullOrWhiteSpace(txtMatchStart.Text) ? null : txtMatchStart.Text.Trim(),
                MatchEnd = string.IsNullOrWhiteSpace(txtMatchEnd.Text) ? null : txtMatchEnd.Text.Trim(),
                MatchBy = cbMatchBy.SelectedIndex == 1 ? "NAME" : "CODE",
                NameFields = string.IsNullOrWhiteSpace(txtNameFields.Text) ? null : txtNameFields.Text.Trim(),
                CoilNoPos = ParsePos(txtCoilNoPos?.Text),
                MotherCoilPos = ParsePos(txtMotherPos?.Text),
                CodePos = codePos,
                AltCodePos = alt,
                QtyPos = qty,
                SampleText = (txtSample.Text ?? "").Trim(),
                IsActive = true   // พรีวิวทดสอบเสมอ (ค่า ACTIVE จริงใช้ตอนบันทึก)
            };
            int autoMin = RequiredMinFields(f);
            f.MinFields = Math.Max(ParsePos(txtMinFields.Text) ?? autoMin, autoMin);
            return f;
        }

        // จำนวนช่องขั้นต่ำ = เลขช่องที่ใหญ่ที่สุดที่ใช้ (รหัส / รหัสสำรอง / จำนวน / ช่องชื่อสินค้า)
        private static int RequiredMinFields(BarcodeFormatModel f)
        {
            int m = Math.Max(f.CodePos, Math.Max(f.AltCodePos ?? 0, Math.Max(f.QtyPos ?? 0, Math.Max(f.CoilNoPos ?? 0, f.MotherCoilPos ?? 0))));
            if (f.MatchByName) foreach (int n in f.NamePositions()) m = Math.Max(m, n);
            return m;
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (txtDelimiter == null || txtCodeCut == null || txtNameFields == null || cbMatchBy == null) return;
            txtDelimiter.IsEnabled = cbDelimMode.SelectedIndex != 1;
            txtCodeCut.IsEnabled = cbCodePrefix.SelectedIndex == 2;
            txtNameFields.IsEnabled = cbMatchBy.SelectedIndex == 1;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (icFields == null || txtParseResult == null || cbMatchBy == null || txtLookupResult == null || txtCoilNoPos == null) return;

            var f = BuildFromInputs();
            string sample = txtSample.Text ?? "";
            var fields = string.IsNullOrEmpty(sample) || (!f.SplitBySpaces && string.IsNullOrEmpty(f.Delimiter))
                ? new string[0]
                : f.SplitFields(sample);

            var purple = (Brush)FindResource("AccentPurple");
            var green = (Brush)new BrushConverter().ConvertFrom("#2E7D32");
            var blue = (Brush)new BrushConverter().ConvertFrom("#0288D1");
            var gray = (Brush)new BrushConverter().ConvertFrom("#F1EEF6");
            var orange = (Brush)new BrushConverter().ConvertFrom("#EF6C00");
            var dark = (Brush)FindResource("MainPurple");

            var nameFields = f.MatchByName ? f.NamePositions() : new List<int>();
            icFields.ItemsSource = fields.Select((text, i) =>
            {
                int n = i + 1;
                Brush bg = (f.MatchByName ? nameFields.Contains(n) : n == f.CodePos) ? purple : n == f.AltCodePos ? blue : n == f.QtyPos ? green : (n == f.CoilNoPos || n == f.MotherCoilPos) ? orange : gray;
                return new FieldChip { Number = n, Text = text.Trim(), Background = bg, Foreground = bg == gray ? dark : Brushes.White };
            }).ToList();

            List<string> codes = null;
            bool ok = false;
            if (string.IsNullOrWhiteSpace(sample))
                txtParseResult.Text = "Scan a label above to test this format.";
            else if (f.CodePos == 0)
                txtParseResult.Text = "Set CODE FIELD # to test.";
            else if (ok = f.TryParse(sample, out codes, out decimal? q))
                txtParseResult.Text = f.MatchByName
                    ? $"✓ MATCH   PRODUCT NAME: {codes[0]}{(codes.Count > 1 ? "   ALT CODE: " + string.Join(" / ", codes.Skip(1)) : "")}   QTY: {(q.HasValue ? q.Value.ToString("0.##") : "Pack Size")}"
                    : $"✓ MATCH   CODE: {string.Join("  /  ", codes)}   QTY: {(q.HasValue ? q.Value.ToString("0.##") : "Pack Size")}";
            else
                txtParseResult.Text = !f.Matches(sample)
                    ? "✗ NOT MATCH   (label does not start / end with the text set in LABEL FILTERS)"
                    : $"✗ NOT MATCH   ({fields.Length} field(s) found, need at least {f.MinFields})";

            // เลข Coil ที่อ่านได้จากป้ายทดสอบ
            if (txtCoilResult != null)
            {
                string coilNo = null, mother = null;
                if (ok) f.ReadCoil(sample, out coilNo, out mother);
                txtCoilResult.Visibility = ok && (f.HasCoilNo || f.MotherCoilPos > 0) ? Visibility.Visible : Visibility.Collapsed;
                txtCoilResult.Text = $"🧲 COIL NO: {coilNo ?? "-"}     MOTHER COIL: {mother ?? "-"}";
            }

            ShowLookup(ok ? f : null, codes);
        }

        // ผลค้นหาสินค้าจริงในระบบจากป้ายทดสอบ (ไว้เช็กว่าตั้งรูปแบบถูกไหม) - ค้นเบื้องหลัง เอาผลล่าสุดเท่านั้น
        private int _lookupVersion;
        private async void ShowLookup(BarcodeFormatModel f, List<string> codes)
        {
            int v = ++_lookupVersion;
            if (f == null || codes == null || codes.Count == 0) { txtLookupResult.Visibility = Visibility.Collapsed; return; }
            string text; bool found;
            try
            {
                var r = await System.Threading.Tasks.Task.Run(() =>
                {
                    var p = new ScanService().FindPart(f, codes, out string note);
                    return (p, note);
                });
                found = r.p != null;
                text = found ? $"FOUND IN SYSTEM:  {r.p.PartCode}  •  {r.p.PartName}"
                             : "NOT FOUND IN SYSTEM" + (string.IsNullOrEmpty(r.note) ? "  (no product has this " + (f.MatchByName ? "name" : "code") + ")" : "  -  " + r.note);
            }
            catch (Exception ex) { found = false; text = "LOOKUP ERROR: " + ex.Message; }
            if (v != _lookupVersion) return;
            txtLookupResult.Text = text;
            txtLookupResult.Foreground = found ? (Brush)new BrushConverter().ConvertFrom("#2E7D32") : (Brush)new BrushConverter().ConvertFrom("#C62828");
            txtLookupResult.Visibility = Visibility.Visible;
        }

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !DigitsOnly.IsMatch(e.Text);

        private void New_Click(object sender, RoutedEventArgs e)
        {
            lstFormats.SelectedItem = null;
            ShowFormat(new BarcodeFormatModel { Delimiter = ";", CodePos = 1, IsActive = true });
            txtName.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var f = BuildFromInputs();
            if (!(f.FmtId == 0 ? _session.CanAddStore : _session.CanEditStore))
            {
                DialogHelper.ShowWarning(f.FmtId == 0 ? "คุณไม่มีสิทธิ์เพิ่มรูปแบบบาร์โค้ด" : "คุณไม่มีสิทธิ์แก้ไขรูปแบบบาร์โค้ด", "ACCESS DENIED");
                return;
            }
            f.IsActive = chkActive.IsChecked == true;
            f.SourceStkId = SelectedSourceStock();

            if (string.IsNullOrEmpty(f.Name)) { DialogHelper.ShowWarning("กรุณากรอกชื่อรูปแบบ (FORMAT NAME)"); txtName.Focus(); return; }
            if (!f.SplitBySpaces && string.IsNullOrEmpty(f.Delimiter)) { DialogHelper.ShowWarning("กรุณากรอกตัวคั่น (DELIMITER) เช่น ; หรือเลือก SPLIT FIELDS BY = ANY SPACES"); txtDelimiter.Focus(); return; }
            if (f.CodePrefix == "CUT" && !(f.CodeCut > 0)) { DialogHelper.ShowWarning("กรุณากรอกจำนวนตัวอักษรที่ต้องการตัด (N)"); txtCodeCut.Focus(); return; }
            if (f.CodePos <= 0) { DialogHelper.ShowWarning("กรุณากรอกตำแหน่งช่องรหัสสินค้า (CODE FIELD #)"); txtCodePos.Focus(); return; }
            if (f.MatchByName && !string.IsNullOrWhiteSpace(f.NameFields) && f.NamePositions().Count == 0)
            { DialogHelper.ShowWarning("กรุณากรอกช่องชื่อสินค้า (NAME FIELDS #) เป็นตัวเลข เช่น 6,7"); txtNameFields.Focus(); return; }
            // MIN FIELDS ที่กรอกต้องไม่น้อยกว่าเลขช่องที่ใหญ่ที่สุด - เดิมระบบแอบปรับเป็นค่านั้นตอนบันทึก (ดูเหมือนบันทึกแล้วค่าไม่เปลี่ยน)
            int? typedMin = ParsePos(txtMinFields.Text);
            int needMin = RequiredMinFields(f);
            if (typedMin.HasValue && typedMin.Value < needMin)
            {
                DialogHelper.ShowWarning($"MIN FIELDS ต้องไม่น้อยกว่า {needMin}\n(ช่องที่ใช้อยู่ไกลสุดคือช่อง #{needMin}) - เว้นว่างไว้ได้ ระบบใส่ให้เอง");
                txtMinFields.Focus();
                return;
            }

            var list = lstFormats.ItemsSource as IEnumerable<BarcodeFormatModel> ?? Enumerable.Empty<BarcodeFormatModel>();
            if (list.Any(x => x.FmtId != f.FmtId && string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase)))
            {
                DialogHelper.ShowWarning($"ชื่อรูปแบบ \"{f.Name}\" มีอยู่แล้ว กรุณาใช้ชื่ออื่น");
                return;
            }

            if (!string.IsNullOrEmpty(f.SampleText) && !f.TryParse(f.SampleText, out _, out _) &&
                !DialogHelper.ShowConfirm("ตัวอย่างบาร์โค้ดที่ทดสอบไว้ ไม่ตรงกับรูปแบบนี้\nต้องการบันทึกต่อหรือไม่?", "CONFIRM SAVE"))
                return;

            try
            {
                bool isNew = f.FmtId == 0;
                int id = _stockService.SaveFormat(f, _session.UserId);
                LogService.WriteLog(_session.UserId, isNew ? "BARCODE_FMT_CREATE" : "BARCODE_FMT_UPDATE",
                    $"{f.Name} | Delim: {f.Delimiter} | Code#{f.CodePos} Alt#{f.AltCodePos} Qty#{f.QtyPos} Min{f.MinFields} Coil#{f.CoilNoPos} Mother#{f.MotherCoilPos} | Match: {(f.MatchByName ? "NAME#" + string.Join(",", f.NamePositions()) : "CODE")} | Active: {f.IsActive} | DeductStk: {f.SourceStkId}", f.Name);
                NotificationManager.Show("Barcode Format", $"บันทึกรูปแบบ {f.Name} สำเร็จ", true);
                Reload(id);
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("บันทึกรูปแบบบาร์โค้ดไม่สำเร็จ\n" + ex.Message);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _current.FmtId == 0 || !_session.CanDeleteStore) return;
            if (!DialogHelper.ShowConfirm($"ต้องการลบรูปแบบ \"{_current.Name}\" ใช่หรือไม่?\nคลังที่ใช้รูปแบบนี้จะไม่รับป้ายรูปแบบนี้อีก", "CONFIRM DELETE"))
                return;
            try
            {
                _stockService.DeleteFormat(_current.FmtId);
                LogService.WriteLog(_session.UserId, "BARCODE_FMT_DELETE", $"Deleted format {_current.Name}", _current.Name);
                Reload(null);
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ลบรูปแบบบาร์โค้ดไม่สำเร็จ\n" + ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
