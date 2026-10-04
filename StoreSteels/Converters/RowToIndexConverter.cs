using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace CIMS.Converters // ตรวจสอบ Namespace ให้ตรงกับ Project ของนนท์นะครับ
{
    public class RowToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DataGridRow row)
            {
                return row.GetIndex() + 1; // คืนค่าลำดับแถว + 1
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }


    }
}