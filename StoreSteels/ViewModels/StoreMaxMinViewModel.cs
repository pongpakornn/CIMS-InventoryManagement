// Store ( Max - Min )
using Microsoft.Data.SqlClient;
using CIMS.Converters;
using CIMS.Core;
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
using System.Windows.Data;

namespace CIMS.ViewModels
{
    public class StoreMaxMinViewModel : INotifyPropertyChanged
    {
        #region === [ INotifyPropertyChanged ] ===

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public BulkObservableCollection<StoreProductModel> Products { get; set; }
        public ObservableCollection<string> Categories { get; set; }

        private readonly StoreProductService _service = new StoreProductService();

        public UserSession CurrentUser { get; set; }

        // คลังที่กำลังแสดง (Stock-CHR = พฤติกรรมเดิมทุกอย่าง, คลังอื่นอ่าน/แก้ไขที่ CIMS.PartStocks)
        public StockModel Stock { get; }

        public ICollectionView GroupedProducts { get; set; }

        private int _currentOffset = 0;
        private bool _isLoading = false;
        private bool _isRealTimeUpdating = false;
        private bool _hasMoreData = true;

        private string _selectedFilterType = "";
        public string SelectedFilterType
        {
            get => _selectedFilterType;
            set { _selectedFilterType = value; OnPropertyChanged(); }
        }

        // ตัวกรองกลุ่มด้านบน: CATEGORY (ค่าเดิม) หรือ CUSTOMER ตามที่คลังตั้งค่าจัดกลุ่มไว้
        public string GroupLabel => Stock != null ? Stock.GroupCode : "CATEGORY";
        public string AllLabel => Stock != null && Stock.GroupByCustomer ? "ALL CUSTOMERS" : Stock != null && Stock.GroupBySupplier ? "ALL SUPPLIERS" : "ALL CATEGORIES";
        private bool IsAll(string value) => string.IsNullOrEmpty(value) || value == AllLabel || value == "ALL CATEGORIES";

        // เปิด SHOW PRODUCTION / การ์ดสินค้า -> โหลดทุกรายการ (ต้องวนครบทุกแถว ไม่ใช่แค่หน้าที่โหลดมา)
        public bool LoadAllMode { get; set; }

        private string _selectedCategory = "ALL CATEGORIES";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set { _selectedCategory = value; OnPropertyChanged(); }
        }

        #endregion

        public StoreMaxMinViewModel() : this(null) { }

        public StoreMaxMinViewModel(StockModel stock)
        {
            Stock = stock;
            Products = new BulkObservableCollection<StoreProductModel>();
            // ตัวเลือกกลุ่มโหลดเบื้องหลัง - เปิดหน้าได้ทันทีไม่ต้องรอฐานข้อมูล
            Categories = new ObservableCollection<string> { AllLabel };
            _selectedCategory = AllLabel;
            LoadCategoriesAsync();

            // จัดกลุ่มตาม GroupKey (+ รอบวน) และไม่เรียงใหม่ในตาราง - ลำดับมาจาก SQL (GroupKey, PartCode) แล้ว
            // เพื่อให้ย้ายแถวไปต่อท้ายตอนวนแบบป้ายโฆษณาได้
            GroupedProducts = CollectionViewSource.GetDefaultView(Products);
            GroupedProducts.GroupDescriptions.Add(new PropertyGroupDescription(nameof(StoreProductModel.LoopGroup)));
        }

