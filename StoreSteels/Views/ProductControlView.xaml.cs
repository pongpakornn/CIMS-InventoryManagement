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
    using Brush = System.Windows.Media.Brush;
    using Brushes = System.Windows.Media.Brushes;
    using Color = System.Windows.Media.Color;
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
            private readonly string _userId;   // คลังที่เลือกค้างไว้จำตาม UserID (CIMS.UserSettings)
            private const string KeyFilter = "ProductControl.Filter", KeyStocks = "ProductControl.Stocks";
            // ✅ แก้ไขตรงนี้: เปลี่ยนจาก _oldPartCode เป็น _oldPartACode เพื่อให้สามารถเรียกใช้งานในเมธอดด้านล่างได้
            private string _oldPartACode = "";

            public ProductControlView(UserSession session)
            {
                InitializeComponent();

                _viewModel = new ProductControlViewModel();
                _viewModel.CurrentUser = session;
                _userId = session?.UserId;

                this.DataContext = _viewModel;

                // --- ปลดล็อกทุกปุ่มให้ทุกคน ---
                //btnGenerate.IsEnabled = true;
                btnRegister.Visibility = session?.CanAddProduct == true ? Visibility.Visible : Visibility.Collapsed;
                // 🛠 ADMIN: Level 1 เท่านั้น (ผู้ใช้อื่นไม่เห็นปุ่ม)
                btnAdmin.Visibility = session?.UserLevel == 1 ? Visibility.Visible : Visibility.Collapsed;

                SetupSuggestions();
                LoadStocks();
                RunEntryAnimation();

                // ⚡ เรียลไทม์: เครื่องอื่นลงทะเบียน / แก้ / ลบสินค้า -> ตารางอัพเดทเอง (คงคำค้น / คลังที่กรอง / ที่ติ๊ก / ตำแหน่งเลื่อน)
                //    ดูเฉพาะข้อมูลที่หน้านี้แสดง (ยอดคงคลังเปลี่ยนจากการสแกนไม่ทำให้โหลดใหม่)
                LiveRefresh.Attach(this, TimeSpan.FromSeconds(3),
                    () => LiveRefresh.DbToken(
                        $@"SELECT COUNT(*), CHECKSUM_AGG(CHECKSUM(PartID, PartCode, Description, PackSize, Bin, QRCode, Category, Supplier, Customer,
                                                                 PartA, PartNumber, Model, ImageFileName, IsActive, IsShowInMaster{(DbSchema.HasPartB ? ", PartB" : "")})) FROM CIMS.Parts",
                        "SELECT COUNT(*), CHECKSUM_AGG(CHECKSUM(PartID, StockID, IsShow)) FROM CIMS.PartStocks"),
                    async () =>
                    {
                        var sv = FindScroll(dgQRHistory);
                        double y = sv?.VerticalOffset ?? 0;
                        await _viewModel.LiveReloadAsync(txtSearch.Text);
                        if (sv != null && y > 0) { dgQRHistory.UpdateLayout(); sv.ScrollToVerticalOffset(y); }
                    });
            }

            private static ScrollViewer FindScroll(DependencyObject root)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    if (c is ScrollViewer sv) return sv;
                    var r = FindScroll(c);
                    if (r != null) return r;
                }
                return null;
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
                SetDefaultStockChips();
                LoadLayouts();

                // ช่องกรองตารางตามคลัง: ALL STOCKS + ทุกคลัง
                _loadingFilter = true;
                cbStockFilter.Items.Clear();
                cbStockFilter.Items.Add(new ComboBoxItem { Content = "ALL STOCKS", Tag = null });
                foreach (var s in _stocks) cbStockFilter.Items.Add(new ComboBoxItem { Content = s.Code, Tag = s.StkId, ToolTip = s.Name });
                // คลังที่ผู้ใช้เลือกดูไว้ครั้งก่อน (ไม่มีแล้ว = ALL STOCKS)
                string savedFilter = UserSettings.Get(_userId, KeyFilter, "");
                var savedItem = cbStockFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is int id && id.ToString() == savedFilter);
                cbStockFilter.SelectedItem = savedItem ?? cbStockFilter.Items[0];
                if (savedItem != null) _viewModel.FilterStkId = (int)savedItem.Tag;
                ApplyLayout();
                if (savedItem != null || _viewModel.Layout.Columns.Contains("MOTHER") || _viewModel.Layout.Columns.Contains("COILNO")) _viewModel.LoadData(txtSearch.Text);
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
                UserSettings.Set(_userId, KeyFilter, _viewModel.FilterStkId?.ToString() ?? "");   // เลือกค้างไว้ (ตาม UserID)
                ApplyLayout();
                _viewModel.LoadData(txtSearch.Text);
            }

            #region === [ ตารางของแต่ละคลัง (ตั้งที่ปุ่ม ADMIN) ] ===
            private Dictionary<int, ProductControlLayout> _layouts = new Dictionary<int, ProductControlLayout>();

            private void LoadLayouts()
            {
                try { _layouts = new ProductControlLayoutService().GetAll(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadLayouts: {ex.Message}"); _layouts = new Dictionary<int, ProductControlLayout>(); }
            }

            // คลังในช่องกรอง (ALL STOCKS = 0) -> คอลัมน์ตามลำดับที่ตั้งไว้ + จัดกลุ่ม / ยังไม่ได้ตั้ง = ตารางเดิม
            private ProductControlLayout CurrentLayout()
            {
                int id = _viewModel.FilterStkId ?? 0;
                return _layouts.TryGetValue(id, out var l) ? l : ProductControlColumns.Default(id);
            }

            private Dictionary<string, DataGridColumn> ColumnMap() => new Dictionary<string, DataGridColumn>
            {
                ["IMAGE"] = colImage, ["SUPPLIER"] = colSupplier, ["CUSTOMER"] = colCustomer, ["CATEGORY"] = colCategory,
                ["PARTA"] = colPartA, ["PARTB"] = colPartB, ["MODEL"] = colModel, ["CODE"] = colCode, ["PARTNO"] = colPartNo,
                ["NAME"] = colName, ["PACKSIZE"] = colPackSize, ["BIN"] = colBin, ["MOTHER"] = colMother, ["COILNO"] = colCoilNo, ["QRCODE"] = colQr
            };

            private void ApplyLayout()
            {
                var layout = CurrentLayout();
                var map = ColumnMap();
                // ช่องติ๊กเลือกหน้าสุด -> คอลัมน์ที่เลือกตามลำดับ -> คอลัมน์ที่ไม่แสดง -> ACTION ท้ายสุด
                var order = new List<DataGridColumn> { colSelect };
                order.AddRange(layout.Columns.Where(map.ContainsKey).Select(k => map[k]));
                order.AddRange(map.Values.Where(c => !order.Contains(c)));
                order.Add(colAction);
                for (int i = 0; i < order.Count; i++) order[i].DisplayIndex = i;
                foreach (var kv in map) kv.Value.Visibility = layout.Columns.Contains(kv.Key) ? Visibility.Visible : Visibility.Collapsed;
                // 🧲 คลังที่นับ Coil + เปิดแถว Coil ย่อย (ตั้งที่ EDIT STOCK) -> กด PRODUCT CODE ดู Coil แม่ / Coil ลูกได้
                var coilStock = _stocks.FirstOrDefault(s => s.StkId == _viewModel.FilterStkId);
                _viewModel.CoilStockId = coilStock != null && coilStock.ShowCoilRows && DbSchema.HasCoilRegister ? coilStock.StkId : (int?)null;
                _viewModel.Layout = layout;
                ResetWidthsLater();
            }

            // ตารางว่าง (คลังยังไม่มีสินค้า) แล้วเปลี่ยนคอลัมน์ -> DataGrid คำนวณความกว้างแบบ * เป็น 0 (หัวตารางหาย)
            //   ตั้งความกว้างเดิมของทุกคอลัมน์ใหม่หลังจัดหน้าเสร็จ / ตอนตารางว่างได้แถวแรก
            private Dictionary<DataGridColumn, DataGridLength> _widths;
            private bool _hookedRows;

            private void ResetWidthsLater()
            {
                if (_widths == null) _widths = dgQRHistory.Columns.ToDictionary(c => c, c => c.Width);
                if (!_hookedRows)
                {
                    _hookedRows = true;
                    _viewModel.Products.CollectionChanged += (s, e) => { if (_viewModel.Products.Count == 1) ResetWidthsLater(); };
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var kv in _widths) { kv.Key.Width = new DataGridLength(0); kv.Key.Width = kv.Value; }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            #endregion

            #region === [ 🧲 แถว Coil แม่ / Coil ลูก (คลังที่นับ Coil) - แบบหน้า Store (Max-Min) ] ===
            // แถวถูกใช้ซ้ำ (Recycling) -> เปิด / ปิดแถวย่อยตามสินค้าของแถวนั้น
            private void dgQRHistory_LoadingRow(object sender, DataGridRowEventArgs e)
            {
                e.Row.DetailsVisibility = e.Row.Item is ProductControlModel p && p.IsCoilOpen ? Visibility.Visible : Visibility.Collapsed;
            }

            // กด PRODUCT CODE (มีป้าย ▸ n COIL) -> เปิด / ปิด Coil ของสินค้านั้น
            private async void dgQRHistory_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (!_viewModel.CoilStockId.HasValue) return;
                // DataGrid จับเมาส์ไว้ตอนเลือกเซลล์ -> หาเซลล์จากจุดที่กด
                DependencyObject dep = VisualTreeHelper.HitTest(dgQRHistory, e.GetPosition(dgQRHistory))?.VisualHit;
                while (dep != null && !(dep is DataGridCell) && !(dep is System.Windows.Controls.Primitives.DataGridDetailsPresenter))
                    dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(dep) : LogicalTreeHelper.GetParent(dep);
                if (!(dep is DataGridCell cell) || cell.Column != colCode || !(cell.DataContext is ProductControlModel p) || p.CoilCount <= 0) return;

                if (p.IsCoilOpen)
                {
                    p.IsCoilOpen = false; _viewModel.OpenCoilParts.Remove(p.PtId); ShowCoilRows(p);
                    return;
                }
                int stk = _viewModel.CoilStockId.Value, ptId = p.PtId;
                try
                {
                    p.CoilRows = await Task.Run(() => new CoilService().GetCoilRows(stk, ptId));
                    p.IsCoilOpen = true; _viewModel.OpenCoilParts.Add(ptId); _justOpened.Add(ptId);
                    ShowCoilRows(p);
                }
                catch (Exception ex) { NotificationManager.Show("COIL", "โหลดรายการ Coil ไม่สำเร็จ\n" + ex.Message, false); }
            }

            private void ShowCoilRows(ProductControlModel p)
            {
                if (dgQRHistory.ItemContainerGenerator.ContainerFromItem(p) is DataGridRow row)
                    row.DetailsVisibility = p.IsCoilOpen ? Visibility.Visible : Visibility.Collapsed;
            }

            private void CoilDetails_Loaded(object sender, RoutedEventArgs e)
            {
                if (!(sender is StackPanel sp)) return;
                if (sp.Tag == null) { sp.Tag = "hooked"; sp.DataContextChanged += (s, a) => BuildCoilRows(sp); sp.IsVisibleChanged += (s, a) => { if (sp.IsVisible) BuildCoilRows(sp); }; }
                BuildCoilRows(sp);
            }

            // 🧲 แบบเดียวกับหน้า Store (Max-Min) - Helpers/CoilRowsView: เต็มความกว้างตาราง แถบ Coil แม่ -> Coil ลูก ตามคอลัมน์ที่คลังตั้งไว้
            //    แอนิเมชันเฉพาะตอนกดเปิด (ตารางอัพเดทเอง = ไม่เล่นซ้ำ)
            private readonly HashSet<int> _justOpened = new HashSet<int>();

            private void BuildCoilRows(StackPanel sp)
            {
                sp.Children.Clear();
                if (!(sp.DataContext is ProductControlModel p) || !p.IsCoilOpen) return;
                var stock = _stocks.FirstOrDefault(s => s.StkId == _viewModel.CoilStockId);
                CoilRowsView.Build(sp, p.CoilRows, stock, (Brush)FindResource("MainPurple"), _justOpened.Remove(p.PtId));
            }
            #endregion

            #region === [ SUPPLIER / CUSTOMER / CATEGORY : พิมพ์แล้วขึ้นค่าที่มีในระบบให้เลือก ] ===
            private SuggestBox _sgSupplier, _sgCustomer, _sgCategory;

            private void SetupSuggestions()
            {
                _sgSupplier = SuggestBox.Attach(txtSupplier);
                _sgCustomer = SuggestBox.Attach(txtCustomer);
                _sgCategory = SuggestBox.Attach(txtCategory);
                LoadSuggestions();
            }

            private async void LoadSuggestions()
            {
                try
                {
                    var v = await Task.Run(() => new ProductControlLayoutService().GetSuggestions());
                    _sgSupplier.SetValues(v.Suppliers);
                    _sgCustomer.SetValues(v.Customers);
                    _sgCategory.SetValues(v.Categories);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadSuggestions: {ex.Message}"); }
            }

            private void RememberSuggestions(ProductControlModel m)
            {
                _sgSupplier?.Add(m.CustomerCode);
                _sgCustomer?.Add(m.Customer);
                _sgCategory?.Add(m.Category);
            }
            #endregion

            // คลังที่ติ๊กไว้ตอนลงทะเบียนสินค้าใหม่: จำค่าที่ผู้ใช้ติ๊กครั้งล่าสุด "main|id,id" (ยังไม่เคยเลือก = คลังหลัก)
            private void SetDefaultStockChips()
            {
                bool inMain = true; var ids = new List<int>();
                string v = UserSettings.Get(_userId, KeyStocks, "");
                if (!string.IsNullOrEmpty(v))
                {
                    var parts = v.Split('|');
                    inMain = parts[0] == "1";
                    if (parts.Length > 1)
                        foreach (var t in parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            if (int.TryParse(t, out int id) && _stocks.Any(x => x.StkId == id && !x.IsMain)) ids.Add(id);
                    if (!inMain && ids.Count == 0) inMain = true;
                }
                SetStockChips(inMain, ids);
            }

            private void SaveDefaultStockChips() =>
                UserSettings.Set(_userId, KeyStocks, (SelectedInMain() ? "1" : "0") + "|" + string.Join(",", SelectedOtherStockIds()));

            // Stock-CHR ติ๊ก = แสดงในตารางคลังหลัก (IsShowInMaster) / คลังอื่นติ๊ก = อยู่ในคลังนั้น
            private void SetStockChips(bool inMain, IEnumerable<int> otherIds)
            {
                var others = new HashSet<int>(otherIds ?? Enumerable.Empty<int>());
                wpStocks.Children.Clear();
                foreach (var s in _stocks)
                {
                    var chip = new CheckBox
                    {
                        Content = s.Code,
                        Tag = s,
                        ToolTip = s.Name,
                        IsChecked = s.IsMain ? inMain : others.Contains(s.StkId),
                        Style = (Style)FindResource("StkChip")
                    };
                    // ติ๊กตอนเพิ่มสินค้าใหม่ (ไม่ได้เปิดสินค้าเดิมอยู่) = จำเป็นค่าเริ่มต้นของผู้ใช้
                    chip.Click += (o, e) => { if (string.IsNullOrEmpty(_currentViewingCode)) SaveDefaultStockChips(); };
                    wpStocks.Children.Add(chip);
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
                var user = _viewModel.CurrentUser;
                if (user?.CanAddProduct != true) { DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ Import สินค้า", "ACCESS DENIED"); return; }

                // เลือกคลัง -> Template ตามคอลัมน์ที่คลังนั้นแสดงใน Store (Max-Min) / ALL COLUMNS = ทุกคอลัมน์ทุกคลัง
                var options = new List<TemplateChoiceWindow.Option>
                {
                    new TemplateChoiceWindow.Option { Key = "", Title = "ALL COLUMNS (ทุกคลัง)", Detail = "ทุกคอลัมน์ที่ระบบอ่านได้ ใส่รหัสคลังเองที่ STOCK1 - STOCK3", UseWith = "IMPORT EXCEL  •  Inventory Registration / Store (Max-Min)" }
                };
                foreach (var s in _stocks.Where(s => user.CanViewStock(s) && !s.IsLiveView))
                    options.Add(new TemplateChoiceWindow.Option
                    {
                        Key = s.StkId.ToString(),
                        Title = $"{s.Code}  •  {s.Name}",
                        Detail = string.Join(" · ", ImportTemplateService.PartHeadsFor(s).Where(h => h != "NO")),
                        UseWith = $"คอลัมน์ตาม Store (Max-Min) ของ {s.Code}  •  STOCK1 = {s.Code}" + (s.CountCoil ? "  •  มี COIL NO / MOTHER COIL" : "")
                    });
                string key = TemplateChoiceWindow.Choose("เลือกคลัง: ระบบสร้าง Template ตามคอลัมน์ที่คลังนั้นแสดงใน Store (Max-Min)", options.ToArray());
                if (key == null) return;
                var forStock = key.Length == 0 ? null : _stocks.FirstOrDefault(s => s.StkId.ToString() == key);

                if (!DialogHelper.ShowConfirm(
                        $"ต้องการสร้างไฟล์ Template สำหรับลงทะเบียนสินค้าหลายรายการ{(forStock == null ? "" : $" ของคลัง {forStock.Code}")} ใช่หรือไม่?\n\n" +
                        (forStock == null ? "• ไฟล์มีหัวคอลัมน์ที่ระบบอ่านได้ทั้งหมด พร้อมชีทวิธีกรอก และรายชื่อรหัสคลัง\n"
                                          : $"• คอลัมน์ตามที่ {forStock.Code} แสดงใน Store (Max-Min) และ STOCK1 = {forStock.Code} ใส่ให้แล้ว\n") +
                        "• กรอกข้อมูลในชีทแรก แล้วนำเข้าด้วยปุ่ม IMPORT EXCEL\n\n" +
                        "กด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก", "TEMPLATE EXCEL")) return;
                btnTemplate.IsEnabled = false;
                try
                {
                    string path = ImportTemplateService.NewPath(ImportTemplateService.Systems.Product, forStock == null ? "ALL_STOCKS_Template" : $"{forStock.Code}_Template");
                    var stocks = _stocks;
                    await Task.Run(() => new ImportTemplateService().CreatePartTemplate(path, stocks, forStock));
                    LogService.WriteLog(_viewModel.CurrentUser?.UserId, "PART_TEMPLATE", $"File: {System.IO.Path.GetFileName(path)} | Stock: {forStock?.Code ?? "ALL"}", "");
                    NotificationManager.Show("Template", $"สร้างไฟล์ Template แล้ว\n{ImportTemplateService.ShortPath(path)}", true);
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

                int products = valid.Count(r => !r.IsCoilExtra), coilCount = valid.Count(r => r.CoilNo != null);
                string summary = $"ไฟล์: {Path.GetFileName(dlg.FileName)}\n\nลงทะเบียนสินค้าใหม่ได้ {products:N0} รายการ" +
                                 (coilCount > 0 ? $"\nCOIL {coilCount:N0} ลูก  •  น้ำหนักรวม {valid.Where(r => r.CoilNo != null).Sum(r => r.CoilWeight):#,##0.###} KG (STOCK (UNIT) ของสินค้า = น้ำหนักรวม Coil ในทะเบียน)" : "") +
                                 (invalid.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {invalid.Count:N0} แถว:\n{errors}" : "") +
                                 "\n\nยืนยันการลงทะเบียนหรือไม่?";
                if (!DialogHelper.ShowConfirm(summary, "CONFIRM IMPORT")) return;

                try
                {
                    string importUser = _viewModel.CurrentUser?.UserId;
                    int count = await Task.Run(() => service.ApplyImport(rows, importUser));
                    string uid = _viewModel.CurrentUser?.UserId ?? "Unknown";
                    LogService.WriteLog(uid, "REGISTER_PART_IMPORT",
                        $"Import Excel: {Path.GetFileName(dlg.FileName)} | Registered: {count} | Skipped rows: {invalid.Count} | Coils: {coilCount} | Codes: {string.Join(", ", valid.Where(v => !v.IsCoilExtra).Select(v => v.Code).Take(50))}", "");
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

            // 📋 เปิดจาก NOT DEDUCTED (หน้าต่าง PRODUCT CONTROL): ผู้มีสิทธิ์ APPROVE ของ Multi-Scanner ลงทะเบียนได้ / ใส่ข้อมูลจากป้ายไว้ให้ / แจ้งเมื่อบันทึกแล้ว
            private bool _forceAllowRegister;
            public bool ForceAllowRegister
            {
                get => _forceAllowRegister;
                set { _forceAllowRegister = value; if (value) btnRegister.Visibility = Visibility.Visible; }
            }
            public event Action<int, string> PartRegistered;   // PartID, PartCode
            public ProductControlModel SelectedTableProduct => dgQRHistory.SelectedItem as ProductControlModel;

            public void Prefill(string partCode, string partName, string qrCode, string supplier, string search)
            {
                ClearOnlyInputs();
                if (!string.IsNullOrWhiteSpace(partCode)) txtProductCode.Text = partCode.Trim();
                if (!string.IsNullOrWhiteSpace(partName)) txtProductName.Text = partName.Trim();
                if (!string.IsNullOrWhiteSpace(qrCode)) txtQRCode.Text = qrCode.Trim();
                if (!string.IsNullOrWhiteSpace(supplier)) txtSupplier.Text = supplier.Trim();
                if (!string.IsNullOrWhiteSpace(search)) { txtSearch.Text = search.Trim(); _viewModel.LoadData(txtSearch.Text); }
            }

            private async void RegisterPart_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.CanAddProduct != true && !ForceAllowRegister) { DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ลงทะเบียนสินค้า", "ACCESS DENIED"); return; }
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
                    RememberSuggestions(newModel);
                    await ApplyStockSelection(newModel.PartACode, inMain, otherIds, newModel.PtId);
                    if (PartRegistered != null)
                    {
                        // เปิดจาก NOT DEDUCTED: ไปทำขั้นต่อ (รับเข้าคลัง) เลย ไม่ต้องแจ้งซ้ำ
                        ClearOnlyInputs();
                        PartRegistered.Invoke(newModel.PtId, newModel.PartCode);
                        return;
                    }
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
                    RememberSuggestions(editModel);
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
                    PartB = txtPartB.Text.Trim(),
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
            txtPartB.Clear();
            txtPartNo.Clear();
            txtModel.Clear();
            txtMotherCoil.Clear();
            txtCoilNo.Clear();

            // 🎯 หัวใจสำคัญ: สร้างตัวใหม่ไปเลย เพื่อตัดความสัมพันธ์กับ Row เดิมในตาราง
            _viewModel.SelectedProduct = new ProductControlModel();

            // คลังกลับเป็นค่าที่ผู้ใช้เลือกค้างไว้ (ยังไม่เคยเลือก = คลังหลัก)
            SetDefaultStockChips();

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
                PartB = model.PartB,
                PartNo = model.PartNo,
                Model = model.Model,
                ImageFileName = model.ImageFileName
                // ถ้ามี Field อื่นให้เพิ่มตรงนี้
            };
            FillCoils(model);

            // แสดงคลังที่สินค้านี้อยู่ตอนนี้
            List<int> otherIds = null;
            try { if (_stocks.Count > 0) otherIds = _partService.GetPartStockIds(model.PartCode, model.PtId); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"GetPartStockIds: {ex.Message}"); }
            SetStockChips(model.IsShowMain, otherIds);
        }

        // 🧲 MOTHER COIL / COIL NO ของสินค้าที่กด VIEW / EDIT (Coil ในคลังตอนนี้ ตามคลังในช่องกรอง / ALL STOCKS = ทุกคลัง) - แสดงอย่างเดียว
        private async void FillCoils(ProductControlModel model)
        {
            txtMotherCoil.Clear(); txtCoilNo.Clear();
            if (model == null || model.PtId <= 0) return;
            int ptId = model.PtId; int? stk = _viewModel.FilterStkId;
            try
            {
                var map = await Task.Run(() => new ProductControlLayoutService().GetCoils(stk, ptId));
                if (_viewModel.SelectedProduct?.PartCode != model.PartCode || !map.TryGetValue(ptId, out var c)) return;
                txtMotherCoil.Text = string.Join(", ", c.Mothers);
                txtCoilNo.Text = c.Coils.Count == 0 ? "" : $"{c.Coils.Count:N0} COIL: " + string.Join(", ", c.Coils);
                txtCoilNo.ToolTip = string.Join("\n", c.Coils.Take(60)) + (c.Coils.Count > 60 ? $"\n+{c.Coils.Count - 60:N0}" : "");
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"FillCoils: {ex.Message}"); }
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

            // 🛠 ADMIN (Level 1 เท่านั้น): หน้าต่างจัดการ - STOCK DELETE / ตารางของแต่ละคลัง / รูปแบบ QR Code
            private void Admin_Click(object sender, RoutedEventArgs e)
            {
                if (_viewModel.CurrentUser?.UserLevel != 1) { DialogHelper.ShowWarning("เฉพาะ Admin (Level 1) เท่านั้น", "ACCESS DENIED"); return; }
                var ticked = _viewModel.Products.Where(x => x.IsSelected && x.PtId > 0).Select(x => x.PtId).Distinct().ToList();
                var w = new ProductControlAdminWindow(_viewModel.CurrentUser, _stocks, ticked);
                w.ShowDialog();
                if (w.LayoutChanged) { LoadLayouts(); ApplyLayout(); }
                if (w.LayoutChanged || w.DeletedCount > 0) _viewModel.LoadData(txtSearch.Text);
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

                    // QR ตามที่ Admin ตั้งต่อคลังในช่องกรอง (ALL STOCKS = ค่าของ ALL STOCKS) ที่ปุ่ม ADMIN > QR CODE:
                    //   รูปแบบที่ 2 = สิ่งที่อยู่ใน QR (หน้าแสกนอ่าน) / รูปแบบที่ 1 = ข้อความใต้ QR
                    LoadLayouts();
                    var lay = CurrentLayout();
                    var qrFields = lay.QrFields.ToList(); var labelFields = lay.QrLabel.ToList();
                    int? coilStk = _viewModel.FilterStkId;
                    bool needCoils = qrFields.Concat(labelFields).Any(k => k == "MOTHER" || k == "COILNO");
                    string pdf = await Task.Run(() =>
                    {
                        var coils = needCoils ? new ProductControlLayoutService().GetCoils(coilStk) : null;
                        return new ExportService().GenerateA4Pdf(selectedItems, qrFields, labelFields, coils);
                    });
                    string qrText = QrFormat.Sample(qrFields);

                    foreach (var item in selectedItems)
                    {
                        LogService.WriteLog(currentUserId, "EXPORT_QR_PDF", $"Exported QR Code to PDF for Part: {item.PartName} | QR: {qrText}", item.PartCode);
                    }

                    DialogHelper.ShowSuccess($"ส่งออกไฟล์ QR เรียบร้อยแล้ว!\n{ImportTemplateService.ShortPath(pdf)}");
                    ImportTemplateService.OpenFile(pdf);
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError($"Export ไม่สำเร็จ: {ex.Message}");
                }
            }
        } // 👈 ปิดตัวคลาส ProductControlView
    } // 👈 ปิดตัวเนมสเปซ CIMS.Views
