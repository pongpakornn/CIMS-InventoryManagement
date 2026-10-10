using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CIMS.Views
{
    // เพิ่ม / แก้ไขคลัง - ตอนสร้างเสร็จ ระบบให้สิทธิ์ VIEW กับผู้ใช้ทุกคนอัตโนมัติ (CIMS.sp_Stock_GrantViewAll)
    public partial class StockEditorWindow : Window
    {
        private readonly UserSession _session;
        private readonly StockService _stockService = new StockService();
        private readonly StockModel _original;   // null = เพิ่มคลังใหม่

        public StockModel Result { get; private set; }

        public StockEditorWindow(UserSession session, StockModel stock)
        {
            InitializeComponent();
            _session = session;
            _original = stock;

            var s = stock?.Clone() ?? new StockModel
            {
                // ค่าเริ่มต้นคลังใหม่: เห็นทุกคอลัมน์, หน่วย KG, รับเข้าด้วย Import Excel + QR ระบบ, จ่ายออกด้วย QR ระบบ
                Unit = "KG", UseMaxMin = true,
                InExcel = true, InSysQr = true, OutSysQr = true
            };

            txtTitle.Text = stock == null ? "ADD STOCK" : "EDIT STOCK";
            txtSubtitle.Text = stock == null ? "Create a new stock - every user gets VIEW permission automatically"
                                             : $"{stock.Code}  •  {stock.Name}" + (stock.IsMain ? "  •  MAIN STOCK" : "");
            btnSave.Content = stock == null ? "CREATE STOCK" : "SAVE";
            btnManageFormats.Visibility = _session.CanManageFormats ? Visibility.Visible : Visibility.Collapsed;

            txtCode.Text = s.Code ?? "";
            txtName.Text = s.Name ?? "";
            cbUnit.SelectedIndex = string.Equals(s.Unit, "PCS", StringComparison.OrdinalIgnoreCase) ? 1
                                 : string.Equals(s.Unit, "SHEET", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            cbGroupBy.SelectedIndex = s.GroupByCustomer ? 1 : s.GroupBySupplier ? 2 : 0;
            // หน่วย MAX / MIN (ค่าเดิม UNIT = ตามหน่วยคลัง)
            cbMaxMinBasis.SelectedIndex = Math.Max(0, Array.IndexOf(MaxMinUnits, s.MaxMinUnit));

            chkColNo.IsChecked = s.ColNo;
            chkColModel.IsChecked = s.ColModel;
            chkAllowDecimal.IsChecked = s.AllowDecimal;
            chkMaxMinDecimal.IsChecked = s.MaxMinDecimal;
            chkCountCoil.IsChecked = s.CountCoil;
            chkCoilRows.IsChecked = s.ShowCoilRowsSetting ?? true;
            foreach (var cb in SubRowChips()) cb.IsChecked = s.CoilRowShows((string)cb.Tag);
            InitSubOrder(s);
            chkColCustomer.IsChecked = s.ColCustomer;
            chkColPartA.IsChecked = s.ColPartA;
            chkColPartNo.IsChecked = s.ColPartNo;
            chkColStockBox.IsChecked = s.ColStockBox;
            chkColStockPcs.IsChecked = s.ColStockPcs;
            chkColImage.IsChecked = s.ColImage;
            chkColCode.IsChecked = s.ColCode;
            chkColName.IsChecked = s.ColName;
            chkMaxMin.IsChecked = s.UseMaxMin;
            chkColQty.IsChecked = s.ColQty;
            chkColRemark.IsChecked = s.ColRemark;
            InitColumnOrder(s);
            // 🃏 การ์ดสินค้า: ค่าที่ใช้จริงตอนนี้ (ยังไม่เคยตั้ง = ตามคอลัมน์รูป)
            cbCardStyle.SelectedIndex = s.CardStyle == "IMAGE" ? 0 : s.CardStyle == "TEXT" ? 1 : 2;
            chkColImage.Checked += (o, e) => UpdateCardHint();
            chkColImage.Unchecked += (o, e) => UpdateCardHint();

            chkInPickList.IsChecked = s.InPickList;
            chkInSupplier.IsChecked = s.InSupplier;
            chkInSysQr.IsChecked = s.InSysQr;
            chkInExcel.IsChecked = s.InExcel;
            chkOutPickList.IsChecked = s.OutPickList;
            chkOutSupplier.IsChecked = s.OutSupplier;
            chkOutSysQr.IsChecked = s.OutSysQr;

            LoadFormats(s.FormatIds);
            UpdateFormatPanel();

            Loaded += (o, e) => txtCode.Focus();
        }

        private void cbCardStyle_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCardHint();

        // WITH IMAGE แต่ปิดคอลัมน์ IMAGE -> แจ้งว่าจะได้การ์ดแบบไม่มีรูปแทน
        private void UpdateCardHint()
        {
            if (txtCardHint == null || cbCardStyle == null) return;
            bool warn = cbCardStyle.SelectedIndex == 0 && chkColImage.IsChecked != true;
            txtCardHint.Foreground = warn ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x84, 0x90));
            txtCardHint.Text = warn
                ? "IMAGE column is off - cards will show as NO IMAGE. Tick IMAGE in TABLE COLUMNS to show pictures on the cards."
                : "Cards that scroll above the table (switch SHOW CARDS on the Store page). WITH IMAGE = product picture + details. NO IMAGE = big part number and big stock numbers, for stocks without pictures.";
        }

        private static readonly string[] MaxMinUnits = { "KG", "PCS", "SHEET", "BOX", "COIL" };

        // 📋 ลำดับคอลัมน์ตามที่ติ๊กเลือก: ติ๊กก่อน = แสดงก่อน / เอาติ๊กออก = หายจากลำดับ (ติ๊กใหม่ = ต่อท้าย)
        private readonly List<string> _colOrder = new List<string>();

        private (CheckBox Box, string Key)[] ColumnChips() => new[]
        {
            (chkColNo, "NO"), (chkColImage, "IMAGE"), (chkColCustomer, "CUSTOMER"), (chkColCode, "CODE"), (chkColPartA, "PARTA"),
            (chkColPartNo, "PARTNO"), (chkColModel, "MODEL"), (chkColName, "NAME"), (chkMaxMin, "MAXMIN"), (chkColQty, "QTY"),
            (chkColStockBox, "BOX"), (chkCountCoil, "COIL"), (chkColStockPcs, "PCS"), (chkColRemark, "REMARK")
        };

        // ช่องเลือกคอลัมน์ของแถว Coil ย่อย (Tag = COILNO / MOTHER / WEIGHT / COIL / TON / RECEIVED)
        private CheckBox[] SubRowChips() => new[] { chkSubCoilNo, chkSubMother, chkSubWeight, chkSubCoil, chkSubTon, chkSubReceived };

        // 📋 ลำดับคอลัมน์ของแถว Coil ย่อย: ติ๊กก่อน = แสดงก่อน (แบบเดียวกับคอลัมน์หลัก)
        private readonly List<string> _subOrder = new List<string>();

        private void InitSubOrder(StockModel s)
        {
            var chips = SubRowChips();
            foreach (string key in s.CoilRowOrder())
                if (chips.Any(c => (string)c.Tag == key && c.IsChecked == true)) _subOrder.Add(key);
            foreach (var cb in chips)
            {
                string key = (string)cb.Tag;
                cb.Checked += (o, e) => { if (!_subOrder.Contains(key)) _subOrder.Add(key); ShowSubOrder(); };
                cb.Unchecked += (o, e) => { _subOrder.Remove(key); ShowSubOrder(); };
            }
            ShowSubOrder();
        }

        private void ShowSubOrder()
        {
            var chips = SubRowChips();
            var names = _subOrder.Select((k, i) => $"{i + 1}. {chips.First(c => (string)c.Tag == k).Content}");
            var idx = _subOrder.Select(k => Array.IndexOf(StockModel.CoilRowKeys, k)).ToList();
            bool aligned = !idx.Zip(idx.Skip(1), (a, b) => b < a).Any(x => x);
            txtSubOrder.Text = _subOrder.Count == 0 ? "ORDER: - (no sub rows)" : "ORDER:  " + string.Join("   →   ", names) + (aligned ? "   (lined up with the table)" : "");
        }

        private void InitColumnOrder(StockModel s)
        {
            var chips = ColumnChips();
            foreach (string key in s.ColumnOrderList())
            {
                var c = chips.FirstOrDefault(x => x.Key == key);
                if (c.Box != null && c.Box.IsChecked == true) _colOrder.Add(key);
            }
            foreach (var (box, key) in chips)
            {
                box.Checked += (o, e) => { if (!_colOrder.Contains(key)) _colOrder.Add(key); ShowColumnOrder(); };
                box.Unchecked += (o, e) => { _colOrder.Remove(key); ShowColumnOrder(); };
            }
            ShowColumnOrder();
        }

        private void ShowColumnOrder()
        {
            var chips = ColumnChips();
            var names = _colOrder.Select((k, i) => $"{i + 1}. {chips.First(x => x.Key == k).Box.Content}");
            txtColumnOrder.Text = _colOrder.Count == 0 ? "ORDER: -" : "ORDER:  " + string.Join("   →   ", names);
        }

        private void LoadFormats(IEnumerable<int> selectedIds)
        {
            var selected = new HashSet<int>(selectedIds ?? Enumerable.Empty<int>());
            wpFormats.Children.Clear();
            try
            {
                foreach (var f in _stockService.GetFormats())
                {
                    wpFormats.Children.Add(new CheckBox
                    {
                        Content = f.IsActive ? f.Name : f.Name + " (INACTIVE)",
                        Tag = f.FmtId,
                        IsChecked = selected.Contains(f.FmtId),
                        Style = (Style)FindResource("StkChip")
                    });
                }
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ไม่สามารถโหลดรูปแบบบาร์โค้ดได้\n" + ex.Message);
            }
            txtNoFormats.Visibility = wpFormats.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<int> SelectedFormatIds() =>
            wpFormats.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (int)c.Tag).ToList();

        private void Channel_Changed(object sender, RoutedEventArgs e) => UpdateFormatPanel();

        // เลือกรูปแบบป้ายได้เฉพาะเมื่อเปิดช่องทาง SUPPLIER LABEL (รับเข้าหรือจ่ายออก)
        // ปุ่ม MANAGE FORMATS กดได้เสมอ (เผื่อต้องสร้างรูปแบบก่อนเปิดช่องทาง)
        private void UpdateFormatPanel()
        {
            if (wpFormats == null) return;
            wpFormats.IsEnabled = chkInSupplier.IsChecked == true || chkOutSupplier.IsChecked == true;
            wpFormats.Opacity = wpFormats.IsEnabled ? 1 : 0.5;
        }

        private void ManageFormats_Click(object sender, RoutedEventArgs e)
        {
            var keep = SelectedFormatIds();
            new BarcodeFormatWindow(_session) { Owner = this }.ShowDialog();
            LoadFormats(keep);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string code = (txtCode.Text ?? "").Trim();
            string name = (txtName.Text ?? "").Trim();

            if (string.IsNullOrEmpty(code)) { DialogHelper.ShowWarning("กรุณากรอกรหัสคลัง (STOCK CODE)"); txtCode.Focus(); return; }
            if (string.IsNullOrEmpty(name)) { DialogHelper.ShowWarning("กรุณากรอกชื่อคลัง (STOCK NAME)"); txtName.Focus(); return; }

            // รหัสคลังใช้เป็นชื่อสิทธิ์ (SystemID) -> ห้ามชนกับสิทธิ์ของระบบ และยาวได้ไม่เกิน 30 ตัว
            if (StockModel.ReservedCodes.Contains(code.ToUpperInvariant()))
            {
                DialogHelper.ShowWarning($"รหัสคลัง \"{code}\" ซ้ำกับชื่อสิทธิ์ของระบบ กรุณาใช้รหัสอื่น");
                txtCode.Focus();
                return;
            }

            if (chkColImage.IsChecked != true && chkColCode.IsChecked != true && chkColName.IsChecked != true &&
                chkMaxMin.IsChecked != true && chkColQty.IsChecked != true && chkColRemark.IsChecked != true &&
                chkColCustomer.IsChecked != true && chkColPartA.IsChecked != true && chkColPartNo.IsChecked != true &&
                chkColStockBox.IsChecked != true && chkColStockPcs.IsChecked != true && chkCountCoil.IsChecked != true &&
                chkColNo.IsChecked != true && chkColModel.IsChecked != true)
            {
                DialogHelper.ShowWarning("กรุณาเลือกคอลัมน์ที่ต้องการแสดงอย่างน้อย 1 คอลัมน์");
                return;
            }

            var formatIds = SelectedFormatIds();
            bool usesSupplier = chkInSupplier.IsChecked == true || chkOutSupplier.IsChecked == true;
            if (usesSupplier && formatIds.Count == 0)
            {
                DialogHelper.ShowWarning("เปิดช่องทาง SUPPLIER LABEL แล้ว กรุณาเลือกรูปแบบบาร์โค้ด Supplier อย่างน้อย 1 รูปแบบ");
                return;
            }

            var s = _original?.Clone() ?? new StockModel();
            s.Code = code;
            s.Name = name;
            s.Unit = cbUnit.SelectedIndex == 1 ? "PCS" : cbUnit.SelectedIndex == 2 ? "SHEET" : "KG";
            s.GroupBy = cbGroupBy.SelectedIndex == 1 ? "CUSTOMER" : cbGroupBy.SelectedIndex == 2 ? "SUPPLIER" : "CATEGORY";
            s.MaxMinBasis = MaxMinUnits[Math.Max(0, cbMaxMinBasis.SelectedIndex)];
            s.ColNo = chkColNo.IsChecked == true;
            s.ColModel = chkColModel.IsChecked == true;
            s.AllowDecimal = chkAllowDecimal.IsChecked == true;
            s.MaxMinDecimalSetting = chkMaxMinDecimal.IsChecked == true;
            s.CountCoil = chkCountCoil.IsChecked == true;
            s.ShowCoilRowsSetting = chkCoilRows.IsChecked == true;
            s.CardStyleSetting = (cbCardStyle.SelectedItem as ComboBoxItem)?.Tag as string ?? "OFF";
            // ทั้งหมดตามลำดับเริ่มต้น = null (วางตรงกับคอลัมน์ตาราง) / ไม่เลือกเลย = "-" / อื่น ๆ = ลำดับที่ติ๊ก
            var subKeys = _subOrder.ToList();
            s.CoilRowColumns = subKeys.SequenceEqual(StockModel.CoilRowKeys) ? null : subKeys.Count == 0 ? "-" : string.Join(",", subKeys);
            s.ColumnOrder = string.Join(",", _colOrder);
            s.ColCustomer = chkColCustomer.IsChecked == true;
            s.ColPartA = chkColPartA.IsChecked == true;
            s.ColPartNo = chkColPartNo.IsChecked == true;
            s.ColStockBox = chkColStockBox.IsChecked == true;
            s.ColStockPcs = chkColStockPcs.IsChecked == true;
            s.ColImage = chkColImage.IsChecked == true;
            s.ColCode = chkColCode.IsChecked == true;
            s.ColName = chkColName.IsChecked == true;
            s.UseMaxMin = chkMaxMin.IsChecked == true;
            s.ColQty = chkColQty.IsChecked == true;
            s.ColRemark = chkColRemark.IsChecked == true;
            s.InPickList = chkInPickList.IsChecked == true;
            s.InSupplier = chkInSupplier.IsChecked == true;
            s.InSysQr = chkInSysQr.IsChecked == true;
            s.InExcel = chkInExcel.IsChecked == true;
            s.OutPickList = chkOutPickList.IsChecked == true;
            s.OutSupplier = chkOutSupplier.IsChecked == true;
            s.OutSysQr = chkOutSysQr.IsChecked == true;
            s.FormatIds = usesSupplier ? formatIds : new List<int>();

            try
            {
                if (_stockService.IsStockCodeTaken(code, s.StkId))
                {
                    DialogHelper.ShowWarning($"รหัสคลัง \"{code}\" มีอยู่ในระบบแล้ว กรุณาใช้รหัสอื่น");
                    txtCode.Focus();
                    return;
                }

                if (_original == null)
                {
                    _stockService.CreateStock(s, _session.UserId);
                    LogService.WriteLog(_session.UserId, "STOCK_CREATE", $"Created stock {s.Code} ({s.Name}) | Unit: {s.Unit}", s.Code);
                }
                else
                {
                    _stockService.UpdateStock(s, _session.UserId);
                    // ชื่อสิทธิ์ = รหัสคลัง -> ผู้ใช้ที่ Login อยู่ใช้สิทธิ์ต่อได้ทันทีหลังเปลี่ยนรหัส
                    _session.RenameStockPermission(_original.PermSysId, s.PermSysId);
                    LogService.WriteLog(_session.UserId, "STOCK_UPDATE", $"Updated stock {_original.Code} -> {s.Code} ({s.Name}) | Unit: {s.Unit}", s.Code);
                }

                Result = s;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("บันทึกข้อมูลคลังไม่สำเร็จ\n" + ex.Message);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
