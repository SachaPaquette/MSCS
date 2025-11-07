using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MSCS.ViewModels;

namespace MSCS.Converters
{
    public sealed class TrackingStatusCommandParameterConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
            {
                return DependencyProperty.UnsetValue;
            }

            if (values[0] is not TrackingLibraryEntryViewModel entry)
            {
                return DependencyProperty.UnsetValue;
            }

            var status = values[1];
            return new TrackingStatusChangeParameter(status!, entry);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}