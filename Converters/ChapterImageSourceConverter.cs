using MSCS.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Binding = System.Windows.Data.Binding;

namespace MSCS.Converters
{
    public class ChapterImageSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not ChapterImage image)
            {
                return Binding.DoNothing;
            }

            if (image.StreamFactory != null)
            {
                try
                {
                    using var stream = image.StreamFactory();
                    if (stream == null)
                    {
                        return null;
                    }

                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load chapter image stream: {ex.Message}");
                    return null;
                }
            }

            if (!string.IsNullOrWhiteSpace(image.ImageUrl))
            {
                return image.ImageUrl;
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}