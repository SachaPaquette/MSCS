using MSCS.Commands;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.ViewModels.Library;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static MSCS.ViewModels.Library.TrackingLibraryStatisticsViewModel;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MSCS.ViewModels
{
    public abstract class TrackingLibraryViewModelBase<TMedia, TTrackingInfo, TStatus> : BaseViewModel, ITrackingLibraryViewModel
        where TStatus : struct
    {
        private readonly IMediaTrackingService<TMedia, TTrackingInfo, TStatus> _service;
        private readonly ObservableCollection<TrackingLibrarySectionViewModel> _sections;
        private readonly CancellationTokenSource _cts = new();
        private CancellationTokenSource? _activeLoadCts;
        private bool _isLoading;
        private string? _statusMessage;
        private bool _disposed;

        protected TrackingLibraryViewModelBase(IMediaTrackingService<TMedia, TTrackingInfo, TStatus> service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            Statistics = new TrackingLibraryStatisticsViewModel();
            StatusOptions = CreateStatusOptions();

            _sections = new ObservableCollection<TrackingLibrarySectionViewModel>(
                GetOrderedStatuses().ToList().Select((status, index) =>
                    new TrackingLibrarySectionViewModel(status!, GetStatusDisplayName(status), index == 0)));
            Sections = new ReadOnlyObservableCollection<TrackingLibrarySectionViewModel>(_sections);

            RefreshCommand = new AsyncRelayCommand(_ => LoadAsync(), _ => !IsLoading);
            OpenSeriesCommand = new RelayCommand(OpenSeries);
            ChangeStatusCommand = new AsyncRelayCommand(parameter => ChangeStatusAsync(parameter), _ => SupportsStatusChanges && !IsLoading);
            EditTrackingCommand = new AsyncRelayCommand(parameter => OpenTrackingEditorAsync(parameter), _ => SupportsTrackingEditor && !IsLoading);

            _service.AuthenticationChanged += OnAuthenticationChanged;
            _service.MediaTrackingChanged += OnMediaTrackingChanged;

            _ = LoadAsync();
        }

        public string ServiceId => _service.ServiceId;

        public string ServiceDisplayName => _service.DisplayName;

        public bool IsAuthenticated => _service.IsAuthenticated;

        public string? UserName => _service.UserName;

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string? StatusMessage
        {
            get => _statusMessage;
            protected set => SetProperty(ref _statusMessage, value);
        }

        public bool HasAnySeries => _sections.Any(section => section.HasItems);

        public ReadOnlyObservableCollection<TrackingLibrarySectionViewModel> Sections { get; }

        public TrackingLibraryStatisticsViewModel Statistics { get; }

        public IReadOnlyList<ITrackingLibraryStatusOption> StatusOptions { get; }

        public virtual bool SupportsStatusChanges => StatusOptions.Count > 0;

        public virtual bool SupportsTrackingEditor => true;

        public ICommand RefreshCommand { get; }

        public ICommand OpenSeriesCommand { get; }

        public ICommand ChangeStatusCommand { get; }

        public ICommand EditTrackingCommand { get; }

        protected IMediaTrackingService<TMedia, TTrackingInfo, TStatus> Service => _service;

        protected virtual string ConnectAccountMessage => $"Connect your {ServiceDisplayName} account from the Settings tab to view your library.";

        protected virtual string LoadingMessage => $"Loading your {ServiceDisplayName} series...";

        protected virtual string EmptyLibraryMessage => $"Your {ServiceDisplayName} library is currently empty.";

        protected virtual string LoadFailedMessage => $"Unable to load your {ServiceDisplayName} library right now.";

        protected virtual string UpdateStatusMessage(TStatus status) => $"Updating {ServiceDisplayName} status to {GetStatusDisplayName(status)}...";

        protected virtual string UpdateStatusSuccessMessage(TStatus status) => $"{ServiceDisplayName} status updated to {GetStatusDisplayName(status)}.";

        protected virtual string UpdateStatusFailureMessage => $"Unable to update the {ServiceDisplayName} status. Please try again.";

        protected virtual string UpdateStatusCancelledMessage => $"{ServiceDisplayName} update cancelled.";

        protected virtual IReadOnlyList<ITrackingLibraryStatusOption> CreateStatusOptions()
        {
            var options = new List<ITrackingLibraryStatusOption>();
            foreach (var status in GetOrderedStatuses())
            {
                options.Add(new TrackingLibraryStatusOption<TStatus>(status, GetStatusDisplayName(status)));
            }

            return options;
        }

        protected abstract IEnumerable<TStatus> GetOrderedStatuses();

        protected abstract string GetStatusDisplayName(TStatus status);

        protected abstract TrackingLibraryEntryViewModel CreateEntryViewModel(TMedia media, TStatus status);

        protected abstract string GetTrackingKey(TMedia media);

        protected abstract TrackingLibraryStatisticsSummary CreateStatisticsSummary(IReadOnlyDictionary<TStatus, IReadOnlyList<TMedia>> lists);

        protected abstract Task OpenTrackingEditorAsync(object? parameter);

        protected virtual void HandleTrackingChanged(MediaTrackingChangedEventArgs<TTrackingInfo> e)
        {
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (!IsAuthenticated)
            {
                CancelActiveLoad();
                ClearSections();
                StatusMessage = ConnectAccountMessage;
                Statistics.Reset();
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(UserName));
                OnPropertyChanged(nameof(HasAnySeries));
                return;
            }

            CancelActiveLoad();

            var loadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _activeLoadCts = loadCts;

            IsLoading = true;
            StatusMessage = LoadingMessage;

            try
            {
                var lists = await _service.GetUserListsAsync(loadCts.Token).ConfigureAwait(true);
                UpdateSections(lists);
                StatusMessage = HasAnySeries ? null : EmptyLibraryMessage;
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation triggered by refresh or disposal.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load {ServiceDisplayName} library: {ex}");
                StatusMessage = LoadFailedMessage;
            }
            finally
            {
                if (ReferenceEquals(_activeLoadCts, loadCts))
                {
                    _activeLoadCts.Dispose();
                    _activeLoadCts = null;
                }

                IsLoading = false;
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(UserName));
                OnPropertyChanged(nameof(HasAnySeries));
            }
        }

        private void UpdateSections(IReadOnlyDictionary<TStatus, IReadOnlyList<TMedia>>? lists)
        {
            foreach (var section in _sections)
            {
                if (section.StatusValue is TStatus status && lists != null && lists.TryGetValue(status, out var items) && items != null)
                {
                    var entries = items.Select(item => CreateEntryViewModel(item, status)).ToList();
                    section.ReplaceItems(entries);
                }
                else
                {
                    section.ReplaceItems(Array.Empty<TrackingLibraryEntryViewModel>());
                }
            }

            var summary = CreateStatisticsSummary(lists ?? new Dictionary<TStatus, IReadOnlyList<TMedia>>());
            if (summary.TotalSeries > 0)
            {
                Statistics.Update(summary);
            }
            else
            {
                Statistics.Reset();
            }

            OnPropertyChanged(nameof(HasAnySeries));
        }

        private void ClearSections()
        {
            foreach (var section in _sections)
            {
                section.ReplaceItems(Array.Empty<TrackingLibraryEntryViewModel>());
            }
        }

        private async Task ChangeStatusAsync(object? parameter)
        {
            if (!SupportsStatusChanges)
            {
                return;
            }

            if (parameter is null)
            {
                return;
            }

            TrackingLibraryEntryViewModel? entry = null;
            TStatus? status = null;

            switch (parameter)
            {
                case TrackingStatusChangeParameter typed:
                    entry = typed.Entry;
                    status = ConvertStatus(typed.Status);
                    break;
                case object[] values when values.Length >= 2 && values[0] is TrackingLibraryEntryViewModel entryValue:
                    entry = entryValue;
                    status = ConvertStatus(values[1]);
                    break;
            }

            if (entry == null || status == null)
            {
                return;
            }

            if (!IsAuthenticated)
            {
                MessageBox.Show(ConnectAccountMessage, ServiceDisplayName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (entry.Media is not TMedia media)
            {
                return;
            }

            var trackingKey = GetTrackingKey(media);
            var resolvedStatus = status.Value;
            var updated = false;

            try
            {
                IsLoading = true;
                StatusMessage = UpdateStatusMessage(resolvedStatus);

                await _service.TrackSeriesAsync(
                    trackingKey,
                    media,
                    resolvedStatus,
                    null,
                    null,
                    _cts.Token).ConfigureAwait(true);

                updated = true;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = UpdateStatusCancelledMessage;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, ServiceDisplayName, MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusMessage = ex.Message;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update {ServiceDisplayName} status: {ex}");
                MessageBox.Show(UpdateStatusFailureMessage, ServiceDisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = UpdateStatusFailureMessage;
            }
            finally
            {
                IsLoading = false;
            }

            if (updated)
            {
                await LoadAsync().ConfigureAwait(true);
                StatusMessage = UpdateStatusSuccessMessage(resolvedStatus);
            }
        }

        private void OpenSeries(object? parameter)
        {
            if (parameter is not TrackingLibraryEntryViewModel entry)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.SiteUrl))
            {
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = entry.SiteUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open {ServiceDisplayName} entry: {ex}");
                StatusMessage = $"Unable to open the {ServiceDisplayName} page.";
            }
        }

        private void OnAuthenticationChanged(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(IsAuthenticated));
                    OnPropertyChanged(nameof(UserName));
                    _ = LoadAsync();
                }));
            }
            else
            {
                OnPropertyChanged(nameof(IsAuthenticated));
                OnPropertyChanged(nameof(UserName));
                _ = LoadAsync();
            }
        }

        private void OnMediaTrackingChanged(object? sender, MediaTrackingChangedEventArgs<TTrackingInfo> e)
        {
            if (_disposed)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => HandleTrackingChanged(e)));
            }
            else
            {
                HandleTrackingChanged(e);
            }
        }

        private void CancelActiveLoad()
        {
            var existing = Interlocked.Exchange(ref _activeLoadCts, null);
            if (existing != null)
            {
                existing.Cancel();
                existing.Dispose();
            }
        }

        private static TStatus? ConvertStatus(object? value)
        {
            if (value is TStatus typed)
            {
                return typed;
            }

            if (value is IConvertible convertible)
            {
                try
                {
                    return (TStatus)Enum.ToObject(typeof(TStatus), convertible);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.AuthenticationChanged -= OnAuthenticationChanged;
            _service.MediaTrackingChanged -= OnMediaTrackingChanged;
            CancelActiveLoad();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}