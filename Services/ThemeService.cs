using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MSCS.Enums;

namespace MSCS.Services
{
    public class ThemeService
    {
        private readonly Dictionary<AppTheme, Uri> _themeResources = new()
        {
            { AppTheme.Dark, new Uri("pack://application:,,,/Ressources/Styles/Themes/DarkTheme.xaml", UriKind.Absolute) },
            { AppTheme.Light, new Uri("pack://application:,,,/Ressources/Styles/Themes/LightTheme.xaml", UriKind.Absolute) }
        };

        private ResourceDictionary? _currentDictionary;

        public event EventHandler? ThemeChanged;

        public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

        public void ApplyTheme(AppTheme theme)
        {
            if (!_themeResources.TryGetValue(theme, out var resourceUri))
            {
                var fallbackTheme = AppTheme.Dark;
                if (!_themeResources.TryGetValue(fallbackTheme, out resourceUri))
                {
                    return;
                }

                theme = fallbackTheme;
            }

            if (CurrentTheme == theme && _currentDictionary != null)
            {
                return;
            }

            var application = System.Windows.Application.Current;
            if (application == null)
            {
                return;
            }

            void Apply()
            {
                var dictionaries = application.Resources.MergedDictionaries;

                if (_currentDictionary == null)
                {
                    _currentDictionary = dictionaries.FirstOrDefault(d => IsThemeDictionary(d.Source));
                }

                var newDictionary = new ResourceDictionary { Source = resourceUri };

                if (_currentDictionary != null)
                {
                    var index = dictionaries.IndexOf(_currentDictionary);
                    if (index >= 0)
                    {
                        dictionaries.RemoveAt(index);
                        dictionaries.Insert(index, newDictionary);
                    }
                    else
                    {
                        dictionaries.Insert(0, newDictionary);
                    }
                }
                else
                {
                    dictionaries.Insert(0, newDictionary);
                }

                _currentDictionary = newDictionary;
                CurrentTheme = theme;
                ThemeChanged?.Invoke(this, EventArgs.Empty);
            }

            if (application.Dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                application.Dispatcher.Invoke(Apply);
            }
        }

        private static bool IsThemeDictionary(Uri? source)
        {
            return source != null &&
                   source.OriginalString.Contains("/Ressources/Styles/Themes/", StringComparison.OrdinalIgnoreCase);
        }
    }
}