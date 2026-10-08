// แถวย่อย Coil ใต้สินค้าในตาราง Store (Max-Min): หัวกลุ่ม Coil แม่ + Coil ลูกแต่ละม้วน
using System;

namespace CIMS.Models
{
    public class CoilRowModel
    {
        public int CoilId { get; set; }
        public int PartId { get; set; }
        public bool IsMother { get; set; }
        public int No { get; set; }
        public string CoilNo { get; set; }
        public string MotherCoil { get; set; }
        public decimal WeightKG { get; set; }
        public string LabelDate { get; set; }
        public DateTime? ReceivedDate { get; set; }

        public string NoText => IsMother ? "" : No.ToString();
        public string WeightKGText => WeightKG.ToString("#,##0.###");
        public string WeightTonText => (WeightKG / 1000m).ToString("#,##0.000");
        public string DateText => IsMother ? "" : (ReceivedDate?.ToString("dd/MM/yyyy HH:mm") ?? "-");
    }
}
