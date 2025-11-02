using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace MSCS.Models
{
    public class ChapterImage : INotifyPropertyChanged
    {
        public string ImageUrl { get; set; } = string.Empty;
        public IDictionary<string, string>? Headers { get; set; }
        public Func<Stream>? StreamFactory { get; set; }
        public Action? ReleaseResources { get; set; }

        int _pixelWidth;
        public int PixelWidth
        {
            get => _pixelWidth;
            set { if (_pixelWidth != value) { _pixelWidth = value; PropertyChanged?.Invoke(this, new(nameof(PixelWidth))); } }
        }

        int _pixelHeight;
        public int PixelHeight
        {
            get => _pixelHeight;
            set { if (_pixelHeight != value) { _pixelHeight = value; PropertyChanged?.Invoke(this, new(nameof(PixelHeight))); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
