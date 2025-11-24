using System;
using System.IO;

namespace MSCS.ViewModels
{
    public class LocalLibraryFolderEntryViewModel
    {
        public LocalLibraryFolderEntryViewModel(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentNullException(nameof(fullPath));
            }

            FullPath = fullPath;
            Name = Path.GetFileName(fullPath);
        }

        public string Name { get; }

        public string FullPath { get; }

        public string ExtensionLabel => "FOLDER";
    }
}