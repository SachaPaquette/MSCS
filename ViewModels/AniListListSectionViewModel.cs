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

        public void Upsert(AniListMedia media)
        {
            if (media == null)
            {
                return;
            }

            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i]?.Id == media.Id)
                {
                    Items[i] = media;
                    RepositionItem(i);
                    return;
                }
            }

            var insertIndex = GetInsertIndex(media);
            Items.Insert(insertIndex, media);
        }

        public bool RemoveById(int mediaId)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i]?.Id == mediaId)
                {
                    Items.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private void RepositionItem(int index)
        {
            if (index < 0 || index >= Items.Count)
            {
                return;
            }

            var item = Items[index];
            Items.RemoveAt(index);

            var insertIndex = GetInsertIndex(item);
            Items.Insert(insertIndex, item);
        }

        private int GetInsertIndex(AniListMedia? media)
        {
            var updatedAt = media?.UserUpdatedAt ?? DateTimeOffset.MinValue;
            for (var i = 0; i < Items.Count; i++)
            {
                var other = Items[i];
                var otherUpdated = other?.UserUpdatedAt ?? DateTimeOffset.MinValue;
                if (updatedAt >= otherUpdated)
                {
                    return i;
                }
            }

            return Items.Count;
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(Header));
        }
    }
}