using MSCS.Interfaces;
using MSCS.Models;
using MSCS.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using MSCS.Helpers;
namespace MSCS.ViewModels
{

    public class ReaderViewModel : BaseViewModel
    {
        private readonly List<ChapterImage> _allImages;  // full list of images
        private int _loadedCount = 0;
        public int remainingImages => _allImages.Count - _loadedCount;
        private double _widthFactor = Constants.DefaultWidthFactor;
        public double WidthFactor
        {
            get => _widthFactor;
            set => SetProperty(ref _widthFactor, Math.Clamp(value, 0.3, 1.0));
        }

        private double _maxPageWidth = Constants.DefaultMaxPageWidth;
        public double MaxPageWidth
        {
            get => _maxPageWidth;
            set => SetProperty(ref _maxPageWidth, Math.Max(400, value));
        }
        private bool _isSidebarOpen;
        public bool IsSidebarOpen
        {
            get => _isSidebarOpen;
            set => SetProperty(ref _isSidebarOpen, value);
        }
        public ObservableCollection<ChapterImage> ImageUrls { get; }

        public ReaderViewModel()
        {
            Debug.WriteLine("ReaderViewModel initialized with no images");
            _allImages = new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();
        }
        private readonly ChapterListViewModel _chapterListViewModel;
        private int _currentChapterIndex;

        public ReaderViewModel(
            ObservableCollection<ChapterImage>? imageUrls,
            string title,
            INavigationService navigationService,
            ChapterListViewModel chapterListViewModel,
            int currentChapterIndex)
        {
            Debug.WriteLine($"ReaderViewModel initialized with {imageUrls?.Count ?? 0} images");
            Debug.WriteLine($"Current chapter index {currentChapterIndex}");

            _chapterListViewModel = chapterListViewModel;
            _currentChapterIndex = currentChapterIndex;
            _allImages = imageUrls?.ToList() ?? new List<ChapterImage>();
            ImageUrls = new ObservableCollection<ChapterImage>();

            LoadMoreImages();  // initial batch
        }


        public void LoadMoreImages()
        {
            int remaining = _allImages.Count - _loadedCount;
            if (remaining <= 0) return;

            int countToLoad = Math.Min(Constants.DefaultLoadedBatchSize, remaining);
            for (int i = 0; i < countToLoad; i++)
            {
                ImageUrls.Add(_allImages[_loadedCount + i]);
            }
            _loadedCount += countToLoad;

            Debug.WriteLine($"Loaded {_loadedCount} / {_allImages.Count} images");
        }
        public async Task GoToNextChapterAsync()
        {
            Debug.WriteLine("Navigating to next chapter...");

            var nextImages = await _chapterListViewModel.GetNextChapterImages(_currentChapterIndex);

            if (nextImages != null)
            {
                _allImages.Clear();
                ImageUrls.Clear();
                _loadedCount = 0;
                _currentChapterIndex++;

                foreach (var img in nextImages)
                {
                    _allImages.Add(img);
                }

                LoadMoreImages();

                Debug.WriteLine("Next chapter loaded.");
            }
            else
            {
                Debug.WriteLine("No next chapter available.");
            }
        }

    }
}