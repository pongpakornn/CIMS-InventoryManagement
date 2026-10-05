// Store ( Max - Min )
using CIMS.Helpers;
using System;
using System.IO;
using CIMS.Converters;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;

namespace CIMS.Models
{
    public class StoreProductModel : INotifyPropertyChanged
    {
        // 1. ID
        private string _id;
        public string ID { get => _id; set { _id = value; OnPropertyChanged(); } }

        // PartID (คลังอื่นที่ไม่ใช่คลังหลัก)
        public int PartId { get; set; }

        // คอลัมน์เสริมที่คลังเลือกแสดงได้ (CUSTOMER / PART A / PART NO / STOCK (BOX) / STOCK (PCS))
        public string Customer { get; set; }
        public string PartA { get; set; }
        public string PartNo { get; set; }
        public string Model { get; set; }

        // No. = ลำดับในกลุ่มของตาราง (กลุ่ม A = 1..5, กลุ่ม B = 1..3) - ViewModel ตั้งให้หลังโหลดข้อมูล
        private int _groupNo;
        public int GroupNo { get => _groupNo; set { _groupNo = value; OnPropertyChanged(); } }

        private string _stockBox;
        public string StockBox { get => _stockBox; set { _stockBox = value; OnPropertyChanged(); } }

        private string _stockPcs;
        public string StockPcs { get => _stockPcs; set { _stockPcs = value; OnPropertyChanged(); } }

        // หัวกลุ่มของตาราง: Category (ค่าเดิม) หรือ Customer ตามการตั้งค่าคลัง
        private string _groupKey;
        public string GroupKey { get => _groupKey ?? Category; set { _groupKey = value; _loopGroup = null; OnPropertyChanged(); } }

        // กลุ่มที่ตารางใช้จัดกลุ่มจริง = GroupKey + รอบของการวนแบบป้ายโฆษณา (SHOW PRODUCTION)
        // แถวที่เลื่อนพ้นด้านบนจะถูกย้ายไปต่อท้ายเป็นรอบถัดไป -> หัวกลุ่มเดิมโผล่ใหม่ที่ด้านล่างเหมือนรายการวนต่อกัน
        private LoopGroupKey _loopGroup;
        public LoopGroupKey LoopGroup { get => _loopGroup ??= new LoopGroupKey(GroupKey, 0); set { _loopGroup = value; OnPropertyChanged(); } }

        // ตัวระบุหลักของสินค้า (เดิมคือ PartACode/PartACode - schema ใหม่ตัดออก เหลือ PartCode ตัวเดียว)
        private string _partCode;
        public string PartCode { get => _partCode; set { _partCode = value; OnPropertyChanged(); } }

        private string _partName;
        public string PartName { get => _partName; set { _partName = value; OnPropertyChanged(); } }

        private string _packSize;
        public string PackSize { get => _packSize; set { _packSize = value; OnPropertyChanged(); } }

        // แทนที่ CustomerCode เดิม - ใช้จัดกลุ่มตาราง Store (Max-Min) แทน
        private string _category;
        public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }

        private string _supplier;
        public string Supplier { get => _supplier; set { _supplier = value; OnPropertyChanged(); } }

        private string _bin;
        public string Bin { get => _bin; set { _bin = value; OnPropertyChanged(); } }

        #region === [ Max / Min ] ===

        private string _max;
        public string Max
        {
            get => (_max == "0" || string.IsNullOrWhiteSpace(_max)) ? "-" : _max;
            set
            {
                string val = (value == "-" || string.IsNullOrWhiteSpace(value)) ? "0" : value;
                if (_max == val) return;
                _max = val;
                OnPropertyChanged();
            }
        }

        private string _min;
        public string Min
        {
            get => (_min == "0" || string.IsNullOrWhiteSpace(_min)) ? "-" : _min;
            set
            {
                string val = (value == "-" || string.IsNullOrWhiteSpace(value)) ? "0" : value;
                if (_min == val) return;
                _min = val;
                OnPropertyChanged();
            }
        }

        #endregion

        // ยอดคงคลัง (StockQuantity) - schema ใหม่เหลือค่าเดียว ไม่มี QTY_STK/PackSize auto-calc pcs↔box
        // อีกต่อไป เพราะค่านี้แทนน้ำหนัก (กก.) ไม่ใช่จำนวนกล่อง/ชิ้น
        private string _qty;
        public string Qty { get => _qty; set { _qty = value; OnPropertyChanged(); } }

        private string _remark;
        public string Remark { get => _remark; set { _remark = value; OnPropertyChanged(); } }

        private bool _isRemarkEditing;
        public bool IsRemarkEditing
        {
            get => _isRemarkEditing;
            set { _isRemarkEditing = value; OnPropertyChanged(); }
        }

        public int Priority { get; set; }
        public bool IsActive { get; set; }
        public bool IsShow { get; set; }

        private string _stockStatus;
        public string StockStatus
        {
            get => _stockStatus;
            set
            {
                if (_stockStatus == value) return;
                _stockStatus = value;
                OnPropertyChanged();
            }
        }

        #region === [ Image ] ===

        private string _imageFileName;
        public string ImageFileName
        {
            get => _imageFileName;
            set
            {
                _imageFileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FullImagePath));
                OnPropertyChanged(nameof(ProductImage));
            }
        }

        public string FullImagePath
        {
            get
            {
                // 1. Image Stock\<คลัง>\<คลัง>-01.png (ใหม่) หรือรูปเก่าในโฟลเดอร์ Image เดิม
                return ImagePaths.Resolve(ImageFileName);
            }
        }

        public BitmapImage ProductImage
        {
            // โหลดเบื้องหลัง (ตารางไม่รอ Network) เสร็จแล้ววาดรูปให้เอง
            get { return ImageCacheHelper.GetOrLoad(FullImagePath, 200, () => OnPropertyChanged(nameof(ProductImage))); }
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // คีย์กลุ่มของตาราง: ชื่อกลุ่ม (CATEGORY / CUSTOMER) + รอบที่วน - แสดงผลเป็นชื่อกลุ่มอย่างเดียว
    public sealed class LoopGroupKey
    {
        public string Name { get; }
        public int Cycle { get; }

        public LoopGroupKey(string name, int cycle)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "-" : name;
            Cycle = cycle;
        }

        public LoopGroupKey Next() => new LoopGroupKey(Name, Cycle + 1);

        public override bool Equals(object obj) => obj is LoopGroupKey k && k.Cycle == Cycle && k.Name == Name;
        public override int GetHashCode() => HashCode.Combine(Name, Cycle);
        public override string ToString() => Name;
    }
}
