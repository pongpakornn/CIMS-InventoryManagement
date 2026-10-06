using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CIMS.Models
{
    public enum TransactionType { IN, OUT }

    public class ScanItemModel : INotifyPropertyChanged
    {
        private int _inCount;
        private decimal _totalInQty;
        private int _outCount;
        private decimal _totalOutQty;
        private decimal _finalStock;
        private string _partNo;
        private string _partACode;
        private string _partCode;
        private string _partName;
        private System.Windows.Media.ImageSource _productImagePath;

        public int PartId { get; set; }

        // คลังที่รายการนี้ลงจริง (สแกนร่วมหลายคลัง / AUTO ระบบเลือกคลังให้ -> ตารางสรุปแยกยอดตามคลัง)
        public int StkId { get; set; }
        public string StockCode { get; set; }

        // ข้อความรหัสในตาราง: สแกนร่วมหลายคลังต่อท้ายรหัสคลังบรรทัดล่าง (สินค้าเดียวกันคนละคลังจะได้แยกออก)
        public bool ShowStockInCode { get; set; }
        public string CodeLabel => ShowStockInCode && !string.IsNullOrEmpty(StockCode) ? $"{CodeText}\n{StockCode}" : CodeText;

        // ค่าที่ตั้งให้แสดงแทนรหัส (ตั้งต่อคลังที่ปุ่ม DISPLAY ในหน้า Multi-Scanner) - ไม่ได้ตั้ง = รหัสเดิม
        private string _displayCode;
        public string DisplayCode { get => _displayCode; set { _displayCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(CodeLabel)); OnPropertyChanged(nameof(CodeText)); } }
        public string CodeText => string.IsNullOrWhiteSpace(DisplayCode) ? PartACode : DisplayCode;

        // ข้อมูลเสริมสำหรับเลือกค่าที่แสดง
        public string PartA { get; set; }
        public string PartNumber { get; set; }
        public string Model { get; set; }
        public string RefNo { get; set; }

        // เปลี่ยนมารับเป็น ImageSource เพื่อรองรับภาพจาก UI Thread โดยตรง
        public System.Windows.Media.ImageSource ProductImagePath
        {
            get => _productImagePath;
            set { _productImagePath = value; OnPropertyChanged(); }
        }

        // ปรับเป็น Full Properties + OnPropertyChanged เพื่อให้ UI ตารางสรุปอัปเดต Real-time
        public string PartCode
        {
            get => _partCode;
            set { _partCode = value; OnPropertyChanged(); }
        }
        public string PartName
        {
            get => _partName;
            set { _partName = value; OnPropertyChanged(); }
        }
        public string PartNo
        {
            get => _partNo;
            set { _partNo = value; OnPropertyChanged(); }
        }
        public string PartACode
        {
            get => _partACode;
            set { _partACode = value; OnPropertyChanged(); }
        }

        // ยอดเป็นทศนิยมได้ (คลังที่เปิด DECIMAL QTY เช่น KG) - คลังอื่นเป็นจำนวนเต็มเหมือนเดิม
        public decimal Qty { get; set; }
        public string Status { get; set; }
        public DateTime UpdateTime { get; set; }

        public int InCount { get => _inCount; set { _inCount = value; OnPropertyChanged(); } }
        public decimal TotalInQty { get => _totalInQty; set { _totalInQty = value; OnPropertyChanged(); } }
        public int OutCount { get => _outCount; set { _outCount = value; OnPropertyChanged(); } }
        public decimal TotalOutQty { get => _totalOutQty; set { _totalOutQty = value; OnPropertyChanged(); } }
        public decimal FinalStock { get => _finalStock; set { _finalStock = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}