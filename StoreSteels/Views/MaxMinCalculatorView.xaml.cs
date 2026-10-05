using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CIMS.Views
{
    // 📐 Max-Min Calculator (จาก StorePC) - เลือกคลัง / MAX-MIN (DAYS) ต่อสินค้า / สูตร / วันทำงานลูกค้า / Import
    // ผลคำนวณเขียนลงค่า MAX / MIN ของหน้า Store (Max-Min) ของคลังนั้นโดยตรง
    public partial class MaxMinCalculatorView : Page
    {
        private readonly UserSession _session;
        private readonly MaxMinCalcService _service = new MaxMinCalcService();
        private List<StockModel> _stocks = new List<StockModel>();
        private StockModel _stock;
        private MaxMinFormula _formula = new MaxMinFormula();          // สูตรที่คลังนี้ใช้อยู่ (บันทึกแล้ว)
        private List<MaxMinFormula> _formulas = new List<MaxMinFormula>();
        private StockCalcSetting _setting = new StockCalcSetting();
        private bool _loadingSetting;

        // ข้อมูลทั้งหมด + ส่วนที่แสดง (แสดง 20 แถวแรก เลื่อนลงเพิ่มทีละ 10)
        private List<MaxMinCalcRow> _all = new List<MaxMinCalcRow>();
        private readonly ObservableCollection<MaxMinCalcRow> _shown = new ObservableCollection<MaxMinCalcRow>();
        private const int FirstPage = 20, NextPage = 10;
        private MaxMinCalcRow _editing;
        private DispatcherTimer _searchTimer;

        public bool CanEdit { get; }

        public MaxMinCalculatorView(UserSession session)
        {
            InitializeComponent();
            _session = session;
            CanEdit = session != null && session.CanEditMaxMinCalc;
            DataContext = this;

            var view = CollectionViewSource.GetDefaultView(_shown);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MaxMinCalcRow.GroupKey)));
            dgCalc.ItemsSource = view;

            foreach (var b in new[] { btnFormula, btnImportDays, btnImportForecast, btnCalculate })
                b.Visibility = CanEdit ? Visibility.Visible : Visibility.Collapsed;
            cbFormula.IsEnabled = chkAutoCalc.IsEnabled = CanEdit;
            btnSaveSetting.Visibility = CanEdit ? Visibility.Visible : Visibility.Collapsed;

            Loaded += (s, e) => LoadStocks();
            // คอลัมน์แบบ * กองรวมกันตอนเปิดหน้า (ตารางวัดความกว้างก่อนหน้าจอพร้อม) -> คำนวณความกว้างใหม่เมื่อขนาดเปลี่ยน
            dgCalc.SizeChanged += (s, e) => { if (e.WidthChanged) FitColumns(); };
        }

        #region === [ คลัง / สูตร ] ===

        private void LoadStocks()
        {
            try { _stocks = new StockService().GetStocks().Where(s => _session == null || _session.CanViewStock(s)).ToList(); }
            catch (Exception ex) { DialogHelper.ShowError("โหลดรายชื่อคลังไม่สำเร็จ\n" + ex.Message); return; }

            cbStock.Items.Clear();
            foreach (var s in _stocks) cbStock.Items.Add(new ComboBoxItem { Content = s.Code, Tag = s, ToolTip = s.Name });
            int last = UiPrefs.GetInt("MaxMinCalc.Stock", 0);
            int idx = _stocks.FindIndex(s => s.StkId == last);
            cbStock.SelectedIndex = _stocks.Count == 0 ? -1 : Math.Max(0, idx);
        }

        private async void cbStock_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!((cbStock.SelectedItem as ComboBoxItem)?.Tag is StockModel s)) return;
            _stock = s;
            UiPrefs.Set("MaxMinCalc.Stock", s.StkId);
            string unit = s.MaxMinInBox ? "BOX" : s.MaxMinUnit;
            colQtyMax.Header = $"MAX ({unit})";
            colQtyMin.Header = $"MIN ({unit})";
            ClearForm();
            await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            if (_stock == null) return;
            var stock = _stock;
            string key = txtSearch.Text;
            try
            {
                var (formulas, setting, rows, month) = await Task.Run(() =>
                {
                    var fl = _service.GetFormulas();
                    var st = _service.GetStockSetting(stock.StkId);
                    var f = fl.FirstOrDefault(x => x.FormulaId == st.FormulaId) ?? fl.FirstOrDefault(x => x.IsDefault) ?? fl.FirstOrDefault() ?? new MaxMinFormula();
                    return (fl, st, _service.GetRows(stock, f, key), _service.GetLatestImportMonth());
                });
                if (stock != _stock) return;
                _formulas = formulas;
                _setting = setting;
                _formula = formulas.FirstOrDefault(x => x.FormulaId == setting.FormulaId) ?? formulas.FirstOrDefault(x => x.IsDefault) ?? formulas.FirstOrDefault() ?? new MaxMinFormula();
                _all = rows;
                ShowSetting();

                // No. นับใหม่ในแต่ละลูกค้า
                foreach (var g in _all.GroupBy(r => r.GroupKey)) { int n = 0; foreach (var r in g) r.No = ++n; }

                _shown.Clear();
                foreach (var r in _all.Take(FirstPage)) _shown.Add(r);
                txtEmpty.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                txtCount.Text = $"{_all.Count:N0} ITEMS";

                bdAuto.Background = _setting.AutoCalc ? (Brush)FindResource("AccentPurple") : new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
                txtAuto.Text = _setting.AutoCalc ? "AUTO CALC : ON" : "AUTO CALC : OFF";
                txtFormula.Text = $"{_formula.Name} :  " + _formula.Describe(stock.MaxMinInBox);
                txtFormula.ToolTip = txtFormula.Text + $"\nDefault: MAX {_formula.DefDayMax} days / MIN {_formula.DefDayMin} days / {_formula.DefWorkdays} workdays";
                var inv = CultureInfo.InvariantCulture;
                txtLast.Text = (month.HasValue ? "ORDER : " + month.Value.ToString("MMM yyyy", inv).ToUpperInvariant() : "NO ORDER IMPORTED")
                             + "   •   LAST CALC : " + (_setting.LastCalc.HasValue ? _setting.LastCalc.Value.ToString("dd MMM yyyy HH:mm", inv).ToUpperInvariant() : "-");
                FitColumns();
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("โหลดข้อมูล Max-Min Calculator ไม่สำเร็จ\n" + ex.Message + "\n\n(ตรวจสอบว่ารัน Database/MaxMinCalc.sql แล้ว)");
            }
        }

        // เลื่อนใกล้ล่างสุด -> แสดงเพิ่มอีก 10 แถว จนครบ
        private void dgCalc_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeight <= 0 || _shown.Count >= _all.Count) return;
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 40 || e.ExtentHeight <= e.ViewportHeight)
                foreach (var r in _all.Skip(_shown.Count).Take(NextPage).ToList()) _shown.Add(r);
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _searchTimer.Tick += async (s, ev) => { _searchTimer.Stop(); await ReloadAsync(); };
            _searchTimer.Start();
        }

        // ⚙ รายการสูตร (เพิ่ม / แก้ไข / ลบ) - เลือกใช้กับคลังที่ช่อง FORMULA แล้วกด SAVE
        private async void Formula_Click(object sender, RoutedEventArgs e)
        {
            var w = new MaxMinFormulaWindow(_session, _stock, _formula?.FormulaId);
            w.ShowDialog();
            if (w.Changed) await ReloadAsync();
        }

        // ช่อง FORMULA / AUTO CALC ของคลัง: แสดงค่าที่บันทึกไว้ (ยังไม่เลือก = สูตร DEFAULT)
        private void ShowSetting()
        {
            _loadingSetting = true;
            cbFormula.Items.Clear();
            foreach (var f in _formulas) cbFormula.Items.Add(new ComboBoxItem { Content = f.DisplayName, Tag = f, ToolTip = f.Describe(_stock.MaxMinInBox) });
            cbFormula.SelectedIndex = _formulas.FindIndex(f => f.FormulaId == _formula.FormulaId);
            chkAutoCalc.IsChecked = _setting.AutoCalc;
            btnSaveSetting.IsEnabled = false;
            _loadingSetting = false;
        }

        // เปลี่ยนสูตร / AUTO CALC แล้วยังไม่บันทึก -> ปุ่ม SAVE กดได้
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSetting || _stock == null) return;
            var f = (cbFormula.SelectedItem as ComboBoxItem)?.Tag as MaxMinFormula;
            btnSaveSetting.IsEnabled = CanEdit && f != null && (f.FormulaId != _formula.FormulaId || (chkAutoCalc.IsChecked == true) != _setting.AutoCalc || _setting.FormulaId == null);
        }

        private async void SaveSetting_Click(object sender, RoutedEventArgs e)
        {
            if (_stock == null || !((cbFormula.SelectedItem as ComboBoxItem)?.Tag is MaxMinFormula f)) return;
            bool auto = chkAutoCalc.IsChecked == true;
            try
            {
                var stock = _stock; string uid = _session?.UserId;
                await Task.Run(() => _service.SaveStockSetting(stock.StkId, f.FormulaId, auto, uid));
                LogService.WriteLog(uid, "MAXMIN_STOCK_SETTING", $"Stock: {stock.Code} | Formula: {f.Name} | Auto calc: {(auto ? "ON" : "OFF")}", stock.Code);
                NotificationManager.Show("Saved", $"คลัง {stock.Code} ใช้สูตร {f.Name}  •  AUTO CALC {(auto ? "ON" : "OFF")}", true);
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกการตั้งค่าคลังไม่สำเร็จ\n" + ex.Message); }
        }

        // 📄 TEMPLATE EXCEL: หน้านี้มี Import 2 ปุ่ม -> ให้เลือกก่อนว่าจะเตรียมไฟล์ของปุ่มไหน แล้วยืนยันก่อนสร้างไฟล์
        //    SET MAX MIN = สินค้าในตาราง (ตามคำค้นหา) + MAX / MIN (DAY) ปัจจุบัน / FORECAST = ข้อมูลเดือนนี้รายลูกค้า
        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_stock == null) return;
            string mon = DateTime.Today.ToString("MMM yyyy", CultureInfo.InvariantCulture);
            string pick = TemplateChoiceWindow.Choose($"Stock {_stock.Code}  •  choose the import you want to prepare a file for",
                new TemplateChoiceWindow.Option
                {
                    Key = "DAYS", Title = "SET MAX / MIN",
                    Detail = $"Products of the table ({_all.Count:N0}) with current MAX (DAY) / MIN (DAY). Fill MAX / MIN (DAY) or (BOX), FORECAST / ORDER / DELIVERY in any column.",
                    UseWith = "USE WITH:  📥 IMPORT SET MAX MIN"
                },
                new TemplateChoiceWindow.Option
                {
                    Key = "FORECAST", Title = "FORECAST / ORDER / DELIVERY",
                    Detail = $"One row per customer + part + month ({mon} rows already filled in). For forecast / order / delivery of any customer and month.",
                    UseWith = "USE WITH:  📊 IMPORT FORECAST & ORDER"
                });
            if (pick == null) return;

            bool days = pick == "DAYS";
            if (days && _all.Count == 0) { DialogHelper.ShowWarning($"ยังไม่มีสินค้าในตารางของคลัง {_stock.Code} ให้สร้าง Template"); return; }
            string confirm = days
                ? $"ต้องการตั้งค่า MAX / MIN ของคลัง {_stock.Code} ด้วยไฟล์ Template นี้ใช่หรือไม่?\n\n" +
                  $"• ไฟล์จะมีสินค้าตามตาราง {_all.Count:N0} รายการ พร้อมค่า MAX / MIN (DAY) ปัจจุบัน\n" +
                  "• กรอกหรือแก้ค่าในไฟล์ แล้วนำเข้ากลับด้วยปุ่ม IMPORT SET MAX MIN\n\n" +
                  "กด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก"
                : "ต้องการสร้างไฟล์ Template สำหรับนำเข้า Forecast / Order / Delivery ใช่หรือไม่?\n\n" +
                  $"• ไฟล์จะมีรายการของเดือน {mon} ที่มีอยู่แล้วให้ (ไม่มีจะเป็นตารางว่าง)\n" +
                  "• กรอกหรือแก้ตัวเลขในไฟล์ แล้วนำเข้ากลับด้วยปุ่ม IMPORT FORECAST & ORDER\n\n" +
                  "กด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก";
            if (!DialogHelper.ShowConfirm(confirm, days ? "TEMPLATE • SET MAX MIN" : "TEMPLATE • FORECAST")) return;

            var stock = _stock;
            string path = ImportTemplateService.NewPath(days ? $"{stock.Code}_Max-MinCal_Template" : "ForecastOrder_Template_" + DateTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            btnExport.IsEnabled = false;
            try
            {
                System.IO.Directory.CreateDirectory(ImportTemplateService.ExportFolder);
                int count = 0;
                if (days)
                {
                    var rows = _all.ToList(); count = rows.Count;
                    await Task.Run(() => _service.ExportDays(rows, path));
                }
                else
                {
                    var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                    await Task.Run(() =>
                    {
                        var rows = new ForecastOrderService().GetRows(month, "", 0, 1000000);
                        count = rows.Count;
                        new ImportTemplateService().CreateForecastTemplate(path, month, rows);
                    });
                }
                LogService.WriteLog(_session?.UserId, days ? "MAXMIN_TEMPLATE" : "FORECAST_TEMPLATE",
                    $"Stock: {stock.Code} | Rows: {count} | File: {System.IO.Path.GetFileName(path)}", stock.Code);
                NotificationManager.Show("Template", $"สร้างไฟล์ Template แล้ว ({count:N0} รายการ)\nDesktop\\CIMS_Export\\{System.IO.Path.GetFileName(path)}", true);
                ImportTemplateService.OpenFile(path);   // เปิดไฟล์ Template ขึ้นมาเลย
            }
            catch (System.IO.IOException) { DialogHelper.ShowError("บันทึกไฟล์ไม่สำเร็จ กรุณาปิดไฟล์ Excel ที่เปิดอยู่ก่อนแล้วลองใหม่"); }
            catch (Exception ex) { DialogHelper.ShowError("สร้างไฟล์ Template ไม่สำเร็จ\n" + ex.Message); }
            finally { btnExport.IsEnabled = true; }
        }

        // ให้คอลัมน์แบบ * ยืดเต็มความกว้างตาราง (แก้ตอนเปิดหน้าแล้วคอลัมน์กองรวมกันทางซ้าย)
        private void FitColumns()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var col in dgCalc.Columns)
                {
                    var w = col.Width;
                    col.Width = new DataGridLength(0);
                    col.Width = w;
                }
            }));
        }

        private async void Workdays_Click(object sender, RoutedEventArgs e)
        {
            var w = new WorkdayWindow(_session, CanEdit);
            w.ShowDialog();
            if (w.Changed) await ReloadAsync();
        }

        private async void Calculate_Click(object sender, RoutedEventArgs e)
        {
            if (_stock == null) return;
            if (!DialogHelper.ShowConfirm($"คำนวณ MAX / MIN ของคลัง {_stock.Code} ใหม่ทั้งหมด?\n\nค่า MAX / MIN เดิมในหน้า Store (Max-Min) จะถูกแทนที่ด้วยค่าที่คำนวณได้\n(สินค้าที่ยังไม่มี Order จะไม่ถูกเปลี่ยน)", "CALCULATE NOW")) return;
            await RunCalc(_stock.StkId, null, $"คำนวณคลัง {_stock.Code}");
        }

        private async Task RunCalc(int? stkId, int? ptId, string what)
        {
            try
            {
                string uid = _session?.UserId;
                var r = await Task.Run(() => _service.Calculate(stkId, ptId, uid));
                LogService.WriteLog(uid, "MAXMIN_CALCULATE", $"{what} | Stocks: {r.Stocks} | Updated: {r.Updated} | Skipped (no order): {r.Skipped}", _stock?.Code);
                NotificationManager.Show("Max-Min calculated",
                    r.Stocks == 0 ? "ไม่มีคลังที่เปิด AUTO CALC" : $"อัปเดต MAX / MIN แล้ว {r.Updated:N0} รายการ" + (r.Skipped > 0 ? $"\nยังไม่มี Order {r.Skipped:N0} รายการ (ไม่เปลี่ยน)" : ""), true);
                await ReloadAsync();
            }
            catch (Exception ex) { NotificationManager.Show("Calculate failed", "คำนวณไม่สำเร็จ: " + ex.Message, false); }
        }

        #endregion

        #region === [ EDIT / RESET / SAVE ] ===

        // EDIT: กดครั้งที่ 1 แสดงข้อมูลด้านบนให้แก้ / กดซ้ำแถวเดิม = ยกเลิก
        private void EditRow_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is MaxMinCalcRow row)) return;
            if (_editing == row) { ClearForm(); return; }
            _editing = row;
            txtCust.Text = row.GroupKey;
            txtPartNo.Text = row.PartNo;
            txtName.Text = row.PartName;
            txtDayMax.Text = row.DayMax.ToString();
            txtDayMin.Text = row.DayMin.ToString();
            txtDayMax.IsReadOnly = txtDayMin.IsReadOnly = false;
            btnSave.IsEnabled = true;
            txtDayMax.Focus();
            txtDayMax.SelectAll();
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => ClearForm();

        private void ClearForm()
        {
            _editing = null;
            txtCust.Text = txtPartNo.Text = txtName.Text = txtDayMax.Text = txtDayMin.Text = "";
            txtDayMax.IsReadOnly = txtDayMin.IsReadOnly = true;
            btnSave.IsEnabled = false;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var row = _editing;
            if (row == null || _stock == null) return;
            if (!int.TryParse(txtDayMax.Text, out int dmax) || !int.TryParse(txtDayMin.Text, out int dmin) || dmax <= 0 || dmin < 0)
            { DialogHelper.ShowWarning("MAX (DAYS) ต้องมากกว่า 0 และ MIN (DAYS) ต้องไม่ติดลบ"); return; }
            if (dmin > dmax) { DialogHelper.ShowWarning("MIN (DAYS) ต้องไม่มากกว่า MAX (DAYS)"); return; }

            try
            {
                var stock = _stock; string uid = _session?.UserId;
                await Task.Run(() => _service.SaveDays(stock, row, dmax, dmin, uid));
                LogService.WriteLog(uid, "MAXMIN_DAYS", $"Stock: {stock.Code} | {row.PartNo} | MAX DAYS {row.DayMax}->{dmax} | MIN DAYS {row.DayMin}->{dmin}", row.PartCode);
                ClearForm();
                // คำนวณสินค้านี้ใหม่ทันทีด้วยจำนวนวันใหม่ (ถ้ามี Order)
                var r = await Task.Run(() => _service.Calculate(stock.StkId, row.PtId, uid));
                NotificationManager.Show("Saved", $"บันทึก MAX {dmax} วัน / MIN {dmin} วัน ของ {row.PartNo} แล้ว" +
                    (r.Updated > 0 ? "\nคำนวณ MAX / MIN ใหม่แล้ว" : "\nยังไม่มี Order - ค่า MAX / MIN ยังไม่เปลี่ยน"), true);
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
        }

        // RESET: ล้างค่า MAX / MIN ปัจจุบันในหน้า Store (Max-Min) (แสดงเป็น -)
        private async void ResetRow_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is MaxMinCalcRow row) || _stock == null) return;
            if (!DialogHelper.ShowConfirm($"ล้างค่า MAX / MIN ของ {row.PartNo} ในคลัง {_stock.Code}?\n\nหน้า Store (Max-Min) จะแสดงเป็น \"-\"", "RESET MAX / MIN")) return;
            try
            {
                var stock = _stock;
                await Task.Run(() => _service.ResetMaxMin(stock, row.PtId));
                LogService.WriteLog(_session?.UserId, "MAXMIN_RESET", $"Stock: {stock.Code} | {row.PartNo} | MAX {row.QtyMax}->0 | MIN {row.QtyMin}->0", row.PartCode);
                row.QtyMax = 0; row.QtyMin = 0;
                NotificationManager.Show("Reset", $"ล้างค่า MAX / MIN ของ {row.PartNo} แล้ว", true);
            }
            catch (Exception ex) { DialogHelper.ShowError("ล้างค่าไม่สำเร็จ\n" + ex.Message); }
        }

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

        #endregion

        #region === [ Import ] ===

        private static string ErrorList(List<CalcImportRow> bad) =>
            string.Join("\n", bad.Take(8).Select(r => $"• แถว {r.RowNumber}: {r.Error}")) + (bad.Count > 8 ? $"\n• ... และอีก {bad.Count - 8:N0} แถว" : "");

        // 📥 IMPORT SET MAX MIN: ตั้ง MAX / MIN (DAYS) ของสินค้าในคลังที่เลือกทั้งไฟล์
        private async void ImportDays_Click(object sender, RoutedEventArgs e)
        {
            if (_stock == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = $"Import Set Max Min -> {_stock.Code}", Filter = "Excel Files (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;

            var stock = _stock;
            if (await TryImportTemplate(dlg.FileName)) return;
            List<CalcImportRow> rows;
            try
            {
                var f = _formula;
                rows = await Task.Run(() => _service.ReadDaysExcel(dlg.FileName, stock, _service.GetRows(stock, f, "")));
            }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }

            var valid = rows.Where(r => r.IsValid).ToList();
            var bad = rows.Where(r => !r.IsValid).ToList();
            if (valid.Count == 0) { DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + ErrorList(bad)); return; }
            if (!DialogHelper.ShowConfirm($"ไฟล์: {System.IO.Path.GetFileName(dlg.FileName)}\nคลัง: {stock.Code}\n\nตั้ง MAX / MIN (DAYS) {valid.Count:N0} รายการ" +
                                          (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{ErrorList(bad)}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?", "IMPORT SET MAX MIN")) return;
            try
            {
                string uid = _session?.UserId;
                int n = await Task.Run(() => _service.ApplyDays(stock, rows, uid));
                LogService.WriteLog(uid, "MAXMIN_DAYS_IMPORT", $"Stock: {stock.Code} | File: {System.IO.Path.GetFileName(dlg.FileName)} | Items: {n} | Skipped rows: {bad.Count}", stock.Code);
                NotificationManager.Show("Imported", $"ตั้ง MAX / MIN (DAYS) แล้ว {n:N0} รายการ", true);
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("นำเข้าไม่สำเร็จ (ยกเลิกทั้งไฟล์)\n" + ex.Message); }
        }

        // 📥 ไฟล์รูปแบบ Max-MinCal (NO / PART NO / PRODUCT NAME / MAX(DAY) / MIN(DAY) / MAX(BOX) / MIN(BOX) / FORECAST / ORDER / DELIVERY)
        //    ใช้ได้ทั้งปุ่ม IMPORT SET MAX MIN และ IMPORT FORECAST & ORDER - ช่องไหนมีข้อมูลก็นำเข้าช่องนั้น
        //    คืน false = ไม่ใช่ไฟล์รูปแบบนี้ (ให้ใช้วิธีอ่านแบบเดิมต่อ)
        private async Task<bool> TryImportTemplate(string file)
        {
            if (_stock == null) return false;
            var stock = _stock; var f = _formula;
            List<CalcImportRow> rows;
            try { rows = await Task.Run(() => _service.ReadTemplateExcel(file, stock, _service.GetRows(stock, f, ""))); }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return true; }
            if (rows == null) return false;

            var valid = rows.Where(r => r.IsValid).ToList();
            var bad = rows.Where(r => !r.IsValid).ToList();
            if (valid.Count == 0)
            {
                DialogHelper.ShowError(bad.Count == 0 ? "ไม่มีข้อมูลให้นำเข้า\nกรอก MAX(DAY) / MIN(DAY) / MAX(BOX) / MIN(BOX) / FORECAST / ORDER / DELIVERY อย่างน้อย 1 ช่อง"
                                                      : "ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + ErrorList(bad));
                return true;
            }
            int days = valid.Count(v => v.HasDays), box = valid.Count(v => v.HasBox), fc = valid.Count(v => v.HasForecast);
            string what = string.Join("\n", new[]
            {
                days > 0 ? $"• MAX / MIN (DAY)  {days:N0} รายการ" : null,
                box > 0 ? $"• MAX / MIN (BOX)  {box:N0} รายการ" : null,
                fc > 0 ? $"• FORECAST / ORDER / DELIVERY (เดือน {DateTime.Today.ToString("MMM yyyy", CultureInfo.InvariantCulture)})  {fc:N0} รายการ" : null
            }.Where(s => s != null));
            if (!DialogHelper.ShowConfirm($"ไฟล์: {System.IO.Path.GetFileName(file)}\nคลัง: {stock.Code}\n\n{what}" +
                                          (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{ErrorList(bad)}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?", "IMPORT MAX-MIN CAL")) return true;
            try
            {
                string uid = _session?.UserId;
                var r = await Task.Run(() => _service.ApplyTemplate(stock, rows, uid));
                LogService.WriteLog(uid, "MAXMIN_TEMPLATE_IMPORT", $"Stock: {stock.Code} | File: {System.IO.Path.GetFileName(file)} | Days: {r.Days} | Box: {r.Box} | Forecast: {r.Forecast} | Skipped rows: {bad.Count}", stock.Code);
                NotificationManager.Show("Imported", $"นำเข้าแล้ว  •  DAY {r.Days:N0}  •  BOX {r.Box:N0}  •  FORECAST {r.Forecast:N0}", true);
                if (r.Forecast > 0) await RunCalc(null, null, "Auto calculate after Max-MinCal import");
                else await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("นำเข้าไม่สำเร็จ (ยกเลิกทั้งไฟล์)\n" + ex.Message); }
            return true;
        }

        // 📊 IMPORT FORECAST & ORDER (+ DELIVERY): เก็บต่อเดือน (ซ้ำ = แทนที่) -> คำนวณคลังที่เปิด AUTO CALC -> กราฟ Dashboard
        private async void ImportForecast_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import Forecast & Order & Delivery", Filter = "Excel Files (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;
            if (await TryImportTemplate(dlg.FileName)) return;

            List<CalcImportRow> rows;
            try { rows = await Task.Run(() => _service.ReadForecastExcel(dlg.FileName)); }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }

            var valid = rows.Where(r => r.IsValid).ToList();
            var bad = rows.Where(r => !r.IsValid).ToList();
            if (valid.Count == 0) { DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + ErrorList(bad)); return; }
            var months = string.Join(", ", valid.Select(v => v.Month).Distinct().OrderBy(m => m).Select(m => m.ToString("MMM yyyy", CultureInfo.InvariantCulture)));
            if (!DialogHelper.ShowConfirm($"ไฟล์: {System.IO.Path.GetFileName(dlg.FileName)}\n\nนำเข้า {valid.Count:N0} แถว  (เดือน: {months})\n" +
                                          $"Forecast {valid.Sum(v => v.Forecast):N0}  •  Order {valid.Sum(v => v.Order):N0}  •  Delivery {valid.Sum(v => v.Delivery):N0}\n" +
                                          "ลูกค้า/สินค้า/เดือนเดียวกันที่เคยนำเข้าจะถูกแทนที่" +
                                          (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{ErrorList(bad)}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?", "IMPORT FORECAST & ORDER")) return;
            try
            {
                string uid = _session?.UserId;
                int n = await Task.Run(() => _service.ApplyForecast(rows, uid));
                LogService.WriteLog(uid, "FORECAST_IMPORT", $"File: {System.IO.Path.GetFileName(dlg.FileName)} | Rows: {n} | Months: {months} | Skipped rows: {bad.Count}", "");
                NotificationManager.Show("Forecast imported", $"นำเข้า Forecast / Order / Delivery แล้ว {n:N0} แถว", true);
                await RunCalc(null, null, "Auto calculate after forecast import");
            }
            catch (Exception ex) { DialogHelper.ShowError("นำเข้าไม่สำเร็จ (ยกเลิกทั้งไฟล์)\n" + ex.Message); }
        }

        #endregion
    }
}
