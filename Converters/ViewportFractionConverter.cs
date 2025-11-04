using System;
using System.Globalization;
using System.Windows.Data;
namespace MSCS.Converters
{

    // values: [0]=viewportWidth (double), [1]=widthFactor (double), [2]=maxPageWidth (double)
    public class ViewportFractionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double vp = values.Length > 0 && values[0] is double d0 ? d0 : 800;
            double frac = values.Length > 1 && values[1] is double d1 ? d1 : 0.65;
            double max = values.Length > 2 && values[2] is double d2 ? d2 : double.PositiveInfinity;
            bool twoPage = values.Length > 3 && values[3] is bool b3 && b3;
            bool autoWidth = values.Length > 4 && values[4] is bool b4 && b4;

            if (vp <= 0) vp = 800;
            double pages = twoPage ? 2.0 : 1.0;
            var divisor = Math.Max(pages, 1.0);
            var target = autoWidth ? vp / divisor : (vp * frac) / divisor;
            if (autoWidth && !double.IsInfinity(max) && max > 0)
            {
                target = Math.Min(target, max);
            }
            return Math.Max(320, target);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
