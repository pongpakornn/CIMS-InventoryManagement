//using CIMS.Helpers;
//using CIMS.Models;
//using CIMS.Services;
//using System;
//using System.Collections.Generic;
//using System.Collections.ObjectModel;
//using System.ComponentModel;
//using System.Linq;
//using System.Runtime.CompilerServices;
//using System.Threading.Tasks;
//using System.Windows;
//using System.Windows.Media; // สำหรับ ImageSource

//namespace CIMS.ViewModels
//{
//    public class ScanOutViewModel : INotifyPropertyChanged
//    {
//        private readonly ScanService _scanService = new ScanService();

//        public UserSession CurrentUser { get; set; }

//        public ObservableCollection<ScanItemModel> ScannedItems { get; set; } = new ObservableCollection<ScanItemModel>();
//        public ObservableCollection<ScanItemModel> HistoryItems { get; set; } = new ObservableCollection<ScanItemModel>();

//        #region Properties สำหรับผูกกับ UI
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

//        // ✅ ปรับ ImageSource ให้เป็นแบบเดียวกับหน้าอิน
//        private ImageSource _showProductImage;
//        public ImageSource ShowProductImage
//        {
//            get => _showProductImage;
//            set { _showProductImage = value; OnPropertyChanged(); }
//        }
//        #endregion

//        public ScanOutViewModel()
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
//                        if (item.Status == "OUT")
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
//                // จัดการหารหัสสินค้าก้อนแรกสุด (เช่น "2A122-00059")
//                string firstChunk = parts[0].Trim();

//                if (firstChunk.Length > 2 && char.IsDigit(firstChunk[0]) && char.IsLetter(firstChunk[1]))
//                {
//                    finalSearchCode = firstChunk.Substring(1); // หั่นเหลือ "A122-00059"
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
//                    // ค้นหาสินค้าด้วยรหัสที่สะอาดเรียบร้อย "A122-00059"
//                    var part = _scanService.GetPartByScan(finalSearchCode);
//                    if (part == null) return null;

//                    // 💡 [อ้างอิงจำนวนตามระบบเดิมของคุณนนท์] 
//                    // ใช้ค่า part.Qty จากฐานข้อมูลตรงๆ โดยแปลง Type เป็น int เพื่อส่งเข้าฟังก์ชันจ่ายออก
//                    int originalQty = (int)part.Qty;

//                    // ✅ ยิงอัปเดตสต็อกออกคลังสินค้า พร้อมส่ง rawBarcodeFull เข้าไปบันทึกลง ReferenceNo
//                    bool isSaved = _scanService.UpdateStockOut(part.PartId, part.PartCode, part.PartACode, originalQty, uid, rawBarcodeFull);

//                    if (isSaved)
//                    {
//                        // บันทึก Log การจ่ายออก
//                        LogService.WriteScanLog(uid, "SCAN_OUT", part.PartCode, part.PartACode, originalQty);

//                        // ส่งค่าจำนวนเดิมจากตัวระบบกลับไปแสดงผล
//                        part.Qty = originalQty;
//                        return part;
//                    }
//                    return null;
//                });

//                if (result != null)
//                {
//                    // ✅ แสดงโค้ดหลักเป็น PartACode เหมือนกับหน้ารับเข้า
//                    ShowCode = result.PartACode;
//                    ShowName = result.PartName;
//                    ShowQty = result.Qty.ToString();

//                    // ✅ ค้นหารูปภาพแบบ Local Folder รองรับ .png และ .jpg อิงจาก PartACode
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

//                    // ✅ จัดการดึงรูปขึ้นและ Freeze บน UI Thread ป้องกัน Thread Conflict
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
//                        Qty = (int)result.Qty, // แปลง Type เป็น int ให้สอดคล้องกับ Model เดิม
//                        Status = "OUT",
//                        UpdateTime = DateTime.Now,
//                        ProductImagePath = ShowProductImage // แบกสตรีมภาพไปแสดงในตารางสรุป
//                    };

