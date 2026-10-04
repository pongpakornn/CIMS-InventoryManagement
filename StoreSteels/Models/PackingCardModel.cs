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
        public string JobName { get; set; }         // FREMARK -> แสดงเป็น Part Name
        public decimal Qty { get; set; }            // FQTY
        public DateTime TicketDate { get; set; }    // FMDATE

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
