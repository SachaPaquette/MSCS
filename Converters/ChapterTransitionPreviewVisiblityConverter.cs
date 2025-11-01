using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MSCS.Converters
{
    public class ChapterTransitionPreviewVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 3)
            {
                return Visibility.Collapsed;
            }

            if (values[0] is int alternationIndex &&
                values[1] is int itemCount &&
                values[2] is bool isPreviewVisible &&
                isPreviewVisible &&
                itemCount > 0 &&
                alternationIndex == itemCount - 1)
            {
                return Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}