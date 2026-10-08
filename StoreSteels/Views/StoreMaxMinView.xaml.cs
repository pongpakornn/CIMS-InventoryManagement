using CIMS.Models;
using CIMS.Services;
using CIMS.Helpers;
using CIMS.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CIMS.Views
{
    public partial class StoreMaxMinView : Page
    {
        private StoreMaxMinViewModel _viewModel;
        private bool _isEditing = false;

        // Auto-Scroll Fields
        private DispatcherTimer _autoScrollTimer; // 🟢 ปรับมาใช้ Timer ควบคุมความเร็วคงที่แทน Rendering
        private double _currentScrollOffset = 0;
        private bool _isUserInteracting = false;
        private DispatcherTimer _interactionResetTimer;
        private DispatcherTimer _searchDebounceTimer;
        private DispatcherTimer _refreshTimer;

        // ตัวแปรเก็บค่าเดิมก่อนแก้ไข
        private StoreProductModel _originalData;

        // ตัวแปรระดับคลาสสำหรับเก็บค่าที่ส่งมาจาก Dashboard
        private string _selectedCategory;
        private string _filterType;

        private readonly UserSession _session;
        private readonly StockModel _stock;
        public string StockCode => _stock?.Code;

        // เปิดจากเมนูเดิม / Dashboard = คลังหลัก (Stock-CHR)
        public StoreMaxMinView(UserSession session) : this(session, (StockModel)null) { }

        // เปิดจากการ์ดคลังในหน้า StockCardsView
        public StoreMaxMinView(UserSession session, StockModel stock)
        {
            InitializeComponent();
            _session = session;
            _stock = stock ?? ResolveMainStock();
            _viewModel = new StoreMaxMinViewModel(_stock);
            _viewModel.CurrentUser = session;
            this.DataContext = _viewModel;
            ApplyStockSettings();

            _viewModel.Products.CollectionChanged += Products_CollectionChanged;

            this.Loaded += (s, e) => {
                // โหลดครั้งเดียวตอนเปิดหน้า (ใช้กลุ่ม / ตัวกรองที่ส่งมาจาก Dashboard ถ้ามี)
                _viewModel.LoadData();
                InitializeAutoScroll();
                InitializeRealTimeRefresh();
                _uiReady = true;
            };

            // 🧼 เมื่อ User ย้ายหน้า ย้ายแท็บ หรือปิดหน้าจอ ให้เคลียร์ทุก Timer ทันที ป้องกันการทำงานรั่วไหลเบื้องหลัง
            this.Unloaded += (s, e) => {
                StopRealTimeRefresh();
                StopAutoScroll();
            };

            RunEntryAnimation();
        }

        public StoreMaxMinView(UserSession session, string categoryCode, string filterType) : this(session)
        {
            _selectedCategory = categoryCode;
            _filterType = filterType;

            if (_viewModel != null)
            {
                _viewModel.SelectedCategory = !string.IsNullOrEmpty(_selectedCategory) ? _selectedCategory : _viewModel.AllLabel;
                _viewModel.SelectedFilterType = _filterType;
            }
        }

        // เปิดจากการ์ด Dashboard: คลังที่เลือก + กรอง MAX / MIN (filterType ว่าง = ทั้งหมด)
        public StoreMaxMinView(UserSession session, StockModel stock, string filterType) : this(session, stock)
        {
            _viewModel.SelectedFilterType = filterType ?? "";
        }

        #region --- Multi-Stock ---

        // คลังหลักจาก CIMS.Stocks - ถ้ายังไม่ได้รัน Database/MultiStock.sql ให้ใช้ค่าเริ่มต้น (แสดงทุกคอลัมน์เหมือนเดิม)
        private static StockModel ResolveMainStock()
        {
            try
            {
                var main = new StockService().GetMainStock();
                if (main != null) return main;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveMainStock: {ex.Message}");
            }
            return new StockModel { StkId = 0, IsMain = true, Code = "Stock-CHR", Name = "CHR Main Store", Unit = "KG", UseMaxMin = true };
        }

        // แสดง/ซ่อนปุ่มตามการตั้งค่าคลัง - ส่วนคอลัมน์ทำตอนตารางโหลดเสร็จ (ApplyColumnSettings)
        private void ApplyStockSettings()
        {
            txtStockInfo.Text = $"{_stock.Code}  •  {_stock.Name}  •  UNIT : {_stock.Unit}"
                                + (_stock.IsLiveView ? $"  •  🔴 LIVE FROM {_stock.LiveSource.ToUpperInvariant()} (READ ONLY)" : "");

            btnOverMax.Visibility = Show(_stock.UseMaxMin);
            btnUnderMin.Visibility = Show(_stock.UseMaxMin);
            btnImport.Visibility = Show(_session != null && _session.CanImportStock(_stock));
            btnCoilTemplate.Visibility = Show(_session != null && _session.CanImportStock(_stock) && _stock.CountCoil && DbSchema.HasCoilRegister && !_stock.IsLiveView);

            // ความเร็วการเลื่อน + การ์ดสินค้า (เฉพาะคลังที่แสดงรูปภาพ) จำค่าไว้ในเครื่องนี้
            // ค่าเดิม (ระดับ 1-5) -> แปลงเป็นตัวเลขความเร็วครั้งแรก
            int oldLevel = Math.Max(1, Math.Min(5, UiPrefs.GetInt("ScrollSpeed", 2)));
            _speed = Math.Max(1, Math.Min(MaxSpeed, UiPrefs.GetInt("ShowSpeed", (int)LevelSpeeds[oldLevel - 1])));
            txtScrollSpeed.Text = _speed.ToString();
            pnlCardToggle.Visibility = Show(_stock.ColImage);
            chkShowCards.IsChecked = _stock.ColImage && UiPrefs.GetBool("ShowCards." + _stock.Code, true);
            pnlCards.Visibility = Show(chkShowCards.IsChecked == true);
            UpdateLoadMode();

            dgStore.Loaded += (s, e) => ApplyColumnSettings();
        }

        private static Visibility Show(bool on) => on ? Visibility.Visible : Visibility.Collapsed;

        // แสดง/ซ่อนคอลัมน์ตามการตั้งค่าคลัง (ไม่แตะ Style ของตาราง) - ทำหลังตารางรู้ความกว้างจริงแล้วเท่านั้น
        // ถ้าไปซ่อนคอลัมน์ตั้งแต่ตอนสร้างหน้า DataGrid จะคำนวณคอลัมน์แบบ * จากความกว้าง 0 จนทุกคอลัมน์โดนบีบ
        // เหลือ MinWidth (หัวตารางหายบางคอลัมน์) เลยแก้เฉพาะคอลัมน์ที่ต้องเปลี่ยนจริง แล้วคำนวณความกว้างใหม่
        private bool _columnsApplied;
        private void ApplyColumnSettings()
        {
            if (_columnsApplied) return;
            _columnsApplied = true;

            bool changed = false;
            void Set(DataGridColumn col, bool on)
            {
                var v = Show(on);
                if (col.Visibility != v) { col.Visibility = v; changed = true; }
            }

            Set(colNo, _stock.ColNo);
            Set(colImage, _stock.ColImage);
            Set(colCustomer, _stock.ColCustomer);
            Set(colCode, _stock.ColCode);
            Set(colPartA, _stock.ColPartA);
            Set(colPartNo, _stock.ColPartNo);
            Set(colModel, _stock.ColModel);
            Set(colName, _stock.ColName);
            Set(colMax, _stock.UseMaxMin);
            Set(colMin, _stock.UseMaxMin);
            Set(colStatus, _stock.UseMaxMin);
            Set(colQty, _stock.ColQty);
            Set(colStockBox, _stock.ShowBoxColumn);
            colStockBox.Header = _stock.BoxHeader;   // คลัง KG ที่นับ Coil = STOCK (COIL)
            Set(colStockPcs, _stock.ColStockPcs);
            Set(colRemark, _stock.ColRemark);
            // หัวคอลัมน์ตามหน่วยของคลัง เช่น MAX (KG.) / MIN (KG.) / QTY (KG.) หรือ MAX (BOX)
            if (!Equals(colQty.Header, _stock.QtyHeader)) colQty.Header = _stock.QtyHeader;
            if (!Equals(colMax.Header, _stock.MaxHeader)) colMax.Header = _stock.MaxHeader;
            if (!Equals(colMin.Header, _stock.MinHeader)) colMin.Header = _stock.MinHeader;

            // 📋 ลำดับคอลัมน์ตามที่ติ๊กเลือกในหน้า EDIT STOCK (STATUS อยู่ท้ายสุดเสมอ)
            var byKey = new Dictionary<string, DataGridColumn[]>
            {
                ["NO"] = new DataGridColumn[] { colNo }, ["IMAGE"] = new DataGridColumn[] { colImage }, ["CUSTOMER"] = new DataGridColumn[] { colCustomer },
                ["CODE"] = new DataGridColumn[] { colCode }, ["PARTA"] = new DataGridColumn[] { colPartA }, ["PARTNO"] = new DataGridColumn[] { colPartNo },
                ["MODEL"] = new DataGridColumn[] { colModel }, ["NAME"] = new DataGridColumn[] { colName }, ["MAXMIN"] = new DataGridColumn[] { colMax, colMin },
                ["QTY"] = new DataGridColumn[] { colQty }, ["BOX"] = new DataGridColumn[] { colStockBox }, ["COIL"] = new DataGridColumn[] { colStockBox },
                ["PCS"] = new DataGridColumn[] { colStockPcs }, ["REMARK"] = new DataGridColumn[] { colRemark }
            };
            var ordered = new List<DataGridColumn>();
            foreach (string key in _stock.ColumnOrderList())
                if (byKey.TryGetValue(key, out var cols)) foreach (var c in cols) if (!ordered.Contains(c)) ordered.Add(c);
            foreach (var c in dgStore.Columns) if (!ordered.Contains(c) && c != colStatus) ordered.Add(c);   // คอลัมน์อื่นที่ไม่อยู่ในรายการ
            ordered.Add(colStatus);
            // ย้ายตำแหน่งในคอลเลกชันคอลัมน์โดยตรง (ตั้ง DisplayIndex อย่างเดียวตอนตารางเพิ่งโหลดไม่มีผล) แล้วตั้ง DisplayIndex ให้ตรงด้วย
            for (int i = 0; i < ordered.Count; i++)
            {
                int cur = dgStore.Columns.IndexOf(ordered[i]);
                if (cur >= 0 && cur != i) { dgStore.Columns.Move(cur, i); changed = true; }
            }
            for (int i = 0; i < dgStore.Columns.Count; i++)
                if (dgStore.Columns[i].DisplayIndex != i) { dgStore.Columns[i].DisplayIndex = i; changed = true; }
            if (!changed) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                foreach (var col in dgStore.Columns)
                {
                    var w = col.Width;
                    col.Width = new DataGridLength(0);
                    col.Width = w;
                }
            }));
        }

        private void btnBackToStocks_Click(object sender, RoutedEventArgs e)
        {
            var main = Window.GetWindow(this) as MainView;
            main?.NavigateToPage(new StockCardsView(_session), "STORE ( MAX-MIN )");
        }

        // 📥 Import Excel: บวกจำนวนเพิ่มจากยอดเดิม (เดิม 100 + Import 100 = 200)
        private async void btnImport_Click(object sender, RoutedEventArgs e)
        {
            if (_stock?.IsLiveView == true) { DialogHelper.ShowWarning(StockModel.LiveViewMessage, "READ ONLY"); return; }
            if (_session == null || !_session.CanImportStock(_stock))
            {
                DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ Import ข้อมูลเข้าคลังนี้", "ACCESS DENIED");
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Import Excel -> {_stock.Code}",
                Filter = "Excel Files (*.xlsx)|*.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            // ไฟล์มี PRODUCT CODE + PRODUCT NAME (แบบ Template ของ Inventory Registration) = อัพเดทข้อมูลทั้งแถว + ตั้งยอดตามไฟล์
            bool master;
            try { master = await Task.Run(() => StoreMasterImportService.IsMasterFile(dlg.FileName)); }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }
            if (master) { await ImportMasterFile(dlg.FileName); return; }

            // ไฟล์ COIL LIST (PRODUCT CODE + COIL NO + WEIGHT) ของคลังที่นับ Coil
            if (_stock.CountCoil && DbSchema.HasCoilRegister)
            {
                bool coilFile;
                try { coilFile = await Task.Run(() => CoilImportService.IsCoilFile(dlg.FileName)); }
                catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }
                if (coilFile) { await ImportCoilFile(dlg.FileName); return; }
            }

            var service = new StockService();
            List<StockImportRow> rows;
            try
            {
                rows = await Task.Run(() => service.ReadImportExcel(dlg.FileName));
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message);
                return;
            }

            var valid = rows.Where(r => r.IsValid).ToList();
            var invalid = rows.Where(r => !r.IsValid).ToList();
            if (valid.Count == 0)
            {
                DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + DescribeErrors(invalid));
                return;
            }

            decimal totalQty = valid.Sum(r => r.Qty);
            string summary = $"ไฟล์: {System.IO.Path.GetFileName(dlg.FileName)}\nคลัง: {_stock.Code}\n\n" +
                             $"นำเข้าได้ {valid.Count:N0} แถว  (รวม {totalQty:N0} {_stock.Unit})\n" +
                             "จำนวนจะถูกบวกเพิ่มจากยอดเดิม" +
                             (invalid.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {invalid.Count:N0} แถว:\n{DescribeErrors(invalid)}" : "") +
                             "\n\nยืนยันการนำเข้าหรือไม่?";
            if (!DialogHelper.ShowConfirm(summary, "CONFIRM IMPORT")) return;

            try
            {
                var result = await Task.Run(() => service.ApplyImport(_stock.StkId, rows, System.IO.Path.GetFileName(dlg.FileName), _session.UserId));
                LogService.WriteLog(_session.UserId, "STOCK_IMPORT",
                    $"Import Excel -> {_stock.Code} | File: {System.IO.Path.GetFileName(dlg.FileName)} | Items: {result.Items} | Total qty: {result.TotalQty} | Skipped rows: {invalid.Count}",
                    _stock.Code);
                DialogHelper.ShowSuccess($"นำเข้าข้อมูลสำเร็จ {result.Items:N0} รายการ\nรวม {result.TotalQty:N0} {_stock.Unit}");
                _viewModel.LoadData(txtSearch.Text);
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("นำเข้าข้อมูลไม่สำเร็จ (ยกเลิกทั้งไฟล์ ไม่มีรายการใดถูกบันทึก)\n" + ex.Message);
            }
        }

        // 📥 Import COIL LIST: ลงทะเบียน Coil ลูก / Coil แม่ / น้ำหนัก -> STOCK (COIL) นับใหม่จากทะเบียน (KG ไม่เปลี่ยน)
        private async Task ImportCoilFile(string file)
        {
            var svc = new CoilImportService();
            List<CoilImportRow> rows;
            var stock = _stock;
            try { rows = await Task.Run(() => svc.Read(file, stock)); }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }

            var valid = rows.Where(r => r.IsValid).ToList();
            var bad = rows.Where(r => !r.IsValid).ToList();
            string errs = string.Join("\n", bad.Take(8).Select(r => $"• แถว {r.RowNumber}: {r.Error}")) + (bad.Count > 8 ? $"\n• ... และอีก {bad.Count - 8:N0} แถว" : "");
            if (valid.Count == 0) { DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + errs); return; }

            string summary = $"ไฟล์: {System.IO.Path.GetFileName(file)}\nคลัง: {_stock.Code}\n\n" +
                             $"COIL ใหม่ {valid.Count(r => !r.IsUpdate):N0} ลูก  •  อัพเดท COIL เดิม {valid.Count(r => r.IsUpdate):N0} ลูก\n" +
                             $"สินค้า {valid.Select(r => r.PartId).Distinct().Count():N0} รายการ  •  น้ำหนักรวม {valid.Sum(r => r.WeightKG):#,##0.###} KG\n" +
                             "STOCK (COIL) ของสินค้าในไฟล์ = นับใหม่จากทะเบียน Coil (ยอด KG ไม่เปลี่ยน)" +
                             (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{errs}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?";
            if (!DialogHelper.ShowConfirm(summary, "CONFIRM IMPORT")) return;

            try
            {
                string user = _session.UserId;
                var r = await Task.Run(() => svc.Apply(stock, rows, user));
                LogService.WriteLog(_session.UserId, "COIL_IMPORT",
                    $"Import COIL LIST -> {_stock.Code} | File: {System.IO.Path.GetFileName(file)} | Added: {r.Added} | Updated: {r.Updated} | Products: {r.Parts} | Skipped rows: {bad.Count}", _stock.Code);
                DialogHelper.ShowSuccess($"นำเข้า COIL สำเร็จ\nCOIL ใหม่ {r.Added:N0} ลูก  •  อัพเดท {r.Updated:N0} ลูก  •  สินค้า {r.Parts:N0} รายการ");
                _viewModel.LoadData(txtSearch.Text);
            }
            catch (Exception ex) { DialogHelper.ShowError("นำเข้าข้อมูลไม่สำเร็จ (ยกเลิกทั้งไฟล์ ไม่มีรายการใดถูกบันทึก)\n" + ex.Message); }
        }

        // 📄 Template COIL LIST (YES / NO -> Desktop\CIMS_Export -> เปิดไฟล์)
        private async void btnCoilTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!DialogHelper.ShowConfirm($"ต้องการสร้างไฟล์ Template สำหรับนำเข้า COIL LIST ของคลัง {_stock.Code} ใช่หรือไม่?\n\n" +
                    "• กรอก PRODUCT CODE / MOTHER COIL / COIL NO / WEIGHT (KG.) ของ Coil ลูกแต่ละม้วน\n" +
                    "• นำเข้ากลับด้วยปุ่ม IMPORT EXCEL\n\n" +
                    "กด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก", "TEMPLATE EXCEL")) return;
            try
            {
                var stock = _stock;
                string path = await Task.Run(() => new CoilImportService().WriteTemplate(stock));
                ImportTemplateService.OpenFile(path);
            }
            catch (Exception ex) { DialogHelper.ShowError("สร้างไฟล์ Template ไม่สำเร็จ\n" + ex.Message); }
        }

        // 📥 Import แบบอัพเดทข้อมูล: ข้อมูลสินค้าตามหัวคอลัมน์ + คลังตาม STOCK / STOCK1-10 (ไม่ระบุ = คลังนี้)
        //    QTY / STOCK (BOX) / MAX / MIN / REMARK = ตั้งค่าตามไฟล์ / ช่องว่าง = ไม่เปลี่ยน / รหัสใหม่ = ลงทะเบียนให้
        private async Task ImportMasterFile(string file)
        {
            var svc = new StoreMasterImportService();
            List<StoreMasterRow> rows;
            try
            {
                var stocks = await Task.Run(() => new StockService().GetStocks());
                var page = _stock;
                rows = await Task.Run(() => svc.Read(file, page, stocks));
            }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }

            // ทุกคลังที่จะถูกแก้ ต้องมีสิทธิ์ Import
            var noRight = rows.Where(r => r.IsValid).SelectMany(r => r.Stocks).GroupBy(s => s.StkId).Select(g => g.First())
                              .Where(s => !_session.CanImportStock(s)).Select(s => s.Code).ToList();
            if (noRight.Count > 0) { DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ Import ข้อมูลเข้าคลัง: " + string.Join(", ", noRight), "ACCESS DENIED"); return; }

            var valid = rows.Where(r => r.IsValid).ToList();
            var bad = rows.Where(r => !r.IsValid).ToList();
            string errs = string.Join("\n", bad.Take(8).Select(r => $"• แถว {r.RowNumber}: {r.Error}")) + (bad.Count > 8 ? $"\n• ... และอีก {bad.Count - 8:N0} แถว" : "");
            if (valid.Count == 0) { DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + errs); return; }

            var stockCodes = string.Join(", ", valid.SelectMany(r => r.Stocks).Select(s => s.Code).Distinct());
            string summary = $"ไฟล์: {System.IO.Path.GetFileName(file)}\nคลัง: {stockCodes}\n\n" +
                             $"อัพเดทสินค้าเดิม {valid.Count(r => !r.IsNew):N0} รายการ  •  เพิ่มสินค้าใหม่ {valid.Count(r => r.IsNew):N0} รายการ\n" +
                             "ยอดคงคลัง / MAX / MIN จะถูกตั้งตามตัวเลขในไฟล์ (ช่องว่าง = ไม่เปลี่ยน)" +
                             (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{errs}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?";
            if (!DialogHelper.ShowConfirm(summary, "CONFIRM IMPORT")) return;

            try
            {
                var r = await Task.Run(() => svc.Apply(rows));
                LogService.WriteLog(_session.UserId, "STOCK_MASTER_IMPORT",
                    $"Import Excel (update) -> {stockCodes} | File: {System.IO.Path.GetFileName(file)} | Updated: {r.Updated} | Added: {r.Added} | Stock rows: {r.StockRows} | Skipped rows: {bad.Count}", _stock.Code);
                DialogHelper.ShowSuccess($"นำเข้าข้อมูลสำเร็จ\nอัพเดท {r.Updated:N0} รายการ  •  เพิ่มใหม่ {r.Added:N0} รายการ");
                _viewModel.LoadData(txtSearch.Text);
            }
            catch (Exception ex) { DialogHelper.ShowError("นำเข้าข้อมูลไม่สำเร็จ (ยกเลิกทั้งไฟล์ ไม่มีรายการใดถูกบันทึก)\n" + ex.Message); }
        }

        // 📤 Export Excel: PD CODE / PRODUCT NAME / QTY ตามตารางที่แสดงอยู่ (ไม่มีรูปภาพ) - รูปแบบไฟล์จริงรอผู้ใช้ส่งมา
        private async void btnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            // บันทึกที่ Desktop\CIMS_Export (โฟลเดอร์เดียวกับ Export QR ของหน้า Inventory Registration)
            string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CIMS_Export");
            // ปี ค.ศ. เสมอ (เครื่องตั้งปฏิทินไทยไว้ จะได้ 2569 ถ้าใช้รูปแบบของเครื่อง)
            string fileName = $"{_stock.Code}_{DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture)}.xlsx";

            try
            {
                string key = txtSearch.Text;
                var rows = await Task.Run(() => _viewModel.GetAllForExport(key));
                if (rows.Count == 0)
                {
                    DialogHelper.ShowWarning("ไม่มีข้อมูลในตารางให้ Export");
                    return;
                }

                System.IO.Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, fileName);
                string qtyHeader = _stock.QtyHeader;
                string groupLabel = _viewModel.GroupLabel;
                bool dec = _stock.AllowDecimal;
                bool coil = _stock.CountCoil;   // คลัง KG ที่นับ Coil -> มีคอลัมน์ QTY (COIL) ด้วย
                int stkId = _stock.StkId;
                await Task.Run(() =>
                {
                    using (var wb = new ClosedXML.Excel.XLWorkbook())
                    {
                        // รูปแบบเดียวกับไฟล์ Export / Template ทั้งระบบ (หัวตาราง #002060 + เส้นตาราง + แถวกลุ่ม)
                        var ws = wb.Worksheets.Add("Store");
                        string[] heads = coil ? new[] { "NO", "PD CODE", "PRODUCT NAME", qtyHeader, "STOCK (COIL)" } : new[] { "NO", "PD CODE", "PRODUCT NAME", qtyHeader };
                        ImportTemplateService.Header(ws, heads, new double[] { 6.4, 20, 46, 16, 13 });

                        int r = 2;
                        var groupRows = new List<(int Row, string Text)>();
                        foreach (var g in rows.GroupBy(p => string.IsNullOrWhiteSpace(p.GroupKey) ? "-" : p.GroupKey))
                        {
                            groupRows.Add((r, $"{groupLabel} : {g.Key}   ({g.Count():N0} ITEMS)"));
                            r++;
                            int no = 1;
                            foreach (var p in g)
                            {
                                ws.Cell(r, 1).Value = no++;
                                ws.Cell(r, 2).Value = p.PartCode;
                                ws.Cell(r, 3).Value = p.PartName;
                                // จำนวนเป็นตัวเลขใน Excel (คำนวณต่อได้)
                                if (CIMS.Helpers.Qty.TryParse(p.Qty, out decimal q)) ws.Cell(r, 4).Value = q; else ws.Cell(r, 4).Value = p.Qty;
                                if (coil) { if (CIMS.Helpers.Qty.TryParse(p.StockBox, out decimal cq)) ws.Cell(r, 5).Value = cq; else ws.Cell(r, 5).Value = 0; }
                                r++;
                            }
                        }
                        int last = r - 1;
                        ImportTemplateService.Body(ws, last, heads.Length);
                        ImportTemplateService.LeftAlign(ws, last, 3);
                        ws.Range(2, 1, last, 1).Style.Font.Bold = true;
                        ws.Range(2, 4, last, 4).Style.NumberFormat.Format = ImportTemplateService.QtyFormat(dec);
                        if (coil) ws.Range(2, 5, last, 5).Style.NumberFormat.Format = "#,##0";
                        foreach (var (row, text) in groupRows) ImportTemplateService.GroupRow(ws, row, heads.Length, text);

                        // คลังที่นับ Coil: แผ่น COIL DETAIL = Coil แม่ / Coil ลูก / น้ำหนัก ของสินค้าที่ Export (จัดกลุ่มตาม PD CODE)
                        if (coil && CIMS.Helpers.DbSchema.HasCoilRegister)
                        {
                            var coils = new CoilService().GetCoils(stkId, 0).ToLookup(c => c.PartId);
                            var cs = wb.Worksheets.Add("COIL DETAIL");
                            string[] ch = { "NO", "MOTHER COIL", "COIL NO", "WEIGHT (KG.)", "WEIGHT (TON)", "LABEL DATE", "RECEIVED" };
                            ImportTemplateService.Header(cs, ch, new double[] { 6.4, 20, 22, 15, 15, 15, 19 });
                            int cr = 2;
                            var cGroups = new List<(int Row, string Text)>();
                            foreach (var p in rows.Where(p => coils.Contains(p.PartId)))
                            {
                                var list = coils[p.PartId].ToList();
                                cGroups.Add((cr, $"{p.PartCode}  {p.PartName}   ({list.Count:N0} COIL  •  {list.Sum(c => c.WeightKG):#,##0.###} KG)"));
                                cr++;
                                int no = 1;
                                foreach (var c in list)
                                {
                                    cs.Cell(cr, 1).Value = no++;
                                    cs.Cell(cr, 2).Value = c.MotherCoil ?? "-";
                                    cs.Cell(cr, 3).Value = c.CoilNo;
                                    cs.Cell(cr, 4).Value = c.WeightKG;
                                    cs.Cell(cr, 5).Value = Math.Round(c.WeightKG / 1000m, 3);
                                    cs.Cell(cr, 6).Value = c.LabelDate ?? "-";
                                    cs.Cell(cr, 7).Value = c.ReceivedDate?.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "-";
                                    cr++;
                                }
                            }
                            int clast = cr - 1;
                            ImportTemplateService.Body(cs, clast, ch.Length);
                            if (clast >= 2)
                            {
                                cs.Range(2, 1, clast, 1).Style.Font.Bold = true;
                                cs.Range(2, 4, clast, 4).Style.NumberFormat.Format = "#,##0.###";
                                cs.Range(2, 5, clast, 5).Style.NumberFormat.Format = "#,##0.000";
                            }
                            foreach (var (row, text) in cGroups) ImportTemplateService.GroupRow(cs, row, ch.Length, text);
                        }
                        wb.SaveAs(path);
                    }
                });

                LogService.WriteLog(_session?.UserId, "EXPORT_STORE_EXCEL", $"Exported {rows.Count} rows | Stock: {_stock.Code} | File: {System.IO.Path.GetFileName(path)}", _stock.Code);
                NotificationManager.Show("Export complete", $"Export {_stock.Code} {rows.Count:N0} รายการ\nDesktop\\CIMS_Export\\{fileName}", true);
                ImportTemplateService.OpenFile(path);   // เปิดไฟล์ขึ้นมาเลย (เหมือนทุกหน้า)
            }
            catch (System.IO.IOException)
            {
                DialogHelper.ShowError("บันทึกไฟล์ไม่สำเร็จ กรุณาปิดไฟล์ Excel ที่เปิดอยู่ก่อนแล้วลองใหม่");
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("Export ไม่สำเร็จ\n" + ex.Message);
            }
        }

        private static string DescribeErrors(List<StockImportRow> invalid)
        {
            var lines = invalid.Take(8).Select(r => $"• แถว {r.RowNumber}: {(string.IsNullOrEmpty(r.Code) ? "-" : r.Code)} - {r.Error}");
            string more = invalid.Count > 8 ? $"\n• ... และอีก {invalid.Count - 8:N0} แถว" : "";
            return string.Join("\n", lines) + more;
        }

        #endregion

        #region --- RealTime Refresh Logic ---

        private void InitializeRealTimeRefresh()
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Tick -= RefreshTimer_Tick;
                _refreshTimer = null;
            }

            _refreshTimer = new DispatcherTimer();
            // ⏱️ แก้ไข: ล็อกเวลารีเฟรชเบื้องหลังให้เป็น 3 วินาที (ไม่ใช้ 0 วินาทีแล้ว เพื่อไม่ให้เบียดบังฟังก์ชันอื่น)
            _refreshTimer.Interval = TimeSpan.FromSeconds(3);
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        private void StopRealTimeRefresh()
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Tick -= RefreshTimer_Tick;
                _refreshTimer = null;
            }
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_viewModel == null) return;

            // 🛡️ เช็กสถานะการใช้งานหน้าจอ
            if (chkAutoScroll.IsChecked == true && _isUserInteracting) return;

            // ตรวจจับว่าผู้ใช้กำลังแก้ไขข้อมูลคาไว้ที่เซลล์อยู่หรือไม่ ชัวร์ที่สุดครับ
            var cellEditMode = dgStore.CurrentCell != null && dgStore.IsReadOnly == false && _isEditing;
            if (cellEditMode) return;

            try
            {
                await _viewModel.UpdateStockFromDbAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in View RefreshTimer_Tick: {ex.Message}");
            }
        }

        #endregion

        #region --- Filter Button Events ---

        private void cbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null) return;
            // เลือกค่าเริ่มต้นตอนเปิดหน้า (ไม่มีค่าเดิม) -> หน้าโหลดข้อมูลเองอยู่แล้ว ไม่ต้องดึงซ้ำ
            if (e.RemovedItems.Count == 0) return;
            string searchKey = txtSearch != null ? txtSearch.Text : "";
            _viewModel.LoadData(searchKey);
        }

        private void btnOverMax_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is StoreMaxMinViewModel vm)
            {
                vm.SelectedFilterType = "OVER_MAX";
                vm.LoadData(txtSearch.Text);
            }
        }

        private void btnUnderMin_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is StoreMaxMinViewModel vm)
            {
                vm.SelectedFilterType = "UNDER_MIN";
                vm.LoadData(txtSearch.Text);
            }
        }

        private void btnShowAll_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectedFilterType = "";
            _viewModel.SelectedCategory = _viewModel.AllLabel;
            if (txtSearch != null) txtSearch.Text = "";

            _viewModel.LoadData("");
        }

        #endregion

        #region --- Auto-Scroll Logic ---

        // 🔁 SHOW PRODUCTION = เลื่อนตารางขึ้นวนไปเรื่อยๆ แบบป้ายโฆษณา: แถวที่เลื่อนพ้นด้านบนจะถูกย้ายไปต่อท้าย
        //    (หัวกลุ่มเดิมโผล่ใหม่ที่ด้านล่าง) ไม่มีการกระโดดกลับขึ้นบนสุด
        // 🃏 SHOW CARD = แถบการ์ดสินค้าเลื่อนซ้ายวนไปเรื่อยๆ (เฉพาะคลังที่แสดงรูปภาพ)
        // ทั้งสองอย่างขยับตามเวลาจริงทุกเฟรม (CompositionTarget.Rendering) ความเร็วคงที่ไม่ขึ้นกับความเร็วเครื่อง

        // ความเร็ว (พิกเซล/วินาที) ระดับ 1-5 - ระดับ 2 = ความเร็วเดิมของตาราง
        // ตอนนี้ผู้ใช้กรอกตัวเลขเอง (1-200) = พิกเซล/วินาทีของตาราง / การ์ดวิ่งเร็วกว่า 2.5 เท่า (ระยะการ์ดยาวกว่า)
        private static readonly double[] LevelSpeeds = { 8, 12, 20, 32, 50 };   // ค่าระดับ 1-5 แบบเดิม (ใช้แปลงค่าเก่า)
        private const int MaxSpeed = 200;
        private int _speed = 12;

        private bool _uiReady;
        private bool _renderingHooked;
        private readonly System.Diagnostics.Stopwatch _frameClock = new System.Diagnostics.Stopwatch();
        private double _lastFrame;

        private ScrollViewer _tableScroll;
        private ScrollContentPresenter _tablePresenter;
        private readonly Dictionary<object, DataGridRow> _rowMap = new Dictionary<object, DataGridRow>();
        private bool _rotating;

        private void InitializeAutoScroll()
        {
            if (_interactionResetTimer == null)
            {
                _interactionResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _interactionResetTimer.Tick += (s, e) =>
                {
                    _interactionResetTimer.Stop();
                    _isUserInteracting = false;
                };
            }

            if (!_renderingHooked)
            {
                CompositionTarget.Rendering += OnFrame;
                _renderingHooked = true;
            }
            _frameClock.Restart();
            _lastFrame = 0;
        }

        private void StopAutoScroll()
        {
            if (_renderingHooked)
            {
                CompositionTarget.Rendering -= OnFrame;
                _renderingHooked = false;
            }
            _frameClock.Stop();
            _interactionResetTimer?.Stop();
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double now = _frameClock.Elapsed.TotalSeconds;
            double dt = now - _lastFrame;
            _lastFrame = now;
            if (dt <= 0) return;
            if (dt > 0.1) dt = 0.1; // เครื่องค้างชั่วคราว - ไม่กระโดดไกล

            try
            {
                if (chkAutoScroll.IsChecked == true && !_isEditing && !_isUserInteracting)
                    ScrollTableStep(dt);

                if (_cardsActive && !_cardsHover)
                    CardStep(dt);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnFrame: {ex.Message}");
            }
        }

        private void ScrollTableStep(double dt)
        {
            if (_tableScroll == null)
            {
                _tableScroll = GetVisualChild<ScrollViewer>(dgStore);
                if (_tableScroll == null) return;
            }
            var sv = _tableScroll;
            if (sv.ScrollableHeight <= 0) return;

            _currentScrollOffset += _speed * dt;
            if (_currentScrollOffset > sv.ScrollableHeight) _currentScrollOffset = sv.ScrollableHeight;
            sv.ScrollToVerticalOffset(_currentScrollOffset);

            TryRotateFirstRow(sv);
        }

        // แถวแรกเลื่อนพ้นด้านบนแล้ว -> ย้ายไปต่อท้าย แล้วชดเชยตำแหน่งให้แถวถัดไปอยู่ที่เดิม (ภาพไม่กระตุก)
        // ถ้าข้อมูลสั้นจนเลื่อนสุดล่างก่อนแถวแรกพ้นขอบ ก็ย้ายเลย เพื่อให้วนต่อได้ไม่ค้าง
        private void TryRotateFirstRow(ScrollViewer sv)
        {
            var products = _viewModel.Products;
            if (products.Count < 2) return;

            if (_tablePresenter == null)
                _tablePresenter = sv.Template?.FindName("PART_ScrollContentPresenter", sv) as ScrollContentPresenter;
            UIElement origin = (UIElement)_tablePresenter ?? sv;

            if (!_rowMap.TryGetValue(products[0], out var firstRow) || !firstRow.IsLoaded) return;
            if (!_rowMap.TryGetValue(products[1], out var anchorRow) || !anchorRow.IsLoaded) return;

            double firstBottom = firstRow.TranslatePoint(new Point(0, firstRow.ActualHeight), origin).Y;
            bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight - 0.5;
            if (firstBottom > 0 && !atBottom) return;

            var anchor = products[1];
            double anchorBefore = anchorRow.TranslatePoint(new Point(0, 0), origin).Y;

            _rotating = true;
            try { _viewModel.RotateFirst(); }
            finally { _rotating = false; }

            dgStore.UpdateLayout();
            if (!_rowMap.TryGetValue(anchor, out var anchorAfterRow) || !anchorAfterRow.IsLoaded) return;

            double anchorContentY = anchorAfterRow.TranslatePoint(new Point(0, 0), origin).Y + sv.VerticalOffset;
            _currentScrollOffset = Math.Max(0, anchorContentY - anchorBefore);
            sv.ScrollToVerticalOffset(_currentScrollOffset);
        }

        // ตั้งที่ตัวแถวโดยตรง (Style ของแถวแพ้ค่า RowDetailsVisibilityMode ของตาราง)
        private void ShowCoilRows(StoreProductModel p)
        {
            if (dgStore.ItemContainerGenerator.ContainerFromItem(p) is DataGridRow row)
                row.DetailsVisibility = p.IsCoilOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void dgStore_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item != null) _rowMap[e.Row.Item] = e.Row;
            // แถวถูกใช้ซ้ำ (Recycling) -> เปิด / ปิด Coil ตามสินค้าของแถวนั้น
            e.Row.DetailsVisibility = e.Row.Item is StoreProductModel sp && sp.IsCoilOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        // คลังที่นับ Coil: กด PD CODE -> เปิด / ปิด Coil แม่ / Coil ลูก ใต้แถวสินค้านั้น
        private async void dgStore_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_stock == null || !_stock.CountCoil || !DbSchema.HasCoilRegister) return;

            // DataGrid จับเมาส์ไว้ตอนเลือกเซลล์ -> OriginalSource เป็นตัวตาราง: หาเซลล์จากจุดที่กดแทน
            var dep = VisualTreeHelper.HitTest(dgStore, e.GetPosition(dgStore))?.VisualHit;
            while (dep != null && !(dep is DataGridCell) && !(dep is System.Windows.Controls.Primitives.DataGridDetailsPresenter))
                dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(dep) : LogicalTreeHelper.GetParent(dep);
            if (!(dep is DataGridCell cell) || cell.Column != colCode) return;
            if (!(cell.DataContext is StoreProductModel p)) return;

            if (p.IsCoilOpen) { p.IsCoilOpen = false; ShowCoilRows(p); return; }

            int stkId = _stock.StkId, ptId = p.PartId;
            try
            {
                p.CoilRows = await Task.Run(() => new CoilService().GetCoilRows(stkId, ptId));
                p.IsCoilOpen = true;
                ShowCoilRows(p);
            }
            catch (Exception ex)
            {
                NotificationManager.Show("COIL", "โหลดรายการ Coil ไม่สำเร็จ\n" + ex.Message, false);
            }
        }

        private void dgStore_UnloadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item != null && _rowMap.TryGetValue(e.Row.Item, out var r) && r == e.Row)
                _rowMap.Remove(e.Row.Item);
        }

        private void dgStore_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            UserActiveTrigger();
        }

        private void dgStore_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            UserActiveTrigger();
        }

        private void UserActiveTrigger()
        {
            if (chkAutoScroll.IsChecked == true)
            {
                _isUserInteracting = true;
                _interactionResetTimer?.Stop();
                _interactionResetTimer?.Start();
            }
        }

        // เปิด SHOW PRODUCTION / SHOW CARD ต้องมีข้อมูลครบทุกแถว (วนครบทั้งคลัง) -> โหลดทั้งหมดครั้งเดียว
        private void UpdateLoadMode()
        {
            _viewModel.LoadAllMode = chkAutoScroll.IsChecked == true || (_stock.ColImage && chkShowCards.IsChecked == true);
        }

        private void ReloadForMode()
        {
            bool wasAll = _viewModel.LoadAllMode;
            UpdateLoadMode();
            if (!_uiReady) return;
            // เปิดโหมดวน -> โหลดทั้งหมด / ปิดโหมดวน -> โหลดใหม่ให้ลำดับกลับเป็นปกติ (หน้าแรก 50 แถว)
            if (wasAll != _viewModel.LoadAllMode || chkAutoScroll.IsChecked != true)
                _viewModel.LoadData(txtSearch.Text);
        }

        // SHOW: ซ่อน Scrollbar + วางแถวจริงทุกแถว (ไม่ประมาณความสูงแถวแบบ virtualization) ให้เลื่อนวนต่อเนื่องไม่กระโดด
        //       ปิด SHOW: กลับเป็นตารางปกติ (มี Scrollbar + สร้างแถวเฉพาะที่เห็น เปิดไว)
        private void ApplyShowLayout(bool show)
        {
            dgStore.VerticalScrollBarVisibility = show ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(dgStore, !show);
            _tableScroll = null;
            _tablePresenter = null;
        }

        private void chkAutoScroll_Checked(object sender, RoutedEventArgs e)
        {
            _isUserInteracting = false;
            _interactionResetTimer?.Stop();
            ApplyShowLayout(true);
            ReloadForMode();

            var scrollViewer = GetVisualChild<ScrollViewer>(dgStore);
            if (scrollViewer != null) _currentScrollOffset = scrollViewer.VerticalOffset;
        }

        private void chkAutoScroll_Unchecked(object sender, RoutedEventArgs e)
        {
            _interactionResetTimer?.Stop();
            _isUserInteracting = false;
            ApplyShowLayout(false);
            ReloadForMode();

            // ลำดับกลับเป็นปกติแล้ว -> เริ่มดูจากแถวแรกสุด
            _currentScrollOffset = 0;
            GetVisualChild<ScrollViewer>(dgStore)?.ScrollToVerticalOffset(0);
        }

        private void txtScrollSpeed_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

        // พิมพ์ตัวเลขแล้วใช้ทันที (1-200) / ช่องว่างหรือ 0 ระหว่างพิมพ์ = ยังไม่เปลี่ยน
        private void txtScrollSpeed_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!int.TryParse(txtScrollSpeed.Text, out int v) || v <= 0) return;
            _speed = Math.Min(MaxSpeed, v);
            if (_uiReady) UiPrefs.Set("ShowSpeed", _speed);
        }

        private void txtScrollSpeed_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtScrollSpeed.Text, out int v) || v <= 0 || v > MaxSpeed) txtScrollSpeed.Text = _speed.ToString();
        }

        #endregion

        #region --- Product Cards (เลื่อนซ้ายวน) ---

        // สร้างการ์ดแค่พอเต็มความกว้าง + 1 ใบ แล้ววนใช้ซ้ำ: ใบซ้ายสุดที่พ้นขอบจะย้ายไปต่อท้ายพร้อมสินค้าถัดไป
        // จึงวนได้ครบทุกสินค้าแม้มีเป็นพันรายการ - ถ้าสินค้าน้อยจนแสดงครบในแถบเดียว จะหยุดนิ่งไม่เลื่อน
        private const double CardGap = 12;
        private List<StoreProductModel> _cardItems = new List<StoreProductModel>();
        private int _nextCard;
        private bool _cardsActive;
        private bool _cardsHover;
        private bool _cardsStatic;
        private bool _cardRebuildQueued;

        private double CardWidth
        {
            get
            {
                int stats = CardStats().Count;
                return 330 + Math.Max(0, stats - 3) * 64;
            }
        }

        private double CardSlot => CardWidth + CardGap;

        private void chkShowCards_Changed(object sender, RoutedEventArgs e)
        {
            bool on = chkShowCards.IsChecked == true;
            pnlCards.Visibility = Show(on);
            if (!_uiReady) return;
            UiPrefs.Set("ShowCards." + _stock.Code, on);
            ReloadForMode();
            QueueCardRebuild();
        }

        private void pnlCards_MouseEnter(object sender, MouseEventArgs e) => _cardsHover = true;
        private void pnlCards_MouseLeave(object sender, MouseEventArgs e) => _cardsHover = false;

        private void Products_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_rotating) return; // แค่ย้ายแถวตอนวนตาราง ไม่ต้องสร้างการ์ดใหม่
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset ||
                e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                QueueCardRebuild();
                QueueColumnRefit();
            }
        }

        // ข้อมูลโหลดเบื้องหลังมาถึงหลังตารางวัดความกว้างไปแล้ว -> คำนวณความกว้างคอลัมน์แบบ * ใหม่ 1 ครั้งหลังแถวชุดใหม่เข้ามา
        private bool _refitQueued;
        private void QueueColumnRefit()
        {
            if (_refitQueued) return;
            _refitQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _refitQueued = false;
                foreach (var col in dgStore.Columns)
                {
                    if (col.Visibility != Visibility.Visible) continue;
                    var w = col.Width;
                    col.Width = new DataGridLength(0);
                    col.Width = w;
                }
            }));
        }

        private void QueueCardRebuild()
        {
            if (_cardRebuildQueued) return;
            _cardRebuildQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _cardRebuildQueued = false;
                RebuildCards();
            }));
        }

        private void RebuildCards()
        {
            cardTrack.Children.Clear();
            cardShift.X = 0;
            _nextCard = 0;
            _cardsActive = _stock.ColImage && chkShowCards.IsChecked == true;
            if (!_cardsActive) return;

            _cardItems = _viewModel.Products.ToList();
            if (_cardItems.Count == 0) { _cardsActive = false; return; }

            double width = cardCanvas.ActualWidth > 0 ? cardCanvas.ActualWidth : pnlCards.ActualWidth;
            _cardsStatic = _cardItems.Count * CardSlot <= width;
            FillCards(width);
        }

        private void FillCards(double width)
        {
            if (_cardItems.Count == 0) return;
            int limit = _cardsStatic ? _cardItems.Count : int.MaxValue;
            while (cardTrack.Children.Count < limit && cardTrack.Children.Count * CardSlot < width + CardSlot)
            {
                var card = CreateCard();
                card.DataContext = _cardItems[_nextCard % _cardItems.Count];
                _nextCard = (_nextCard + 1) % _cardItems.Count;
                cardTrack.Children.Add(card);
            }
        }

        private void CardStep(double dt)
        {
            if (_cardsStatic || cardTrack.Children.Count == 0) return;
            double width = cardCanvas.ActualWidth;
            if (width <= 0) return;

            cardShift.X -= _speed * 2.5 * dt;

            // ใบซ้ายสุดพ้นขอบแล้ว -> ย้ายไปต่อท้ายเป็นสินค้าถัดไป
            while (cardTrack.Children.Count > 0 && cardShift.X <= -CardSlot)
            {
                var first = (FrameworkElement)cardTrack.Children[0];
                cardTrack.Children.RemoveAt(0);
                first.DataContext = _cardItems[_nextCard % _cardItems.Count];
                _nextCard = (_nextCard + 1) % _cardItems.Count;
                cardTrack.Children.Add(first);
                cardShift.X += CardSlot;
            }

            FillCards(width); // ขยายหน้าต่างแล้วเติมการ์ดให้เต็ม
        }

        // ค่าบนการ์ดตามคอลัมน์ที่คลังเลือกแสดง (หัวข้อ, ชื่อ Property)
        private List<(string Label, string Path)> CardStats()
        {
            // เรียงตามลำดับคอลัมน์ที่เลือกในหน้า EDIT STOCK เหมือนตาราง
            var list = new List<(string, string)>();
            bool boxAdded = false;
            foreach (string key in _stock.ColumnOrderList())
            {
                if (key == "MAXMIN" && _stock.UseMaxMin) { list.Add((_stock.MaxHeader, nameof(StoreProductModel.Max))); list.Add((_stock.MinHeader, nameof(StoreProductModel.Min))); }
                else if (key == "QTY" && _stock.ColQty) list.Add((_stock.QtyHeader, nameof(StoreProductModel.Qty)));
                else if ((key == "BOX" || key == "COIL") && _stock.ShowBoxColumn && !boxAdded) { list.Add((_stock.BoxHeader, nameof(StoreProductModel.StockBox))); boxAdded = true; }
                else if (key == "PCS" && _stock.ColStockPcs) list.Add(("STOCK (PCS)", nameof(StoreProductModel.StockPcs)));
            }
            return list;
        }

        private FrameworkElement CreateCard()
        {
            var purple = (Brush)FindResource("MainPurple");

            // หัวข้อหลักของการ์ด: PART NO > PD CODE > PART A (ตามคอลัมน์ที่คลังเปิดไว้)
            string titlePath = _stock.ColPartNo ? nameof(StoreProductModel.PartNo)
                             : _stock.ColCode ? nameof(StoreProductModel.PartCode)
                             : _stock.ColPartA ? nameof(StoreProductModel.PartA)
                             : nameof(StoreProductModel.PartCode);

            var root = new Border
            {
                Width = CardWidth,
                Height = 200,
                Margin = new Thickness(0, 0, CardGap, 0),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(2),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.12, Color = Colors.Black }
            };

            // สีการ์ดตามสถานะ: ต่ำกว่า MIN = แดง / ปกติ - เกิน MAX = เขียว / ยังไม่ตั้ง MAX-MIN = เทา
            root.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(StoreProductModel.StockStatus)) { Converter = CardTone.Instance, ConverterParameter = "bg" });
            root.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(StoreProductModel.StockStatus)) { Converter = CardTone.Instance, ConverterParameter = "border" });

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Child = grid;

            // --- ส่วนบน: รูป + กลุ่ม + รหัส + ชื่อ ---
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(top);

            var imgBox = new Border
            {
                Width = 100, Height = 100, CornerRadius = new CornerRadius(12),
                Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xF1)), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left
            };
            var img = new Image { Width = 94, Height = 94, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            img.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(StoreProductModel.ProductImage)) { IsAsync = false });
            imgBox.Child = img;
            top.Children.Add(imgBox);

            var info = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetColumn(info, 1);
            top.Children.Add(info);

            var head = new DockPanel { LastChildFill = false };
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xE5, 0xF5)),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), MaxWidth = CardWidth - 230
            };
            var chipText = new TextBlock { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = purple, TextTrimming = TextTrimming.CharacterEllipsis };
            chipText.SetBinding(TextBlock.TextProperty, nameof(StoreProductModel.GroupKey));
            chip.Child = chipText;
            head.Children.Add(chip);

            if (_stock.UseMaxMin)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(dot, Dock.Right);
                dot.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(StoreProductModel.StockStatus))
                {
                    Converter = (System.Windows.Data.IValueConverter)FindResource("StatusToColorConverter")
                });
                head.Children.Add(dot);

                // ป้ายสถานะ (LOW STOCK / NORMAL / OVER MAX / NO MAX-MIN)
                var badge = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(badge, Dock.Right);
                badge.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(StoreProductModel.StockStatus)) { Converter = CardTone.Instance, ConverterParameter = "border" });
                var badgeText = new TextBlock { FontSize = 10, FontWeight = FontWeights.Black, Foreground = Brushes.White };
                badgeText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(StoreProductModel.StockStatus)) { Converter = CardTone.Instance, ConverterParameter = "label" });
                badge.Child = badgeText;
                head.Children.Add(badge);
            }
            info.Children.Add(head);

            var title = new TextBlock
            {
                FontSize = 17, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2A, 0x32)),
                Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            };
            title.SetBinding(TextBlock.TextProperty, titlePath);
            info.Children.Add(title);

            if (_stock.ColName)
            {
                var name = new TextBlock
                {
                    FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x69, 0x76)),
                    TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 34, Margin = new Thickness(0, 1, 0, 0)
                };
                name.SetBinding(TextBlock.TextProperty, nameof(StoreProductModel.PartName));
                info.Children.Add(name);
            }

            // --- ส่วนล่าง: MAX / MIN / QTY / STOCK (BOX) / STOCK (PCS) ---
            var stats = CardStats();
            if (stats.Count > 0)
            {
                var row = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = stats.Count, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 6, 0, 0) };
                Grid.SetRow(row, 1);
                foreach (var (label, path) in stats)
                {
                    var cell = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                        CornerRadius = new CornerRadius(10), Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(2, 4, 2, 4)
                    };
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock
                    {
                        Text = label, FontSize = 10, FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x90)), HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    var val = new TextBlock { FontSize = 16, FontWeight = FontWeights.Black, Foreground = purple, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    val.SetBinding(TextBlock.TextProperty, path);
                    sp.Children.Add(val);
                    cell.Child = sp;
                    row.Children.Add(cell);
                }
                grid.Children.Add(row);
            }

            return root;
        }

        // สี / ข้อความของการ์ดตามสถานะสินค้า
        private sealed class CardTone : System.Windows.Data.IValueConverter
        {
            public static readonly CardTone Instance = new CardTone();
            private static SolidColorBrush B(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
            private static readonly SolidColorBrush RedBg = B(0xFF, 0xEB, 0xEE), RedLine = B(0xE5, 0x39, 0x35);
            private static readonly SolidColorBrush GreenBg = B(0xE8, 0xF5, 0xE9), GreenLine = B(0x43, 0xA0, 0x47);
            private static readonly SolidColorBrush DarkGreenLine = B(0x2E, 0x7D, 0x32);
            private static readonly SolidColorBrush GrayBg = B(0xFA, 0xF9, 0xFC), GrayLine = B(0xC9, 0xC3, 0xD3);

            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            {
                string s = value as string ?? "";
                bool red = s == "UNDER_MIN" || s == "OUT_OF_STOCK";
                bool green = s == "NORMAL_GOOD" || s == "OVER_MAX" || s == "NORMAL";
                switch (parameter as string)
                {
                    case "bg": return red ? RedBg : green ? GreenBg : GrayBg;
                    case "label": return red ? "LOW STOCK" : s == "OVER_MAX" ? "OVER MAX" : green ? "NORMAL" : "NO MAX-MIN";
                    default: return red ? RedLine : s == "OVER_MAX" ? DarkGreenLine : green ? GreenLine : GrayLine;
                }
            }

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
        }

        #endregion

        #region --- DataGrid Events ---
        private void dgStore_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange > 0 && e.ExtentHeight > 0 && !_viewModel.LoadAllMode)
            {
                if (e.VerticalOffset >= (e.ExtentHeight - e.ViewportHeight) - 20)
                {
                    _viewModel.LoadData(txtSearch.Text, isLoadMore: true);
                }
            }

            if (_isUserInteracting || chkAutoScroll.IsChecked != true)
            {
                _currentScrollOffset = e.VerticalOffset;
            }
        }

        private void dgStore_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var p = e.Row.Item as StoreProductModel;
            if (p != null)
            {
                _originalData = new StoreProductModel
                {
                    PartCode = p.PartCode,
                    Remark = p.Remark,
                    Max = p.Max,
                    Min = p.Min,
                    Qty = p.Qty,
                    StockBox = p.StockBox,
                    StockPcs = p.StockPcs
                };
            }

            // ⏳ คลังที่แสดงสดจาก StorePC (ชั่วคราว) = ดูอย่างเดียว ทุกคอลัมน์รวม REMARK
            if (_stock?.IsLiveView == true)
            {
                DialogHelper.ShowWarning(StockModel.LiveViewMessage, "READ ONLY");
                e.Cancel = true;
                return;
            }

            string header = e.Column.Header.ToString().ToUpper();
            if (header.Contains("REMARK"))
            {
                if (p != null) p.IsRemarkEditing = true;
                return;
            }

            if (!_viewModel.CanEditMaster)
            {
                DialogHelper.ShowWarning("คุณไม่มีสิทธิ์แก้ไขข้อมูลในคลังนี้ (แก้ไขได้เฉพาะ REMARK)", "ACCESS DENIED");
                e.Cancel = true;
            }
            else
            {
                _isEditing = true;
            }
        }

        private async void dgStore_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
{
    if (e.EditAction == DataGridEditAction.Commit)
    {
        var product = e.Row.Item as StoreProductModel;
        if (product != null && _originalData != null)
        {
            // CellEditEnding เกิดก่อนตารางเขียนค่ากลับเข้า model -> เอาค่าจากช่องที่กำลังแก้ใส่ให้เองก่อนบันทึก
            if (e.EditingElement is TextBox tb && e.Column is DataGridBoundColumn bc && bc.Binding is System.Windows.Data.Binding b && !string.IsNullOrEmpty(b.Path?.Path))
            {
                var prop = typeof(StoreProductModel).GetProperty(b.Path.Path);
                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string)) prop.SetValue(product, tb.Text?.Trim());
            }
            try
            {
                // 🔒 ล็อกสถานะไว้ต่อ ป้องกันระบบ Realtime ดึงข้อมูลมาทับระหว่างยิงเน็ตเวิร์กอัปเดตฐานข้อมูล
                _isEditing = true; 

                bool success = await _viewModel.ProcessUpdate(product, _originalData);
                product.IsRemarkEditing = false;

                if (!success)
                {
                    DialogHelper.ShowError("ไม่สามารถบันทึกข้อมูลได้");
                    _viewModel.LoadData(txtSearch.Text);
                }
                else
                {
                    // แก้ BOX / PCS แล้วอีกค่าคำนวณใหม่ใน SQL -> ดึงยอดล่าสุดมาแสดงทันที
                    _isEditing = false;
                    await _viewModel.UpdateStockFromDbAsync();
                }
            }
            catch (Exception ex)
            {
                // บันทึกไม่สำเร็จ -> แจ้งผู้ใช้ + ดึงค่าจริงจากฐานกลับมาแสดง (ไม่ปล่อยให้ค่าในตารางค้างเหมือนบันทึกแล้ว)
                DialogHelper.ShowError($"ไม่สามารถบันทึกข้อมูลได้\n{ex.Message}");
                _viewModel.LoadData(txtSearch.Text);
            }
            finally
            {
                // 🔓 ปลดล็อกให้ระบบ Realtime ทำงานได้ตามปกติเมื่อบันทึกเสร็จชัวร์ๆ แล้ว
                _originalData = null;
                _isEditing = false;
            }
        }
    }
    else
    {
        // กรณีผู้ใช้กด Cancel (Esc) ให้ปลดล็อกได้เลย
        _originalData = null;
        _isEditing = false;
    }
}

        #endregion

        #region --- General Logic ---

        private void RunEntryAnimation()
        {
            TimeSpan duration = TimeSpan.FromSeconds(0.6);
            IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = duration };
            DoubleAnimation slideUp = new DoubleAnimation { From = 30, To = 0, Duration = duration, EasingFunction = ease };

            this.BeginAnimation(Page.OpacityProperty, fadeIn);
            if (PageTransform != null) PageTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private T GetVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T) return (T)child;
                var result = GetVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        #region --- Search Events ---

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            if (txtSearch != null)
            {
                _viewModel.LoadData(txtSearch.Text);
            }
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchDebounceTimer != null) _searchDebounceTimer.Stop();

            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _searchDebounceTimer.Tick += (s, ev) =>
            {
                _searchDebounceTimer.Stop();
                if (!_isEditing)
                {
                    _viewModel.LoadData(txtSearch.Text);
                }
            };
            _searchDebounceTimer.Start();
        }

        private void txtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Keyboard.ClearFocus();
                _viewModel.LoadData(txtSearch.Text);
                e.Handled = true;
            }
        }

        #endregion
    }
}
