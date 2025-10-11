using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using MSCS.Enums;
using MSCS.Helpers;
using MSCS.Models;

namespace MSCS.ViewModels
{
    public class AniListListSectionViewModel : BaseViewModel
    {
        private bool _isExpanded = true;

        public AniListListSectionViewModel(AniListMediaListStatus status)
        {
            Status = status;
            Items = new ObservableCollection<AniListMedia>();
            Items.CollectionChanged += OnItemsCollectionChanged;
        }

        public AniListMediaListStatus Status { get; }

        public string Title => Status.ToDisplayString();

        public ObservableCollection<AniListMedia> Items { get; }

        public int ItemCount => Items.Count;

        public bool HasItems => Items.Count > 0;

        public string Header => $"{Title} ({ItemCount})";

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public void ReplaceItems(IEnumerable<AniListMedia> items)
        {
            Items.CollectionChanged -= OnItemsCollectionChanged;
            Items.Clear();
            foreach (var item in items ?? Enumerable.Empty<AniListMedia>())
            {
                Items.Add(item);
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