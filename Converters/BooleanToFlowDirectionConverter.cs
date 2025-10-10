using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MSCS.Converters
{
    public class BooleanToFlowDirectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool b && b;
            return flag ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Windows.FlowDirection direction)
            {
                return direction == System.Windows.FlowDirection.RightToLeft;
            }

            return false;
        }
    }
}