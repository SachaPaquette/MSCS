using System;
using System.IO;
using System.Text.Json;

namespace MSCS.Services
{
    public class UserSettings
    {
        private readonly string _settingsPath;
        private readonly object _syncLock = new();
        private SettingsData _data;

        public event EventHandler? SettingsChanged;

        public UserSettings()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "MSCS");
            _settingsPath = Path.Combine(folder, "settings.json");
            _data = LoadInternal();
        }

        public string? LocalLibraryPath
        {
            get => _data.LocalLibraryPath;
            set
            {
                var sanitized = string.IsNullOrWhiteSpace(value) ? null : value?.Trim();
                if (string.Equals(_data.LocalLibraryPath, sanitized, StringComparison.Ordinal))
                {
                    return;
                }

                _data.LocalLibraryPath = sanitized;
                SaveInternal();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private SettingsData LoadInternal()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new SettingsData();
                }

                var json = File.ReadAllText(_settingsPath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                return data ?? new SettingsData();
            }
            catch
            {
                return new SettingsData();
            }
        }

        private void SaveInternal()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                lock (_syncLock)
                {
                    File.WriteAllText(_settingsPath, json);
                }
            }
            catch
            {
                // Swallow IO issues – we do not want settings persistence failures to crash the app.
            }
        }

        private class SettingsData
        {
            public string? LocalLibraryPath { get; set; }
        }
    }
}