using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using MSCS.Helpers;

namespace MSCS.ViewModels
{
    public class TrackingLibrarySectionViewModel : BaseViewModel
    {
        private bool _isExpanded;

        public TrackingLibrarySectionViewModel(object statusValue, string title, bool isExpanded = true)
        {
            StatusValue = statusValue;
            Title = title;
            _isExpanded = isExpanded;
            Items = new ObservableCollection<TrackingLibraryEntryViewModel>();
            Items.CollectionChanged += OnItemsCollectionChanged;
        }

        public object StatusValue { get; }

        public string Title { get; }

        public ObservableCollection<TrackingLibraryEntryViewModel> Items { get; }

        public int ItemCount => Items.Count;

        public bool HasItems => Items.Count > 0;

        public string Header => $"{Title} ({ItemCount})";

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public void ReplaceItems(IEnumerable<TrackingLibraryEntryViewModel> items)
        {
            Items.CollectionChanged -= OnItemsCollectionChanged;
            Items.Clear();

            if (items != null)
            {
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }

            Items.CollectionChanged += OnItemsCollectionChanged;
            OnItemsCollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(Header));
        }
    }
}