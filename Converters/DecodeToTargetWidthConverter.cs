using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;


namespace MSCS.Converters
{
    public class DecodeToTargetWidthConverter : IMultiValueConverter
    {
        // values: [0]=url (string), [1]=viewportWidth (double), [2]=zoom (double)
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3) return null;
            var url = values[0] as string;
            if (string.IsNullOrWhiteSpace(url)) return null;

            double vp = values[1] is double d1 ? d1 : 800;
            double zoom = values[2] is double d2 ? d2 : 1.0;
            var decodeWidth = (int)Math.Round(Math.Max(320, (vp <= 0 ? 800 : vp) * zoom));

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnDemand;      // lighter RAM
            bmp.CreateOptions = BitmapCreateOptions.DelayCreation;
            bmp.DecodePixelWidth = decodeWidth;                // key: decode at display size
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}