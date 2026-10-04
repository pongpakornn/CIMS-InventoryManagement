using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace CIMS.Models
{
    public class PRModel : INotifyPropertyChanged
    {
        // 1. ประกาศ Backing Fields สำหรับตัวที่ต้องการให้ UI อัพเดททันที
        private bool _isSelected;
        private string _status;
        private string _partName;
        private int _qty;
        private string _prRem;

        // --- 2. เพิ่ม Property IsSelected ไว้บนสุด (เพื่อให้หาง่าย) ---
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(); // ใช้แบบสั้นได้เลยเพราะมี CallerMemberName
            }
        }

        public int ID { get; set; } // ID ปกติไม่ค่อยเปลี่ยนตอนโชว์ ไม่ต้องทำ Notify ก็ได้ครับ
        public string PR_NO { get; set; }
        public string Requester { get; set; }
        public string PR_DATE_Display => PR_DATE.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

        public string PartCode
        {
            get => _partCode;
            set { _partCode = value; OnPropertyChanged(); }
        }
        private string _partCode;

        // 2. ปรับ Property ให้มีการเรียก OnPropertyChanged
        public string PartName
        {
            get => _partName;
            set { _partName = value; OnPropertyChanged(); }
        }

        public int QTY
        {
            get => _qty;
            set { _qty = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string PR_REM
        {
            get => _prRem;
            set { _prRem = value; OnPropertyChanged(); }
        }

        // ตัวพวกนี้ถ้าไม่ได้มีการแก้ระหว่างหน้าจอเปิดอยู่ ใช้ Auto-Property แบบเดิมได้ครับ
        public string Department { get; set; }
        public string REQ_DEPT { get; set; }
        public string USR_ID { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime PR_DATE { get; set; }

        // รายการที่ Approve แล้ว (หน้าต่าง APPROVED LIST)
        public string Unit { get; set; }
        public string TargetDept { get; set; }
        public DateTime? AppDate { get; set; }
        public DateTime? ExportDate { get; set; }
        public string AppDateDisplay => AppDate?.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "-";
        public string ExportDisplay => ExportDate?.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "NOT YET";
        public bool IsExported => ExportDate.HasValue;

        // 3. ส่วนสำคัญ: Event และ Method สำหรับแจ้งเตือน UI
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}