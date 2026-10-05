    using QRCoder;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media.Imaging;
    using System.Windows.Media.Animation;
    using System.Windows.Media;
    using System.Threading.Tasks; // ใส่เพิ่มเพื่อให้รองรับ Task.Run สวยๆ ครับ
    using CIMS.Helpers;
    using CIMS.Services;
    using CIMS.Models;
    using CIMS.ViewModels;

    namespace CIMS.Views
    {
        public partial class ProductControlView : Page
        {
            private readonly ProductControlViewModel _viewModel;
            private string _currentViewingCode = "";
            // ✅ แก้ไขตรงนี้: เปลี่ยนจาก _oldPartCode เป็น _oldPartACode เพื่อให้สามารถเรียกใช้งานในเมธอดด้านล่างได้
            private string _oldPartACode = "";

            public ProductControlView(UserSession session)
            {
                InitializeComponent();

                _viewModel = new ProductControlViewModel();
                _viewModel.CurrentUser = session;

                this.DataContext = _viewModel;

                // --- ปลดล็อกทุกปุ่มให้ทุกคน ---
                //btnGenerate.IsEnabled = true;
                btnRegister.Visibility = session?.CanAddProduct == true ? Visibility.Visible : Visibility.Collapsed;

                LoadStocks();
                RunEntryAnimation();
            }

            #region === [ PACKSIZE : ใส่ได้เฉพาะตัวเลข ] ===
            private static bool IsDigitsOnly(string s) => !string.IsNullOrEmpty(s) && s.All(char.IsDigit);

            private void txtPcs_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
            {
                e.Handled = !IsDigitsOnly(e.Text);
            }

            // กันการวาง (Ctrl+V) ข้อความที่ไม่ใช่ตัวเลข
            private void txtPcs_Pasting(object sender, DataObjectPastingEventArgs e)
            {
                string text = e.DataObject.GetDataPresent(DataFormats.UnicodeText) ? e.DataObject.GetData(DataFormats.UnicodeText) as string : null;
                if (!IsDigitsOnly(text?.Trim())) e.CancelCommand();
            }

            // ตรวจซ้ำก่อนบันทึก (เผื่อค่าเก่าในระบบไม่ใช่ตัวเลข)
            private bool ValidatePackSize()
            {
                string v = (txtPcs.Text ?? "").Trim();
                if (v.Length == 0 || IsDigitsOnly(v)) return true;
                DialogHelper.ShowWarning("PACKSIZE ต้องเป็นตัวเลขเท่านั้นครับ");
                txtPcs.Focus();
                return false;
            }
            #endregion

            #region === [ Multi-Stock : เลือกคลังของสินค้า ] ===
            private readonly ProductControlService _partService = new ProductControlService();
            private List<StockModel> _stocks = new List<StockModel>();

            private void LoadStocks()
            {
                try { _stocks = new StockService().GetStocks(); }
                catch (Exception ex)
                {
                    // ยังไม่ได้รัน Database/MultiStock.sql -> ใช้งานแบบเดิม (คลังหลักอย่างเดียว)
                    System.Diagnostics.Debug.WriteLine($"LoadStocks: {ex.Message}");
                    _stocks = new List<StockModel>();
                }
                SetStockChips(true, null);

                // ช่องกรองตารางตามคลัง: ALL STOCKS + ทุกคลัง
                _loadingFilter = true;
                cbStockFilter.Items.Clear();
                cbStockFilter.Items.Add(new ComboBoxItem { Content = "ALL STOCKS", Tag = null });
                foreach (var s in _stocks) cbStockFilter.Items.Add(new ComboBoxItem { Content = s.Code, Tag = s.StkId, ToolTip = s.Name });
                cbStockFilter.SelectedIndex = 0;
                cbStockFilter.Visibility = _stocks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                _loadingFilter = false;

                _viewModel.ImageStockCode = ImageStockCode;
                // โฟลเดอร์รูปของทุกคลัง (1. Image Stock\<รหัสคลัง>) - หายก็สร้างใหม่ (ทำเบื้องหลัง ไม่ให้หน้ารอ Network)
                var codes = _stocks.Select(s => s.Code).ToList();
                System.Threading.Tasks.Task.Run(() =>
                {
                    foreach (var c in codes)
                        try { CIMS.Helpers.ImagePaths.EnsureStockFolder(c); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"EnsureStockFolder {c}: {ex.Message}"); }
                });
            }

            // คลังของรูปที่อัปโหลด: คลังที่เลือกในช่องกรอง (ถ้าติ๊กไว้) > คลังแรกที่ติ๊ก > คลังในช่องกรอง > คลังหลัก
            private string ImageStockCode()
            {
                var ticked = wpStocks.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (StockModel)c.Tag).ToList();
                int? filterId = (cbStockFilter.SelectedItem as ComboBoxItem)?.Tag as int?;
                var filter = filterId.HasValue ? _stocks.FirstOrDefault(s => s.StkId == filterId.Value) : null;
                return (filter != null && ticked.Any(s => s.StkId == filter.StkId) ? filter : null)?.Code
                       ?? ticked.FirstOrDefault()?.Code
                       ?? filter?.Code
                       ?? _stocks.FirstOrDefault(s => s.IsMain)?.Code
                       ?? "STOCK";
            }

            private bool _loadingFilter;

            // เลือกคลังแล้วตารางแสดงเฉพาะสินค้าที่อยู่ในคลังนั้น
            private void cbStockFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                if (_loadingFilter || _viewModel == null) return;
                _viewModel.FilterStkId = (cbStockFilter.SelectedItem as ComboBoxItem)?.Tag as int?;
                _viewModel.LoadData(txtSearch.Text);
            }

            // Stock-CHR ติ๊ก = แสดงในตารางคลังหลัก (IsShowInMaster) / คลังอื่นติ๊ก = อยู่ในคลังนั้น
            private void SetStockChips(bool inMain, IEnumerable<int> otherIds)
            {
                var others = new HashSet<int>(otherIds ?? Enumerable.Empty<int>());
                wpStocks.Children.Clear();
                foreach (var s in _stocks)
                {
                    wpStocks.Children.Add(new CheckBox
                    {
                        Content = s.Code,
                        Tag = s,
                        ToolTip = s.Name,
                        IsChecked = s.IsMain ? inMain : others.Contains(s.StkId),
                        Style = (Style)FindResource("StkChip")
                    });
                }
            }

            private bool SelectedInMain() =>
                _stocks.Count == 0 || wpStocks.Children.OfType<CheckBox>().Any(c => c.IsChecked == true && ((StockModel)c.Tag).IsMain);

            private List<int> SelectedOtherStockIds() =>
                wpStocks.Children.OfType<CheckBox>().Where(c => c.IsChecked == true && !((StockModel)c.Tag).IsMain)
                                 .Select(c => ((StockModel)c.Tag).StkId).ToList();

            // บันทึกคลังของสินค้าหลังลงทะเบียน/แก้ไขสำเร็จ แล้วแจ้งถ้ามีคลังที่เอาออกไม่ได้ (ยังมียอด)
            // ทำงานเบื้องหลัง (Task.Run) เพื่อไม่ให้หน้าจอค้างระหว่างรอฐานข้อมูล
            private int _editPtId;   // แถวที่กำลังแก้ (PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน)

            private async Task ApplyStockSelection(string partCode, bool inMain, List<int> otherIds, int ptId = 0)
            {
                if (_stocks.Count == 0) return;
                try
                {
                    var blocked = await Task.Run(() => _partService.SyncPartStocks(partCode, inMain, otherIds, ptId));
                    var row = ptId > 0 ? _viewModel.Products.FirstOrDefault(p => p.PtId == ptId) : _viewModel.Products.FirstOrDefault(p => p.PartACode == partCode);
                    if (row != null) { row.IsShowMain = inMain; if (_viewModel.FilterStkId == null) row.IsShow = inMain; }

                    if (blocked.Count > 0)
                        DialogHelper.ShowWarning($"ไม่สามารถเอาสินค้า {partCode} ออกจากคลังต่อไปนี้ได้ เพราะยังมียอดคงเหลือ\n\n• {string.Join("\n• ", blocked)}\n\nกรุณาโอนย้ายหรือตัดยอดให้เป็น 0 ก่อน");
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError("บันทึกคลังของสินค้าไม่สำเร็จ\n" + ex.Message);
                }
            }

            // 📄 Template สำหรับ IMPORT EXCEL: หัวคอลัมน์ที่ระบบอ่านได้ + ชีทวิธีกรอก + รายชื่อรหัสคลัง
            private async void Template_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.CanAddProduct != true) { DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ Import สินค้า", "ACCESS DENIED"); return; }
                if (!DialogHelper.ShowConfirm(
                        "ต้องการสร้างไฟล์ Template สำหรับลงทะเบียนสินค้าหลายรายการใช่หรือไม่?\n\n" +
                        "• ไฟล์มีหัวคอลัมน์ที่ระบบอ่านได้ พร้อมชีทวิธีกรอก และรายชื่อรหัสคลัง\n" +
                        "• กรอกข้อมูลในชีทแรก แล้วนำเข้าด้วยปุ่ม IMPORT EXCEL\n\n" +
                        "กด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก", "TEMPLATE EXCEL")) return;
                btnTemplate.IsEnabled = false;
                try
                {
                    string path = ImportTemplateService.NewPath("Inventory_Registration_Template");
                    var stocks = _stocks;
                    await Task.Run(() =>
                    {
                        System.IO.Directory.CreateDirectory(ImportTemplateService.ExportFolder);
                        new ImportTemplateService().CreatePartTemplate(path, stocks);
                    });
                    LogService.WriteLog(_viewModel.CurrentUser?.UserId, "PART_TEMPLATE", $"File: {System.IO.Path.GetFileName(path)}", "");
                    NotificationManager.Show("Template", $"สร้างไฟล์ Template แล้ว\nDesktop\\CIMS_Export\\{System.IO.Path.GetFileName(path)}", true);
                    ImportTemplateService.OpenFile(path);   // เปิดไฟล์ Template ขึ้นมาเลย
                }
                catch (Exception ex)
                {
                    string msg = ex.Message.Contains("being used") ? "กรุณาปิดไฟล์ Excel ก่อน" : ex.Message;
                    DialogHelper.ShowError("สร้างไฟล์ Template ไม่สำเร็จ\n" + msg);
                }
                finally { btnTemplate.IsEnabled = true; }
            }

            // 📥 Import Excel ลงทะเบียนสินค้าใหม่หลายรายการ (รหัสที่มีอยู่แล้วจะถูกข้าม)
            private async void Import_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.CanAddProduct != true) { DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ Import สินค้า", "ACCESS DENIED"); return; }
                var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import Excel -> Inventory Registration", Filter = "Excel Files (*.xlsx)|*.xlsx" };
                if (dlg.ShowDialog() != true) return;

                var service = new PartImportService();
                List<PartImportRow> rows;
                try
                {
                    var stocks = _stocks;
                    rows = await Task.Run(() => service.ReadExcel(dlg.FileName, stocks));
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message);
                    return;
                }

                var valid = rows.Where(r => r.IsValid).ToList();
                var invalid = rows.Where(r => !r.IsValid).ToList();
                string errors = string.Join("\n", invalid.Take(8).Select(r => $"• แถว {r.RowNumber}: {(string.IsNullOrEmpty(r.Code) ? "-" : r.Code)} - {r.Error}"))
                              + (invalid.Count > 8 ? $"\n• ... และอีก {invalid.Count - 8:N0} แถว" : "");

                if (valid.Count == 0)
                {
                    DialogHelper.ShowError("ไม่พบรายการที่ลงทะเบียนได้ในไฟล์นี้\n\n" + errors);
                    return;
                }

                string summary = $"ไฟล์: {Path.GetFileName(dlg.FileName)}\n\nลงทะเบียนสินค้าใหม่ได้ {valid.Count:N0} รายการ" +
                                 (invalid.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {invalid.Count:N0} แถว:\n{errors}" : "") +
                                 "\n\nยืนยันการลงทะเบียนหรือไม่?";
                if (!DialogHelper.ShowConfirm(summary, "CONFIRM IMPORT")) return;

                try
                {
                    int count = await Task.Run(() => service.ApplyImport(rows));
                    string uid = _viewModel.CurrentUser?.UserId ?? "Unknown";
                    LogService.WriteLog(uid, "REGISTER_PART_IMPORT",
                        $"Import Excel: {Path.GetFileName(dlg.FileName)} | Registered: {count} | Skipped rows: {invalid.Count} | Codes: {string.Join(", ", valid.Select(v => v.Code).Take(50))}", "");
                    DialogHelper.ShowSuccess($"ลงทะเบียนสินค้าใหม่สำเร็จ {count:N0} รายการ");
                    _viewModel.LoadData(txtSearch.Text);
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError("ลงทะเบียนไม่สำเร็จ (ยกเลิกทั้งไฟล์ ไม่มีรายการใดถูกบันทึก)\n" + ex.Message);
                }
            }
            #endregion

            private void btnSelectImage_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel != null && (_viewModel.CurrentUser?.CanAddProduct == true || _viewModel.CurrentUser?.CanEditProduct == true))
                {
                    _viewModel.SelectProductImage();
                }
            }

            private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
            {
                if (_viewModel != null)
                {
                    _viewModel.SearchText = txtSearch.Text;
                }
            }

            private void Find_Click(object sender, RoutedEventArgs e)
            {
                _viewModel.LoadData(txtSearch.Text);
            }

            private async void RegisterPart_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.CanAddProduct != true) { DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ลงทะเบียนสินค้า", "ACCESS DENIED"); return; }
                if (string.IsNullOrEmpty(txtProductCode.Text))
                {
                    DialogHelper.ShowWarning("กรุณากรอกรหัสสินค้าก่อนทำการลงทะเบียนครับ");
                    return;
                }
                if (!ValidatePackSize()) return;

                if (_stocks.Count > 0 && !wpStocks.Children.OfType<CheckBox>().Any(c => c.IsChecked == true))
                {
                    DialogHelper.ShowWarning("กรุณาเลือกคลัง (STOCK) อย่างน้อย 1 คลังครับ");
                    return;
                }

                var newModel = GetModelFromInputs();
                bool inMain = SelectedInMain();
                var otherIds = SelectedOtherStockIds();

                // บันทึกข้อมูล
                bool result = await _viewModel.SaveToDb(newModel, isUpdate: false);

                if (result)
                {
                    await ApplyStockSelection(newModel.PartACode, inMain, otherIds, newModel.PtId);
                    DialogHelper.ShowSuccess("ลงทะเบียนสินค้าใหม่เรียบร้อยแล้ว!");
                    ClearOnlyInputs();
                }
                else
                {
                    // เพิ่มจุดนี้เข้าไปเพื่อให้รู้ตัวทันทีว่าฟังก์ชันส่ง False กลับมา
                    DialogHelper.ShowWarning("การลงทะเบียนถูกปฏิเสธ (ตรวจสอบข้อผิดพลาดด้านบน)");
                }
            }
            private async void SaveEdit_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.CanEditProduct != true) { DialogHelper.ShowWarning("คุณไม่มีสิทธิ์แก้ไขสินค้า", "ACCESS DENIED"); return; }
                if (!ValidatePackSize()) return;
                if (_stocks.Count > 0 && !wpStocks.Children.OfType<CheckBox>().Any(c => c.IsChecked == true))
                {
                    DialogHelper.ShowWarning("กรุณาเลือกคลัง (STOCK) อย่างน้อย 1 คลังครับ");
                    return;
                }

                var editModel = GetModelFromInputs();
                editModel.PtId = _editPtId;
                bool inMain = SelectedInMain();
                var otherIds = SelectedOtherStockIds();

                // 🎯 ส่งพารามิเตอร์ตัวสุดท้ายเป็น _oldPartACode (เดิมส่ง _oldPartCode)
                bool result = await _viewModel.SaveToDb(editModel, isUpdate: true, oldCode: _oldPartACode);

                if (result)
                {
                    await ApplyStockSelection(editModel.PartACode, inMain, otherIds, _editPtId);
                    _editPtId = 0;
                    DialogHelper.ShowSuccess("แก้ไขข้อมูลเรียบร้อยแล้ว!");
                    SetEditMode(false);
                    ClearOnlyInputs();
                    _oldPartACode = null; // ล้างค่าหลังทำงานสำเร็จ
                }
            }

            private void ViewAction_Click(object sender, RoutedEventArgs e)
            {
                if (sender is Button btn && btn.DataContext is ProductControlModel selected)
                {
                    if (_currentViewingCode == selected.PartACode)
                    {
                        // กดซ้ำ: ล้างแค่ช่องกรอกข้อมูล ไม่ล้างตาราง
                        ClearOnlyInputs();
                        dgQRHistory.SelectedItem = null;
                    }
                    else
                    {
                        // กดใหม่: เติมข้อมูล
                        _currentViewingCode = selected.PartACode;
                        FillInputsFromModel(selected);
                        GenerateQRPreviewLogic();
                        dgQRHistory.SelectedItem = selected;
                    }
                }
            }

            private void EditPD_Click(object sender, RoutedEventArgs e)
            {
                if (sender is Button btn && btn.DataContext is ProductControlModel selected)
                {
                    if (btnSave.Visibility == Visibility.Visible && _oldPartACode == selected.PartACode)
                    {
                        // กดซ้ำ: ล้างแค่ช่องกรอกข้อมูล + ปิดโหมดแก้ไข
                        ClearOnlyInputs();
                        SetEditMode(false);
                        dgQRHistory.SelectedItem = null;
                    }
                    else
                    {
                        // กดใหม่: เข้าโหมดแก้ไข
                        FillInputsFromModel(selected);
                        _currentViewingCode = selected.PartACode;
                        _oldPartACode = selected.PartACode;
                        _editPtId = selected.PtId;   // PRODUCT CODE ซ้ำได้ -> แก้แถวนี้ด้วย PartID
                        SetEditMode(true);
                        txtProductName.Focus();
                        GenerateQRPreviewLogic();
                        dgQRHistory.SelectedItem = selected;
                    }
                }
            }

            private async void DeletePD_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.CanDeleteProduct != true) return;
                if (sender is Button btn && btn.DataContext is ProductControlModel selected)
                {
                    if (DialogHelper.ShowConfirm($"ลบสินค้า: {selected.PartName}?", "CONFIRM"))
                    {
                        if (await _viewModel.DeleteProductAsync(selected))
                        {
                            DialogHelper.ShowSuccess("ลบสำเร็จ!");
                        }
                        else
                        {
                            DialogHelper.ShowError($"ลบสินค้า {selected.PartCode} ไม่สำเร็จ\n\n{_viewModel.LastDeleteError ?? "ไม่ทราบสาเหตุ"}");
                        }
                    }
                }
            }

            private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.CanEditProduct != true) return;
                if (sender is Button btn && btn.DataContext is ProductControlModel selected)
                {
                    if (_viewModel.ToggleStatus(selected))
                    {
                        DialogHelper.ShowSuccess(selected.IsShow ? "แสดงรายการแล้ว" : "ซ่อนรายการแล้ว");
                    }
                }
            }

            private void SelectAll_Click(object sender, RoutedEventArgs e)
            {
                var checkBox = sender as CheckBox;
                if (checkBox == null || _viewModel?.Products == null) return;

                bool isChecked = checkBox.IsChecked ?? false;

                foreach (var product in _viewModel.Products)
                {
                    product.IsSelected = isChecked;
                }

                dgQRHistory.Items.Refresh();
            }

            // --- UI Helpers ---
            private ProductControlModel GetModelFromInputs()
            {
                // schema ใหม่ตัด PartACode/Model/PT_NO ออกจาก CIMS.Parts แล้ว PartCode เป็นตัวระบุหลักตัวเดียว
                // PartACode จึงมิเรอร์ค่าจาก PartCode เพื่อให้ logic เดิมที่อ้างอิง PartACode (lookup แถว, edit, delete) ยังทำงานถูกต้อง
                string code = txtProductCode.Text.Trim();
                return new ProductControlModel
                {
                    CustomerCode = txtSupplier.Text.Trim(),
                    Category = txtCategory.Text.Trim(),
                    PartCode = code,
                    PartACode = code,
                    PartName = txtProductName.Text.Trim(),
                    PackSize = txtPcs.Text.Trim(),
                    Location = txtBin.Text.Trim(),
                    QRCodeData = txtQRCode.Text.Trim(),
                    Customer = txtCustomer.Text.Trim(),
                    PartA = txtPartA.Text.Trim(),
                    PartNo = txtPartNo.Text.Trim(),
                    Model = txtModel.Text.Trim(),
                    ImageFileName = _viewModel.SelectedProduct?.ImageFileName
                };
            }

            private void RefreshUI()
            {
                ClearOnlyInputs();
                _viewModel.LoadData();
            }

        private void ClearOnlyInputs()
        {
            // เคลียร์ TextBox
            txtSupplier.Clear();
            txtCategory.Clear();
            txtProductCode.Clear();
            txtProductName.Clear();
            txtPcs.Clear();
            txtBin.Clear();
            txtQRCode.Clear();
            txtCustomer.Clear();
            txtPartA.Clear();
            txtPartNo.Clear();
            txtModel.Clear();

            // 🎯 หัวใจสำคัญ: สร้างตัวใหม่ไปเลย เพื่อตัดความสัมพันธ์กับ Row เดิมในตาราง
            _viewModel.SelectedProduct = new ProductControlModel();

            // คลังกลับเป็นค่าเริ่มต้น (Stock-CHR)
            SetStockChips(true, null);

            _currentViewingCode = string.Empty;
            _oldPartACode = null;

            // เคลียร์ Selection ในตาราง
            dgQRHistory.SelectedItem = null;

            // บังคับให้ตาราง Refresh เพื่อให้แน่ใจว่าแสดงผลค่าล่าสุด
            dgQRHistory.Items.Refresh();
        }

        private void FillInputsFromModel(ProductControlModel model)
                {
            _viewModel.SelectedProduct = new ProductControlModel
            {
                CustomerCode = model.CustomerCode,
                Category = model.Category,
                PartCode = model.PartCode,
                PartACode = model.PartACode,
                PartName = model.PartName,
                PackSize = model.PackSize,
                Location = model.Location,
                QRCodeData = model.QRCodeData,
                Customer = model.Customer,
                PartA = model.PartA,
                PartNo = model.PartNo,
                Model = model.Model,
                ImageFileName = model.ImageFileName
                // ถ้ามี Field อื่นให้เพิ่มตรงนี้
            };

            // แสดงคลังที่สินค้านี้อยู่ตอนนี้
            List<int> otherIds = null;
            try { if (_stocks.Count > 0) otherIds = _partService.GetPartStockIds(model.PartCode, model.PtId); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"GetPartStockIds: {ex.Message}"); }
            SetStockChips(model.IsShowMain, otherIds);
        }

                private void SetEditMode(bool isEdit)
                {
                    btnRegister.Visibility = isEdit ? Visibility.Collapsed : (_viewModel.CurrentUser?.CanAddProduct == true ? Visibility.Visible : Visibility.Collapsed);
                    btnSave.Visibility = isEdit && _viewModel.CurrentUser?.CanEditProduct == true ? Visibility.Visible : Visibility.Collapsed;
                    txtProductCode.IsEnabled = true;
                    txtProductCode.Opacity = 1.0;
                }

            // --- QR Logic ---
            private void GenerateQR_Click(object sender, RoutedEventArgs e)
            {
                GenerateQRPreviewLogic();
            }

            private void GenerateQRPreviewLogic()
            {
                try
                {
                    string qrText = txtProductCode.Text.Trim();
                    if (string.IsNullOrEmpty(qrText)) return;

                    using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                    {
                        using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.Q))
                        {
                            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
                            {
                                byte[] qrCodeAsPngByteArr = qrCode.GetGraphic(20);

                                using (MemoryStream ms = new MemoryStream(qrCodeAsPngByteArr))
                                {
                                    BitmapImage bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.StreamSource = ms;
                                    bitmap.EndInit();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError($"ไม่สามารถสร้าง QR Code ได้: {ex.Message}");
                }
            }

            private void RunEntryAnimation()
            {
                if (PageTransform != null)
                {
                    DoubleAnimation anim = new DoubleAnimation
                    {
                        To = 0,
                        Duration = TimeSpan.FromSeconds(0.4),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    PageTransform.BeginAnimation(TranslateTransform.YProperty, anim);
                }
            }

            private void dgQRHistory_ScrollChanged(object sender, ScrollChangedEventArgs e)
            {
                // ไว้สำหรับจัดการ Infinite Scroll หรือโหลดหน้าเพิ่มในกรณีที่เปิดใช้งาน Virtualization
            }

            // ✅ นำฟังก์ชันกลับเข้ามาอยู่ก่อนปีกกาปิดคลาสอันแรกแล้วครับ
            // 🗑 ADMIN DELETE (Level 1): เลือกคลังในหน้าต่าง แล้วลบรายการที่ติ๊ก / ลบทั้งหมดในคลัง
            private void AdminDelete_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.UserLevel != 1)
                {
                    DialogHelper.ShowWarning("เฉพาะผู้ดูแลระบบ (Level 1) เท่านั้นที่ใช้ปุ่มนี้ได้", "ACCESS DENIED");
                    return;
                }
                var ticked = _viewModel.Products.Where(x => x.IsSelected && x.PtId > 0).Select(x => x.PtId).Distinct().ToList();
                var w = new AdminDeleteWindow(_viewModel.CurrentUser, _stocks, ticked);
                if (w.ShowDialog() == true) _viewModel.LoadData(txtSearch.Text);
            }

            private async void Export_Click(object sender, RoutedEventArgs e)
            {
                var selectedItems = _viewModel.Products.Where(x => x.IsSelected).ToList();

                if (selectedItems.Count == 0)
                {
                    DialogHelper.ShowWarning("กรุณาเลือกรายการที่ต้องการ Export อย่างน้อย 1 รายการครับ");
                    return;
                }

                try
                {
                    string currentUserId = _viewModel.CurrentUser?.UserId ?? "Unknown";

                    await Task.Run(() =>
                    {
                        var exportService = new ExportService();
                        exportService.GenerateA4Pdf(selectedItems);
                    });

                    foreach (var item in selectedItems)
                    {
                        LogService.WriteLog(currentUserId, "EXPORT_QR_PDF", $"Exported QR Code to PDF for Part: {item.PartName}", item.PartCode);
                    }

                    DialogHelper.ShowSuccess("ส่งออกไฟล์ QR ไปที่โฟลเดอร์ Desktop\\CIMS_Export เรียบร้อยแล้ว!");
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError($"Export ไม่สำเร็จ: {ex.Message}");
                }
            }
        } // 👈 ปิดตัวคลาส ProductControlView
    } // 👈 ปิดตัวเนมสเปซ CIMS.Views