//                    // ควบคุมการแสดงผลข้อมูลที่ Daily ไม่เกิน 20 บรรทัดตามโค้ดปัจจุบัน
//                    ScannedItems.Insert(0, newItem);

//                    if (ScannedItems.Count > 12)
//                    {
//                        ScannedItems.RemoveAt(ScannedItems.Count - 1);
//                    }

//                    UpdateSummary(newItem);
//                }
//                else
//                {
//                    DialogHelper.ShowError($"[ระงับการทำรายการ] ไม่พบข้อมูลสินค้า หรือ สินค้าในระบบไม่เพียงพอสำหรับจ่ายออก (คลังสินค้ามีไม่พอ)\nCode: {finalSearchCode}");
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

//        public void RemoveItems(IEnumerable<ScanItemModel> itemsToRemove)
//        {
//            if (itemsToRemove == null) return;
//            var toRemove = itemsToRemove.ToList();
//            foreach (var item in toRemove)
//            {
//                ScannedItems.Remove(item);
//            }
//        }

//        // ✅ ตรรกะอัปเดตตารางขวา (ดึงสต็อกจริงยอดตรงจาก DB แบบเดียวกับหน้าอิน)
//        private void UpdateSummary(ScanItemModel item)
//        {
//            if (item == null || string.IsNullOrWhiteSpace(item.PartACode)) return;

//            // ดึงยอดคงเหลือจริงปัจจุบันจาก SQL ผ่าน PartId
//            decimal actualCurrentStock = _scanService.GetInventoryBalance(item.PartId);

//            Application.Current.Dispatcher.Invoke(() =>
//            {
//                var existing = HistoryItems.FirstOrDefault(x => x.PartACode == item.PartACode);

//                if (existing != null)
//                {
//                    if (string.IsNullOrEmpty(existing.PartCode) && !string.IsNullOrEmpty(item.PartCode))
//                        existing.PartCode = item.PartCode;
//                    if (string.IsNullOrEmpty(existing.PartNo) && !string.IsNullOrEmpty(item.PartNo))
//                        existing.PartNo = item.PartNo;
//                    if (string.IsNullOrEmpty(existing.PartName) && !string.IsNullOrEmpty(item.PartName))
//                        existing.PartName = item.PartName;
//                    if (existing.ProductImagePath == null && item.ProductImagePath != null)
//                        existing.ProductImagePath = item.ProductImagePath;

//                    if (item.Status == "IN")
//                    {
//                        existing.InCount += 1;
//                        existing.TotalInQty += item.Qty;
//                    }
//                    else if (item.Status == "OUT")
//                    {
//                        existing.OutCount += 1;
//                        existing.TotalOutQty += item.Qty;
//                    }

//                    // เอายอดจริงจาก SQL มาเขียนทับเลย ไม่ต้องคำนวณมือฝั่ง Client
//                    existing.FinalStock = actualCurrentStock;
//                }
//                else
//                {
//                    HistoryItems.Add(new ScanItemModel
//                    {
//                        PartId = item.PartId,
//                        PartCode = item.PartCode,
//                        PartName = item.PartName,
//                        PartACode = item.PartACode,
//                        PartNo = item.PartNo,
//                        InCount = item.Status == "IN" ? 1 : 0,
//                        TotalInQty = item.Status == "IN" ? item.Qty : 0,
//                        OutCount = item.Status == "OUT" ? 1 : 0,
//                        TotalOutQty = item.Status == "OUT" ? item.Qty : 0,

//                        // สแกนเจอรายการใหม่ครั้งแรกของวัน ให้ยึดจากฐานข้อมูลตัวล่าสุด
//                        FinalStock = actualCurrentStock,
//                        ProductImagePath = item.ProductImagePath
//                    });
//                }
//            });
//        }

//        public event PropertyChangedEventHandler PropertyChanged;
//        protected void OnPropertyChanged([CallerMemberName] string name = null)
//            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
//    }
//}



