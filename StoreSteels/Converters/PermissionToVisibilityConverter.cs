// MainView กำหนดสิทธิ์

using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace CIMS.Converters
{
    public class PermissionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ป้องกัน Error เบื้องต้น
            if (value == null || parameter == null) return Visibility.Collapsed;

            try
            {
                string userLevel = value.ToString();
                string allowedLevelsString = parameter.ToString();

                // แยกเงื่อนไขการตรวจสอบ
                string[] allowedLevels = allowedLevelsString.Split(',');

                // ตรวจสอบว่า UserLevel ปัจจุบันอยู่ในลิสต์ที่อนุญาตหรือไม่
                if (allowedLevels.Any(x => x.Trim() == userLevel))
                {
                    return Visibility.Visible;
                }
            }
            catch
            {
                return Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}