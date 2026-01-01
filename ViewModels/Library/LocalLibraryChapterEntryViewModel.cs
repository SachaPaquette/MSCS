using System;
using System.IO;

namespace MSCS.ViewModels
{
    public class LocalLibraryChapterEntryViewModel
    {
        public LocalLibraryChapterEntryViewModel(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new ArgumentNullException(nameof(fullPath));
            }

            FullPath = fullPath;
            Name = Path.GetFileNameWithoutExtension(fullPath) ?? Path.GetFileName(fullPath) ?? fullPath;
            ExtensionLabel = (Path.GetExtension(fullPath) ?? string.Empty).TrimStart('.').ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(ExtensionLabel))
            {
                ExtensionLabel = "FILE";
            }
        }

        public string Name { get; }

        public string FullPath { get; }

        public string ExtensionLabel { get; }
    }
}