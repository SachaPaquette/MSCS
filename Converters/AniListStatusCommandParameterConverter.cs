using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows;
using MSCS.Enums;
using MSCS.Models;

namespace MSCS.Converters
{
    public sealed class AniListStatusCommandParameterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not AniListMedia media)
            {
                return DependencyProperty.UnsetValue;
            }

            if (parameter is not AniListMediaListStatus status)
            {
                return DependencyProperty.UnsetValue;
            }

            return new AniListStatusChangeParameter(media, status);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}