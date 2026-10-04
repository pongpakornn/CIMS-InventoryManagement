using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CIMS.Models
{
    // 1 แถวในตาราง Max-Min Calculator: สินค้าที่แสดงในหน้า Store (Max-Min) ของคลัง + MAX / MIN (DAYS) ที่ตั้งไว้
    public class MaxMinCalcRow : INotifyPropertyChanged
    {
        public int PtId { get; set; }
        public string Customer { get; set; }
        public string PartCode { get; set; }
        public string PartA { get; set; }
        public string PartNoRaw { get; set; }
        public string PartName { get; set; }
        public int PackSize { get; set; }
        public bool HasOwnDays { get; set; }     // มีค่า MAX / MIN (DAYS) ของตัวเอง (ไม่ใช่ค่าเริ่มต้นของสูตร)

        // PART NO ในตาราง: Part No ถ้ามี ไม่งั้นใช้ PD CODE
        public string PartNo => string.IsNullOrWhiteSpace(PartNoRaw) ? PartCode : PartNoRaw;
        public string GroupKey => string.IsNullOrWhiteSpace(Customer) ? "-" : Customer;

        private int _no;
        public int No { get => _no; set { _no = value; OnPropertyChanged(); } }

        private int _dayMax;
        public int DayMax { get => _dayMax; set { _dayMax = value; OnPropertyChanged(); } }

        private int _dayMin;
        public int DayMin { get => _dayMin; set { _dayMin = value; OnPropertyChanged(); } }

        // ค่า MAX / MIN ปัจจุบันในหน้า Store (Max-Min) - 0 แสดงเป็น "-"
        private int _qtyMax;
        public int QtyMax { get => _qtyMax; set { _qtyMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(QtyMaxText)); } }

        private int _qtyMin;
        public int QtyMin { get => _qtyMin; set { _qtyMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(QtyMinText)); } }

        public string QtyMaxText => QtyMax > 0 ? QtyMax.ToString("N0") : "-";
        public string QtyMinText => QtyMin > 0 ? QtyMin.ToString("N0") : "-";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // สูตรคำนวณ 1 รายการ (MST_CALC_FORMULA) - เพิ่มได้หลายสูตรเหมือน Barcode Formats แล้วแต่ละคลังเลือกใช้
    public class MaxMinFormula
    {
        public int FormulaId { get; set; }
        public string Name { get; set; } = "";
        public bool IsDefault { get; set; }
        public string QtySource { get; set; } = "ORDER";     // ORDER / FORECAST
        public string RoundMode { get; set; } = "UP";        // UP / NEAREST / DOWN
        public int DefDayMax { get; set; } = 3;
        public int DefDayMin { get; set; } = 1;
        public int DefWorkdays { get; set; } = 21;

        public string DisplayName => IsDefault ? $"{Name}  (DEFAULT)" : Name;
        public override string ToString() => DisplayName;

        public string Describe(bool inBox)
        {
            string qty = QtySource == "FORECAST" ? "Forecast" : "Order";
            string div = inBox ? " / PackSize" : "";
            string r = RoundMode == "DOWN" ? "RoundDown" : RoundMode == "NEAREST" ? "Round" : "RoundUp";
            return $"MAX = {r}({qty} / Workdays{div}) x MAX DAYS   •   MIN = {r}({qty} / Workdays{div}) x MIN DAYS";
        }

        // ตัวอย่างสดในหน้าต่างสูตร (ไม่ได้บันทึก / ไม่ได้ใช้คำนวณจริง): ปัดยอดต่อวันก่อน แล้วคูณจำนวนวัน
        public (decimal perDay, decimal perUnit, decimal rounded, decimal max, decimal min) Example(decimal qty, int workdays, int packSize, bool inBox, int dayMax, int dayMin)
        {
            decimal perDay = workdays > 0 ? qty / workdays : 0;
            decimal perUnit = inBox && packSize > 0 ? perDay / packSize : perDay;
            decimal r = RoundMode == "DOWN" ? Math.Floor(perUnit) : RoundMode == "NEAREST" ? Math.Round(perUnit, 0, MidpointRounding.AwayFromZero) : Math.Ceiling(perUnit);
            return (perDay, perUnit, r, r * dayMax, r * dayMin);
        }
    }

    // การตั้งค่าของคลัง (MST_CALC_STOCK): ใช้สูตรไหน + AUTO CALC (ไม่ได้เลือก = สูตร DEFAULT)
    public class StockCalcSetting
    {
        public int StkId { get; set; }
        public int? FormulaId { get; set; }
        public bool AutoCalc { get; set; }
        public DateTime? LastCalc { get; set; }
    }
    // การ์ดลูกค้าในหน้าต่างวันทำงาน
    public class WorkdayCustomer
    {
        public string Customer { get; set; }
        public int YearDays { get; set; }
        public int MonthsSet { get; set; }
    }

    // แถวนำเข้า Excel (Forecast / Order / Delivery, MAX-MIN DAYS, วันทำงาน)
    public class CalcImportRow
    {
        public int RowNumber { get; set; }
        public string Customer { get; set; }
        public string Part { get; set; }
        public DateTime Month { get; set; }
        public DateTime Date { get; set; }
        public decimal Forecast { get; set; }
        public decimal Order { get; set; }
        public decimal Delivery { get; set; }
        public int? Workdays { get; set; }
        public int DayMax { get; set; }
        public int DayMin { get; set; }
        public int PtId { get; set; }
        public string Error { get; set; }
        public bool IsValid => string.IsNullOrEmpty(Error);

        // ไฟล์รูปแบบ Max-MinCal (NO / PART NO / PRODUCT NAME / MAX(DAY) / MIN(DAY) / MAX(BOX) / MIN(BOX) / FORECAST / ORDER / DELIVERY)
        // null = ช่องว่างในไฟล์ (ไม่เปลี่ยนค่านั้น)
        public string PartName { get; set; }
        public int? TDayMax { get; set; }
        public int? TDayMin { get; set; }
        public int? TBoxMax { get; set; }
        public int? TBoxMin { get; set; }
        public decimal? TForecast { get; set; }
        public decimal? TOrder { get; set; }
        public decimal? TDelivery { get; set; }
        public bool HasDays => TDayMax.HasValue || TDayMin.HasValue;
        public bool HasBox => TBoxMax.HasValue || TBoxMin.HasValue;
        public bool HasForecast => TForecast.HasValue || TOrder.HasValue || TDelivery.HasValue;
    }

    // ผลการนำเข้าไฟล์รูปแบบ Max-MinCal
    public class CalcTemplateResult
    {
        public int Days { get; set; }
        public int Box { get; set; }
        public int Forecast { get; set; }
    }
}
