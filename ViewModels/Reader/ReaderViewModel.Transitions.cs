using MSCS.Models;

namespace MSCS.ViewModels
{
    public partial class ReaderViewModel
    {
        public void SetChapterTransitionPreviewVisible(bool isVisible)
        {
            if (isVisible)
            {
                UpdateChapterTransitionPreview();
            }

            IsChapterTransitionPreviewVisible = isVisible;
        }

        private void UpdateChapterTransitionPreview()
        {
            var previous = GetChapterAtOffset(-1);
            var current = GetChapterAtOffset(0);
            var next = GetChapterAtOffset(1);

            PreviousChapterDisplay = previous?.Title;
            CurrentChapterDisplay = current?.Title ?? ChapterTitle;
            NextChapterDisplay = next?.Title;
        }

        private Chapter? GetChapterAtOffset(int offset)
        {
            if (_chapterListViewModel == null)
            {
                return null;
            }

            var index = _currentChapterIndex + offset;
            if (index < 0 || index >= _chapterListViewModel.Chapters.Count)
            {
                return null;
            }

            return _chapterListViewModel.Chapters[index];
        }
    }
}