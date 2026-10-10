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

            // 🚦 สัญญาณไฟ: "lamp" = ดวงไฟ (สว่างกลาง เข้มขอบ) / "glow" = แสงฟุ้งรอบดวงไฟ (จางออกจนใส)
            //   ใช้แปรงไล่สีแทน DropShadowEffect - ไม่มีเอฟเฟกต์เบลอ ตารางเลื่อนลื่นเหมือนเดิม
            string mode = parameter as string;
            if (mode == "lamp" || mode == "glow")
            {
                var key = status + "|" + mode;
                lock (_cache)
                {
                    if (!_cache.TryGetValue(key, out var b))
                    {
                        Color c = ((SolidColorBrush)Convert(value, targetType, null, culture)).Color;
                        b = mode == "lamp" ? Lamp(c) : Glow(c);
                        _cache[key] = b;
                    }
                    return b;
                }
            }

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

        private static readonly System.Collections.Generic.Dictionary<string, Brush> _cache = new System.Collections.Generic.Dictionary<string, Brush>();

        private static Color Mix(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ดวงไฟ: จุดสะท้อนสว่างด้านบนซ้าย -> สีไฟ -> ขอบเข้มขึ้นเล็กน้อย
        private static Brush Lamp(Color c)
        {
            var b = new RadialGradientBrush { GradientOrigin = new System.Windows.Point(0.38, 0.32), Center = new System.Windows.Point(0.45, 0.42), RadiusX = 0.62, RadiusY = 0.62 };
            b.GradientStops.Add(new GradientStop(Mix(c, Colors.White, 0.75), 0.0));
            b.GradientStops.Add(new GradientStop(Mix(c, Colors.White, 0.25), 0.35));
            b.GradientStops.Add(new GradientStop(c, 0.75));
            b.GradientStops.Add(new GradientStop(Mix(c, Colors.Black, 0.18), 1.0));
            b.Freeze();
            return b;
        }

        // แสงฟุ้ง: สีไฟจางลงเรื่อยๆ จนใสที่ขอบ (ไม่มีเส้นขอบ)
        private static Brush Glow(Color c)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, c.R, c.G, c.B), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xA0, c.R, c.G, c.B), 0.45));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x48, c.R, c.G, c.B), 0.68));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x14, c.R, c.G, c.B), 0.86));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1.0));
            b.Freeze();
            return b;
        }
    }
}