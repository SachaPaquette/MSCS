using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MSCS.Converters
{
    public class BoolToGridLengthConverter : IValueConverter
    {
        public double OpenWidth { get; set; } = 300;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? new GridLength(OpenWidth) : new GridLength(0);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
