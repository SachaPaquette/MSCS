using MSCS.Commands;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;
using MSCS.Services;
using MSCS.Services.Reader;
using System;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        public ReaderPreferencesViewModel Preferences => _preferences;
        public ReaderChapterCoordinator? ChapterCoordinator => _chapterCoordinator;

        private void InitializePreferences()
        {
            _preferences.SetProfileKey(DetermineProfileKey());
            if (_chapterCoordinator != null)
            {
                _chapterCoordinator.ImageCached += OnChapterCoordinatorImageCached;
            }
        }

        public void RefreshPreferencesProfileKey()
        {
            _preferences.SetProfileKey(DetermineProfileKey());
        }

        private void OnChapterCoordinatorImageCached(object? sender, EventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ImageCacheVersion++);
            }
            else
            {
                ImageCacheVersion++;
            }
        }
    }
}