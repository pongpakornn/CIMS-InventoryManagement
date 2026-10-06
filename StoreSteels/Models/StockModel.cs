using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CIMS.Models
{
    // 1 แถวของ CIMS.Stocks (คลัง) + ค่าตั้งค่าการแสดงผล/ช่องทางแสกนของคลังนั้น
    // Stock-CHR (IsMain) ยังใช้ยอดใน CIMS.Parts เหมือนเดิม คลังอื่นใช้ CIMS.PartStocks
    public class StockModel : INotifyPropertyChanged
    {
        public int StkId { get; set; }

        private string _code;
        public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private string _unit = "KG";
        public string Unit { get => _unit; set { _unit = value; OnPropertyChanged(); OnPropertyChanged(nameof(QtyHeader)); } }

        public bool IsMain { get; set; }

        private bool _useMaxMin = true;
        public bool UseMaxMin { get => _useMaxMin; set { _useMaxMin = value; OnPropertyChanged(); } }

        // คอลัมน์ที่แสดงในตาราง Store
        public bool ColImage { get; set; } = true;
        public bool ColCode { get; set; } = true;
        public bool ColName { get; set; } = true;
        public bool ColQty { get; set; } = true;
        public bool ColRemark { get; set; } = true;
        public bool ColCustomer { get; set; }
        public bool ColPartA { get; set; }
        public bool ColPartNo { get; set; }
        public bool ColStockBox { get; set; }
        public bool ColStockPcs { get; set; }
        public bool ColNo { get; set; }       // No. = ลำดับในแต่ละกลุ่ม (กลุ่ม A 1-5, กลุ่ม B 1-3 ...)
        public bool ColModel { get; set; }    // MODEL (รหัสโมเดล Model)

        // จัดกลุ่มตาราง: CATEGORY (ค่าเดิม) หรือ CUSTOMER
        public string GroupBy { get; set; } = "CATEGORY";
        public bool GroupByCustomer => string.Equals(GroupBy, "CUSTOMER", System.StringComparison.OrdinalIgnoreCase);
        public bool GroupBySupplier => string.Equals(GroupBy, "SUPPLIER", System.StringComparison.OrdinalIgnoreCase);
        // CATEGORY (ค่าเริ่มต้น) / CUSTOMER / SUPPLIER
        public string GroupCode => GroupByCustomer ? "CUSTOMER" : GroupBySupplier ? "SUPPLIER" : "CATEGORY";
        public string GroupLabel => GroupCode;

        // หน่วยของ MAX / MIN เลือกแยกเอง: KG / PCS / SHEET / BOX (BOX เทียบกับ STOCK (BOX) ที่เหลือเทียบกับ QTY)
        // ค่าเดิม UNIT = ใช้หน่วยเดียวกับคลัง
        public string MaxMinBasis { get; set; } = "UNIT";
        public bool MaxMinInBox => string.Equals(MaxMinBasis, "BOX", System.StringComparison.OrdinalIgnoreCase);
        public string MaxMinUnit => string.IsNullOrWhiteSpace(MaxMinBasis) || string.Equals(MaxMinBasis, "UNIT", System.StringComparison.OrdinalIgnoreCase)
                                    ? UnitText : MaxMinBasis.ToUpperInvariant();

        // ช่องทางรับเข้า (IN)
        public bool InPickList { get; set; }
        public bool InSupplier { get; set; }
        public bool InSysQr { get; set; }
        public bool InExcel { get; set; }

        // ช่องทางจ่ายออก (OUT)
        public bool OutPickList { get; set; }
        public bool OutSupplier { get; set; }
        public bool OutSysQr { get; set; }
        public bool OutExcel { get; set; }    // จ่ายออกด้วย Import Excel
        // 🔢 เก็บ / แสดงยอดเป็นทศนิยม (เช่น KG 1,234.50) - ปิด = ปัดเป็นจำนวนเต็มเหมือนเดิม
        public bool AllowDecimal { get; set; }

        public int SortNo { get; set; }

        // รูปแบบบาร์โค้ด Supplier ที่คลังนี้รับได้ (CIMS.StockBarcodeFormats)
        public List<int> FormatIds { get; set; } = new List<int>();

        // จำนวนรายการสินค้าในคลัง (ไม่ใช่ยอดจำนวนเหล็ก) - ใช้แสดงบนการ์ด
        private int _itemCount;
        public int ItemCount { get => _itemCount; set { _itemCount = value; OnPropertyChanged(); } }

        public bool CanScanIn => InPickList || InSupplier || InSysQr;
        public bool CanScanOut => OutPickList || OutSupplier || OutSysQr;

        // ทุกคลัง (รวมคลังหลัก) ใช้รหัสคลังเป็น SystemID เลย (เช่น STOCK-MAT, STOCK-PANTA) ดูในตารางสิทธิ์แล้วรู้ทันทีว่าคลังไหน
        public string PermSysId => Code;

        // รหัสคลังห้ามชนกับ SystemID ของระบบ (เพราะใช้เป็นชื่อสิทธิ์)
        public static readonly string[] ReservedCodes =
            { "STK", "STOCK_MGR", "SCAN_IN", "SCAN_OUT", "STORE", "SCANNER", "DASHBOARD", "PDCONTROL", "PACKINGCARD", "PR", "MAXMINCALC", "ACTIVITYLOG", "USERMGMT",
              "STOREMAXMIN", "MULTISCANNER", "PRODUCTCONTROL", "USERMANAGEMENT", "FORECASTORDER" };

        private string UnitText => string.IsNullOrWhiteSpace(Unit) ? "KG" : Unit;
        public string UnitDisplay => UnitText;
        public string QtyHeader => $"QTY ({UnitText}.)";
        public string MaxHeader => MaxMinInBox ? "MAX (BOX)" : $"MAX ({MaxMinUnit}.)";
        public string MinHeader => MaxMinInBox ? "MIN (BOX)" : $"MIN ({MaxMinUnit}.)";

        public string DisplayName => $"{Code} - {Name}";

        public StockModel Clone()
        {
            var copy = (StockModel)MemberwiseClone();
            copy.FormatIds = new List<int>(FormatIds);
            copy.PropertyChanged = null;
            return copy;
        }

        public override string ToString() => DisplayName;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // 1 แถวที่อ่านได้จากไฟล์ Excel สำหรับ Import เข้าคลัง
    public class StockImportRow
    {
        public int RowNumber { get; set; }
        public string Code { get; set; }
        public decimal Qty { get; set; }

        // เติมหลังตรวจสอบกับ CIMS.Parts
        public int PartId { get; set; }
        public string PartCode { get; set; }
        public string Error { get; set; }
        public bool IsValid => string.IsNullOrEmpty(Error) && PartId > 0;
    }
}