using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.ViewModels
{
    public class ScanOutViewModel : INotifyPropertyChanged
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

        public string TestButtonText => IsTestMode ? "ยกเลิกเทส (Exit Test)" : "เปิดโหมด Test";
        public Brush TestButtonBackground => IsTestMode
            ? (Brush)new BrushConverter().ConvertFrom("#D32F2F") // สีแดงเมื่อกดถอนโหมด
            : (Brush)new BrushConverter().ConvertFrom("#ED6C02"); // สีส้มโหมด Test

        public Visibility TestBannerVisibility => IsTestMode ? Visibility.Visible : Visibility.Collapsed;

        // Command สำหรับกดปุ่มสลับโหมด Test
        public ICommand ToggleTestModeCommand { get; }
        #endregion

        #region Properties สำหรับผูกกับ UI
        private string _showName;
        public string ShowName { get => _showName; set { _showName = value; OnPropertyChanged(); } }

        private string _showCode;
        public string ShowCode { get => _showCode; set { _showCode = value; OnPropertyChanged(); } }

        private int _showQtyValue;
        public string ShowQty
        {
            get => _showQtyValue.ToString();
            set { if (int.TryParse(value, out int res)) _showQtyValue = res; OnPropertyChanged(); }
        }

        private string _barcodeInput;
        public string BarcodeInput
        {
            get => _barcodeInput;
            set { _barcodeInput = value; OnPropertyChanged(); }
        }

        private ImageSource _showProductImage;
        public ImageSource ShowProductImage
        {
            get => _showProductImage;
            set { _showProductImage = value; OnPropertyChanged(); }
        }
        #endregion

        public ScanOutViewModel()
        {
            ToggleTestModeCommand = new RelayCommand(p => ExecuteToggleTestMode());
            LoadTodayData();
        }

        private void ExecuteToggleTestMode()
        {
            IsTestMode = !IsTestMode;
        }

        private async void LoadTodayData()
        {
            try
            {
                var data = await Task.Run(() => _scanService.GetTodayTransactions());

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ScannedItems.Clear();
                    HistoryItems.Clear();
                    foreach (var item in data)
                    {
                        if (item.Status == "OUT")
                        {
                            ScannedItems.Insert(0, item);
                        }
                        UpdateSummary(item);
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

            string finalSearchCode = inputCode.Trim();
            string rawBarcodeFull = finalSearchCode;
            string uid = CurrentUser.UserId;

            // ✅ รองรับ QR ที่พิมพ์จากหน้า Packing Card (TicketNo | MaterialCode | WorkOrder | LotNo | JobName | Qty)
            // ให้ค้นหาด้วย MaterialCode และใช้ Qty ตามใบเบิกแทนค่า Pack Size เริ่มต้นของสินค้า
            bool isPackingCardScan = PackingCardBarcodeParser.TryParse(finalSearchCode, out string packingMaterialCode, out decimal packingQty);

            if (isPackingCardScan)
            {
                finalSearchCode = packingMaterialCode;
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

            try
            {
                var result = await Task.Run(() =>
                {
                    var part = _scanService.GetPartByScan(finalSearchCode);
                    if (part == null) return null;

                    int originalQty = isPackingCardScan ? (int)packingQty : (int)part.Qty;

                    // ⚙️ [เพิ่มเงื่อนไข Test Mode]: ถ้าอยู่ในโหมด Test จะไม่ยิง UpdateStockOut เข้า DB
                    if (IsTestMode)
                    {
                        part.Qty = originalQty;
                        return part; // ส่งข้อมูลคืนทันที โดยไม่ตัดสต็อกจริงและไม่เขียน Log
                    }

                    // บันทึกและตัดสต็อกจริงกรณีโหมดปกติ
                    bool isSaved = _scanService.UpdateStockOut(part.PartId, part.PartCode, part.PartACode, originalQty, uid, rawBarcodeFull);

                    if (isSaved)
                    {
                        LogService.WriteScanLog(uid, "SCAN_OUT", part.PartCode, part.PartACode, originalQty);
                        part.Qty = originalQty;
                        return part;
                    }
                    return null;
                });

                if (result != null)
                {
                    ShowCode = result.PartACode;
                    ShowName = result.PartName;
                    ShowQty = result.Qty.ToString();

                    string baseFolder = @"\\192.168.10.56\ProgramCHR\2. Store Only\StoreSteels\Image";
                    string fileName = result.PartACode;
                    string imgPath = System.IO.Path.Combine(baseFolder, $"{fileName}.png");

                    if (!System.IO.File.Exists(imgPath))
                        imgPath = System.IO.Path.Combine(baseFolder, $"{fileName}.jpg");

                    if (!System.IO.File.Exists(imgPath))
                        imgPath = System.IO.Path.Combine(baseFolder, "no-image.png");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (System.IO.File.Exists(imgPath))
                            {
                                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                                bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                                bitmap.UriSource = new Uri(imgPath, UriKind.Absolute);
                                bitmap.EndInit();
                                bitmap.Freeze();

                                ShowProductImage = bitmap;
                            }
                            else
                            {
                                ShowProductImage = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Load Image Error: {ex.Message}");
                            ShowProductImage = null;
                        }
                    });

                    var newItem = new ScanItemModel
                    {
                        PartId = result.PartId,
                        PartCode = result.PartCode,
                        PartName = result.PartName,
                        PartNo = result.PartNo,
                        PartACode = result.PartACode,
                        Qty = (int)result.Qty,
                        Status = IsTestMode ? "TEST" : "OUT", // กำหนดสถานะให้ชัดเจน
                        UpdateTime = DateTime.Now,
                        ProductImagePath = ShowProductImage
                    };

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
                }
                else
                {
                    DialogHelper.ShowError($"[ระงับการทำรายการ] ไม่พบข้อมูลสินค้า หรือ สินค้าในระบบไม่เพียงพอสำหรับจ่ายออก (คลังสินค้ามีไม่พอ)\nCode: {finalSearchCode}");
                }
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("เกิดข้อผิดพลาด: " + ex.Message);
            }
            finally
            {
                BarcodeInput = string.Empty;
            }
        }

        public void RemoveItems(IEnumerable<ScanItemModel> itemsToRemove)
        {
            if (itemsToRemove == null) return;
            var toRemove = itemsToRemove.ToList();
            foreach (var item in toRemove)
            {
                ScannedItems.Remove(item);
            }
        }

        private void UpdateSummary(ScanItemModel item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.PartACode)) return;

            decimal actualCurrentStock = _scanService.GetInventoryBalance(item.PartId);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = HistoryItems.FirstOrDefault(x => x.PartACode == item.PartACode);

                if (existing != null)
                {
                    if (string.IsNullOrEmpty(existing.PartCode) && !string.IsNullOrEmpty(item.PartCode))
                        existing.PartCode = item.PartCode;
                    if (string.IsNullOrEmpty(existing.PartNo) && !string.IsNullOrEmpty(item.PartNo))
                        existing.PartNo = item.PartNo;
                    if (string.IsNullOrEmpty(existing.PartName) && !string.IsNullOrEmpty(item.PartName))
                        existing.PartName = item.PartName;
                    if (existing.ProductImagePath == null && item.ProductImagePath != null)
                        existing.ProductImagePath = item.ProductImagePath;

                    if (item.Status == "IN")
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
                        PartCode = item.PartCode,
                        PartName = item.PartName,
                        PartACode = item.PartACode,
                        PartNo = item.PartNo,
                        InCount = item.Status == "IN" ? 1 : 0,
                        TotalInQty = item.Status == "IN" ? item.Qty : 0,
                        OutCount = item.Status == "OUT" ? 1 : 0,
                        TotalOutQty = item.Status == "OUT" ? item.Qty : 0,
                        FinalStock = actualCurrentStock,
                        ProductImagePath = item.ProductImagePath
                    });
                }
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}