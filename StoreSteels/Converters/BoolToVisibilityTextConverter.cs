// --- GenerateQRCode.cs ---
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CIMS.Converters
{
    public class BoolToShowHideConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? "SHOW" : "HIDE";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToShowColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return new SolidColorBrush(Color.FromRgb(0x2D, 0xFF, 0xB4)); // MintGreen = SHOW
            return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));     // Gray = HIDE
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }


}