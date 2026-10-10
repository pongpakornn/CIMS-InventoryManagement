//using Microsoft.Extensions.Logging;
//using CIMS.Core;
using CIMS.Helpers;
//using CIMS.Models;
//using CIMS.Services;
//using System;
//using System.Collections.ObjectModel;
//using System.ComponentModel;
//using System.Linq;
//using System.Runtime.CompilerServices;
//using System.Threading.Tasks;
//using System.Windows;

//namespace CIMS.ViewModels
//{
//    public class ScanInViewModel : INotifyPropertyChanged
//    {
//        private readonly ScanService _scanService = new ScanService();

//        public UserSession CurrentUser { get; set; }
//        public ObservableCollection<ScanItemModel> ScannedItems { get; set; } = new ObservableCollection<ScanItemModel>();
//        public ObservableCollection<ScanItemModel> HistoryItems { get; set; } = new ObservableCollection<ScanItemModel>();

//        #region Properties สำหรับผูกกับ UI
//        private System.Windows.Media.ImageSource _showProductImage;
//        public System.Windows.Media.ImageSource ShowProductImage
//        {
//            get => _showProductImage;
//            set { _showProductImage = value; OnPropertyChanged(); }
//        }

//        private string _showName;
//        public string ShowName { get => _showName; set { _showName = value; OnPropertyChanged(); } }

//        private string _showCode;
//        public string ShowCode { get => _showCode; set { _showCode = value; OnPropertyChanged(); } }

//        private int _showQtyValue;
//        public string ShowQty
//        {
//            get => _showQtyValue.ToString();
//            set { if (int.TryParse(value, out int res)) _showQtyValue = res; OnPropertyChanged(); }
//        }

//        private string _barcodeInput;
//        public string BarcodeInput
//        {
//            get => _barcodeInput;
//            set { _barcodeInput = value; OnPropertyChanged(); }
//        }
//        #endregion

//        public ScanInViewModel()
//        {
//            LoadTodayData();
//        }

//        private async void LoadTodayData()
//        {
//            try
//            {
//                var data = await Task.Run(() => _scanService.GetTodayTransactions());

//                Application.Current.Dispatcher.Invoke(() =>
//                {
//                    ScannedItems.Clear();
//                    HistoryItems.Clear();
//                    foreach (var item in data)
//                    {
//                        if (item.Status == "IN")
//                        {
//                            ScannedItems.Insert(0, item);
//                        }
//                        UpdateSummary(item);
//                    }
//                });
//            }
//            catch (Exception ex)
//            {
//                System.Diagnostics.Debug.WriteLine($"Load Error: {ex.Message}");
//            }
//        }


//        public async Task ProcessScan(string inputCode)
//        {
//            if (string.IsNullOrWhiteSpace(inputCode) || CurrentUser == null) return;

//            string finalSearchCode = inputCode.Trim();

//            // ✅ เก็บบาร์โค้ดดิบ "ทั้งชุด" ไว้ก่อนตัดส่วนหัวออก เพื่อบันทึกลง ReferenceNo
//            string rawBarcodeFull = finalSearchCode;

//            string uid = CurrentUser.UserId;

//            // =========================================================================
//            // ⚙️ [ระบบคัดแยก] สกัดเอา PartACode คลีนๆ (เช่น "A122-00059") เพื่อให้เสถียร ไม่หลุด Error
//            // =========================================================================
//            var parts = finalSearchCode.Split(new[] { '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

//            if (parts.Length > 0)
//            {
//                string firstChunk = parts[0].Trim();

//                if (firstChunk.Length > 2 && char.IsDigit(firstChunk[0]) && char.IsLetter(firstChunk[1]))
//                {
//                    finalSearchCode = firstChunk.Substring(1);
//                }
//                else
//                {
//                    finalSearchCode = firstChunk;
//                }
//            }
//            // =========================================================================

//            try
//            {
//                var result = await Task.Run(() =>
//                {
//                    var part = _scanService.GetPartByScan(finalSearchCode);
//                    if (part == null) return null;

//                    int originalQty = (int)part.Qty;

//                    // ✅ ส่ง rawBarcodeFull เข้าไปบันทึกลง ReferenceNo
//                    bool isSaved = _scanService.UpdateStock(part.PartId, part.PartCode, part.PartACode, originalQty, uid, rawBarcodeFull);
//                    if (isSaved)
//                    {
//                        LogService.WriteScanLog(uid, "SCAN_IN", part.PartCode, part.PartACode, originalQty);
//                        part.Qty = originalQty;
//                        return part;
//                    }
//                    return null;
//                });

//                if (result != null)
//                {
//                    ShowCode = result.PartACode;
//                    ShowName = result.PartName;
//                    ShowQty = Qty.Plain(result.Qty); // หน้าจอแสดงผลยอดตามระบบเก่าของคุณนนท์

//                    // เส้นทางไฟล์รูปเครือข่าย IP เครื่องหลัก
//                    string baseFolder = @"\\192.168.10.56\ProgramCHR\2. Store Only\StoreSteels\ImageStore";
//                    //string baseFolder = @"C:\Users\pongp\Desktop\WorkMe\3. Project WPF\2. Program StoreSteels\1. ImageStore";
//                    string fileName = result.PartACode;
//                    string imgPath = System.IO.Path.Combine(baseFolder, $"{fileName}.png");

//                    if (!System.IO.File.Exists(imgPath))
//                    {
//                        imgPath = System.IO.Path.Combine(baseFolder, $"{fileName}.jpg");
//                    }

//                    if (!System.IO.File.Exists(imgPath))
//                    {
//                        imgPath = System.IO.Path.Combine(baseFolder, "no-image.png");
//                    }

//                    Application.Current.Dispatcher.Invoke(() =>
//                    {
//                        try
//                        {
//                            if (System.IO.File.Exists(imgPath))
//                            {
//                                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
//                                bitmap.BeginInit();
//                                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
//                                bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
//                                bitmap.UriSource = new Uri(imgPath, UriKind.Absolute);
//                                bitmap.EndInit();
//                                bitmap.Freeze();

//                                ShowProductImage = bitmap;
//                            }
//                            else
//                            {
//                                ShowProductImage = null;
//                            }
//                        }
//                        catch (Exception ex)
//                        {
//                            System.Diagnostics.Debug.WriteLine($"Load Image Error: {ex.Message}");
//                            ShowProductImage = null;
//                        }
//                    });

//                    var newItem = new ScanItemModel
//                    {
//                        PartId = result.PartId,
//                        PartCode = result.PartCode,
//                        PartName = result.PartName,
//                        PartNo = result.PartNo,
//                        PartACode = result.PartACode,
//                        Qty = result.Qty, // ปรับ Type ให้เป็น int ป้องกันตัวแดงเตือนบน Model เดิม
//                        Status = "IN",
//                        UpdateTime = DateTime.Now,
//                        ProductImagePath = ShowProductImage
//                    };

//                    ScannedItems.Insert(0, newItem);

//                    if (ScannedItems.Count > 12)
//                    {
//                        ScannedItems.RemoveAt(ScannedItems.Count - 1);
//                    }
//                    UpdateSummary(newItem);
//                }
//                else
//                {
//                    DialogHelper.ShowError($"[รายการไม่สำเร็จ] ไม่พบข้อมูลสินค้าในระบบ หรือบาร์โค้ดยังไม่ได้ลงทะเบียน\nCode: {finalSearchCode}");
//                }
//            }
//            catch (Exception ex)
//            {
//                DialogHelper.ShowError("เกิดข้อผิดพลาด: " + ex.Message);
//            }
//            finally
//            {
//                BarcodeInput = string.Empty;
//            }
//        }


//        private void UpdateSummary(ScanItemModel item)
//        {
//            if (string.IsNullOrWhiteSpace(item.PartACode)) return;

//            // ดึงยอดคงคลังปัจจุบันจาก Database ผ่านตัวแปรไอดีสินค้า (PartId)
//            int actualCurrentStock = _scanService.GetInventoryBalance(item.PartId);

//            var existing = HistoryItems.FirstOrDefault(x => x.PartACode == item.PartACode);

//            if (existing != null)
//            {
//                if (string.IsNullOrEmpty(existing.PartCode) && !string.IsNullOrEmpty(item.PartCode))
//                    existing.PartCode = item.PartCode;
//                if (string.IsNullOrEmpty(existing.PartNo) && !string.IsNullOrEmpty(item.PartNo))
//                    existing.PartNo = item.PartNo;
//                if (string.IsNullOrEmpty(existing.PartName) && !string.IsNullOrEmpty(item.PartName))
//                    existing.PartName = item.PartName;

//                if (item.Status == "IN")
//                {
//                    existing.InCount += 1;
//                    existing.TotalInQty += item.Qty;
//                }
//                else if (item.Status == "OUT")
//                {
//                    existing.OutCount += 1;
//                    existing.TotalOutQty += item.Qty;
//                }

//                // เอายอดจริงจาก SQL มาเขียนทับเลย ไม่ต้องบวกลบอินเมมโมรี่ให้เสี่ยงติดลบอีกต่อไป
//                existing.FinalStock = actualCurrentStock;
//            }
//            else
//            {
//                HistoryItems.Add(new ScanItemModel
//                {
//                    PartId = item.PartId, // อย่าลืมแมปค่าตัวนี้เผื่อสแกนซ้ำตัวเดิมในแถวถัดไป
//                    PartCode = item.PartCode,
//                    PartName = item.PartName,
//                    PartACode = item.PartACode,
//                    PartNo = item.PartNo,
//                    InCount = item.Status == "IN" ? 1 : 0,
//                    TotalInQty = item.Status == "IN" ? item.Qty : 0,
//                    OutCount = item.Status == "OUT" ? 1 : 0,
//                    TotalOutQty = item.Status == "OUT" ? item.Qty : 0,

