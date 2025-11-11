using System;

namespace MSCS.Services
{
    public enum LibraryChangeKind
    {
        Reset,
        DirectoryAdded,
        DirectoryRemoved,
        DirectoryChanged,
        FileChanged,
        Renamed
    }

    public sealed class LibraryChangedEventArgs : EventArgs
    {
        public LibraryChangedEventArgs(
            LibraryChangeKind kind,
            string? fullPath = null,
            string? oldPath = null,
            string? entryPath = null,
            string? oldEntryPath = null)
        {
            Kind = kind;
            FullPath = fullPath;
            OldPath = oldPath;
            EntryPath = entryPath;
            OldEntryPath = oldEntryPath;
        }

        public LibraryChangeKind Kind { get; }

        public string? FullPath { get; }

        public string? OldPath { get; }

        public string? EntryPath { get; }

        public string? OldEntryPath { get; }
    }
}