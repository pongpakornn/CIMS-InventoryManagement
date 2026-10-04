//using System;
//using System.Globalization;
//using System.Windows.Data;
//using System.Windows.Media;

//namespace CIMS.Converters
//{
//    public class StatusToColorConverter : IMultiValueConverter
//    {
//        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
//        {
//            if (values == null || values.Length < 3) return Brushes.Gray;

//            double.TryParse(values[0]?.ToString(), out double qtyStkb); // QtyStkb
//            double.TryParse(values[1]?.ToString(), out double max);
//            double.TryParse(values[2]?.ToString(), out double min);

//            // 1. ถ้า Max และ Min เป็น 0 ทั้งคู่ ให้แสดงเป็นสีเทา (ยังไม่ได้ตั้งค่า)
//            if (max == 0 && min == 0)
//                return Brushes.LightGray;

//            // 2. ถ้า Stock เป็น 0 -> แดง (ของหมด)
//            if (qtyStkb == 0)
//                return Brushes.Crimson;

//            // 3. ถ้า Stock เกิน Max -> เขียวแก่ (#2F8247)
//            if (max > 0 && qtyStkb > max)
//                return new SolidColorBrush(Color.FromRgb(47, 130, 71));

//            // 4. ถ้า Stock ต่ำกว่า Min -> แดง
//            if (min > 0 && qtyStkb < min)
//                return Brushes.Crimson;

//            // 5. กรณีที่เหลือ (สถานะปกติ / อยู่ระหว่าง Min-Max) -> ปรับเป็นเขียวสว่างขึ้น
//            return new SolidColorBrush(Color.FromRgb(76, 175, 80));
//        }
//        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
//        {
//            throw new NotImplementedException();
//            //return null;
//        }
//    }
//}

using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CIMS.Converters
{
    // 👑 IValueConverter รับ StockStatus string จาก DB โดยตรง
    //    ไม่มีการ Parse เป็น int ใดๆ ทั้งสิ้น
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            // 🛡️ ป้องกัน null หรือ type ที่ไม่ใช่ string ทุกกรณี
            string status = value?.ToString()?.Trim() ?? "";

            switch (status)
            {
                case "NO_CONFIG":
                    // ⚪ ยังไม่ได้ตั้งค่า Max/Min
                    return Brushes.LightGray;

                case "UNDER_MIN":
                    // 🔴 Stock ต่ำกว่า Min → กะพริบแดง
                    return Brushes.Crimson;

                case "OUT_OF_STOCK":
                    // 🔴 Stock = 0 → แดงเข้ม
                    return Brushes.DarkRed;

                case "OVER_MAX":
                    // 🟢 Stock มากกว่า Max → เขียวแก่
                    return new SolidColorBrush(Color.FromRgb(47, 130, 71));

                case "NORMAL_GOOD":
                    // 🍏 Stock อยู่ในช่วง Min-Max → เขียวอ่อน
                    return new SolidColorBrush(Color.FromRgb(76, 175, 80));

                case "NORMAL":
                    // 🍏 สถานะปกติทั่วไป
                    return new SolidColorBrush(Color.FromRgb(76, 175, 80));

                default:
                    // ❓ ค่าอื่นๆ ที่ไม่รู้จัก → เทา
                    return Brushes.LightGray;
            }
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}