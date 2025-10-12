using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace MSCS.ViewModels
{
    public class ContinueReadingViewModel : BaseViewModel, IDisposable
    {
        private readonly UserSettings _userSettings;
        private bool _disposed;

        public ContinueReadingViewModel(UserSettings userSettings)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            Entries = new ObservableCollection<ContinueReadingEntryViewModel>();
            RemoveEntryCommand = new RelayCommand(
                parameter =>
                {
                    if (parameter is ContinueReadingEntryViewModel entry)
                    {
                        RemoveEntry(entry);
                    }
                },
                parameter => parameter is ContinueReadingEntryViewModel);

            _userSettings.ReadingProgressChanged += OnReadingProgressChanged;

            ReloadEntries();
        }

        public ObservableCollection<ContinueReadingEntryViewModel> Entries { get; }

        public bool HasEntries => Entries.Count > 0;

        public ICommand RemoveEntryCommand { get; }

        public event EventHandler<ContinueReadingRequestedEventArgs>? ContinueReadingRequested;

        private void OnReadingProgressChanged(object? sender, EventArgs e)
        {
            ReloadEntries();
        }

        private void ReloadEntries()
        {
            if (_disposed)
            {
                return;
            }

            var allEntries = _userSettings
                .GetAllReadingProgress()
                .OrderByDescending(entry => entry.Value.LastUpdatedUtc)
                .ToList();

            Entries.Clear();
            foreach (var entry in allEntries)
            {
                Entries.Add(new ContinueReadingEntryViewModel(entry.Key, entry.Value, OnContinueRequested, RemoveEntryCommand));
            }

            OnPropertyChanged(nameof(HasEntries));
        }

        private void OnContinueRequested(ContinueReadingEntryViewModel entry)
        {
            ContinueReadingRequested?.Invoke(this, new ContinueReadingRequestedEventArgs(entry.MangaTitle, entry.Progress));
        }

        public void RemoveEntry(ContinueReadingEntryViewModel entry)
        {
            if (entry == null)
            {
                return;
            }

            _userSettings.ClearReadingProgress(entry.MangaTitle);
            ReloadEntries();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _userSettings.ReadingProgressChanged -= OnReadingProgressChanged;
        }
    }

    public class ContinueReadingEntryViewModel : BaseViewModel
    {
        private readonly Action<ContinueReadingEntryViewModel> _onContinue;

        public ContinueReadingEntryViewModel(
            string mangaTitle,
            MangaReadingProgress progress,
            Action<ContinueReadingEntryViewModel> onContinue,
            ICommand removeCommand)
        {
            MangaTitle = mangaTitle ?? string.Empty;
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _onContinue = onContinue ?? throw new ArgumentNullException(nameof(onContinue));
            RemoveCommand = removeCommand ?? throw new ArgumentNullException(nameof(removeCommand));

            ContinueCommand = new RelayCommand(_ => _onContinue(this), _ => CanContinue);
        }

        public string MangaTitle { get; }

        public MangaReadingProgress Progress { get; }

        public string ChapterDisplay => !string.IsNullOrWhiteSpace(Progress.ChapterTitle)
            ? Progress.ChapterTitle!
            : $"Chapter {Progress.ChapterIndex + 1}";

        public double ScrollProgress => Math.Clamp(Progress.ScrollProgress, 0.0, 1.0);

        public double ScrollPercentage => Math.Round(ScrollProgress * 100, 0);

        public string ProgressSummary => $"{ScrollPercentage:0}% read";

        public string LastUpdatedDisplay => Progress.LastUpdatedUtc.ToLocalTime().ToString("g");

        public bool CanContinue => !string.IsNullOrWhiteSpace(Progress.MangaUrl);

        public ICommand ContinueCommand { get; }

        public ICommand RemoveCommand { get; }
    }

    public class ContinueReadingRequestedEventArgs : EventArgs
    {
        public ContinueReadingRequestedEventArgs(string mangaTitle, MangaReadingProgress progress)
        {
            MangaTitle = mangaTitle;
            Progress = progress;
        }

        public string MangaTitle { get; }

        public MangaReadingProgress Progress { get; }
    }
}