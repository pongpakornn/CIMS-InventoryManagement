using Microsoft.Win32;
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
using System.Windows.Data;
using System.Windows.Input;

namespace CIMS.ViewModels
{
    public class ProductControlViewModel : INotifyPropertyChanged
    {
        private readonly ProductControlService _qrService = new ProductControlService();

        private ProductControlModel _selectedProduct;
        public ProductControlModel SelectedProduct
        {
            get => _selectedProduct;
            set { _selectedProduct = value; OnPropertyChanged(); }
        }

        private ProductControlModel _inputModel = new ProductControlModel();
        public ProductControlModel InputModel
        {
            get => _inputModel;
            set { _inputModel = value; OnPropertyChanged(); }
        }

        public ICollectionView GroupedProducts { get; private set; }
        public UserSession CurrentUser { get; set; }

        // 🔐 สิทธิ์ PDControl: ADD = ลงทะเบียน + IMPORT / EDIT = แก้ไข + SHOW/HIDE + รูปภาพ / DEL = ลบสินค้า
        private static System.Windows.Visibility Vis(bool on) => on ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility AddVisibility => Vis(CurrentUser?.CanAddProduct == true);
        public System.Windows.Visibility EditVisibility => Vis(CurrentUser?.CanEditProduct == true);
        public System.Windows.Visibility DeleteVisibility => Vis(CurrentUser?.CanDeleteProduct == true);
        public System.Windows.Visibility AdminVisibility => Vis(CurrentUser?.UserLevel == 1);
        public System.Windows.Visibility ImageVisibility => Vis(CurrentUser?.CanAddProduct == true || CurrentUser?.CanEditProduct == true);

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<ProductControlModel> Products { get; set; } = new ObservableCollection<ProductControlModel>();

