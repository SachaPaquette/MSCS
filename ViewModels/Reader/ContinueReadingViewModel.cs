using MSCS.Commands;
using MSCS.Models;
using MSCS.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MSCS.ViewModels
{
    public class ContinueReadingViewModel : BaseViewModel, IDisposable
    {
        private readonly UserSettings _userSettings;
        private readonly ReadingListService _readingListService;
        private bool _disposed;

        public ContinueReadingViewModel(UserSettings userSettings, ReadingListService readingListService)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
            _readingListService = readingListService ?? throw new ArgumentNullException(nameof(readingListService));
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
            ExportReadingListCommand = new RelayCommand(_ => ExportReadingList(), _ => !_disposed);
            ImportReadingListCommand = new RelayCommand(_ => ImportReadingList(), _ => !_disposed);

            ReloadEntries();
        }

        public ObservableCollection<ContinueReadingEntryViewModel> Entries { get; }

        public bool HasEntries => Entries.Count > 0;

        public ICommand RemoveEntryCommand { get; }
        public ICommand ExportReadingListCommand { get; }

        public ICommand ImportReadingListCommand { get; }

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

            var dispatcher = System.Windows.Application.Current?.Dispatcher;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                ReloadEntriesCore();
            }
            else
            {
                dispatcher.InvokeAsync(ReloadEntriesCore, DispatcherPriority.DataBind);
            }
        }

        private void ReloadEntriesCore()
        {
            if (_disposed)
            {
                return;
            }

            var allEntries = _userSettings
                .GetAllReadingProgress()
                .OrderByDescending(entry => entry.Progress.LastUpdatedUtc)
                .ToList();

            Entries.Clear();
            foreach (var entry in allEntries)
            {
                Entries.Add(new ContinueReadingEntryViewModel(entry, OnContinueRequested, RemoveEntryCommand));
            }

            OnPropertyChanged(nameof(HasEntries));
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnContinueRequested(ContinueReadingEntryViewModel entry)
        {
            ContinueReadingRequested?.Invoke(this, new ContinueReadingRequestedEventArgs(entry.StorageKey, entry.MangaTitle, entry.Progress));
        }

        public void RemoveEntry(ContinueReadingEntryViewModel entry)
        {
            if (entry == null)
            {
                return;
            }

            _userSettings.ClearReadingProgress(entry.StorageKey);
            ReloadEntries();
        }

        private void ExportReadingList()
        {
            if (_disposed)
            {
                return;
            }

            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "Reading list (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                AddExtension = true,
                FileName = $"reading-list-{DateTime.Now:yyyyMMdd-HHmm}.json"
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            try
            {
                var result = _readingListService.Export(dialog.FileName);
                var message = BuildExportSummary(result);
                System.Windows.MessageBox.Show(message, "Reading List Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Reading List Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildExportSummary(ReadingListExportResult result)
        {
            var builder = new StringBuilder();

            if (result.ExportedCount > 0)
            {
                builder.AppendLine($"Exported {result.ExportedCount} entr{(result.ExportedCount == 1 ? "y" : "ies")}.");
            }
            else
            {
                builder.AppendLine("No entries were exported.");
            }

            if (result.SkippedLocalCount > 0 || result.SkippedMissingDataCount > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Skipped:");

                if (result.SkippedLocalCount > 0)
                {
                    builder.AppendLine($" • {result.SkippedLocalCount} local manga (not shareable)");
                }

                if (result.SkippedMissingDataCount > 0)
                {
                    builder.AppendLine($" • {result.SkippedMissingDataCount} entries missing required information");
                }
            }

            if (result.TotalEntries == 0)
            {
                builder.AppendLine("There were no reading entries available to export.");
            }
            else if (result.ExportedCount == 0)
            {
                builder.AppendLine("All entries were skipped because they refer to local manga or lack required information.");
            }

            return builder.ToString();
        }

        private void ImportReadingList()
        {
            if (_disposed)
            {
                return;
            }

            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "Reading list (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            try
            {
                var result = _readingListService.Import(dialog.FileName);
                var message = BuildImportSummary(result);
                System.Windows.MessageBox.Show(message, "Reading List Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Reading List Import", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildImportSummary(ReadingListImportResult result)
        {
            var builder = new StringBuilder();

            if (result.TotalEntries == 0)
            {
                builder.AppendLine("The selected file did not contain any reading entries.");
                return builder.ToString();
            }

            builder.AppendLine($"Imported {result.ImportedCount} entr{(result.ImportedCount == 1 ? "y" : "ies")}.");

            if (result.SkippedLocalCount > 0 || result.SkippedInvalidCount > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Skipped:");

                if (result.SkippedLocalCount > 0)
                {
                    builder.AppendLine($" • {result.SkippedLocalCount} local manga (not shareable)");
                }

                if (result.SkippedInvalidCount > 0)
                {
                    builder.AppendLine($" • {result.SkippedInvalidCount} entries missing required information");
                }
            }
            else if (result.ImportedCount == 0)
            {
                builder.AppendLine();
                builder.AppendLine("All entries were skipped because they refer to local manga or lacked required information.");
            }

            return builder.ToString();
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
            ReadingProgressEntry entry,
            Action<ContinueReadingEntryViewModel> onContinue,
            ICommand removeCommand)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            StorageKey = entry.StorageKey ?? string.Empty;
            MangaTitle = entry.Title ?? string.Empty;
            Progress = entry.Progress;
            _onContinue = onContinue ?? throw new ArgumentNullException(nameof(onContinue));
            RemoveCommand = removeCommand ?? throw new ArgumentNullException(nameof(removeCommand));

            ContinueCommand = new RelayCommand(_ => _onContinue(this), _ => CanContinue);
        }

        public string StorageKey { get; }

        public string MangaTitle { get; }

        public MangaReadingProgress Progress { get; }

        public string ChapterDisplay => !string.IsNullOrWhiteSpace(Progress.ChapterTitle)
            ? Progress.ChapterTitle!
            : $"Chapter {Progress.ChapterIndex + 1}";

        public double ScrollProgress
        {
            get
            {
                return Progress.ScrollProgress;
            }
        }

        public double ScrollPercentage => Math.Round(ScrollProgress * 100, 0);

        public string ProgressSummary => $"{ScrollPercentage:0}% read";

        public string LastUpdatedDisplay => Progress.LastUpdatedUtc.ToLocalTime().ToString("g");

        public bool CanContinue => !string.IsNullOrWhiteSpace(Progress.MangaUrl);

        public ICommand ContinueCommand { get; }

        public ICommand RemoveCommand { get; }
    }

    public class ContinueReadingRequestedEventArgs : EventArgs
    {
        public ContinueReadingRequestedEventArgs(string storageKey, string mangaTitle, MangaReadingProgress progress)
        {
            StorageKey = storageKey ?? string.Empty;
            MangaTitle = mangaTitle ?? string.Empty;
            Progress = progress;
        }

        public string StorageKey { get; }

        public string MangaTitle { get; }

        public MangaReadingProgress Progress { get; }
    }
}