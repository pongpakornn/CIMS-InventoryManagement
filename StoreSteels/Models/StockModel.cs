using System;
using System.Linq;
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

        // ⏳ ชั่วคราว: คลังนี้แสดงข้อมูลสดจากโปรแกรมเดิม (เช่น 'StorePC') - ดูอย่างเดียว ห้ามแก้ / สแกน / Import ใน CIMS
        public string LiveSource { get; set; }
        public bool IsLiveView => !string.IsNullOrWhiteSpace(LiveSource);
        public const string LiveViewMessage = "คลังนี้แสดงข้อมูลสดจากโปรแกรมเดิม (StorePC) ชั่วคราว - ดูได้อย่างเดียว\nการแก้ไข / สแกน / Import ให้ทำในโปรแกรม StorePC";

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

        // STOCK (COIL) - คลัง KG: สแกนเข้า 1 ครั้ง = +1 Coil / ออก = -1 / เศษ = ไม่นับ (ยอด KG ตามที่สแกน)
        //   ใช้ช่องเดียวกับ STOCK (BOX) แต่ไม่คำนวณจาก Pack Size - คลังที่นับ Coil แสดงคอลัมน์นี้เสมอ
        public bool CountCoil { get; set; }
        // กด PD CODE แล้วแสดงแถว Coil ย่อย: null = ตาม STOCK (COIL) / ตั้งเองได้ที่หน้าสร้าง / แก้คลัง
        public bool? ShowCoilRowsSetting { get; set; }
        // MAX / MIN ทศนิยม: null = ตาม DECIMAL QTY / ตั้งเองได้ที่หน้าสร้าง / แก้คลัง
        public bool? MaxMinDecimalSetting { get; set; }
        public bool MaxMinDecimal => MaxMinDecimalSetting ?? AllowDecimal;
        public bool ShowCoilRows => CountCoil && (ShowCoilRowsSetting ?? true);
        // 🃏 การ์ดสินค้า (แถบเลื่อนเหนือตาราง): null = เดิม (คลังที่แสดงรูป = การ์ดมีรูป / ไม่แสดงรูป = ไม่มีการ์ด)
        //   "IMAGE" = การ์ดมีรูป · "TEXT" = การ์ดไม่มีรูป (ตัวเลขใหญ่ อ่านไกล) · "OFF" = ไม่แสดงการ์ด
        public string CardStyleSetting { get; set; }
        public string CardStyle
        {
            get
            {
                string v = (CardStyleSetting ?? "").Trim().ToUpperInvariant();
                if (v == "OFF" || v == "TEXT") return v;
                if (v == "IMAGE") return ColImage ? "IMAGE" : "TEXT";   // ปิดคอลัมน์รูปไปแล้ว -> การ์ดไม่มีรูปแทน
                return ColImage ? "IMAGE" : "OFF";
            }
        }
        public bool ShowsCards => CardStyle != "OFF";
        public bool CardHasImage => CardStyle == "IMAGE";
        // คอลัมน์ของแถว Coil ย่อย: COILNO, MOTHER, WEIGHT, COIL, TON, RECEIVED (null = แสดงทั้งหมด)
        public static readonly string[] CoilRowKeys = { "COILNO", "MOTHER", "WEIGHT", "COIL", "TON", "RECEIVED" };
        public string CoilRowColumns { get; set; }
        // ลำดับคอลัมน์ของแถว Coil ย่อยตามที่ติ๊กเลือก (ติ๊กก่อน = แสดงก่อน แบบเดียวกับคอลัมน์หลัก)
        //   null = ค่าเริ่มต้น (ทุกคอลัมน์ วางตรงกับคอลัมน์ของตาราง) / "-" = ไม่แสดง
        //   ลำดับตามค่าเริ่มต้น (แค่ซ่อนบางคอลัมน์ เช่น STOCK-PANTA ไม่แสดง COIL NO) = ยังวางตรงกับคอลัมน์ของตารางเหมือนเดิม
        //   ลำดับต่างจากค่าเริ่มต้น = เรียงซ้ายไปขวาตามที่ติ๊ก
        public bool CoilRowCustomOrder
        {
            get
            {
                var idx = CoilRowOrder().Select(k => Array.IndexOf(CoilRowKeys, k)).ToList();
                return idx.Zip(idx.Skip(1), (a, b) => b < a).Any(x => x);
            }
        }
        public List<string> CoilRowOrder() =>
            string.IsNullOrWhiteSpace(CoilRowColumns) ? CoilRowKeys.ToList()
            : CoilRowColumns.Split(',').Select(k => k.Trim().ToUpperInvariant()).Where(k => CoilRowKeys.Contains(k)).Distinct().ToList();

        public bool CoilRowShows(string key) =>
            string.IsNullOrWhiteSpace(CoilRowColumns) || CoilRowColumns.Split(',').Any(k => string.Equals(k.Trim(), key, StringComparison.OrdinalIgnoreCase));
        public string BoxHeader => CountCoil ? "STOCK (COIL)" : "STOCK (BOX)";
        public bool ShowBoxColumn => ColStockBox || CountCoil;

        // ลำดับคอลัมน์ในตาราง Store ตามลำดับที่ติ๊กเลือก (คั่นด้วย ,) เช่น "CODE,NAME,MAXMIN,QTY,COIL"
        //   คีย์: NO IMAGE CUSTOMER CODE PARTA PARTNO MODEL NAME MAXMIN QTY BOX COIL PCS REMARK - ว่าง = ลำดับเดิม
        public string ColumnOrder { get; set; }
        public static readonly string[] DefaultColumnOrder = { "NO", "IMAGE", "CUSTOMER", "CODE", "PARTA", "PARTNO", "MODEL", "NAME", "MAXMIN", "QTY", "BOX", "COIL", "PCS", "REMARK" };
        public List<string> ColumnOrderList()
        {
            var list = new List<string>();
            foreach (string k in (ColumnOrder ?? "").Split(','))
            {
                string key = k.Trim().ToUpperInvariant();
                if (key.Length > 0 && System.Array.IndexOf(DefaultColumnOrder, key) >= 0 && !list.Contains(key)) list.Add(key);
            }
            foreach (string k in DefaultColumnOrder) if (!list.Contains(k)) list.Add(k);   // คอลัมน์ที่ไม่ได้ระบุ = ต่อท้ายตามลำดับเดิม
            return list;
        }
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
        // MAX / MIN เป็น COIL (แบบเดียวกับ BOX ของ StorePC) - เทียบกับ STOCK (COIL)
        public bool MaxMinInCoil => string.Equals(MaxMinBasis, "COIL", System.StringComparison.OrdinalIgnoreCase);
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
        public string QtyHeader => $"STOCK ({UnitText}.)";   // ยอดคงคลังตามหน่วย เช่น STOCK (KG.)
        public string MaxHeader => MaxMinInBox ? "MAX (BOX)" : MaxMinInCoil ? "MAX (COIL)" : $"MAX ({MaxMinUnit}.)";
        public string MinHeader => MaxMinInBox ? "MIN (BOX)" : MaxMinInCoil ? "MIN (COIL)" : $"MIN ({MaxMinUnit}.)";

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
