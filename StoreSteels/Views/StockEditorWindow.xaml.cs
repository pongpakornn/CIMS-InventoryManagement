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
    // เพิ่ม / แก้ไขคลัง - ตอนสร้างเสร็จ ระบบให้สิทธิ์ VIEW กับผู้ใช้ทุกคนอัตโนมัติ (sp_Stock_GrantViewAll)
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
            cbGroupBy.SelectedIndex = s.GroupByCustomer ? 1 : 0;
            // หน่วย MAX / MIN (ค่าเดิม UNIT = ตามหน่วยคลัง)
            cbMaxMinBasis.SelectedIndex = Math.Max(0, Array.IndexOf(MaxMinUnits, s.MaxMinUnit));

            chkColNo.IsChecked = s.ColNo;
            chkColModel.IsChecked = s.ColModel;
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

        private static readonly string[] MaxMinUnits = { "KG", "PCS", "SHEET", "BOX" };

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

            // รหัสคลังใช้เป็นชื่อสิทธิ์ (SYS_ID) -> ห้ามชนกับสิทธิ์ของระบบ และยาวได้ไม่เกิน 30 ตัว
            if (StockModel.ReservedCodes.Contains(code.ToUpperInvariant()))
            {
                DialogHelper.ShowWarning($"รหัสคลัง \"{code}\" ซ้ำกับชื่อสิทธิ์ของระบบ กรุณาใช้รหัสอื่น");
                txtCode.Focus();
                return;
            }

            if (chkColImage.IsChecked != true && chkColCode.IsChecked != true && chkColName.IsChecked != true &&
                chkMaxMin.IsChecked != true && chkColQty.IsChecked != true && chkColRemark.IsChecked != true &&
                chkColCustomer.IsChecked != true && chkColPartA.IsChecked != true && chkColPartNo.IsChecked != true &&
                chkColStockBox.IsChecked != true && chkColStockPcs.IsChecked != true &&
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
            s.GroupBy = cbGroupBy.SelectedIndex == 1 ? "CUSTOMER" : "CATEGORY";
            s.MaxMinBasis = MaxMinUnits[Math.Max(0, cbMaxMinBasis.SelectedIndex)];
            s.ColNo = chkColNo.IsChecked == true;
            s.ColModel = chkColModel.IsChecked == true;
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