//                    // ใช้ยอดจริงจาก SQL สำหรับการสแกนเจอรายการใหม่ครั้งแรกของวัน
//                    FinalStock = actualCurrentStock,
//                    ProductImagePath = item.ProductImagePath
//                });
//            }
//        }

//        public event PropertyChangedEventHandler PropertyChanged;
//        protected void OnPropertyChanged([CallerMemberName] string name = null)
//            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
//    }
//}

using Microsoft.Extensions.Logging;
using CIMS.Core;
using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CIMS.ViewModels
{
    public class ScanInViewModel : INotifyPropertyChanged
    {
        private readonly ScanService _scanService = new ScanService();

        public UserSession CurrentUser { get; set; }
        public ObservableCollection<ScanItemModel> ScannedItems { get; set; } = new ObservableCollection<ScanItemModel>();

        // 🚀 เพิ่ม Collection แยกสำหรับเก็บข้อมูลสแกนสภาวะ Test (ไม่ลง DB)
        public ObservableCollection<ScanItemModel> TestScannedItems { get; set; } = new ObservableCollection<ScanItemModel>();

        public ObservableCollection<ScanItemModel> HistoryItems { get; set; } = new ObservableCollection<ScanItemModel>();

        #region Mode Test Properties
        private bool _isTestMode = false;
        public bool IsTestMode
        {
            get => _isTestMode;
            set
            {
                _isTestMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TestButtonText));
                OnPropertyChanged(nameof(TestButtonBackground));
                OnPropertyChanged(nameof(TestBannerVisibility));
            }
        }

        public string TestButtonText => IsTestMode ? "EXIT TEST" : "TEST MODE";
        public System.Windows.Media.Brush TestButtonBackground => IsTestMode
            ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#D32F2F") // สีแดงเมื่อกดถอนโหมด
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#ED6C02"); // สีส้มโหมด Test

        public Visibility TestBannerVisibility => IsTestMode ? Visibility.Visible : Visibility.Collapsed;

        // Command สำหรับกดปุ่มสลับโหมด Test
        public ICommand ToggleTestModeCommand { get; }
        #endregion

        #region Mode Return (คืนเหล็ก) Properties
        // 🔄 โหมดคืนเหล็กที่เหลือจากการผลิตกลับเข้าคลัง: สแกน QR ที่ Export มาจากหน้า ProductControl
        // (รูปแบบ ProductCode|ProductName) แล้วเด้ง Popup ให้กรอกจำนวนรับคืนเอง แทนที่จะรับเข้าตาม
        // Packsize มาตรฐานแบบการสแกนปกติ กดปุ่มซ้ำเพื่อยกเลิกโหมดกลับไปสแกนแบบปกติ
        private bool _isReturnMode = false;
        public bool IsReturnMode
        {
            get => _isReturnMode;
            set
            {
                _isReturnMode = value;
                // คืนเหล็กเป็นการรับเข้า ใช้พร้อมกับโหมดสแกนออกไม่ได้ (และคืนเหล็กกรอกจำนวนเองอยู่แล้ว ปิดโหมดเศษ)
                if (value && IsScanOutMode) IsScanOutMode = false;
                if (value && IsRemainderMode) IsRemainderMode = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReturnButtonText));
                OnPropertyChanged(nameof(ReturnButtonBackground));
                OnPropertyChanged(nameof(ReturnBannerVisibility));
            }
        }

        public string ReturnButtonText => IsReturnMode ? "CANCEL RETURN" : "RETURN";
        public System.Windows.Media.Brush ReturnButtonBackground => IsReturnMode
            ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#D32F2F") // สีแดงเมื่อกำลังอยู่ในโหมด (กดซ้ำ = ยกเลิก)
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#0288D1"); // สีฟ้าโหมดปกติ

        public Visibility ReturnBannerVisibility => IsReturnMode ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ToggleReturnModeCommand { get; }
        #endregion

        #region Mode Remainder (สแกนเศษ)
        // 🔢 สแกนเศษ: สแกนแล้วเด้งให้กรอกจำนวนเอง (ไม่ใช้ Pack Size / จำนวนบนป้าย) ใช้ได้ทั้งเข้าและออก
        //    ไม่นับกล่อง (STOCK(BOX) ไม่เปลี่ยน ปรับเฉพาะ STOCK(PCS)) กดซ้ำ = กลับไปสแกนปกติ
        private bool _isRemainderMode;
        public bool IsRemainderMode
        {
            get => _isRemainderMode;
            set
            {
                _isRemainderMode = value;
                if (value && IsReturnMode) IsReturnMode = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RemainderButtonText));
                OnPropertyChanged(nameof(RemainderButtonBackground));
                OnPropertyChanged(nameof(RemainderBannerVisibility));
            }
        }

        public string RemainderButtonText => IsRemainderMode ? "CANCEL REMAINDER" : "🔢 REMAINDER";
        public System.Windows.Media.Brush RemainderButtonBackground => IsRemainderMode
            ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#D32F2F")
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#00897B");
        public Visibility RemainderBannerVisibility => IsRemainderMode ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RemainderButtonVisibility =>
            CurrentUser?.CanViewScanIn == true || CurrentUser?.CanViewScanOut == true ? Visibility.Visible : Visibility.Collapsed;
        // ✏️ ADJUST SCAN: Level 1 (Admin) เท่านั้น
        public Visibility AdjustButtonVisibility => CurrentUser?.UserLevel == 1 ? Visibility.Visible : Visibility.Collapsed;
        // 🏷️ DISPLAY: เลือกค่าที่แสดงแทนรหัสต่อคลัง (ตั้งค่าคลัง = EDIT ของ Store(Max-Min))
        public Visibility DisplayButtonVisibility => CurrentUser?.CanEditStore == true ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ToggleRemainderModeCommand { get; }
        #endregion

        #region Mode Scan Out (สแกนออก) Properties
        // 📤 หน้า Multi-Scanner เริ่มต้นเป็นสแกนรับเข้าเสมอ กดปุ่ม "สแกนออก" ครั้งแรก = เปลี่ยนเป็นตัดสต็อกออก
        // (UpdateStockOut ตัวเดียวกับหน้า Scan Out เดิม) กดซ้ำ = ยกเลิก กลับไปรับเข้า
        private bool _isScanOutMode = false;
        public bool IsScanOutMode
        {
            get => _isScanOutMode;
            set
            {
                // ผู้ใช้ที่มีสิทธิ์แค่ OUT ห้ามสลับกลับไปรับเข้า
                if (!value && IsScanOutOnlyUser) return;

                _isScanOutMode = value;
                if (value && IsReturnMode) IsReturnMode = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScanOutButtonText));
                OnPropertyChanged(nameof(ScanOutButtonBackground));
                OnPropertyChanged(nameof(ScanOutBannerVisibility));
                OnPropertyChanged(nameof(ScanTitle));
                OnPropertyChanged(nameof(QtyLabel));
            }
        }

        public string ScanOutButtonText => IsScanOutMode ? "CANCEL SCAN OUT" : "SCAN OUT";
        public System.Windows.Media.Brush ScanOutButtonBackground => IsScanOutMode
            ? (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#D32F2F") // สีแดงเมื่อกำลังอยู่ในโหมด (กดซ้ำ = ยกเลิก)
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#7B1FA2"); // สีม่วงโหมดปกติ

        public Visibility ScanOutBannerVisibility => IsScanOutMode ? Visibility.Visible : Visibility.Collapsed;

        public string ScanTitle => IsScanOutMode ? "║▌║║▌║ MULTI-SCANNER ( OUT )" : "║▌║║▌║ MULTI-SCANNER ( IN )";
        public string QtyLabel => IsScanOutMode ? "QTY OUT" : "QTY IN";

        public ICommand ToggleScanOutModeCommand { get; }

        // สิทธิ์ของผู้ใช้กำหนดว่าเห็นปุ่มไหนบ้าง (เรียก ApplyPermissions หลังตั้ง CurrentUser)
        public bool IsScanOutOnlyUser { get; private set; }
        public Visibility ScanOutButtonVisibility =>
            (CurrentUser?.CanViewScanOut == true && CurrentUser?.CanViewScanIn == true &&
             ScopeStocks.Any(s => s.CanScanIn) && ScopeStocks.Any(s => s.CanScanOut)) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ReturnButtonVisibility =>
            (CurrentUser?.CanScanReturn == true && ScopeStocks.Any(SupportsReturn)) ? Visibility.Visible : Visibility.Collapsed;

        public void ApplyPermissions()
        {
            IsScanOutOnlyUser = CurrentUser != null && CurrentUser.CanViewScanOut && !CurrentUser.CanViewScanIn;
            if (IsScanOutOnlyUser) IsScanOutMode = true;
            OnPropertyChanged(nameof(ScanOutButtonVisibility));
            OnPropertyChanged(nameof(ReturnButtonVisibility));
            OnPropertyChanged(nameof(RemainderButtonVisibility));
            OnPropertyChanged(nameof(AdjustButtonVisibility));
            OnPropertyChanged(nameof(DisplayButtonVisibility));
            LoadStocks();
        }
        #endregion

        #region Multi-Stock (เลือกคลังที่แสกน)
        private readonly StockService _stockService = new StockService();

        // รูปแบบป้าย Supplier ที่เปิดใช้งานทั้งหมด (ใช้ตรวจว่าเป็นป้าย Supplier ไหม แล้วค่อยเช็คว่าคลังนี้รับรูปแบบนั้นหรือเปล่า)
        private List<BarcodeFormatModel> _allFormats = new List<BarcodeFormatModel>();

        // คลังที่แสกนได้: ต้องมีสิทธิ์ VIEW ของคลัง (รวมคลังหลัก) + สิทธิ์ SCANNER (ADD = เข้า / EDIT = ออก)
        // และต้องเปิดช่องทางแสกนไว้ตรงกับสิทธิ์ของผู้ใช้ (รับเข้า / จ่ายออก)
        public ObservableCollection<StockModel> Stocks { get; } = new ObservableCollection<StockModel>();

        // 🏬 คลังที่สแกน (SCAN STOCKS): เลือก 1 คลัง (แบบเดิม), หลายคลังสแกนร่วมกัน หรือ AUTO (ALL STOCKS)
        //    1 คลัง  -> ทำงานเหมือนเดิมทุกอย่าง (SelectedStock)
        //    หลายคลัง -> ระบบหาคลังของสินค้าที่สแกนเอง (ResolveStockAsync) - SelectedStock = null
        private bool _scopeAuto;
        private List<int> _scopeIds = new List<int>();
        private int? _priorityId;   // ★ คลังที่ตัดก่อนตอนสแกนออก เมื่อสินค้าอยู่หลายคลัง

        public bool ScopeAuto => _scopeAuto;
        public IReadOnlyList<int> ScopeIds => _scopeIds;
        public int? PriorityStockId => _priorityId;

        public List<StockModel> ScopeStocks => _scopeAuto ? Stocks.ToList() : Stocks.Where(s => _scopeIds.Contains(s.StkId)).ToList();
        public bool IsMultiScope => _scopeAuto || _scopeIds.Count > 1;

        // ข้อความบนปุ่มเลือกคลัง
        public string ScopeText
        {
            get
            {
                if (_scopeAuto) return "⚡ AUTO ( ALL STOCKS )";
                var list = ScopeStocks;
                if (list.Count == 0) return "SELECT STOCK";
                if (list.Count == 1) return list[0].DisplayName;
                string names = string.Join(" + ", list.Select(s => s.Code));
                return names.Length <= 34 ? names : $"{list.Count} STOCKS";
            }
        }
        public string ScopeSubText
        {
            get
            {
                if (!IsMultiScope) return "";
                var p = Stocks.FirstOrDefault(s => s.StkId == _priorityId);
                return "SCAN TOGETHER" + (p != null ? $"  •  ★ OUT FIRST : {p.Code}" : "");
            }
        }
        public Visibility ScopeSubVisibility => IsMultiScope ? Visibility.Visible : Visibility.Collapsed;

        private StockModel _selectedStock;
        public StockModel SelectedStock
        {
            get => _selectedStock;
            set
            {
                if (_selectedStock == value) return;
                if (value == null) return;
                ApplyScope(false, new List<int> { value.StkId }, _priorityId);
            }
        }

        // เปลี่ยนคลังที่สแกน (จากหน้าต่าง SCAN STOCKS) แล้วจำไว้ในเครื่อง
        public void ApplyScope(bool auto, List<int> ids, int? priorityId, bool save = true)
        {
            _scopeAuto = auto;
            _scopeIds = (ids ?? new List<int>()).Where(id => Stocks.Any(s => s.StkId == id)).Distinct().ToList();
            if (!_scopeAuto && _scopeIds.Count == 0)
            {
                var fallback = Stocks.FirstOrDefault(s => s.IsMain) ?? Stocks.FirstOrDefault();
                if (fallback != null) _scopeIds.Add(fallback.StkId);
            }
            _priorityId = priorityId.HasValue && Stocks.Any(s => s.StkId == priorityId.Value) ? priorityId : null;

            _selectedStock = IsMultiScope ? null : ScopeStocks.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedStock));
            OnPropertyChanged(nameof(ScopeText));
            OnPropertyChanged(nameof(ScopeSubText));
            OnPropertyChanged(nameof(ScopeSubVisibility));
            OnPropertyChanged(nameof(IsMultiScope));
            ApplyStockModes();
            if (save) SaveScope();
            LoadTodayData();
        }

        private static bool SupportsReturn(StockModel s) => s != null && (s.InSysQr || s.InSupplier);

        // 🏷️ ค่าที่แสดงแทนรหัสต่อคลัง (ปุ่ม DISPLAY) - แสดงผลอย่างเดียว ไม่มีผลกับการบันทึก
        private Dictionary<int, string> _displayMap = new Dictionary<int, string>();
        private List<BarcodeFormatModel> _displayFormats = new List<BarcodeFormatModel>();

        private void LoadDisplaySettings()
        {
            try
            {
                _displayMap = _scanService.GetScanDisplayMap();
                _displayFormats = _displayMap.Values.Any(v => v.StartsWith("FIELD:")) ? _stockService.GetFormats() : new List<BarcodeFormatModel>();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadDisplaySettings: {ex.Message}"); }
        }

        // หลังบันทึกการตั้งค่าในหน้าต่าง DISPLAY -> ใช้ค่าใหม่กับตารางวันนี้ทันที
        public void ReloadDisplaySettings()
        {
            LoadDisplaySettings();
            _todaySignature = null;
            LoadTodayData();
        }

        private void ApplyDisplay(ScanItemModel item, string raw, bool fetchInfo)
        {
            if (item == null || !_displayMap.TryGetValue(item.StkId, out string key)) { if (item != null) item.DisplayCode = null; return; }
            if (!string.IsNullOrEmpty(raw)) item.RefNo = raw;
            if (fetchInfo && (key == "PartA" || key == "PartNumber" || key == "Model") && item.PartA == null && item.PartId > 0)
            {
                try { var info = _scanService.GetPartDisplayInfo(item.PartId); item.PartA = info.PartA; item.PartNumber = info.PartNumber; item.Model = info.Model; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"GetPartDisplayInfo: {ex.Message}"); }
            }
            string v = ScanDisplay.Resolve(key, item.PartCode, item.PartA, item.PartNumber, item.Model, item.PartName, item.RefNo, _displayFormats);
            item.DisplayCode = string.IsNullOrWhiteSpace(v) ? null : v;
        }

        private void LoadStocks()
        {
            try
            {
                _allFormats = _stockService.GetFormats(activeOnly: true);
                LoadDisplaySettings();
                var all = _stockService.GetStocks();
                bool canIn = CurrentUser?.CanViewScanIn == true;
                bool canOut = CurrentUser?.CanViewScanOut == true;

                Stocks.Clear();
                foreach (var s in all)
                {
                    bool visible = CurrentUser?.CanViewStock(s) == true;
                    // มีแค่ VIEW (ไม่มี ADD / EDIT) -> เห็นคลังที่เปิดช่องทางสแกนไว้ ใช้ได้เฉพาะ TEST MODE / HISTORY
                    bool scannable = (canIn && s.CanScanIn) || (canOut && s.CanScanOut) ||
                                     (!canIn && !canOut && (s.CanScanIn || s.CanScanOut));
                    if (visible && scannable) Stocks.Add(s);
                }

                ReadScope(out bool auto, out List<int> ids, out int? prio);
                ApplyScope(auto, ids, prio, save: false);
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ไม่สามารถโหลดรายชื่อคลังสำหรับแสกนได้\n" + ex.Message);
            }
        }

        // ปรับโหมดให้เข้ากับคลังที่เลือก (คลังที่จ่ายออกได้อย่างเดียว บังคับโหมด OUT / คลังที่ไม่มี OUT ยกเลิกโหมด OUT)
        // สแกนร่วมหลายคลัง: ดูรวมทุกคลังที่เลือก
        private void ApplyStockModes()
        {
            var list = ScopeStocks;
            if (list.Count > 0)
            {
                bool anyIn = list.Any(s => s.CanScanIn), anyOut = list.Any(s => s.CanScanOut);
                if (!anyIn && anyOut) { _isScanOutMode = false; IsScanOutMode = true; }
                else if (!anyOut && IsScanOutMode && !IsScanOutOnlyUser) IsScanOutMode = false;

                if (IsReturnMode && !list.Any(SupportsReturn)) IsReturnMode = false;
            }
            OnPropertyChanged(nameof(ScanOutButtonVisibility));
            OnPropertyChanged(nameof(ReturnButtonVisibility));
        }

        // จำคลังที่เลือกไว้ในเครื่อง: "15" (คลังเดียว - รูปแบบเดิม) / "1,15;15" (หลายคลัง;★) / "AUTO;15"
        private static string LastStockFile =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CIMS", "scanner_stock.txt");

        private static void ReadScope(out bool auto, out List<int> ids, out int? priority)
        {
            auto = false; ids = new List<int>(); priority = null;
            try
            {
                string raw = System.IO.File.ReadAllText(LastStockFile).Trim();
                var parts = raw.Split(';');
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int p)) priority = p;
                if (string.Equals(parts[0].Trim(), "AUTO", StringComparison.OrdinalIgnoreCase)) { auto = true; return; }
                foreach (var t in parts[0].Split(','))
                    if (int.TryParse(t.Trim(), out int id)) ids.Add(id);
            }
            catch { /* ไม่มีไฟล์ = ใช้คลังหลัก */ }
        }

        private void SaveScope()
        {
            try
            {
                string text = (_scopeAuto ? "AUTO" : string.Join(",", _scopeIds)) + (_priorityId.HasValue ? ";" + _priorityId.Value : "");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LastStockFile));
                System.IO.File.WriteAllText(LastStockFile, text);
            }
            catch { /* ไม่สำคัญพอที่จะแจ้งผู้ใช้ */ }
        }

        // 🔎 สแกนร่วมหลายคลัง: หาคลังของสินค้าที่สแกนจากคลังที่เลือกไว้
        //   - สินค้าต้องอยู่ในคลังนั้น + คลังเปิดช่องทาง/ทิศทาง (IN/OUT) ที่สแกนมา + รับรูปแบบป้ายนั้น
        //   - เจอคลังเดียว -> ใช้เลย / หลายคลัง: OUT ใช้ ★ ถ้ามี ไม่งั้นเด้งการ์ดให้เลือก
        //   - ป้ายที่ผูก "ตัดยอดจากคลัง" (Panta) รับเข้า -> ไม่ลงคลังต้นทางเอง (รับเข้าคลังหลักแล้วตัดคลังต้นทางเหมือนเดิม)
        // คืน null = ไม่บันทึก (แจ้งเตือนไปแล้ว หรือผู้ใช้กดยกเลิก)
        private async Task<StockModel> ResolveStockAsync(bool isOut, bool isPickList, BarcodeFormatModel supplierFmt,
                                                         List<string> codes, string rawBarcode)
        {
            var part = await Task.Run(() => FindPartByCodes(supplierFmt, codes));
            if (part == null)
            {
                ScanError($"[รายการไม่สำเร็จ] ไม่พบข้อมูลสินค้าในระบบ หรือบาร์โค้ดยังไม่ได้ลงทะเบียน\nCode: {codes.FirstOrDefault()}{LookupNoteText}");
                return null;
            }

            var memberIds = await Task.Run(() => _scanService.GetPartStockIds(part.PartId));
            var scope = ScopeStocks;
            var candidates = scope.Where(s => memberIds.Contains(s.StkId)
                                           && (isOut ? s.CanScanOut : s.CanScanIn)
                                           && CheckChannel(s, isOut, isPickList, supplierFmt) == null).ToList();

            int? deductSrc = DeductSourceFor(supplierFmt);
            if (!isOut && deductSrc != null && candidates.Count > 1)
                candidates.RemoveAll(s => s.StkId == deductSrc.Value);

            if (candidates.Count == 0)
            {
                var inStocks = scope.Where(s => memberIds.Contains(s.StkId)).Select(s => s.Code).ToList();
                string where = inStocks.Count == 0
                    ? "สินค้านี้ไม่ได้อยู่ในคลังที่เลือกไว้"
                    : $"สินค้านี้อยู่ในคลัง {string.Join(", ", inStocks)} แต่คลังนั้นไม่ได้เปิดให้{(isOut ? "จ่ายออก" : "รับเข้า")}ด้วยป้าย/QR แบบนี้";
                ScanError($"[ระงับการทำรายการ] ไม่พบคลังที่{(isOut ? "จ่ายออก" : "รับเข้า")}รายการนี้ได้\n\n{where}\n\n{part.PartCode} - {part.PartName}\nCode: {rawBarcode}");
                return null;
            }

            if (candidates.Count == 1) return candidates[0];

            if (isOut && _priorityId.HasValue)
            {
                var first = candidates.FirstOrDefault(s => s.StkId == _priorityId.Value);
                if (first != null) return first;
            }

            var chosen = Application.Current.Dispatcher.Invoke(() => Views.StockPickerWindow.ShowChoose(
                candidates,
                isOut ? "SELECT STOCK ( OUT )" : "SELECT STOCK ( IN )",
                $"{part.PartCode}  •  {part.PartName}\nสินค้านี้อยู่หลายคลัง กรุณาเลือกคลังที่จะ{(isOut ? "จ่ายออก" : "รับเข้า")}"));
            return chosen;
        }
        #endregion

        // ⚡ TURBO (เปิดตลอด): รับบาร์โค้ดเร็ว และแจ้งเตือนตอนสแกนเป็น Toast + เสียง (ไม่เด้ง Popup ค้างไว้) จะได้สแกนต่อเนื่องได้ไม่สะดุด
        public bool TurboMode => true;

        private void ScanError(string message, string title = "ERROR")
        {
            System.Media.SystemSounds.Hand.Play();
            Application.Current.Dispatcher.Invoke(() => NotificationManager.Show(title, message, false));
        }

        private void ScanWarning(string message, string title = "WARNING")
        {
            System.Media.SystemSounds.Exclamation.Play();
            Application.Current.Dispatcher.Invoke(() => NotificationManager.Show(title, message, false));
        }

        // คลังที่แสดงบนหน้าจอหลังสแกนล่าสุด (สแกนร่วมหลายคลังจะได้รู้ว่าลงคลังไหน)
        private string _showStock;
        public string ShowStock { get => _showStock; set { _showStock = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowStockVisibility)); } }
        public Visibility ShowStockVisibility => string.IsNullOrEmpty(ShowStock) ? Visibility.Collapsed : Visibility.Visible;

        #region Properties สำหรับผูกกับ UI
        private System.Windows.Media.ImageSource _showProductImage;
        public System.Windows.Media.ImageSource ShowProductImage
        {
            get => _showProductImage;
            set { _showProductImage = value; OnPropertyChanged(); }
        }

        private string _showName;
        public string ShowName { get => _showName; set { _showName = value; OnPropertyChanged(); } }

        private string _showCode;
        public string ShowCode { get => _showCode; set { _showCode = value; OnPropertyChanged(); } }

        // ยอดล่าสุดที่สแกน (ทศนิยมได้ถ้าคลังเปิด DECIMAL QTY)
        private decimal _showQtyValue;
        public string ShowQty
        {
            get => CIMS.Helpers.Qty.Plain(_showQtyValue);
            set { if (CIMS.Helpers.Qty.TryParse(value, out decimal res)) _showQtyValue = res; OnPropertyChanged(); }
        }

        private string _barcodeInput;
        public string BarcodeInput
        {
            get => _barcodeInput;
            set { _barcodeInput = value; OnPropertyChanged(); }
        }
        #endregion

        public ScanInViewModel()
        {
            ToggleTestModeCommand = new RelayCommand(p => ExecuteToggleTestMode());
            ToggleReturnModeCommand = new RelayCommand(p => ExecuteToggleReturnMode());
            ToggleRemainderModeCommand = new RelayCommand(p => IsRemainderMode = !IsRemainderMode);
            ToggleScanOutModeCommand = new RelayCommand(p => IsScanOutMode = !IsScanOutMode);
            // ตารางวันนี้โหลดตอนเลือกคลัง (SelectedStock) ใน ApplyPermissions -> LoadStocks
        }

        private void ExecuteToggleTestMode()
        {
            IsTestMode = !IsTestMode;
        }

        private void ExecuteToggleReturnMode()
        {
            IsReturnMode = !IsReturnMode;
        }

        // หลัง ADJUST SCAN แก้ / ลบรายการ -> โหลดตารางวันนี้ใหม่
        public void ReloadToday() => LoadTodayData();

        // 🔄 เรียลไทม์: ทุก 5 วินาทีดึงรายการวันนี้ใหม่ (เห็นที่เครื่องอื่นสแกน / ADJUST) - เปลี่ยนตารางเฉพาะเมื่อข้อมูลเปลี่ยน
        //    ไม่ดึงระหว่าง TEST MODE หรือภายใน 3 วินาทีหลังสแกน (กันตารางกระพริบตอนกำลังยิงต่อเนื่อง)
        private System.Windows.Threading.DispatcherTimer _liveTimer;
        private string _todaySignature;
        private DateTime _lastScanAt;
        private bool _liveBusy;

        public void StartLive()
        {
            if (_liveTimer == null)
            {
                _liveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _liveTimer.Tick += async (s, e) => await LiveRefreshAsync();
            }
            _liveTimer.Start();
        }

        public void StopLive() => _liveTimer?.Stop();

        private async Task LiveRefreshAsync()
        {
            if (_liveBusy || IsTestMode || (DateTime.Now - _lastScanAt).TotalSeconds < 3) return;
            _liveBusy = true;
            try { await LoadTodayDataAsync(onlyIfChanged: true); }
            finally { _liveBusy = false; }
        }

        private async void LoadTodayData() => await LoadTodayDataAsync(onlyIfChanged: false);

        private async Task LoadTodayDataAsync(bool onlyIfChanged)
        {
            var stocksForLoad = ScopeStocks;
            if (stocksForLoad.Count == 0) return;
            try
            {
                // สแกนร่วมหลายคลัง: รวมรายการวันนี้ของทุกคลังที่เลือก เรียงตามเวลา (แต่ละแถวรู้ว่าเป็นของคลังไหน)
                // ยอดคงเหลือของแต่ละสินค้าในตารางสรุปดึงเบื้องหลังพร้อมกัน (เดิมดึงทีละแถวบน UI Thread ทำให้หน้าจอค้างเมื่อวันนั้นสแกนเยอะ)
                var balances = new Dictionary<(int, int), decimal>();
                var data = await Task.Run(() =>
                {
                    var rows = stocksForLoad
                        .SelectMany(st => _scanService.GetTodayTransactions(st).Select(t => { t.StkId = st.StkId; t.StockCode = st.Code; t.ShowStockInCode = stocksForLoad.Count > 1; return t; }))
                        .OrderBy(t => t.UpdateTime).ToList();
                    foreach (var k in rows.Select(r => (r.StkId, r.PartId)).Distinct())
                    {
                        var st = stocksForLoad.FirstOrDefault(s => s.StkId == k.StkId);
                        balances[k] = _scanService.GetInventoryBalance(k.PartId, st);
                    }
                    return rows;
                });

                string sig = string.Join(",", stocksForLoad.Select(s => s.StkId)) + "|" + data.Count + "|" + data.Sum(d => d.Qty) + "|" +
                             (data.Count > 0 ? data.Max(d => d.UpdateTime).Ticks : 0);
                if (onlyIfChanged && sig == _todaySignature) return;
                _todaySignature = sig;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScannedItems.Clear();
                    HistoryItems.Clear();
                    foreach (var item in data)
                    {
                        // หน้าเดียวรวมทั้งรับเข้า/คืน/สแกนออก จึงแสดงทุกสถานะในตารางวันนี้
                        ApplyDisplay(item, item.RefNo, fetchInfo: false);
                        if (item.Status == "IN" || item.Status == "RETURN" || item.Status == "OUT")
                        {
                            ScannedItems.Insert(0, item);
                        }
                        UpdateSummary(item, balances.TryGetValue((item.StkId, item.PartId), out decimal bal) ? bal : (decimal?)null);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load Error: {ex.Message}");
            }
        }

        public async Task ProcessScan(string inputCode)
        {
            if (string.IsNullOrWhiteSpace(inputCode) || CurrentUser == null) return;
            _lastScanAt = DateTime.Now;

            // 🔄 โหมดคืนเหล็ก: ตัดออกจาก flow ปกติทั้งหมด ใช้ QR รูปแบบ Export จากหน้า ProductControl
            // (ProductCode|ProductName) แทน แล้วเด้ง Popup ให้กรอกจำนวนรับคืนเอง
            if (IsReturnMode)
            {
                if (!IsTestMode && !CurrentUser.CanScanReturn)
                {
                    DialogHelper.ShowWarning("คุณไม่มีสิทธิ์รับคืน (ใช้ได้เฉพาะ TEST MODE)", "ACCESS DENIED");
                    return;
                }
                await ProcessReturnScan(inputCode);
                return;
            }

            // 🔐 VIEW อย่างเดียวสแกนจริงไม่ได้ (TEST MODE ได้) / ADD = สแกนเข้า + คืน / EDIT = สแกนออก
            if (!IsTestMode && !(IsScanOutMode ? CurrentUser.CanViewScanOut : CurrentUser.CanViewScanIn))
            {
                DialogHelper.ShowWarning(IsScanOutMode ? "คุณไม่มีสิทธิ์สแกนออก (ใช้ได้เฉพาะ TEST MODE)" : "คุณไม่มีสิทธิ์สแกนเข้า (ใช้ได้เฉพาะ TEST MODE)", "ACCESS DENIED");
                return;
            }

            string finalSearchCode = inputCode.Trim();
            string rawBarcodeFull = finalSearchCode;
            string uid = CurrentUser.UserId;

            // จับโหมด + คลังไว้ตอนเริ่มสแกน กันกรณีผู้ใช้กดสลับปุ่ม/เปลี่ยนคลังระหว่างที่กำลังบันทึกอยู่
            bool isOut = IsScanOutMode;
            bool multiScope = IsMultiScope;
            var stock = SelectedStock;
            if (stock == null && !multiScope)
            {
                DialogHelper.ShowWarning("กรุณาเลือกคลัง (STOCK) ก่อนแสกน");
                BarcodeInput = string.Empty;
                return;
            }

            // ✅ รองรับ QR ที่พิมพ์จากหน้า Pick List (TicketNo | MaterialCode | WorkOrder | LotNo | JobName | Qty)
            // ให้ค้นหาด้วย MaterialCode และใช้ Qty ตามใบเบิกแทนค่า Pack Size เริ่มต้นของสินค้า
            bool isPackingCardScan = PackingCardBarcodeParser.TryParse(finalSearchCode, out string packingMaterialCode, out decimal packingQty);

            // ✅ ป้าย Supplier (เช่น Panta) ตามรูปแบบที่ตั้งไว้ใน CIMS.BarcodeFormats: ค้นหาด้วยช่องรหัสที่กำหนด
            // (ตัวที่ผู้ใช้ลงทะเบียนไว้ในช่อง QR Code หน้า Inventory Registration) และใช้จำนวนตามป้าย
            List<string> supplierCodes = null;
            decimal? supplierQty = null;
            BarcodeFormatModel supplierFmt = null;
            string supplierRaw = finalSearchCode;   // ป้ายดิบ (ใช้อ่านเลข Coil ตามรูปแบบที่ใช้จริง)
            if (!isPackingCardScan)
            {
                string raw = finalSearchCode;
                bool receiving = !isOut;
                var r = await Task.Run(() => { var f = MatchSupplier(raw, multiScope ? null : stock, out var c, out var q, receiving); return (f, c, q); });
                supplierFmt = r.f; supplierCodes = r.c; supplierQty = r.q;
            }
            bool isSupplierScan = supplierFmt != null;

            if (isPackingCardScan)
            {
                finalSearchCode = packingMaterialCode;
            }
            else if (isSupplierScan)
            {
                finalSearchCode = supplierCodes[0];
            }
            else
            {
                var parts = finalSearchCode.Split(new[] { '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                {
                    string firstChunk = parts[0].Trim();

                    if (firstChunk.Length > 2 && char.IsDigit(firstChunk[0]) && char.IsLetter(firstChunk[1]))
                    {
                        finalSearchCode = firstChunk.Substring(1);
                    }
                    else
                    {
                        finalSearchCode = firstChunk;
                    }
                }
            }

            // 🏬 สแกนร่วมหลายคลัง / AUTO: ระบบหาคลังของสินค้านี้เอง (เด้งให้เลือกถ้าอยู่หลายคลัง)
            if (multiScope)
            {
                var codes = isSupplierScan ? supplierCodes : new List<string> { finalSearchCode };
                stock = await ResolveStockAsync(isOut, isPackingCardScan, supplierFmt, codes, rawBarcodeFull);
                if (stock == null) { BarcodeInput = string.Empty; return; }
            }

            // 🏷️ หลายรูปแบบในคลังเดียว: ใช้รูปแบบที่ผูกกับคลังนี้ (หาสินค้า / จำนวน / ตัดยอดคลังต้นทาง ตามรูปแบบนั้น)
            if (isSupplierScan)
            {
                FormatForStock(stock, ref supplierFmt, ref supplierCodes, ref supplierQty);
                finalSearchCode = supplierCodes[0];
            }

            // 🚦 ตรวจช่องทางแสกนตามการตั้งค่าคลัง (Pick List QR / ป้าย Supplier / QR ระบบ)
            string channelError = CheckChannel(stock, isOut, isPackingCardScan, supplierFmt);
            if (channelError != null)
            {
                ScanError(channelError + $"\n\nCode: {rawBarcodeFull}");
                BarcodeInput = string.Empty;
                return;
            }

            // 🧲 ทะเบียน Coil: เลข Coil ลูก / แม่ จากป้าย (รูปแบบที่ตั้ง COIL NO FIELD #) + กันสแกนลูกเดิมซ้ำ
            ScanService.CoilLabel coil = null;
            var coilFmt = isSupplierScan ? CoilFormatFor(supplierFmt) : null;
            if (isSupplierScan && stock.CountCoil && coilFmt != null)
            {
                coilFmt.ReadCoil(supplierRaw, out string coilNo, out string mother);
                coil = new ScanService.CoilLabel { CoilNo = coilNo, MotherCoil = mother };
                if (coil.HasCoilNo && !IsRemainderMode)
                {
                    var st = await Task.Run(() => _scanService.FindCoil(coil.CoilNo));
                    string err = null;
                    if (!isOut && st != null && st.Status == "IN" && st.StockId == stock.StkId)
                        err = $"Coil {coil.CoilNo} อยู่ในคลัง {stock.Code} แล้ว (สแกนซ้ำ) - ไม่บันทึก";
                    else if (isOut && st != null && st.Status == "OUT")
                        err = $"Coil {coil.CoilNo} จ่ายออกไปแล้ว - ไม่บันทึก";
                    else if (isOut && st != null && st.Status == "IN" && st.StockId != stock.StkId)
                        err = $"Coil {coil.CoilNo} อยู่ที่คลัง {st.StockCode} ไม่ใช่คลัง {stock.Code} - ไม่บันทึก";
                    if (err != null) { ScanError(err + $"\n\nCode: {rawBarcodeFull}"); BarcodeInput = string.Empty; return; }
                }
            }

            ScanService.DeductResult deductResult = null;
            bool remainder = IsRemainderMode;
            bool remainderCancelled = false;
            try
            {
                var result = await Task.Run(() =>
                {
                    _lookupNote = null;
                    var part = isSupplierScan ? FindPartByCodes(supplierFmt, supplierCodes) : _scanService.GetPartByScan(finalSearchCode);
                    if (part == null) return null;

                    // คลังที่เปิด DECIMAL QTY (เช่น KG) เก็บทศนิยมตามป้าย (สูงสุด 3 ตำแหน่ง) / คลังอื่นปัดเป็นจำนวนเต็มที่ใกล้ที่สุดเหมือนเดิม
                    // (ค่าเต็มยังอยู่ครบในบาร์โค้ดดิบที่บันทึกลง ReferenceNo) - รูปแบบที่ไม่ได้กำหนดช่องจำนวน ใช้ Pack Size
                    bool dec = stock.AllowDecimal;
                    decimal originalQty = Qty.Round(isPackingCardScan ? packingQty
                                    : (isSupplierScan && supplierQty.HasValue) ? supplierQty.Value
                                    : part.Qty, dec);

                    // 🔢 สแกนเศษ: กรอกจำนวนเอง (ไม่ใช้ Pack Size / จำนวนบนป้าย) และไม่นับกล่อง
                    if (remainder)
                    {
                        decimal? typed = DialogHelper.ShowDecimalQuantityInput(
                            $"{(isOut ? "สแกนออก (เศษ)" : "สแกนเข้า (เศษ)")}  •  {stock.Code}\n{part.PartCode}  {part.PartName}\n\nกรอกจำนวนเศษ (Pack Size {Qty.Plain(originalQty)})",
                            "REMAINDER QTY", dec);
                        if (typed == null || typed.Value <= 0) { remainderCancelled = true; return null; }
                        originalQty = typed.Value;
                    }

                    // ⚙️ [เพิ่มเงื่อนไข Test Mode]: ถ้าอยู่ในโหมด Test จะไม่ยิง UpdateStock เข้า DB
                    if (IsTestMode)
                    {
                        part.Qty = originalQty;
                        return part; // ส่งข้อมูลคืนทันที โดยไม่สั่งบันทึก DB และไม่เขียน Log
                    }

                    // บันทึกจริงกรณีโหมดปกติ: รับเข้า หรือ ตัดสต็อกออก (UpdateStockOut คืน false ถ้าสต็อกไม่พอ)
                    // 🔁 ป้าย Supplier ที่ผูก "ตัดยอดจากคลัง" ไว้ (เช่น Panta -> STOCK-PANTA) + สแกนรับเข้า (ทุกคลัง ไม่จำกัดคลังหลัก)
                    //    -> รับเข้าเต็มจำนวน และตัดสินค้าที่ BIN เดียวกันในคลังต้นทางอัตโนมัติใน Transaction เดียวกัน
                    int? deductFrom = isSupplierScan ? DeductSourceFor(supplierFmt) : null;
                    bool useDeduct = !isOut && deductFrom.HasValue && deductFrom.Value != stock.StkId;
                    bool isSaved;
                    if (useDeduct)
                    {
                        deductResult = _scanService.UpdateStockWithDeduct(part.PartId, part.PartCode, part.PartACode, originalQty, uid, rawBarcodeFull, stock, deductFrom.Value, remainder, coil);
                        isSaved = deductResult.Saved;
                    }
                    else
                    {
                        isSaved = isOut
                            ? _scanService.UpdateStockOut(part.PartId, part.PartCode, part.PartACode, originalQty, uid, rawBarcodeFull, stock, remainder, coil)
                            : _scanService.UpdateStock(part.PartId, part.PartCode, part.PartACode, originalQty, uid, rawBarcodeFull, "IN", stock, remainder, coil);
                    }
                    if (isSaved)
                    {
                        // คลังหลักเขียน Log แบบเดิม คลังอื่นต่อท้ายรหัสคลังไว้ให้รู้ว่าแสกนคลังไหน
                        string logRef = stock.IsMain ? rawBarcodeFull : $"{rawBarcodeFull} | STOCK: {stock.Code}";
                        if (remainder) logRef += " | REMAINDER";
                        if (deductResult != null)
                            logRef += $" | DEDUCT {deductResult.SourceCode}{(deductResult.MatchedByBin ? $" ({deductResult.SourcePartCode} {deductResult.MatchedHow})" : "")}: {deductResult.Deducted} (bal {deductResult.SourceBefore}->{deductResult.SourceAfter})";
                        if (coil?.HasCoilNo == true) logRef += $" | COIL {coil.CoilNo}";
                        LogService.WriteScanLog(uid, isOut ? "SCAN_OUT" : "SCAN_IN", part.PartCode, part.PartACode, originalQty, logRef);
                        part.Qty = originalQty;
                        return part;
                    }
                    return null;
                });

                if (remainderCancelled) return;   // กดยกเลิกตอนกรอกจำนวนเศษ -> ไม่บันทึก ไม่แจ้ง error

                if (result != null)
                {
                    ShowCode = result.PartACode;
                    ShowName = result.PartName;
                    ShowQty = Qty.Plain(result.Qty);
                    ShowStock = stock.Code;

                    LoadProductImage(result.PartId, result.PartACode);

                    var newItem = new ScanItemModel
                    {
                        PartId = result.PartId,
                        PartCode = result.PartCode,
                        PartName = result.PartName,
                        PartNo = result.PartNo,
                        PartACode = result.PartACode,
                        Qty = result.Qty,
                        StkId = stock.StkId,
                        StockCode = stock.Code,
                        ShowStockInCode = multiScope,
                        Status = IsTestMode ? "TEST" : (isOut ? "OUT" : "IN"), // กำหนดสถานะให้เห็นชัดๆ
                        UpdateTime = DateTime.Now,
                        ProductImagePath = ShowProductImage
                    };
                    ApplyDisplay(newItem, rawBarcodeFull, fetchInfo: true);
                    ShowCode = newItem.CodeText;

                    // ⚙️ [สลับตารางแสดงผล]: ถ้าเป็น Test Mode ให้โยนลง TestScannedItems (ตารางที่ 2)
                    if (IsTestMode)
                    {
                        TestScannedItems.Insert(0, newItem);
                        if (TestScannedItems.Count > 12)
                        {
                            TestScannedItems.RemoveAt(TestScannedItems.Count - 1);
                        }
                    }
                    else
                    {
                        ScannedItems.Insert(0, newItem);
                        if (ScannedItems.Count > 12)
                        {
                            ScannedItems.RemoveAt(ScannedItems.Count - 1);
                        }
                        UpdateSummary(newItem);
                    }

                    // ⚠ ยอดคลังต้นทางไม่พอ: รับเข้าคลังหลักเต็มจำนวนแล้ว แต่ตัดคลังต้นทางได้เท่าที่มี -> แจ้งเตือนให้ตรวจสอบ
                    if (deductResult != null && deductResult.Short && deductResult.MotherCoil != null)
                    {
                        // ตัดตาม Coil แม่: ไม่เจอ Coil แม่ในคลังต้นทาง / Coil แม่เหลือน้อยกว่าป้าย
                        string why = deductResult.MatchedHow == null
                            ? $"ไม่พบ Coil แม่ {deductResult.MotherCoil} ใน {deductResult.SourceCode}\n(ยังไม่ได้ลงทะเบียน หรือใช้หมดแล้ว) จึงไม่ได้ตัดยอด"
                            : $"Coil แม่ {deductResult.MotherCoil} ใน {deductResult.SourceCode} เหลือ {Qty.Plain(deductResult.MotherBefore ?? deductResult.SourceBefore)} KG\nตัดได้ {Qty.Plain(deductResult.Deducted)} KG";
                        ScanWarning(
                            $"{why}\n\n{result.PartCode}  Coil {coil?.CoilNo}\nจำนวนบนป้าย: {Qty.Plain(deductResult.RequestedQty)}\n\n" +
                            $"รับเข้า {stock.Code} เต็มจำนวนแล้ว กรุณาตรวจสอบ {deductResult.SourceCode}",
                            "MOTHER COIL");
                    }
                    else if (deductResult != null && deductResult.Short)
                    {
                        ScanWarning(
                            $"ยอดใน {deductResult.SourceCode} ไม่พอสำหรับรายการนี้\n\n" +
                            $"{result.PartCode}\nจำนวนบนป้าย: {Qty.Plain(deductResult.RequestedQty)}\n" +
                            $"มีใน {deductResult.SourceCode}: {Qty.Plain(deductResult.SourceBefore)}  (ตัดได้ {Qty.Plain(deductResult.Deducted)})\n\n" +
                            $"รับเข้า {stock.Code} เต็มจำนวน {Qty.Plain(deductResult.RequestedQty)} แล้ว กรุณาตรวจสอบยอด {deductResult.SourceCode}",
                            "SOURCE STOCK SHORT");
                    }
                }
                else if (isOut)
                {
                    ScanError($"[ระงับการทำรายการ] ไม่พบข้อมูลสินค้า หรือ สินค้าในระบบไม่เพียงพอสำหรับจ่ายออก (คลัง {stock.Code} มีไม่พอ)\nCode: {finalSearchCode}{LookupNoteText}");
                }
                else
                {
                    ScanError($"[รายการไม่สำเร็จ] ไม่พบข้อมูลสินค้าในระบบ หรือบาร์โค้ดยังไม่ได้ลงทะเบียน\nCode: {finalSearchCode}{LookupNoteText}");
                }
            }
            catch (Exception ex)
            {
                ScanError("เกิดข้อผิดพลาด: " + ex.Message);
            }
            finally
            {
                BarcodeInput = string.Empty;
            }
        }

        // ดึงรูปสินค้าจาก Shared Folder มาแสดงที่ ShowProductImage (ใช้ร่วมกันทั้งสแกนปกติและคืนเหล็ก)
        // รูปสินค้าที่สแกน: ImageFileName ของสินค้า (1. Image Stock\<คลัง>\<คลัง>-01.png หรือรูปเก่า) -> ถ้าไม่มี ลองชื่อ Product Code แบบเดิม
        private void LoadProductImage(int ptId, string partACode)
        {
            string imgPath = null;
            try
            {
                using (var conn = new Microsoft.Data.SqlClient.SqlConnection(GlobalConfig.ConnStr))
                using (var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT ImageFileName FROM CIMS.Parts WHERE PartID = @p", conn))
                {
                    cmd.Parameters.AddWithValue("@p", ptId);
                    conn.Open();
                    imgPath = ImagePaths.Resolve(cmd.ExecuteScalar() as string);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Load ImageFileName: {ex.Message}"); }

            if (!ImageCacheHelper.Exists(imgPath))
            {
                string png = System.IO.Path.Combine(ImagePaths.LegacyRoot, $"{partACode}.png"), jpg = System.IO.Path.Combine(ImagePaths.LegacyRoot, $"{partACode}.jpg");
                imgPath = ImageCacheHelper.Exists(png) ? png : ImageCacheHelper.Exists(jpg) ? jpg : null;
            }

            var bmp = imgPath == null ? null : ImageCacheHelper.LoadImage(imgPath, 400);
            Application.Current.Dispatcher.Invoke(() => ShowProductImage = bmp);
        }
        // ตัด Product Code ออกจาก QR ที่ Export มาจากหน้า ProductControl (รูปแบบ "ProductCode|ProductName")
        // เผื่อกรณีไม่มี "|" (เช่น พิมพ์รหัสตรงๆ) ให้ใช้ค่าที่ trim แล้วทั้งก้อนแทน
        // ป้าย Supplier คืนรหัสที่ลองค้นหาได้หลายตัว (ช่องรหัสสินค้า แล้วค่อยช่องรหัสสำรอง) ตามลำดับ
        private List<string> ExtractReturnScanCodes(string raw, out BarcodeFormatModel matchedFmt)
        {
            matchedFmt = null;
            if (string.IsNullOrWhiteSpace(raw)) return new List<string> { string.Empty };
            matchedFmt = MatchSupplier(raw, SelectedStock, out List<string> codes, out _);
            if (matchedFmt != null) return codes;
            string trimmed = raw.Trim();
            int pipeIndex = trimmed.IndexOf('|');
            return new List<string> { pipeIndex >= 0 ? trimmed.Substring(0, pipeIndex).Trim() : trimmed };
        }

        // 🚦 ตรวจว่าคลังนี้เปิดช่องทางที่แสกนมาหรือไม่ - คืนข้อความแจ้งเตือน (ไทย) หรือ null ถ้าผ่าน
        private string CheckChannel(StockModel stock, bool isOut, bool isPickList, BarcodeFormatModel supplierFmt)
        {
            string dir = isOut ? "จ่ายออก" : "รับเข้า";
            // ⏳ คลังที่แสดงสดจาก StorePC (ชั่วคราว) สแกนใน CIMS ไม่ได้ - ให้สแกนในโปรแกรม StorePC
            if (stock.IsLiveView)
                return $"คลัง {stock.Code} ไม่เปิดให้สแกน{dir}ใน CIMS ชั่วคราว\n{StockModel.LiveViewMessage}";
            if (isPickList)
                return (isOut ? stock.OutPickList : stock.InPickList) ? null
                    : $"คลัง {stock.Code} ไม่ได้เปิดให้{dir}ด้วย Pick List QR";

            if (supplierFmt != null)
            {
                if (!(isOut ? stock.OutSupplier : stock.InSupplier))
                    return $"คลัง {stock.Code} ไม่ได้เปิดให้{dir}ด้วยป้าย Supplier ({supplierFmt.Name})";
                if (!StockAcceptsLabel(stock, supplierFmt))
                    return $"คลัง {stock.Code} ไม่รองรับป้าย Supplier รูปแบบ {supplierFmt.Name}";
                return null;
            }

            return (isOut ? stock.OutSysQr : stock.InSysQr) ? null
                : $"คลัง {stock.Code} ไม่ได้เปิดให้{dir}ด้วย QR ระบบ";
        }

        // ค้นสินค้าตามรูปแบบป้าย (ชื่อสินค้า หรือรหัสทีละตัว) - _lookupNote = เหตุผลที่ไม่เจอ ไว้ต่อท้ายข้อความแจ้งเตือน
        private string _lookupNote;
        private ScanItemModel FindPartByCodes(BarcodeFormatModel fmt, List<string> codes)
        {
            var part = _scanService.FindPart(fmt, codes, out string note, PreferStocks(SelectedStock));
            _lookupNote = note;
            return part;
        }

        // คลังที่รับ (ไว้เลือกสินค้าชื่อซ้ำ): คลังที่เลือก / สแกนร่วม = คลังในขอบเขตที่ไม่ใช่คลังต้นทางที่ถูกตัด
        private List<int> PreferStocks(StockModel stock)
        {
            if (stock != null) return new List<int> { stock.StkId };
            var sources = new HashSet<int>(_allFormats.Where(f => f.SourceStkId.HasValue).Select(f => f.SourceStkId.Value));
            return ScopeStocks.Where(s => !sources.Contains(s.StkId)).Select(s => s.StkId).ToList();
        }

        private string LookupNoteText => string.IsNullOrEmpty(_lookupNote) ? "" : "\n" + _lookupNote;

        // 🏷️ ป้าย Supplier: ลองทุกรูปแบบที่เข้ากับป้าย (รูปแบบที่คลังที่เลือกรับก่อน) แล้วใช้รูปแบบแรกที่เจอสินค้าในระบบ
        //    เช่น Panta (รหัส #11) กับ Panta-2 (ชื่อสินค้า #6 + #7) ป้ายหน้าตาเดียวกัน -> Panta หาไม่เจอก็ใช้ Panta-2
        //    ไม่เจอเลย = ใช้รูปแบบแรกที่เข้ากับป้าย (ไว้แจ้งว่าไม่พบสินค้า)
        // รูปแบบทั้งหมดที่อ่านป้ายล่าสุดได้ (คลังเลือกหลายรูปแบบ = ใช้ร่วมกันได้ ดู FormatForStock)
        private List<(BarcodeFormatModel Fmt, List<string> Codes, decimal? Qty)> _supplierHits = new List<(BarcodeFormatModel, List<string>, decimal?)>();

        // receiving = สแกนรับเข้า: หลายรูปแบบเจอสินค้า -> เลือกรูปแบบที่สินค้าอยู่ "คลังที่รับ" ก่อน (ไม่ใช่คลังต้นทางที่จะถูกตัด)
        //   เช่นป้าย Panta: รูปแบบ Panta (รหัส = Coil แม่) เจอสินค้าม้วนใหญ่ใน STOCK-PANTA / Panta-2 (ชื่อสินค้า) เจอสินค้าใน STOCK-MAT
        //   -> ใช้ Panta-2 รับเข้า STOCK-MAT แล้วตัด PANTA ผ่าน Coil แม่ (ไม่รับเข้า PANTA ผิดคลัง)
        private BarcodeFormatModel MatchSupplier(string raw, StockModel stock, out List<string> codes, out decimal? qty, bool receiving = false)
        {
            codes = null; qty = null;
            // รูปแบบที่คลังรับก่อน - สแกนร่วมหลายคลัง / AUTO = รูปแบบที่คลังไหนก็ได้ในขอบเขตรับก่อน
            var linked = stock != null ? new HashSet<int>(stock.FormatIds) : new HashSet<int>(ScopeStocks.SelectMany(s => s.FormatIds));
            var ordered = _allFormats.OrderBy(f => linked.Contains(f.FmtId) ? 0 : 1).ToList();
            var hits = new List<(BarcodeFormatModel Fmt, List<string> Codes, decimal? Qty)>();
            foreach (var fmt in ordered)
                if (fmt.TryParse(raw, out List<string> c, out decimal? q)) hits.Add((fmt, c, q));
            _supplierHits = hits;
            if (hits.Count == 0) return null;

            var pick = hits[0];
            if (hits.Count > 1)
            {
                var prefer = PreferStocks(stock);
                var found = hits.Select(h => (Hit: h, Part: _scanService.FindPart(h.Fmt, h.Codes, out _, prefer))).Where(x => x.Part != null).ToList();
                if (found.Count > 0)
                {
                    pick = found[0].Hit;
                    if (receiving && found.Count > 1)
                    {
                        var sources = new HashSet<int>(hits.Where(h => h.Fmt.SourceStkId.HasValue).Select(h => h.Fmt.SourceStkId.Value));
                        var scope = stock != null ? new List<StockModel> { stock } : ScopeStocks;
                        var receiveIds = new HashSet<int>(scope.Where(s => !sources.Contains(s.StkId)).Select(s => s.StkId));
                        foreach (var x in found)
                            if (_scanService.GetPartStockIds(x.Part.PartId).Overlaps(receiveIds)) { pick = x.Hit; break; }
                    }
                }
            }
            codes = pick.Codes; qty = pick.Qty;
            return pick.Fmt;
        }

        // 🏷️ คลังเลือกรูปแบบป้ายไว้หลายรูปแบบ = ใช้ร่วมกันได้: ถ้ารูปแบบที่เลือกมาไม่ได้ผูกกับคลังนี้
        //    แต่มีรูปแบบอื่นของคลังนี้อ่านป้ายเดียวกันได้ -> ใช้รูปแบบนั้นแทน (รูปแบบที่เจอสินค้า "ในคลังนี้" ก่อน)
        private void FormatForStock(StockModel stock, ref BarcodeFormatModel fmt, ref List<string> codes, ref decimal? qty)
        {
            if (stock == null || fmt == null || stock.FormatIds.Contains(fmt.FmtId)) return;
            var mine = _supplierHits.Where(h => stock.FormatIds.Contains(h.Fmt.FmtId)).ToList();
            if (mine.Count == 0) return;
            var prefer = new List<int> { stock.StkId };
            var withPart = mine.Select(h => (Hit: h, Part: _scanService.FindPart(h.Fmt, h.Codes, out _, prefer))).Where(x => x.Part != null).ToList();
            var inStock = withPart.FirstOrDefault(x => _scanService.GetPartStockIds(x.Part.PartId).Contains(stock.StkId));
            var pick = inStock.Hit.Fmt != null ? inStock.Hit : withPart.Count > 0 ? withPart[0].Hit : mine[0];
            fmt = pick.Fmt; codes = pick.Codes; qty = pick.Qty;
        }

        // คลังต้นทางที่ตัดยอด: รูปแบบที่ใช้ไม่ได้ตั้ง "ตัดยอดจากคลัง" แต่รูปแบบอื่นที่อ่านป้ายเดียวกันได้ตั้งไว้ -> ใช้ค่านั้น
        private int? DeductSourceFor(BarcodeFormatModel fmt) =>
            fmt?.SourceStkId ?? _supplierHits.Select(h => h.Fmt.SourceStkId).FirstOrDefault(s => s.HasValue);

        // รูปแบบที่ใช้อ่านเลข Coil ลูก / แม่: รูปแบบที่ใช้ไม่มีช่อง Coil -> ใช้รูปแบบอื่นที่อ่านป้ายเดียวกันได้และตั้งช่อง Coil ไว้
        private BarcodeFormatModel CoilFormatFor(BarcodeFormatModel fmt) =>
            fmt != null && (fmt.HasCoilNo || fmt.MotherCoilPos > 0) ? fmt
            : _supplierHits.Select(h => h.Fmt).FirstOrDefault(f => f.HasCoilNo || f.MotherCoilPos > 0);

        // คลังนี้รับป้ายนี้ไหม: รูปแบบที่เลือก หรือรูปแบบอื่นของคลังที่อ่านป้ายเดียวกันได้
        private bool StockAcceptsLabel(StockModel stock, BarcodeFormatModel fmt) =>
            stock.FormatIds.Contains(fmt.FmtId) || _supplierHits.Any(h => stock.FormatIds.Contains(h.Fmt.FmtId));

        // 🔄 [โหมดคืนเหล็ก] สแกน QR Export จากหน้า ProductControl -> ค้นหาสินค้า -> เด้ง Popup กรอกจำนวนรับคืน
        // -> ยืนยันแล้วอัปเดต StockQuantity ตามจำนวนที่กรอกเอง (ไม่ใช่ Packsize มาตรฐาน) พร้อม tag สถานะ "RETURN"
        private async Task ProcessReturnScan(string inputCode)
        {
            string rawBarcodeFull = inputCode.Trim();
            string uid = CurrentUser.UserId;
            var stock = SelectedStock;
            if (stock == null && !IsMultiScope)
            {
                DialogHelper.ShowWarning("กรุณาเลือกคลัง (STOCK) ก่อนแสกน");
                BarcodeInput = string.Empty;
                return;
            }

            var codes = ExtractReturnScanCodes(rawBarcodeFull, out BarcodeFormatModel matchedFmt);
            string code = codes[0];

            // สแกนร่วมหลายคลัง: คืนเหล็กเข้าคลังที่สินค้านั้นอยู่ (เด้งให้เลือกถ้าอยู่หลายคลัง)
            if (stock == null)
            {
                stock = await ResolveStockAsync(false, false, matchedFmt, codes, rawBarcodeFull);
                if (stock == null) { BarcodeInput = string.Empty; return; }
            }

            // คืนเหล็กเป็นการรับเข้า -> ต้องเปิดช่องทางรับเข้าแบบเดียวกับที่แสกนมา
            string channelError = CheckChannel(stock, false, false, matchedFmt);
            if (channelError != null)
            {
                ScanError(channelError + $"\n\nCode: {rawBarcodeFull}");
                BarcodeInput = string.Empty;
                return;
            }

            try
            {
                var part = await Task.Run(() => FindPartByCodes(matchedFmt, codes));

                if (part == null)
                {
                    ScanError($"[รายการไม่สำเร็จ] ไม่พบข้อมูลสินค้าในระบบสำหรับคืนเหล็ก\nCode: {code}{LookupNoteText}");
                    return;
                }

                decimal? enteredQty = DialogHelper.ShowDecimalQuantityInput(
                    $"{part.PartName}\nProduct Code: {part.PartCode}\n\nกรุณากรอกจำนวนที่รับคืนเข้าคลัง",
                    "คืนเหล็กเข้าคลัง", stock.AllowDecimal);

                if (enteredQty == null || enteredQty.Value <= 0)
                    return; // ผู้ใช้กด Cancel หรือปิดหน้าต่าง - ไม่ทำอะไรต่อ

                decimal qty = enteredQty.Value;
                bool isSaved = true;

                if (!IsTestMode)
                {
                    isSaved = await Task.Run(() =>
                        _scanService.UpdateStock(part.PartId, part.PartCode, part.PartACode, qty, uid, rawBarcodeFull, "RETURN", stock));

                    if (isSaved)
                    {
                        LogService.WriteScanLog(uid, "SCAN_RETURN", part.PartCode, part.PartACode, qty, stock.IsMain ? null : $"STOCK: {stock.Code}");
                    }
                    else
                    {
                        ScanError("บันทึกการคืนเหล็กไม่สำเร็จ กรุณาลองใหม่อีกครั้ง");
                        return;
                    }
                }

                ShowCode = part.PartACode;
                ShowName = part.PartName;
                ShowQty = Qty.Plain(qty);
                ShowStock = stock.Code;

                LoadProductImage(part.PartId, part.PartACode);

                var newItem = new ScanItemModel
                {
                    PartId = part.PartId,
                    PartCode = part.PartCode,
                    PartName = part.PartName,
                    PartNo = part.PartNo,
                    PartACode = part.PartACode,
                    Qty = qty,
                    StkId = stock.StkId,
                    StockCode = stock.Code,
                    ShowStockInCode = IsMultiScope,
                    Status = IsTestMode ? "TEST" : "RETURN",
                    UpdateTime = DateTime.Now,
                    ProductImagePath = ShowProductImage
                };
                ApplyDisplay(newItem, inputCode, fetchInfo: true);
                ShowCode = newItem.CodeText;

                if (IsTestMode)
                {
                    TestScannedItems.Insert(0, newItem);
                    if (TestScannedItems.Count > 12)
                    {
                        TestScannedItems.RemoveAt(TestScannedItems.Count - 1);
                    }
                }
                else
                {
                    ScannedItems.Insert(0, newItem);
                    if (ScannedItems.Count > 12)
                    {
                        ScannedItems.RemoveAt(ScannedItems.Count - 1);
                    }
                    UpdateSummary(newItem);
                }
            }
            catch (Exception ex)
            {
                ScanError("เกิดข้อผิดพลาด: " + ex.Message);
            }
            finally
            {
                BarcodeInput = string.Empty;
            }
        }

        // knownBalance: ยอดที่ดึงมาแล้วเบื้องหลัง (โหลดตารางวันนี้) / null = ดึงเองตอนนี้ (หลังสแกน 1 รายการ)
        private void UpdateSummary(ScanItemModel item, decimal? knownBalance = null)
        {
            if (string.IsNullOrWhiteSpace(item.PartACode)) return;

            // ยอดคงเหลือของคลังที่รายการนั้นลงจริง (สแกนร่วมหลายคลังแยกแถวสรุปตามคลัง)
            var itemStock = Stocks.FirstOrDefault(s => s.StkId == item.StkId && item.StkId != 0) ?? SelectedStock;
            decimal actualCurrentStock = knownBalance ?? _scanService.GetInventoryBalance(item.PartId, itemStock);
            var existing = HistoryItems.FirstOrDefault(x => x.PartACode == item.PartACode && x.StkId == item.StkId);

            if (existing != null)
            {
                if (string.IsNullOrEmpty(existing.PartCode) && !string.IsNullOrEmpty(item.PartCode))
                    existing.PartCode = item.PartCode;
                if (string.IsNullOrEmpty(existing.PartNo) && !string.IsNullOrEmpty(item.PartNo))
                    existing.PartNo = item.PartNo;
                if (string.IsNullOrEmpty(existing.PartName) && !string.IsNullOrEmpty(item.PartName))
                    existing.PartName = item.PartName;

                // การคืนเหล็ก (RETURN) นับรวมเป็นยอดรับเข้าเหมือน IN ในตารางสรุปนี้ เพราะเป็นการเพิ่มสต็อกเข้าคลังเช่นกัน
                if (item.Status == "IN" || item.Status == "RETURN")
                {
                    existing.InCount += 1;
                    existing.TotalInQty += item.Qty;
                }
                else if (item.Status == "OUT")
                {
                    existing.OutCount += 1;
                    existing.TotalOutQty += item.Qty;
                }

                existing.FinalStock = actualCurrentStock;
            }
            else
            {
                HistoryItems.Add(new ScanItemModel
                {
                    PartId = item.PartId,
                    StkId = item.StkId,
                    StockCode = item.StockCode,
                    ShowStockInCode = IsMultiScope,
                    PartCode = item.PartCode,
                    PartName = item.PartName,
                    PartACode = item.PartACode,
                    PartNo = item.PartNo,
                    DisplayCode = item.DisplayCode,
                    InCount = (item.Status == "IN" || item.Status == "RETURN") ? 1 : 0,
                    TotalInQty = (item.Status == "IN" || item.Status == "RETURN") ? item.Qty : 0,
                    OutCount = item.Status == "OUT" ? 1 : 0,
                    TotalOutQty = item.Status == "OUT" ? item.Qty : 0,
                    FinalStock = actualCurrentStock,
                    ProductImagePath = item.ProductImagePath
                });
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Helper Class สำหรับ Bind ปุ่ม Toggle
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
