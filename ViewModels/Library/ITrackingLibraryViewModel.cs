using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Windows.Input;
using MSCS.ViewModels.Library;

namespace MSCS.ViewModels
{
    public interface ITrackingLibraryViewModel : IDisposable
    {
        string ServiceId { get; }

        string ServiceDisplayName { get; }

        bool IsAuthenticated { get; }

        string? UserName { get; }

        bool IsLoading { get; }

        string? StatusMessage { get; }

        bool HasAnySeries { get; }

        ReadOnlyObservableCollection<TrackingLibrarySectionViewModel> Sections { get; }

        TrackingLibraryStatisticsViewModel Statistics { get; }

        IReadOnlyList<ITrackingLibraryStatusOption> StatusOptions { get; }

        bool SupportsStatusChanges { get; }

        bool SupportsTrackingEditor { get; }

        ICommand RefreshCommand { get; }

        ICommand OpenSeriesCommand { get; }

        ICommand ChangeStatusCommand { get; }

        ICommand EditTrackingCommand { get; }
    }
}