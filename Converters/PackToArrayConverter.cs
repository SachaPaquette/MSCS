using System;
using System.Globalization;
using System.Windows.Data;

namespace MSCS.Converters
{
    public sealed class PackToArrayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values;
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}