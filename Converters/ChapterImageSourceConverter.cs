using MSCS.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Binding = System.Windows.Data.Binding;

namespace MSCS.Converters
{
    public sealed class ChapterImageSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not ChapterImage image)
                return Binding.DoNothing;

            if (image.StreamFactory is not null)
                return TryLoadFromFactory(image, image.StreamFactory);

            if (!string.IsNullOrWhiteSpace(image.ImageUrl))
            {
                if (File.Exists(image.ImageUrl))
                    return TryLoadFromFile(image, image.ImageUrl);

                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bi.UriSource = new Uri(image.ImageUrl, UriKind.RelativeOrAbsolute);
                    bi.EndInit();

                    if (bi.PixelWidth > 0 && bi.PixelHeight > 0)
                    {
                        image.PixelWidth = bi.PixelWidth;
                        image.PixelHeight = bi.PixelHeight;
                    }
                    else
                    {
                        bi.DownloadCompleted += (_, __) =>
                        {
                            image.PixelWidth = bi.PixelWidth;
                            image.PixelHeight = bi.PixelHeight;
                        };
                    }

                    if (bi.CanFreeze) bi.Freeze();
                    return bi;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load image from uri '{image.ImageUrl}': {ex.Message}");
                    return image.ImageUrl; // graceful fallback
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static BitmapSource? TryLoadFromFactory(ChapterImage owner, Func<Stream> factory)
        {
            try
            {
                using var stream = factory();
                if (stream is null) return null;
                return LoadBitmapFromStream(owner, stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load image from stream factory: {ex.Message}");
                return null;
            }
        }

        private static BitmapSource? TryLoadFromFile(ChapterImage owner, string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return LoadBitmapFromStream(owner, stream);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load image from file '{path}': {ex.Message}");
                return null;
            }
        }

        private static BitmapSource? LoadBitmapFromStream(ChapterImage owner, Stream source)
        {
            Stream input = source;
            MemoryStream? buffer = null;

            if (!source.CanSeek)
            {
                buffer = new MemoryStream();
                source.CopyTo(buffer);
                buffer.Position = 0;
                input = buffer;
            }
            else if (source.Position != 0)
            {
                source.Position = 0;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = input;
                bmp.EndInit();

                owner.PixelWidth = bmp.PixelWidth;
                owner.PixelHeight = bmp.PixelHeight;

                if (bmp.CanFreeze) bmp.Freeze();
                return bmp;
            }
            finally
            {
                buffer?.Dispose();
            }
        }
    }
}