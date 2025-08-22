using System;
using System.Globalization;
using System.Windows.Data;

namespace MSCS.Converters
{
    public class TargetWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not double vp || values[1] is not double zoom) return 800d;
            if (vp <= 0) vp = 800;
            return Math.Max(320, vp * zoom); // avoid tiny widths
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}