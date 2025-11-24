using MSCS.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using ListViewItem = System.Windows.Controls.ListViewItem;
using UserControl = System.Windows.Controls.UserControl;

namespace MSCS.Views
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
        }
        private void OnContinueReadingClicked(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem { DataContext: ContinueReadingEntryViewModel entry } item)
            {
                entry.ContinueCommand?.Execute(entry);
            }
        }

        private void OnLibraryItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListViewItem { DataContext: object dataContext } ||
                DataContext is not HomeViewModel homeViewModel)
            {
                return;
            }

            if (dataContext is LocalLibraryFolderEntryViewModel folder)
            {
                homeViewModel.LocalLibrary.NavigateToFolderCommand.Execute(folder);
            }
            else if (dataContext is LocalMangaEntryItemViewModel entry && Directory.Exists(entry.Path))
            {
                homeViewModel.LocalLibrary.NavigateToPath(entry.Path);
            }
            else if (dataContext is LocalLibraryChapterEntryViewModel chapter)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = chapter.FullPath,
                        UseShellExecute = true
                    });
                }
                catch
                {
                }
            }
        }
    }
}