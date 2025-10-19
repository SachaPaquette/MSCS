using MSCS.Models;
using MSCS.Services;

namespace MSCS.Services.Reader
{
    public sealed class ReaderPreferencesService
    {
        private readonly UserSettings? _userSettings;

        public ReaderPreferencesService(UserSettings? userSettings)
        {
            _userSettings = userSettings;
        }

        public ReaderProfile LoadProfile(string? profileKey)
        {
            if (_userSettings == null)
            {
                return ReaderProfile.CreateDefault();
            }

            return _userSettings.GetReaderProfile(profileKey);
        }

        public void SaveProfile(string? profileKey, ReaderProfile profile)
        {
            if (_userSettings == null || string.IsNullOrWhiteSpace(profileKey))
            {
                return;
            }

            _userSettings.SetReaderProfile(profileKey, profile);
        }
    }
}