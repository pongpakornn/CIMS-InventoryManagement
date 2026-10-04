using System;
using System.Globalization;
using System.IO; // ⬅️ เพิ่มตัวนี้เพื่อแก้ Error 'File'
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace CIMS.Converters
{
    public class PathToImageConverter : IValueConverter
    {
        // TEST PAth 03-05-2026
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string path = value as string;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // โหลดเข้า RAM
                bitmap.DecodePixelWidth = 150; // ⬅️ เพิ่มบรรทัดนี้: ช่วยให้กิน RAM น้อยลงมาก!
                bitmap.EndInit();
                bitmap.Freeze(); // ทำให้ใช้ข้าม Thread ได้
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}