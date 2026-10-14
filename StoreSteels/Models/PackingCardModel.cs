using QRCoder;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace CIMS.Models
{
    // ข้อมูลรายการเบิกจาก ERP (dbo.SD11ICTR) สำหรับปริ้น Packing Card เฉยๆ ไม่กระทบ DB หลัก
    public class PackingCardModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string TicketNo { get; set; }      // FTRNNO  (Bill No)
        public string ItemNo { get; set; }         // FITEMNO (คู่ key กับ TicketNo สำหรับกันเบิกซ้ำ)
        public string GroupCode { get; set; }       // FGDCODE (ใช้จัดกลุ่มคลังในตาราง)
        public string MaterialCode { get; set; }    // FPDCODE
        public string WorkOrder { get; set; }       // FWONO
        public string LotNo { get; set; }           // FLOTNO
        public string JobName { get; set; }         // FREMARK (ยังอยู่ใน QR + ค้นหาได้)
        public decimal Qty { get; set; }            // FQTY
        public DateTime TicketDate { get; set; }    // FMDATE
        public string ProductName { get; set; }     // SD05PDDS.FPDNAME ของ FPDCODE -> PRODUCT NAME
        public string Unit { get; set; }            // SD05PDDS.FUNITM -> หน่วยต่อท้ายจำนวน
        public string FgName { get; set; }          // FG_FPDNAME (ชื่อสินค้าสำเร็จรูปของ Work Order) -> PART NAME

        public string QtyText => string.IsNullOrEmpty(Unit) ? Qty.ToString("0.##") : $"{Qty:0.##} {Unit}";

        // 🖨 พิมพ์แล้วหรือยัง (ว่าง = ยังไม่พิมพ์): ครั้งแรก + จำนวนครั้ง + ครั้งล่าสุด (CIMS.PickListPrintLogs)
        public string PrintedBy { get; private set; }
        public DateTime? PrintedDate { get; private set; }
        public int PrintCount { get; private set; }
        public string LastPrintedBy { get; private set; }
        public DateTime? LastPrintedDate { get; private set; }
        public bool IsPrinted => PrintedDate.HasValue;

        private static string Stamp(DateTime d) => d.ToString("dd-MM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        public string PrintedText => !PrintedDate.HasValue ? "" : $"{Stamp(PrintedDate.Value)}  {PrintedBy}";
        // พิมพ์ซ้ำ: "×2 • 14-10-2026 16:40  2125" (ครั้งล่าสุด)
        public string ReprintText => PrintCount > 1 && LastPrintedDate.HasValue ? $"×{PrintCount} • {Stamp(LastPrintedDate.Value)}  {LastPrintedBy}" : "";

        public void SetPrintInfo(CIMS.Services.PackingPrintLogService.PrintInfo p)
        {
            PrintedBy = p?.By;
            PrintedDate = p == null || p.Date == DateTime.MinValue ? (DateTime?)null : p.Date;
            PrintCount = p?.Count ?? 0;
            LastPrintedBy = p?.LastBy;
            LastPrintedDate = p?.LastDate;
            OnPropertyChanged(nameof(PrintedText));
            OnPropertyChanged(nameof(ReprintText));
            OnPropertyChanged(nameof(IsPrinted));
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        // ข้อความสำหรับ QR Code: TicketNo | MaterialCode | WorkOrder | LotNo | JobName | Qty
        public string QrText =>
            $"{TicketNo} | {MaterialCode} | {WorkOrder} | {LotNo} | {JobName} | {Qty:0.##}";

        private BitmapImage _qrImage;
        // สร้าง QR ครั้งแรกที่ถูกเรียกใช้แล้ว cache ไว้ (ใช้ทั้งพรีวิวและตอนพิมพ์)
        public BitmapImage QrImage
        {
            get
            {
                if (_qrImage == null)
                {
                    try
                    {
                        using (var generator = new QRCodeGenerator())
                        using (var data = generator.CreateQrCode(QrText ?? "", QRCodeGenerator.ECCLevel.M))
                        using (var qrCode = new PngByteQRCode(data))
                        {
                            byte[] bytes = qrCode.GetGraphic(10);
                            using (var ms = new MemoryStream(bytes))
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = ms;
                                bmp.EndInit();
                                bmp.Freeze();
                                _qrImage = bmp;
                            }
                        }
                    }
                    catch
                    {
                        _qrImage = null;
                    }
                }
                return _qrImage;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