        private async void LoadCategoriesAsync()
        {
            try
            {
                var stock = Stock;
                var cats = await Task.Run(() => _service.GetCategories(stock));
                foreach (var c in cats.Skip(1))
                    if (!Categories.Contains(c)) Categories.Add(c);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Store categories: {ex.Message}"); }
        }

        #region === [ Function : ProcessUpdate ] ===

        public async Task<bool> ProcessUpdate(StoreProductModel product, StoreProductModel originalProduct)
        {
            if (product == null || CurrentUser == null) return false;

            if (product.Remark != originalProduct.Remark)
            {
                LogService.WriteLog(CurrentUser.UserId, "UPDATE_REMARK",
                    $"| Remark: {originalProduct.Remark ?? ""} -> {product.Remark ?? ""}", product.PartCode);
            }

            if (CanEditMaster)
            {
                var changes = new List<string>();
                if (product.Max != originalProduct.Max) changes.Add($"MAX: {originalProduct.Max}->{product.Max}");
                if (product.Min != originalProduct.Min) changes.Add($"MIN: {originalProduct.Min}->{product.Min}");
                if (product.Qty != originalProduct.Qty) changes.Add($"QTY: {originalProduct.Qty}->{product.Qty}");

                return await Task.Run(() =>
                {
                    // คลังที่เปิด DECIMAL QTY เก็บทศนิยม (สูงสุด 3 ตำแหน่ง) / คลังอื่นปัดเป็นจำนวนเต็มเหมือนเดิม
                    bool dec = Stock?.AllowDecimal == true;
                    bool mmDec = Stock?.MaxMinDecimal == true;   // MAX / MIN ทศนิยมตามที่คลังตั้ง (MAX / MIN DECIMAL)
                    // ช่องว่าง / "-" = 0 / พิมพ์ผิด (ไม่ใช่ตัวเลข) -> แจ้งเตือน ไม่บันทึก (เดิมบันทึกเป็น 0 ทับค่าเดิมเงียบๆ)
                    decimal Field(string text, string name)
                    {
                        string t = (text ?? "").Trim();
                        if (t.Length == 0 || t == "-") return 0;
                        if (!Qty.TryParse(t, out decimal v)) throw new FormatException($"{name} \"{t}\" ไม่ใช่ตัวเลข");
                        return Qty.Round(Math.Max(0, v), mmDec);
                    }
                    void CheckNumber(string text, string original, string name)
                    {
                        string t = (text ?? "").Trim();
                        if (t != (original ?? "").Trim() && t.Length > 0 && t != "-" && !Qty.TryParse(t, out _))
                            throw new FormatException($"{name} \"{t}\" ไม่ใช่ตัวเลข");
                    }
                    decimal maxVal = Field(product.Max, "MAX");
                    decimal minVal = Field(product.Min, "MIN");
                    CheckNumber(product.Qty, originalProduct.Qty, "QTY");
                    CheckNumber(product.StockPcs, originalProduct.StockPcs, "STOCK (PCS)");
                    CheckNumber(product.StockBox, originalProduct.StockBox, Stock?.BoxHeader ?? "STOCK (BOX)");
                    decimal? qtyVal = Qty.TryParse(product.Qty, out decimal q) ? q : (decimal?)null;

                    // STOCK (BOX) / STOCK (PCS): แก้ช่องไหน อีกช่องคำนวณตาม Pack Size ให้ (BOX x Pack Size = PCS)
                    int? boxVal = null;
                    if (product.StockPcs != originalProduct.StockPcs && Qty.TryParse(product.StockPcs, out decimal pcs))
                    { qtyVal = pcs; changes.Add($"STOCK(PCS): {originalProduct.StockPcs}->{product.StockPcs}"); }
                    else if (product.StockBox != originalProduct.StockBox && Qty.TryParse(product.StockBox, out decimal box))
                    { boxVal = (int)Math.Max(0, Math.Round(box)); changes.Add($"{(Stock?.CountCoil == true ? "QTY(COIL)" : "STOCK(BOX)")}: {originalProduct.StockBox}->{product.StockBox}"); }
                    if (qtyVal.HasValue) qtyVal = Qty.Round(Math.Max(0, qtyVal.Value), dec);

                    if (changes.Count > 0)
                        LogService.WriteLog(CurrentUser.UserId, "UPDATE_PRODUCT_MASTER", $"| {StockTag}Changes: {string.Join(", ", changes)}", product.PartCode);
                    return _service.UpdateProductMaster(Stock, product.PartCode, product.Remark, maxVal, minVal, qtyVal, boxVal, product.PartId);
                });
            }

            return _service.UpdateRemark(Stock, product.PartCode, product.Remark, product.PartId);
        }

        // แก้ MAX / MIN / QTY / STOCK(BOX) / STOCK(PCS) = สิทธิ์ EDIT ของคลังนั้น (SystemID = รหัสคลัง รวมคลังหลัก)
        // คลังที่แสดงสดจาก StorePC (ชั่วคราว) = ดูอย่างเดียว
        public bool CanEditMaster => Stock != null && !Stock.IsLiveView && CurrentUser != null && CurrentUser.CanEditStock(Stock);

        // ต่อท้าย Log ให้รู้ว่าแก้ไขคลังไหน (คลังหลักไม่ต่อ เพื่อให้ Log เหมือนเดิม)
        private string StockTag => (Stock == null || Stock.IsMain) ? "" : $"Stock: {Stock.Code} | ";

        #endregion

        #region === [ Function : Export ] ===

        // ข้อมูลทั้งหมดตามเงื่อนไขที่ตารางแสดงอยู่ (ค้นหา / CATEGORY / MAX-MIN) - ไม่จำกัดแค่แถวที่โหลดมาแล้ว
        public List<StoreProductModel> GetAllForExport(string searchKeyword)
        {
            string catFilter = IsAll(SelectedCategory) ? "" : SelectedCategory;
            return _service.GetProducts(Stock, searchKeyword ?? "", catFilter, SelectedFilterType, 0, 1000000)
                           .OrderBy(p => p.GroupKey).ThenBy(p => p.PartCode).ToList();
        }

        #endregion

        #region === [ Function : LoadData ] ===

        // ⚡ ดึงข้อมูลเบื้องหลัง (หน้าจอไม่ค้างระหว่างรอฐานข้อมูล) - กดกรอง / ค้นหาซ้อนกัน ใช้ผลของคำสั่งล่าสุดเท่านั้น
        private int _loadVersion;

        public async void LoadData(string searchKeyword = "", bool isLoadMore = false)
        {
            if (isLoadMore && (_isLoading || !_hasMoreData))
                return;

            int ver = isLoadMore ? _loadVersion : ++_loadVersion;
            int offset = isLoadMore ? _currentOffset : 0;
            int pageSize = isLoadMore ? 30 : (LoadAllMode ? 1000000 : 50);
            string catFilter = IsAll(SelectedCategory) ? "" : SelectedCategory;
            string filterType = SelectedFilterType;
            var stock = Stock;

            try
            {
                _isLoading = true;
                var newData = await Task.Run(() => _service.GetProducts(stock, searchKeyword, catFilter, filterType, offset, pageSize));
                if (ver != _loadVersion) return;   // มีคำสั่งใหม่กว่าแล้ว ทิ้งผลนี้

                // ใส่ทั้งชุดครั้งเดียว (แจ้งตาราง 1 ครั้ง) - ไม่ใส่ทีละแถวที่ทำให้ตารางจัดกลุ่มใหม่ทุกแถว
                if (!isLoadMore)
                {
                    _currentOffset = 0;
                    _hasMoreData = true;
                    _changeToken = null;   // โหลดใหม่ทั้งชุดแล้ว -> รอบเรียลไทม์ถัดไปเริ่มนับใหม่
                    Products.ReplaceAll(newData ?? new List<StoreProductModel>());
                }
                else if (newData != null && newData.Count > 0)
                    Products.AddRange(newData);

                if (newData == null || newData.Count == 0)
                {
                    _hasMoreData = false;
                    return;
                }

                _currentOffset += newData.Count;
                RenumberGroups();

                if (newData.Count < pageSize)
                    _hasMoreData = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Store LoadData: {ex}");
                if (ver == _loadVersion) DialogHelper.ShowError("โหลดข้อมูลคลังไม่สำเร็จ\n" + ex.Message);
            }
            finally
            {
                if (ver == _loadVersion) _isLoading = false;
            }
        }

        #endregion

        // ⚡ เรียลไทม์ (โครงตาราง): เครื่องอื่นเพิ่ม / ลบสินค้า / แก้ชื่อ รหัส กลุ่ม รูป BIN -> โหลดใหม่เท่าจำนวนแถวที่แสดงอยู่
        //    คงคำค้น / CATEGORY / ตัวกรอง MAX-MIN / แถว Coil ที่เปิดไว้ - กำลังแก้ REMARK หรือกำลังโหลดอยู่ = ข้ามรอบนี้
        //    คืน true = โหลดใหม่แล้ว
        public async Task<bool> LiveReloadAsync(string searchKeyword)
        {
            if (_isLoading || Products.Any(p => p.IsRemarkEditing)) return false;
            int ver = ++_loadVersion;
            int take = LoadAllMode ? 1000000 : Math.Max(50, Products.Count);
            string catFilter = IsAll(SelectedCategory) ? "" : SelectedCategory;
            string filterType = SelectedFilterType;
            var stock = Stock;
            var open = new HashSet<int>(Products.Where(p => p.IsCoilOpen).Select(p => p.PartId));
            _isLoading = true;
            try
            {
                var data = await Task.Run(() => _service.GetProducts(stock, searchKeyword, catFilter, filterType, 0, take)) ?? new List<StoreProductModel>();
                if (ver != _loadVersion || Products.Any(p => p.IsRemarkEditing)) return false;
                foreach (var d in data) if (open.Contains(d.PartId)) d.IsCoilOpen = true;
                Products.ReplaceAll(data);
                _currentOffset = data.Count;
                _hasMoreData = !LoadAllMode && data.Count >= take;
                _changeToken = null;
                RenumberGroups();
                return true;
            }
            finally { if (ver == _loadVersion) _isLoading = false; }
        }

        // No. ในแต่ละกลุ่ม: นับ 1.. ใหม่ทุกกลุ่ม ตามลำดับในตาราง (ข้อมูลเรียงตาม GroupKey, PartCode มาจาก SQL)
        private void RenumberGroups()
        {
            var counters = new Dictionary<string, int>();
            foreach (var p in Products)
            {
                string key = p.GroupKey ?? "";
                counters.TryGetValue(key, out int n);
                counters[key] = ++n;
                p.GroupNo = n;
            }
        }

        // 🔁 วนแถวแรก (ที่เลื่อนพ้นด้านบนแล้ว) ไปต่อท้ายตาราง เป็นรอบถัดไปของกลุ่มเดิม
        public StoreProductModel RotateFirst()
        {
            if (Products.Count < 2) return null;
            var item = Products[0];
            Products.RemoveAt(0);
            item.LoopGroup = item.LoopGroup.Next();
            Products.Add(item);
            return item;
        }

        #region === [ Function : Real-Time Updates ] ===

        // ค่าล่าสุดที่เห็น (จำนวนแถว + CHECKSUM ของคลัง) - เท่าเดิม = ไม่มีอะไรเปลี่ยน ไม่ต้องดึงข้อมูล
        private string _changeToken;
        private DateTime _lastFullCheck = DateTime.MinValue;

        public async Task UpdateStockFromDbAsync()
        {
            if (_isRealTimeUpdating || Products == null || Products.Count == 0)
                return;

            try
            {
                _isRealTimeUpdating = true;
                var stock = Stock;

                List<StoreProductModel> freshData;
                string token = await Task.Run(() => _service.GetChangeToken(stock));
                if (token != null)
                {
                    // ⚡ ถามแค่ "เปลี่ยนไหม" (ไม่กี่ ms) - ไม่เปลี่ยนก็จบ / เปลี่ยน (หรือครบ 1 นาที กันพลาด) ค่อยดึงตัวเลขทั้งคลังครั้งเดียว
                    bool changed = token != _changeToken;
                    if (!changed && DateTime.Now - _lastFullCheck < TimeSpan.FromMinutes(1)) return;
                    freshData = await Task.Run(() => _service.GetStockNumbers(stock));
                    _changeToken = token;
                    _lastFullCheck = DateTime.Now;
                }
                else
                {
                    // ระบบเก่า (ไม่มี vw_StockMonitoring) -> วิธีเดิม
                    var currentPartCodes = Products.Where(x => !string.IsNullOrWhiteSpace(x.PartCode)).Select(x => x.PartCode).Distinct().ToList();
                    if (currentPartCodes.Count == 0) return;
                    freshData = await Task.Run(() => _service.GetMinimalStockUpdates(stock, currentPartCodes));
                }

                if (freshData == null || freshData.Count == 0)
                    return;

                await App.Current.Dispatcher.InvokeAsync(() =>
                {
                    // ToLookup รองรับ key ซ้ำได้ - วน loop อัปเดตทุก row ที่ PartCode ตรงกันพร้อมกันทุกครั้ง
                    // จับคู่ด้วย PartID (PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน) - ไม่มี PartID ใช้ PartCode แบบเดิม
                    var lookup = Products
                        .Where(x => !string.IsNullOrWhiteSpace(x.PartCode))
                        .ToLookup(x => x.PartId > 0 ? "#" + x.PartId : x.PartCode);

                    foreach (var newItem in freshData)
                    {
                        foreach (var existingItem in lookup[newItem.PartId > 0 ? "#" + newItem.PartId : newItem.PartCode])
                        {
                            if (existingItem.Qty != newItem.Qty)
                                existingItem.Qty = newItem.Qty;

                            if (existingItem.Max != newItem.Max)
                                existingItem.Max = newItem.Max;

                            if (existingItem.Min != newItem.Min)
                                existingItem.Min = newItem.Min;

                            if (!existingItem.IsRemarkEditing && existingItem.Remark != newItem.Remark)
                                existingItem.Remark = newItem.Remark;

                            if (existingItem.StockStatus != newItem.StockStatus)
                                existingItem.StockStatus = newItem.StockStatus;

                            if (newItem.StockBox != null && existingItem.StockBox != newItem.StockBox)
                                existingItem.StockBox = newItem.StockBox;

                            if (newItem.StockPcs != null && existingItem.StockPcs != newItem.StockPcs)
                                existingItem.StockPcs = newItem.StockPcs;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in UpdateStockFromDbAsync: {ex.Message}");
            }
            finally
            {
                _isRealTimeUpdating = false;
            }
        }

        #endregion
    }
}
