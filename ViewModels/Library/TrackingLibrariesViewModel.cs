using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MSCS.ViewModels
{
    public class TrackingLibrariesViewModel : BaseViewModel, IDisposable
    {
        private readonly IReadOnlyList<ITrackingLibraryViewModel> _libraries;
        private bool _disposed;
        private ITrackingLibraryViewModel? _selectedLibrary;

        public TrackingLibrariesViewModel(
            AniListTrackingLibraryViewModel aniList,
            MyAnimeListTrackingLibraryViewModel myAnimeList,
            KitsuTrackingLibraryViewModel kitsu)
        {
            if (aniList == null)
            {
                throw new ArgumentNullException(nameof(aniList));
            }

            if (myAnimeList == null)
            {
                throw new ArgumentNullException(nameof(myAnimeList));
            }

            if (kitsu == null)
            {
                throw new ArgumentNullException(nameof(kitsu));
            }

            _libraries = new List<ITrackingLibraryViewModel>
            {
                aniList,
                myAnimeList,
                kitsu
            };

            Libraries = new ObservableCollection<ITrackingLibraryViewModel>(_libraries);
            _selectedLibrary = Libraries.FirstOrDefault();
        }

        public ObservableCollection<ITrackingLibraryViewModel> Libraries { get; }

        public ITrackingLibraryViewModel? SelectedLibrary
        {
            get => _selectedLibrary;
            set => SetProperty(ref _selectedLibrary, value);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var library in _libraries)
            {
                library.Dispose();
            }
        }
    }
}