        private System.Threading.CancellationTokenSource _searchCts;

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                _ = SearchWithDelayAsync(_searchText);
            }
        }

        public void SelectProductImage()
        {
            if (SelectedProduct == null) PrepareAddProduct();

            // 🎯 เปลี่ยนมาเช็คความว่างเปล่าที่ PartACode แทนครับ
            if (string.IsNullOrWhiteSpace(SelectedProduct.PartACode))
            {
                DialogHelper.ShowError("กรุณากรอกรหัสสินค้าจริง (Part A Code) ก่อนเลือกรูปภาพครับ");
                return;
            }

            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpeg;*.jpg)|*.png;*.jpeg;*.jpg"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string sourcePath = openFileDialog.FileName;
                    string extension = System.IO.Path.GetExtension(sourcePath);

                    // 🎯 เก็บที่ 1. Image Stock\<รหัสคลัง>\ ชื่อรูปรันตามรหัสคลัง (STORE-PC-01, STORE-PC-02 ...)
                    //    โฟลเดอร์คลังหาย/ยังไม่มี = สร้างใหม่ / สินค้ามีรูปในคลังนี้แล้ว = ใช้เลขเดิมแทนที่รูปเก่า
                    string stockCode = ImageStockCode?.Invoke() ?? "STOCK";
                    string newFileName = CIMS.Helpers.ImagePaths.NextImageName(stockCode, SelectedProduct.ImageFileName, extension);
                    string destPath = CIMS.Helpers.ImagePaths.Resolve(newFileName);

                    SelectedProduct.ImageFileName = string.Empty;

                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    // เลขเดิมแต่นามสกุลเปลี่ยน (.jpg -> .png) -> ลบไฟล์เก่าทุกนามสกุล
                    string dir = System.IO.Path.GetDirectoryName(destPath), baseName = System.IO.Path.GetFileNameWithoutExtension(destPath);
                    foreach (var old in System.IO.Directory.GetFiles(dir, baseName + ".*"))
                    {
                        System.IO.File.Delete(old);
                        CIMS.Helpers.ImageCacheHelper.Forget(old);
                    }

                    System.IO.File.Copy(sourcePath, destPath, true);
                    CIMS.Helpers.ImageCacheHelper.Forget(destPath);   // รูปใหม่ชื่อเดิม -> ไม่ใช้รูปเก่าที่จำไว้
                    SelectedProduct.ImageFileName = newFileName;

                    DialogHelper.ShowSuccess("อัปโหลดรูปภาพสำเร็จ");
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError("ไม่สามารถอัปโหลดรูปได้\n" + ex.Message);
                }
            }
        }

        // คลังที่ใช้ตั้งชื่อ / โฟลเดอร์รูป (หน้า Inventory Registration ส่งคลังที่ติ๊กไว้มาให้)
        public Func<string> ImageStockCode { get; set; }

        private void PrepareAddProduct()
        {
            SelectedProduct = new ProductControlModel
            {
                PartCode = "",
                PartName = "",
                PartACode = "", // 🎯
                ImageFileName = string.Empty
            };
        }

        private void InitializeGrouping()
        {
            if (GroupedProducts == null)
            {
                GroupedProducts = CollectionViewSource.GetDefaultView(Products);
                if (GroupedProducts.GroupDescriptions.Count == 0)
                {
                    GroupedProducts.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
                }
                if (GroupedProducts.SortDescriptions.Count == 0)
                {
                    GroupedProducts.SortDescriptions.Add(new SortDescription("Category", ListSortDirection.Ascending));
                }
            }
            else
            {
                GroupedProducts.Refresh();
            }
        }

        private async Task SearchWithDelayAsync(string keyword)
        {
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();

            try
            {
                await Task.Delay(300, _searchCts.Token);
                LoadData(keyword);
            }
            catch (TaskCanceledException) { }
        }

        public ProductControlViewModel()
        {
            PrepareAddProduct();
            LoadData();
        }

        // กรองตารางตามคลัง (ช่อง STOCK ด้านบนตาราง) - null = ทุกคลัง
        public int? FilterStkId { get; set; }

        private int _currentOffset = 0;
        private const int PageSize = 100;

        public async void LoadData(string keyword = "", bool isLoadMore = false)
        {
            if (!isLoadMore) _currentOffset = 0;

            try
            {
                var data = await Task.Run(() => _qrService.GetInventoryForQR(keyword?.Trim(), FilterStkId));
                var paged = data.Skip(_currentOffset).Take(PageSize).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!isLoadMore) Products.Clear();

                    foreach (var item in paged)
                    {
                        Products.Add(item);
                    }

                    if (GroupedProducts == null) InitializeGrouping();
                    else GroupedProducts.Refresh();
                });

                _currentOffset += paged.Count;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("โหลดข้อมูลไม่สำเร็จ: " + ex.Message);
            }
        }

        // 🎯 ✅ แก้ไขระบบ UPDATE ใน ViewModel เพื่อดึงและแนบข้อมูล CustomerCode, ModelCode, PartNo ส่งเข้าหลังบ้านครบทุกตัว
        public async Task<bool> UpdateProductAsync(ProductControlModel model, string oldACode)
        {
            if (CurrentUser == null || string.IsNullOrEmpty(oldACode)) return false;

            try
            {
                string uid = CurrentUser.UserId;
                int psz = int.TryParse(model.PackSize, out int rPsz) ? rPsz : 0;

                // PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน -> ห้ามซ้ำเฉพาะ PRODUCT CODE + PART A เดียวกัน (ไม่นับแถวที่กำลังแก้)
                bool isDuplicate = await _qrService.CheckDuplicateCodeAsync(model.PartACode, model.PartA, model.PtId);
                if (isDuplicate)
                {
                    DialogHelper.ShowError($"ไม่สามารถบันทึกได้ เนื่องจากมีสินค้า PRODUCT CODE {model.PartACode} + PART A {model.PartA} นี้ในระบบแล้วครับ");
                    return false;
                }

                var targetRow = model.PtId > 0 ? Products.FirstOrDefault(p => p.PtId == model.PtId) : Products.FirstOrDefault(p => p.PartACode == oldACode);
                string changeLog = "";

                if (targetRow != null)
                {
                    // 🎯 ใช้ Helper ที่เราเพิ่งทำกันไว้ (แต่ต้องเอา Max/Min ออกจาก Helper นั้นด้วยนะครับ)
                    changeLog = GetChangeLogSimplified(targetRow, model);
                }

                if (string.IsNullOrEmpty(changeLog)) changeLog = "No changes detected";
                LogService.WriteLog(uid, "EDIT_PART", $" | Edited Details : {changeLog}", model.PartACode);

                // 🎯 ส่งค่าเข้า Service (สังเกตว่าตัด max, min ออกไปแล้ว)
                bool success = await _qrService.UpdateExistingPartAsync(
                    oldACode,
                    model.PartACode,
                    model.PartCode,
                    model.PartName,
                    psz,
                    model.QRCodeData,
                    model.Category,
                    uid,
                    model.ImageFileName,
                    model.CustomerCode,
                    model.Location,
                    model.Customer,
                    model.PartA,
                    model.PartNo,
                    model.Model,
                    model.PtId
                );

                if (success && targetRow != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        int index = Products.IndexOf(targetRow);
                        model.IsShow = targetRow.IsShow;
                        model.IsShowMain = targetRow.IsShowMain;
                        model.PtId = targetRow.PtId;
                        model.Stock = targetRow.Stock;
                        model.IsActive = targetRow.IsActive;

                        Products.RemoveAt(index);
                        Products.Insert(index, model);
                    });
                }
                return success;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError($"อัปเดตข้อมูลไม่สำเร็จ:\n{ex.Message}");
                return false;
            }
        }

        private string GetChangeLogSimplified(ProductControlModel oldVal, ProductControlModel newVal)
        {
            var diffs = new List<string>();

            if (oldVal.PartCode != newVal.PartCode) diffs.Add($"PDCODE: {oldVal.PartCode}->{newVal.PartCode}");
            if (oldVal.PartName != newVal.PartName) diffs.Add($"PTNAME: {oldVal.PartName}->{newVal.PartName}");
            if (oldVal.PackSize != newVal.PackSize) diffs.Add($"PZ: {oldVal.PackSize}->{newVal.PackSize}");
            if (oldVal.Category != newVal.Category) diffs.Add($"CAT: {oldVal.Category}->{newVal.Category}");
            if (oldVal.CustomerCode != newVal.CustomerCode) diffs.Add($"SUPPLIER: {oldVal.CustomerCode}->{newVal.CustomerCode}");
            if (oldVal.Location != newVal.Location) diffs.Add($"BIN: {oldVal.Location}->{newVal.Location}");
            if ((oldVal.Customer ?? "") != (newVal.Customer ?? "")) diffs.Add($"CUSTOMER: {oldVal.Customer}->{newVal.Customer}");
            if ((oldVal.PartA ?? "") != (newVal.PartA ?? "")) diffs.Add($"PART A: {oldVal.PartA}->{newVal.PartA}");
            if ((oldVal.PartNo ?? "") != (newVal.PartNo ?? "")) diffs.Add($"PART NO: {oldVal.PartNo}->{newVal.PartNo}");
            if ((oldVal.Model ?? "") != (newVal.Model ?? "")) diffs.Add($"MODEL: {oldVal.Model}->{newVal.Model}");
            if (oldVal.QRCodeData != newVal.QRCodeData) diffs.Add($"QR: {oldVal.QRCodeData}->{newVal.QRCodeData}");

            return diffs.Count > 0 ? string.Join("  |  ", diffs) : null;
        }

        // ลงทะเบียนสินค้าใหม่ (INSERT)
        public async Task<bool> RegisterNewProductAsync(ProductControlModel model)
        {
            if (CurrentUser == null)
            {
                DialogHelper.ShowError("ไม่พบข้อมูลผู้ใช้งานระบบ (Session Expired) กรุณาเข้าสู่ระบบใหม่อีกครั้ง");
                return false;
            }

            try
            {
                string uid = CurrentUser.UserId;
                int psz = int.TryParse(model.PackSize, out int rPsz) ? rPsz : 0;
                int max = int.TryParse(model.Max, out int rMax) ? rMax : 0;
                int min = int.TryParse(model.Min, out int rMin) ? rMin : 0;

                // 🎯 เช็คค่าซ้ำด้วยค่า PartACode
                // PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน
                bool existsInDb = await _qrService.CheckDuplicateCodeAsync(model.PartACode, model.PartA);
                if (existsInDb)
                {
                    DialogHelper.ShowError($"มีสินค้า PRODUCT CODE {model.PartACode} + PART A {model.PartA} นี้ในระบบแล้วครับ");
                    return false;
                }

                string fullDetail = $"SUPPLIER: {model.CustomerCode}  | CAT: {model.Category}  | PDCODE : {model.PartCode}  | BIN: {model.Location}  | PDNAME: {model.PartName}  | PZ : {psz}";
                LogService.WriteLog(uid, "REGISTER_PART", $"Registered New Part: {fullDetail}", model.PartACode);

                int newId = await _qrService.InsertNewPartAsync(
                    model.PartCode, model.PartName, psz, model.QRCodeData, max, min, model.Category, model.ImageFileName,
                    model.CustomerCode, model.Location, model.Customer, model.PartA, model.PartNo, model.Model
                );
                bool success = newId > 0;

                if (success)
                {
                    model.PtId = newId;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        model.IsShow = true;
                        model.IsShowMain = true;
                        model.Stock = "0";
                        model.IsActive = true;

                        Products.Insert(0, model);
                    });
                }
                else
                {
                    DialogHelper.ShowError("ฐานข้อมูลปฏิเสธการบันทึกข้อมูล กรุณาตรวจสอบการกรอกข้อมูลให้ครบถ้วน");
                }

                return success;
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ลงทะเบียนสินค้าใหม่ไม่สำเร็จ: " + ex.Message);
                return false;
            }
        }

        // ปรับฟังก์ชัน SaveToDb ให้ใช้ oldACode แทน oldCode
        public async Task<bool> SaveToDb(ProductControlModel model, bool isUpdate, string oldCode = "")
        {
            if (CurrentUser == null)
            {
                DialogHelper.ShowError("ไม่สามารถบันทึกได้เนื่องจาก Session ผู้ใช้งานเป็นว่าง (Null)");
                return false;
            }

            try
            {
                if (isUpdate)
                {
                    // 🎯 ส่ง oldCode เข้าไปทำงานที่ UpdateProductAsync (ซึ่งรับพารามิเตอร์ oldACode)
                    return await UpdateProductAsync(model, oldCode);
                }
                else
                {
                    // PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน
                    bool existsInList = Products.Any(p => p.PartACode == model.PartACode && (p.PartA ?? "") == (model.PartA ?? ""));
                    if (existsInList)
                    {
                        DialogHelper.ShowError($"รหัสสินค้า {model.PartACode} + PART A {model.PartA} มีอยู่ในรายการ");
                        return false;
                    }

                    return await RegisterNewProductAsync(model);
                }
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("ระบบบันทึกเกิดข้อผิดพลาด: " + ex.Message);
                return false;
            }
        }

        // 🎯 [จุดแก้ไขแก้ไขบั๊ก Show/Hide ทั้งตาราง]
        public bool ToggleStatus(ProductControlModel product)
        {
            if (CurrentUser == null || product == null) return false;
            string uid = CurrentUser.UserId;
            bool newStatus = !product.IsShow;

            // 🎯 เปลี่ยนพารามิเตอร์ส่งตัวระบุหลักไปเป็น product.PartACode 
            // เลือกคลังในช่องกรองไว้ = SHOW/HIDE ในตารางของคลังนั้น
            if (_qrService.UpdateShowStatus(product.PartACode, newStatus, uid, FilterStkId, product.PtId))
            {
                product.IsShow = newStatus;
                string statusLabel = newStatus ? "Enabled" : "Disabled";
                LogService.WriteLog(uid, "TOGGLE_STATUS", $" | Set show status to {statusLabel}{(FilterStkId.HasValue ? $" | Stock id: {FilterStkId}" : "")}  | PDNAME : {product.PartName}", product.PartACode);
                return true;
            }
            return false;
        }

        // 🎯 ลบสินค้าโดยอิงตาม PartACode
        // เหตุผลที่ลบไม่สำเร็จครั้งล่าสุด (ให้หน้าจอแสดงผู้ใช้)
        public string LastDeleteError { get; private set; }

        public async Task<bool> DeleteProductAsync(ProductControlModel product)
        {
            LastDeleteError = null;
            if (CurrentUser == null || product == null) return false;
            string uid = CurrentUser.UserId;

            // 🎯 สั่งทำงาน Delete ผ่าน PartACode
            string error = null;
            bool success = await Task.Run(() => _qrService.DeletePart(product.PartACode, uid, out error, product.PtId));
            LastDeleteError = error;

            if (success)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Products.Remove(product);
                });

                LogService.WriteLog(uid, "DELETE_PART", $"Deleted PDNAME : {product.PartName}", product.PartACode);
                return true;
            }
            return false;
        }

        // 🎯 รันกระบวนการสร้าง QR Code ด้วยการอัปเดตผ่าน PartACode
        public async Task<bool> ProcessGenerateQR(ProductControlModel product)
        {
            if (product == null || CurrentUser == null) return false;
            if (!CurrentUser.CanViewProductControl) return false;

            string uid = CurrentUser.UserId;
            string pName = product.PartName;
            string pACode = product.PartACode; // 🎯 เปลี่ยนมาจับตัวแปร PartACode แทน

            return await Task.Run(() =>
            {
                try
                {
                    bool success = _qrService.UpdateQRCodeStatus(pACode);
                    if (success)
                    {
                        LogService.WriteLog(uid, "GENERATE_QR", $"User generated QR Code for Part: {pName}", pACode);
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"QR Error: {ex.Message}");
                    return false;
                }
            });
        }
    }
}