using MSCS.Commands;
using MSCS.Enums;
using MSCS.Interfaces;
using MSCS.Models;
using MSCS.Services;
using MSCS.Services.Kitsu;
using MSCS.Services.MyAnimeList;
using MSCS.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MSCS.ViewModels
{
    public class ReaderTrackingCoordinator : IDisposable
    {
        private readonly MediaTrackingServiceRegistry? _trackingRegistry;
        private readonly ObservableCollection<TrackingProvider> _trackingProviders = new();
        private readonly Func<string?> _getMangaTitle;
        private readonly Func<Chapter?> _getSelectedChapter;
        private readonly Func<Chapter?, int> _progressCalculator;
        private readonly PropertyChangedEventHandler _providerPropertyChangedHandler;
        private TrackingProvider? _activeTrackingProvider;
        private static readonly ICommand DisabledCommand = new RelayCommand(_ => { }, _ => false);

        public ReaderTrackingCoordinator(
            MediaTrackingServiceRegistry? trackingRegistry,
            Func<string?> getMangaTitle,
            Func<Chapter?> getSelectedChapter,
            Func<Chapter?, int> progressCalculator)
        {
            _trackingRegistry = trackingRegistry;
            _getMangaTitle = getMangaTitle ?? throw new ArgumentNullException(nameof(getMangaTitle));
            _getSelectedChapter = getSelectedChapter ?? throw new ArgumentNullException(nameof(getSelectedChapter));
            _progressCalculator = progressCalculator ?? throw new ArgumentNullException(nameof(progressCalculator));
            _providerPropertyChangedHandler = OnTrackingProviderPropertyChanged;
        }

        public event EventHandler? StateChanged;

        public ObservableCollection<TrackingProvider> TrackingProviders => _trackingProviders;

        public TrackingProvider? ActiveProvider
        {
            get => _activeTrackingProvider;
            set
            {
                if (ReferenceEquals(_activeTrackingProvider, value))
                {
                    return;
                }

                _activeTrackingProvider = value;
                _activeTrackingProvider?.OnActivated();
                NotifyStateChanged();
            }
        }

        public bool HasTrackingProviders => _trackingProviders.Count > 0;
        public bool HasMultipleTrackingProviders => _trackingProviders.Count > 1;
        public bool IsTrackingAvailable => ActiveProvider?.IsAvailable == true;
        public string? ActiveTrackerName => ActiveProvider?.DisplayName;
        public bool IsTracked => ActiveProvider?.IsTracked ?? false;
        public ICommand TrackCommand => ActiveProvider?.TrackCommand ?? DisabledCommand;
        public ICommand OpenInBrowserCommand => ActiveProvider?.OpenInBrowserCommand ?? DisabledCommand;
        public ICommand RemoveTrackingCommand => ActiveProvider?.RemoveTrackingCommand ?? DisabledCommand;
        public string TrackButtonText => ActiveProvider?.TrackButtonText ?? "Track";
        public string OpenTrackerButtonText => ActiveTrackerName != null ? $"Open {ActiveTrackerName}" : "Open tracker";
        public string RemoveTrackerButtonText => ActiveTrackerName != null ? $"Remove {ActiveTrackerName}" : "Remove tracking";
        public string? TrackingStatusDisplay => ActiveProvider?.StatusDisplay;
        public string? TrackingProgressDisplay => ActiveProvider?.ProgressDisplay;
        public string? TrackingScoreDisplay => ActiveProvider?.ScoreDisplay;
        public string? TrackingUpdatedDisplay => ActiveProvider?.UpdatedDisplay;
        public bool CanOpenTracker => ActiveProvider?.CanOpenInBrowser ?? false;

        internal string? MangaTitle => _getMangaTitle();
        internal Chapter? SelectedChapter => _getSelectedChapter();
        internal int GetProgressForChapter(Chapter? chapter) => _progressCalculator(chapter);

        public void InitializeTrackingProviders()
        {
            foreach (var provider in _trackingProviders.ToList())
            {
                provider.PropertyChanged -= _providerPropertyChangedHandler;
                provider.Dispose();
            }

            _trackingProviders.Clear();

            if (_trackingRegistry != null)
            {
                foreach (var service in _trackingRegistry.Services)
                {
                    switch (service)
                    {
                        case IAniListService aniListService:
                            var aniListProvider = new AniListTrackingProvider(this, aniListService);
                            aniListProvider.PropertyChanged += _providerPropertyChangedHandler;
                            _trackingProviders.Add(aniListProvider);
                            break;
                        case MyAnimeListService myAnimeListService:
                            var myAnimeListProvider = new MyAnimeListTrackingProvider(this, myAnimeListService);
                            myAnimeListProvider.PropertyChanged += _providerPropertyChangedHandler;
                            _trackingProviders.Add(myAnimeListProvider);
                            break;
                        case KitsuService kitsuService:
                            var kitsuProvider = new KitsuTrackingProvider(this, kitsuService);
                            kitsuProvider.PropertyChanged += _providerPropertyChangedHandler;
                            _trackingProviders.Add(kitsuProvider);
                            break;
                    }
                }
            }

            if (_trackingProviders.Count == 0)
            {
                if (ActiveProvider != null)
                {
                    ActiveProvider = null;
                }
                else
                {
                    NotifyStateChanged();
                }
            }
            else if (!_trackingProviders.Contains(ActiveProvider))
            {
                ActiveProvider = _trackingProviders.FirstOrDefault();
            }
            else
            {
                NotifyStateChanged();
            }

            foreach (var provider in _trackingProviders)
            {
                _ = provider.RefreshAsync();
            }
        }

        public virtual void Dispose()
        {
            foreach (var provider in _trackingProviders)
            {
                provider.PropertyChanged -= _providerPropertyChangedHandler;
                provider.Dispose();
            }

            _trackingProviders.Clear();
        }

        private void OnTrackingProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, ActiveProvider))
            {
                return;
            }

            NotifyStateChanged();
        }

        private void NotifyTrackingProperties()
        {
            NotifyStateChanged();
        }

        internal void NotifyStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
        }


        internal bool CanShowTrackingDialog(TrackingProvider? provider)
        {
            return provider?.CanOpenTrackingDialog == true && !string.IsNullOrWhiteSpace(MangaTitle);
        }

        internal async Task ShowTrackingDialogAsync(TrackingProvider initiatingProvider)
        {
            if (initiatingProvider == null)
            {
                return;
            }

            var providerViewModels = new List<(TrackingProvider Provider, ITrackingDialogViewModel ViewModel)>();

            var initiatingViewModel = await initiatingProvider
                .CreateTrackingDialogViewModelAsync(showErrors: true)
                .ConfigureAwait(true);

            if (initiatingViewModel != null)
            {
                providerViewModels.Add((initiatingProvider, initiatingViewModel));
            }

            foreach (var provider in _trackingProviders)
            {
                if (ReferenceEquals(provider, initiatingProvider) || !provider.CanOpenTrackingDialog)
                {
                    continue;
                }

                var viewModel = await provider
                    .CreateTrackingDialogViewModelAsync(showErrors: false)
                    .ConfigureAwait(true);

                if (viewModel != null)
                {
                    providerViewModels.Add((provider, viewModel));
                }
            }

            if (providerViewModels.Count == 0)
            {
                if (initiatingViewModel == null)
                {
                    MessageBox.Show(
                        "No tracking providers are available for this series.",
                        "Tracking",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            var dialogViewModel = new TrackingWindowViewModel(
                initiatingProvider.IsTracked ? "Manage tracking" : "Add tracking",
                providerViewModels.Select(p => p.ViewModel));

            dialogViewModel.SelectedProvider = initiatingViewModel ?? providerViewModels[0].ViewModel;

            var dialog = new TrackingWindow(dialogViewModel);
            if (Application.Current?.MainWindow != null)
            {
                dialog.Owner = Application.Current.MainWindow;
            }

            var result = dialog.ShowDialog();
            var confirmed = result == true;

            foreach (var entry in providerViewModels)
            {
                await entry.Provider
                    .OnTrackingDialogClosedAsync(entry.ViewModel, confirmed)
                    .ConfigureAwait(true);
            }
        }

        public Task UpdateTrackingProgressAsync()
        {
            return ActiveProvider?.UpdateProgressAsync() ?? Task.CompletedTask;
        }

        private static string FormatEnumValue<TEnum>(TEnum value)
            where TEnum : struct, Enum
        {
            var name = value.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(name.Length * 2);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(c));
            }

            if (builder.Length > 0)
            {
                builder[0] = char.ToUpperInvariant(builder[0]);
            }

            return builder.ToString();
        }

        public abstract class TrackingProvider : BaseViewModel, IDisposable
        {
            protected TrackingProvider(ReaderTrackingCoordinator owner, IMediaTrackingService service)
            {
                Owner = owner ?? throw new ArgumentNullException(nameof(owner));
                Service = service ?? throw new ArgumentNullException(nameof(service));
                _manageTrackingCommand = new AsyncRelayCommand(
                 _ => Owner.ShowTrackingDialogAsync(this),
                 _ => Owner.CanShowTrackingDialog(this));
            }

            protected ReaderTrackingCoordinator Owner { get; }
            protected IMediaTrackingService Service { get; }
            private readonly AsyncRelayCommand _manageTrackingCommand;
            public string ServiceId => Service.ServiceId;
            public virtual string DisplayName => Service.DisplayName;
            public virtual bool IsAvailable => true;
            public abstract bool IsTracked { get; }
            public virtual string TrackButtonText => IsTracked ? "Manage" : "Track";
            public virtual string? StatusDisplay => null;
            public virtual string? ProgressDisplay => null;
            public virtual string? ScoreDisplay => null;
            public virtual string? UpdatedDisplay => null;
            public virtual bool CanOpenInBrowser => false;
            public virtual bool CanOpenTrackingDialog => IsAvailable;
            public virtual ICommand TrackCommand => _manageTrackingCommand;
            public abstract ICommand OpenInBrowserCommand { get; }
            public abstract ICommand RemoveTrackingCommand { get; }

            internal virtual Task<ITrackingDialogViewModel?> CreateTrackingDialogViewModelAsync(bool showErrors)
            {
                return Task.FromResult<ITrackingDialogViewModel?>(null);
            }

            internal virtual Task OnTrackingDialogClosedAsync(ITrackingDialogViewModel viewModel, bool confirmed)
            {
                return Task.CompletedTask;
            }

            public virtual Task UpdateProgressAsync() => Task.CompletedTask;
            public virtual Task RefreshAsync() => Task.CompletedTask;
            public virtual void OnActivated() { }
            public virtual void Dispose() { }
        }

        public sealed class AniListTrackingProvider : TrackingProvider
        {
            private readonly IAniListService _aniListService;
            private AniListTrackingInfo? _trackingInfo;
            private readonly RelayCommand _openInBrowserCommand;
            private readonly AsyncRelayCommand _removeTrackingCommand;
            private AniListTrackingViewModel? _dialogViewModel;

            public AniListTrackingProvider(ReaderTrackingCoordinator owner, IAniListService aniListService)
                : base(owner, aniListService)
            {
                _aniListService = aniListService ?? throw new ArgumentNullException(nameof(aniListService));

                _openInBrowserCommand = new RelayCommand(_ => OpenInBrowser(), _ => CanOpenInBrowser);
                _removeTrackingCommand = new AsyncRelayCommand(_ => RemoveTrackingAsync(), _ => _trackingInfo != null);

                WeakEventManager<IAniListService, AniListTrackingChangedEventArgs>.AddHandler(
                    _aniListService,
                    nameof(IAniListService.TrackingChanged),
                    OnTrackingChanged);

                if (!string.IsNullOrWhiteSpace(owner.MangaTitle) &&
                    _aniListService.TryGetTracking(owner.MangaTitle, out var info))
                {
                    _trackingInfo = info;
                }

                NotifyStateChanged();
            }

            public override bool IsTracked => _trackingInfo != null;
            public override string TrackButtonText => IsTracked ? "Manage" : "Track";
            public override string? StatusDisplay => _trackingInfo?.StatusDisplay;
            public override bool CanOpenTrackingDialog => _aniListService.IsAuthenticated;
            public override string? ProgressDisplay
            {
                get
                {
                    if (_trackingInfo?.Progress is null or <= 0)
                    {
                        return null;
                    }

                    return _trackingInfo.TotalChapters.HasValue
                        ? string.Format(CultureInfo.CurrentCulture, "Progress {0}/{1}", _trackingInfo.Progress, _trackingInfo.TotalChapters)
                        : string.Format(CultureInfo.CurrentCulture, "Progress {0}", _trackingInfo.Progress);
                }
            }

            public override string? ScoreDisplay => _trackingInfo?.Score is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Score {0:0}", _trackingInfo.Score)
                : null;

            public override string? UpdatedDisplay => _trackingInfo?.UpdatedAt.HasValue == true
                ? string.Format(CultureInfo.CurrentCulture, "Updated {0:g}", _trackingInfo.UpdatedAt.Value.ToLocalTime())
                : null;

            public override bool CanOpenInBrowser => _trackingInfo != null && !string.IsNullOrWhiteSpace(_trackingInfo.SiteUrl);
            public override ICommand OpenInBrowserCommand => _openInBrowserCommand;
            public override ICommand RemoveTrackingCommand => _removeTrackingCommand;

            internal override Task<ITrackingDialogViewModel?> CreateTrackingDialogViewModelAsync(bool showErrors)
            {
                if (string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    if (showErrors)
                    {
                        MessageBox.Show(
                            "Unable to determine the manga title for tracking.",
                            "AniList",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                }
                else if (!_aniListService.IsAuthenticated)
                {
                    if (showErrors)
                    {
                        MessageBox.Show(
                            "Connect your AniList account from the Settings tab before tracking a series.",
                            "AniList",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    var suggestedProgress = Owner.GetProgressForChapter(Owner.SelectedChapter);
                    var initialQuery = _trackingInfo?.Title ?? Owner.MangaTitle;
                    _dialogViewModel = new AniListTrackingViewModel(
                        _aniListService,
                        Owner.MangaTitle,
                        initialQuery,
                        _trackingInfo,
                        suggestedProgress > 0 ? suggestedProgress : null);
                    return Task.FromResult<ITrackingDialogViewModel?>(_dialogViewModel);
                }

                return Task.FromResult<ITrackingDialogViewModel?>(null);
            }

            internal override async Task OnTrackingDialogClosedAsync(ITrackingDialogViewModel viewModel, bool confirmed)
            {
                if (!ReferenceEquals(viewModel, _dialogViewModel))
                {
                    return;
                }

                try
                {
                    if (confirmed && _dialogViewModel?.TrackingInfo != null)
                    {
                        _trackingInfo = _dialogViewModel.TrackingInfo;
                        NotifyStateChanged();
                        await UpdateProgressAsync().ConfigureAwait(true);
                    }
                }
                finally
                {
                    _dialogViewModel = null;
                }
            }

            public override async Task UpdateProgressAsync()
            {
                if (_trackingInfo == null || string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                var progress = Owner.GetProgressForChapter(Owner.SelectedChapter);
                if (progress <= 0)
                {
                    return;
                }

                try
                {
                    await _aniListService.UpdateProgressAsync(Owner.MangaTitle, progress).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to update AniList progress: {ex.Message}");
                }
            }

            public override async Task RefreshAsync()
            {
                if (string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                try
                {
                    var refreshed = await _aniListService.RefreshTrackingAsync(Owner.MangaTitle).ConfigureAwait(true);
                    if (refreshed != null)
                    {
                        _trackingInfo = refreshed;
                        NotifyStateChanged();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to refresh AniList tracking: {ex.Message}");
                }
            }

            public override void OnActivated()
            {
                NotifyStateChanged();
            }

            public override void Dispose()
            {
                WeakEventManager<IAniListService, AniListTrackingChangedEventArgs>.RemoveHandler(
                    _aniListService,
                    nameof(IAniListService.TrackingChanged),
                    OnTrackingChanged);
            }

            private void OpenInBrowser()
            {
                var url = _trackingInfo?.SiteUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to open AniList: {ex.Message}", "AniList", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            private async Task RemoveTrackingAsync()
            {
                if (_trackingInfo == null || string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                var confirmation = MessageBox.Show(
                    "Remove AniList tracking for this series?",
                    "AniList",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    var removed = await _aniListService.UntrackSeriesAsync(Owner.MangaTitle).ConfigureAwait(true);
                    if (removed)
                    {
                        _trackingInfo = null;
                        NotifyStateChanged();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to remove AniList tracking: {ex.Message}", "AniList", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            private void OnTrackingChanged(object? sender, AniListTrackingChangedEventArgs e)
            {
                var matchesTitle = !string.IsNullOrEmpty(e.MangaTitle) &&
                                   string.Equals(e.MangaTitle, Owner.MangaTitle, StringComparison.OrdinalIgnoreCase);
                var matchesMediaId = e.MediaId != 0 && _trackingInfo?.MediaId == e.MediaId;

                if (!matchesTitle && !matchesMediaId && !string.IsNullOrEmpty(e.MangaTitle))
                {
                    return;
                }

                if (string.IsNullOrEmpty(e.MangaTitle) && !matchesMediaId)
                {
                    if (_aniListService.TryGetTracking(Owner.MangaTitle, out var refreshed))
                    {
                        _trackingInfo = refreshed;
                    }
                    else
                    {
                        _trackingInfo = null;
                    }

                    NotifyStateChanged();
                    return;
                }

                if (e.TrackingInfo != null)
                {
                    _trackingInfo = e.TrackingInfo;
                    NotifyStateChanged();
                }
                else if (_aniListService.TryGetTracking(Owner.MangaTitle, out var info))
                {
                    _trackingInfo = info;
                    NotifyStateChanged();
                }
                else
                {
                    _trackingInfo = null;
                    NotifyStateChanged();
                }
            }

            private void NotifyStateChanged()
            {
                OnPropertyChanged(nameof(IsTracked));
                OnPropertyChanged(nameof(TrackButtonText));
                OnPropertyChanged(nameof(CanOpenTrackingDialog));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ProgressDisplay));
                OnPropertyChanged(nameof(ScoreDisplay));
                OnPropertyChanged(nameof(UpdatedDisplay));
                OnPropertyChanged(nameof(CanOpenInBrowser));
                CommandManager.InvalidateRequerySuggested();
                Owner.NotifyStateChanged();
            }
        }

        public sealed class MyAnimeListTrackingProvider : TrackingProvider
        {
            private readonly MyAnimeListService _myAnimeListService;
            private MyAnimeListTrackingInfo? _trackingInfo;
            private readonly RelayCommand _openInBrowserCommand;
            private readonly AsyncRelayCommand _removeTrackingCommand;
            private MyAnimeListTrackingViewModel? _dialogViewModel;

            public MyAnimeListTrackingProvider(ReaderTrackingCoordinator owner, MyAnimeListService myAnimeListService)
                : base(owner, myAnimeListService)
            {
                _myAnimeListService = myAnimeListService ?? throw new ArgumentNullException(nameof(myAnimeListService));

                _openInBrowserCommand = new RelayCommand(_ => OpenInBrowser(), _ => CanOpenInBrowser);
                _removeTrackingCommand = new AsyncRelayCommand(_ => RemoveTrackingAsync(), _ => _trackingInfo != null);

                WeakEventManager<MyAnimeListService, MediaTrackingChangedEventArgs<MyAnimeListTrackingInfo>>.AddHandler(
                    _myAnimeListService,
                    nameof(MyAnimeListService.MediaTrackingChanged),
                    OnTrackingChanged);

                WeakEventManager<MyAnimeListService, EventArgs>.AddHandler(
                    _myAnimeListService,
                    nameof(MyAnimeListService.AuthenticationChanged),
                    OnAuthenticationChanged);

                if (!string.IsNullOrWhiteSpace(owner.MangaTitle) &&
                    _myAnimeListService.TryGetTracking(owner.MangaTitle, out var info))
                {
                    _trackingInfo = info;
                }

                NotifyStateChanged();
            }

            public override bool IsAvailable => _myAnimeListService.IsAuthenticated || _trackingInfo != null;

            public override bool IsTracked => _trackingInfo != null;

            public override string TrackButtonText => IsTracked ? "Manage" : "Track";

            public override string? StatusDisplay => _trackingInfo?.Status is { } status
                ? FormatEnumValue(status)
                : null;

            public override string? ProgressDisplay
            {
                get
                {
                    if (_trackingInfo?.Progress is null or <= 0)
                    {
                        return null;
                    }

                    return _trackingInfo.TotalChapters.HasValue
                        ? string.Format(CultureInfo.CurrentCulture, "Progress {0}/{1}", _trackingInfo.Progress, _trackingInfo.TotalChapters)
                        : string.Format(CultureInfo.CurrentCulture, "Progress {0}", _trackingInfo.Progress);
                }
            }

            public override string? ScoreDisplay => _trackingInfo?.Score is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Score {0:0.#}", _trackingInfo.Score)
                : null;

            public override string? UpdatedDisplay => _trackingInfo?.UpdatedAt.HasValue == true
                ? string.Format(CultureInfo.CurrentCulture, "Updated {0:g}", _trackingInfo!.UpdatedAt!.Value.ToLocalTime())
                : null;

            public override bool CanOpenInBrowser => _trackingInfo != null && !string.IsNullOrWhiteSpace(_trackingInfo.SiteUrl);


            public override ICommand OpenInBrowserCommand => _openInBrowserCommand;

            public override ICommand RemoveTrackingCommand => _removeTrackingCommand;


            internal override async Task<ITrackingDialogViewModel?> CreateTrackingDialogViewModelAsync(bool showErrors)
            {
                if (string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    if (showErrors)
                    {
                        MessageBox.Show(
                            "Unable to determine the manga title for tracking.",
                            "MyAnimeList",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }

                    return null;
                }

                if (!_myAnimeListService.IsAuthenticated && showErrors && _trackingInfo == null)
                {
                    var authenticated = await _myAnimeListService
                        .AuthenticateAsync(Application.Current?.MainWindow)
                        .ConfigureAwait(true);
                    if (!authenticated)
                    {
                        return null;
                    }
                }

                var initialQuery = _trackingInfo?.Title ?? Owner.MangaTitle;
                var suggestedProgress = Owner.GetProgressForChapter(Owner.SelectedChapter);
                _dialogViewModel = new MyAnimeListTrackingViewModel(
                    _myAnimeListService,
                    Owner.MangaTitle,
                    initialQuery,
                    _trackingInfo,
                    suggestedProgress > 0 ? suggestedProgress : null);

                return _dialogViewModel;
            }

            internal override async Task OnTrackingDialogClosedAsync(ITrackingDialogViewModel viewModel, bool confirmed)
            {
                if (!ReferenceEquals(viewModel, _dialogViewModel))
                {
                    return;
                }

                try
                {
                    if (confirmed && _dialogViewModel?.TrackingInfo != null)
                    {
                        _trackingInfo = _dialogViewModel.TrackingInfo;
                        NotifyStateChanged();
                        await UpdateProgressAsync().ConfigureAwait(true);
                    }
                }
                finally
                {
                    _dialogViewModel = null;
                }
            }

            public override async Task UpdateProgressAsync()
            {
                if (_trackingInfo == null || string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                var progress = Owner.GetProgressForChapter(Owner.SelectedChapter);
                if (progress <= 0)
                {
                    return;
                }

                try
                {
                    await _myAnimeListService.UpdateProgressAsync(Owner.MangaTitle, progress).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to update MyAnimeList progress: {ex.Message}");
                }
            }

            public override async Task RefreshAsync()
            {
                if (string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                try
                {
                    var refreshed = await _myAnimeListService.RefreshTrackingAsync(Owner.MangaTitle).ConfigureAwait(true);
                    if (refreshed != null)
                    {
                        _trackingInfo = refreshed;
                        NotifyStateChanged();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to refresh MyAnimeList tracking: {ex.Message}");
                }
            }

            public override void OnActivated()
            {
                NotifyStateChanged();
            }

            public override void Dispose()
            {
                WeakEventManager<MyAnimeListService, MediaTrackingChangedEventArgs<MyAnimeListTrackingInfo>>.RemoveHandler(
                    _myAnimeListService,
                    nameof(MyAnimeListService.MediaTrackingChanged),
                    OnTrackingChanged);

                WeakEventManager<MyAnimeListService, EventArgs>.RemoveHandler(
                    _myAnimeListService,
                    nameof(MyAnimeListService.AuthenticationChanged),
                    OnAuthenticationChanged);
            }

            private void OpenInBrowser()
            {
                var url = _trackingInfo?.SiteUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to open MyAnimeList: {ex.Message}", "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            private async Task RemoveTrackingAsync()
            {
                if (_trackingInfo == null || string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                var confirmation = MessageBox.Show(
                    "Remove MyAnimeList tracking for this series?",
                    "MyAnimeList",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    var removed = await _myAnimeListService.UntrackSeriesAsync(Owner.MangaTitle).ConfigureAwait(true);
                    if (removed)
                    {
                        _trackingInfo = null;
                        NotifyStateChanged();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to remove MyAnimeList tracking: {ex.Message}", "MyAnimeList", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            private void OnTrackingChanged(object? sender, MediaTrackingChangedEventArgs<MyAnimeListTrackingInfo> e)
            {
                if (!string.IsNullOrEmpty(e.SeriesTitle) &&
                    !string.Equals(e.SeriesTitle, Owner.MangaTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (e.TrackingInfo != null)
                {
                    _trackingInfo = e.TrackingInfo;
                }
                else if (_myAnimeListService.TryGetTracking(Owner.MangaTitle, out var info))
                {
                    _trackingInfo = info;
                }
                else
                {
                    _trackingInfo = null;
                }

                NotifyStateChanged();
            }

            private void OnAuthenticationChanged(object? sender, EventArgs e)
            {
                NotifyStateChanged();
            }

            private void NotifyStateChanged()
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(IsTracked));
                OnPropertyChanged(nameof(TrackButtonText));
                OnPropertyChanged(nameof(CanOpenTrackingDialog));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ProgressDisplay));
                OnPropertyChanged(nameof(ScoreDisplay));
                OnPropertyChanged(nameof(UpdatedDisplay));
                OnPropertyChanged(nameof(CanOpenInBrowser));
                CommandManager.InvalidateRequerySuggested();
                Owner.NotifyStateChanged();
            }
        }

        public sealed class KitsuTrackingProvider : TrackingProvider
        {
            private readonly KitsuService _kitsuService;
            private KitsuTrackingInfo? _trackingInfo;
            private readonly AsyncRelayCommand _trackCommand;
            private readonly RelayCommand _openInBrowserCommand;
            private readonly AsyncRelayCommand _removeTrackingCommand;

            public KitsuTrackingProvider(ReaderTrackingCoordinator owner, KitsuService kitsuService)
                : base(owner, kitsuService)
            {
                _kitsuService = kitsuService ?? throw new ArgumentNullException(nameof(kitsuService));

                _trackCommand = new AsyncRelayCommand(_ => TrackAsync());
                _openInBrowserCommand = new RelayCommand(_ => OpenInBrowser(), _ => CanOpenInBrowser);
                _removeTrackingCommand = new AsyncRelayCommand(_ => RemoveTrackingAsync(), _ => _trackingInfo != null);

                WeakEventManager<KitsuService, MediaTrackingChangedEventArgs<KitsuTrackingInfo>>.AddHandler(
                    _kitsuService,
                    nameof(KitsuService.MediaTrackingChanged),
                    OnTrackingChanged);

                WeakEventManager<KitsuService, EventArgs>.AddHandler(
                    _kitsuService,
                    nameof(KitsuService.AuthenticationChanged),
                    OnAuthenticationChanged);

                if (!string.IsNullOrWhiteSpace(owner.MangaTitle) &&
                    _kitsuService.TryGetTracking(owner.MangaTitle, out var info))
                {
                    _trackingInfo = info;
                }

                NotifyStateChanged();
            }

            public override bool IsAvailable => _kitsuService.IsAuthenticated || _trackingInfo != null;

            public override bool IsTracked => _trackingInfo != null;

            public override string TrackButtonText => IsTracked ? "Manage" : "Track";

            public override string? StatusDisplay => _trackingInfo?.Status is { } status
                ? FormatEnumValue(status)
                : null;

            public override string? ProgressDisplay
            {
                get
                {
                    if (_trackingInfo?.Progress is null or <= 0)
                    {
                        return null;
                    }

                    return _trackingInfo.TotalChapters.HasValue
                        ? string.Format(CultureInfo.CurrentCulture, "Progress {0}/{1}", _trackingInfo.Progress, _trackingInfo.TotalChapters)
                        : string.Format(CultureInfo.CurrentCulture, "Progress {0}", _trackingInfo.Progress);
                }
            }

            public override string? ScoreDisplay => _trackingInfo?.Score is > 0
                ? string.Format(CultureInfo.CurrentCulture, "Score {0:0.#}", _trackingInfo.Score)
                : null;

            public override string? UpdatedDisplay => _trackingInfo?.UpdatedAt.HasValue == true
                ? string.Format(CultureInfo.CurrentCulture, "Updated {0:g}", _trackingInfo!.UpdatedAt!.Value.ToLocalTime())
                : null;

            public override bool CanOpenInBrowser => _trackingInfo != null && !string.IsNullOrWhiteSpace(_trackingInfo.SiteUrl);

            public override ICommand TrackCommand => _trackCommand;

            public override ICommand OpenInBrowserCommand => _openInBrowserCommand;

            public override ICommand RemoveTrackingCommand => _removeTrackingCommand;

            public override async Task UpdateProgressAsync()
            {
                if (_trackingInfo == null || string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                var progress = Owner.GetProgressForChapter(Owner.SelectedChapter);
                if (progress <= 0)
                {
                    return;
                }

                try
                {
                    await _kitsuService.UpdateProgressAsync(Owner.MangaTitle, progress).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to update Kitsu progress: {ex.Message}");
                }
            }

            public override async Task RefreshAsync()
            {
                if (string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                try
                {
                    var refreshed = await _kitsuService.RefreshTrackingAsync(Owner.MangaTitle).ConfigureAwait(true);
                    if (refreshed != null)
                    {
                        _trackingInfo = refreshed;
                        NotifyStateChanged();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to refresh Kitsu tracking: {ex.Message}");
                }
            }

            public override void OnActivated()
            {
                NotifyStateChanged();
            }

            public override void Dispose()
            {
                WeakEventManager<KitsuService, MediaTrackingChangedEventArgs<KitsuTrackingInfo>>.RemoveHandler(
                    _kitsuService,
                    nameof(KitsuService.MediaTrackingChanged),
                    OnTrackingChanged);

                WeakEventManager<KitsuService, EventArgs>.RemoveHandler(
                    _kitsuService,
                    nameof(KitsuService.AuthenticationChanged),
                    OnAuthenticationChanged);
            }

            private async Task TrackAsync()
            {
                if (string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    MessageBox.Show("Unable to determine the manga title for tracking.", "Kitsu", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_trackingInfo != null)
                {
                    var refresh = MessageBox.Show(
                        "Refresh tracking details from Kitsu?",
                        "Kitsu",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (refresh == MessageBoxResult.Yes)
                    {
                        await RefreshAsync().ConfigureAwait(true);
                    }

                    return;
                }

                if (!_kitsuService.IsAuthenticated)
                {
                    var authenticated = await _kitsuService.AuthenticateAsync(Application.Current?.MainWindow).ConfigureAwait(true);
                    if (!authenticated)
                    {
                        return;
                    }
                }

                var query = Owner.MangaTitle;
                KitsuMedia? selectedMedia = null;
                try
                {
                    var results = await _kitsuService.SearchSeriesAsync(query).ConfigureAwait(true);
                    selectedMedia = results.FirstOrDefault(result => string.Equals(result.Title, query, StringComparison.OrdinalIgnoreCase))
                        ?? results.FirstOrDefault();

                    if (selectedMedia == null)
                    {
                        MessageBox.Show(
                            $"No Kitsu results were found for '{query}'.",
                            "Kitsu",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    if (!string.Equals(selectedMedia.Title, query, StringComparison.OrdinalIgnoreCase))
                    {
                        var confirmation = MessageBox.Show(
                            $"Track '{query}' as '{selectedMedia.Title}' on Kitsu?",
                            "Kitsu",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (confirmation != MessageBoxResult.Yes)
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to search Kitsu: {ex.Message}",
                        "Kitsu",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                try
                {
                    var progress = Owner.GetProgressForChapter(Owner.SelectedChapter);
                    if (selectedMedia == null)
                    {
                        return;
                    }

                    var tracking = await _kitsuService.TrackSeriesAsync(
                        Owner.MangaTitle,
                        selectedMedia,
                        status: KitsuLibraryStatus.Current,
                        progress: progress > 0 ? progress : null).ConfigureAwait(true);
                    _trackingInfo = tracking;
                    NotifyStateChanged();
                    await UpdateProgressAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Unable to track this series on Kitsu: {ex.Message}",
                        "Kitsu",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            private void OpenInBrowser()
            {
                var url = _trackingInfo?.SiteUrl;
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to open Kitsu: {ex.Message}", "Kitsu", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            private async Task RemoveTrackingAsync()
            {
                if (_trackingInfo == null || string.IsNullOrWhiteSpace(Owner.MangaTitle))
                {
                    return;
                }

                var confirmation = MessageBox.Show(
                    "Remove Kitsu tracking for this series?",
                    "Kitsu",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirmation != MessageBoxResult.Yes)
                {
                    return;
                }

                try
                {
                    var removed = await _kitsuService.UntrackSeriesAsync(Owner.MangaTitle).ConfigureAwait(true);
                    if (removed)
                    {
                        _trackingInfo = null;
                        NotifyStateChanged();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to remove Kitsu tracking: {ex.Message}", "Kitsu", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            private void OnTrackingChanged(object? sender, MediaTrackingChangedEventArgs<KitsuTrackingInfo> e)
            {
                if (!string.IsNullOrEmpty(e.SeriesTitle) &&
                    !string.Equals(e.SeriesTitle, Owner.MangaTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (e.TrackingInfo != null)
                {
                    _trackingInfo = e.TrackingInfo;
                }
                else if (_kitsuService.TryGetTracking(Owner.MangaTitle, out var info))
                {
                    _trackingInfo = info;
                }
                else
                {
                    _trackingInfo = null;
                }

                NotifyStateChanged();
            }

            private void OnAuthenticationChanged(object? sender, EventArgs e)
            {
                NotifyStateChanged();
            }

            private void NotifyStateChanged()
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(IsTracked));
                OnPropertyChanged(nameof(TrackButtonText));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ProgressDisplay));
                OnPropertyChanged(nameof(ScoreDisplay));
                OnPropertyChanged(nameof(UpdatedDisplay));
                OnPropertyChanged(nameof(CanOpenInBrowser));
                CommandManager.InvalidateRequerySuggested();
                Owner.NotifyStateChanged();
            }
        }
    }
}