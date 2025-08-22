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

            if (vp <= 0) vp = 800;
            var target = vp * frac;
            if (!double.IsInfinity(max) && max > 0) target = Math.Min(target, max);
            return Math.Max(320, target); // évite trop petit
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
