using System;

namespace CIMS.Models // เปลี่ยน Namespace ให้เป็น .Models
{
    // Model สำหรับตารางที่ 2
    public class DailySummary
    {
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public int InCount { get; set; }  // จำนวนครั้งที่รับเข้า
        public int OutCount { get; set; } // จำนวนครั้งที่จ่ายออก
        public int InQty { get; set; }    // จำนวนชิ้นที่รับเข้า
        public int OutQty { get; set; }   // จำนวนชิ้นที่จ่ายออก
        public int StockPcs { get; set; } // ยอดคงเหลือ
    }
}