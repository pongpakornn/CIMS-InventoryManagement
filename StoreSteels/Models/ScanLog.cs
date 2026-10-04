using System;

namespace CIMS.Models
{
    // Model สำหรับตารางที่ 1 
    public class ScanLog
    {
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public int Qty { get; set; }
        public string Status { get; set; } // IN / OUT
        public DateTime FullDateTime { get; set; }
    }
}