using MSCS.Models;
using MSCS.Sources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MSCS.Services
{
    public class ReadingListService
    {
        private readonly UserSettings _userSettings;
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        public ReadingListService(UserSettings userSettings)
        {
            _userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        }

        public ReadingListExportResult Export(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            var allEntries = _userSettings.GetAllReadingProgress();

            var records = new List<ReadingListRecord>(allEntries.Count);
            var skippedLocal = 0;
            var skippedMissing = 0;

            foreach (var entry in allEntries)
            {
                var progress = entry.Progress;

                if (string.Equals(progress.SourceKey, SourceKeyConstants.LocalLibrary, StringComparison.OrdinalIgnoreCase))
                {
                    skippedLocal++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(progress.SourceKey) || string.IsNullOrWhiteSpace(progress.MangaUrl))
                {
                    skippedMissing++;
                    continue;
                }

                double? normalized = progress.LegacyScrollProgress;
                if (!normalized.HasValue && progress.ScrollOffset.HasValue && progress.ScrollableHeight.HasValue && progress.ScrollableHeight.Value > 0)
                {
                    var ratio = progress.ScrollOffset.Value / progress.ScrollableHeight.Value;
                    normalized = Math.Clamp(ratio, 0.0, 1.0);
                }

                records.Add(new ReadingListRecord
                {
                    Title = entry.Title,
                    ChapterIndex = progress.ChapterIndex,
                    ChapterTitle = progress.ChapterTitle,
                    MangaUrl = progress.MangaUrl!,
                    SourceKey = progress.SourceKey!,
                    ScrollProgress = normalized,
                    LastUpdatedUtc = progress.LastUpdatedUtc,
                    ScrollOffset = progress.ScrollOffset,
                    ScrollableHeight = progress.ScrollableHeight
                });
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(records, SerializerOptions);
            File.WriteAllText(filePath, json);

            return new ReadingListExportResult(records.Count, skippedLocal, skippedMissing);
        }

        public ReadingListImportResult Import(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Reading list file not found.", filePath);
            }

            List<ReadingListRecord>? records;
            try
            {
                var json = File.ReadAllText(filePath);
                records = JsonSerializer.Deserialize<List<ReadingListRecord>>(json, SerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("The selected file is not a valid reading list.", ex);
            }

            if (records == null || records.Count == 0)
            {
                return new ReadingListImportResult(0, 0, 0, 0);
            }

            var imported = 0;
            var skippedLocal = 0;
            var skippedInvalid = 0;

            foreach (var record in records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Title) ||
                    string.IsNullOrWhiteSpace(record.SourceKey) || string.IsNullOrWhiteSpace(record.MangaUrl))
                {
                    skippedInvalid++;
                    continue;
                }

                if (string.Equals(record.SourceKey, SourceKeyConstants.LocalLibrary, StringComparison.OrdinalIgnoreCase))
                {
                    skippedLocal++;
                    continue;
                }

                double? sanitizedProgress = record.ScrollProgress.HasValue
                    ? Math.Clamp(record.ScrollProgress.Value, 0.0, 1.0)
                    : null;
                double? sanitizedOffset = record.ScrollOffset.HasValue
                    ? Math.Max(0, record.ScrollOffset.Value)
                    : null;
                double? sanitizedScrollable = record.ScrollableHeight.HasValue
                    ? Math.Max(0, record.ScrollableHeight.Value)
                    : null;

                var lastUpdated = record.LastUpdatedUtc == default
                    ? DateTimeOffset.UtcNow
                    : record.LastUpdatedUtc;

                var progress = new MangaReadingProgress(
                    Math.Max(0, record.ChapterIndex),
                    string.IsNullOrWhiteSpace(record.ChapterTitle) ? null : record.ChapterTitle,
                    sanitizedProgress,
                    lastUpdated,
                    record.MangaUrl,
                    record.SourceKey,
                    sanitizedOffset,
                    sanitizedScrollable);

                _userSettings.SetReadingProgress(new ReadingProgressKey(record.Title, record.SourceKey, record.MangaUrl), progress);
                imported++;
            }

            return new ReadingListImportResult(imported, skippedLocal, skippedInvalid, records.Count);
        }

        private class ReadingListRecord
        {
            public string Title { get; set; } = string.Empty;
            public int ChapterIndex { get; set; }
            public string? ChapterTitle { get; set; }
            public string MangaUrl { get; set; } = string.Empty;
            public string SourceKey { get; set; } = string.Empty;
            public double? ScrollProgress { get; set; }
            public DateTimeOffset LastUpdatedUtc { get; set; }
            public double? ScrollOffset { get; set; }
            public double? ScrollableHeight { get; set; }
        }
    }

    public readonly record struct ReadingListExportResult(int ExportedCount, int SkippedLocalCount, int SkippedMissingDataCount)
    {
        public int TotalEntries => ExportedCount + SkippedLocalCount + SkippedMissingDataCount;
    }

    public readonly record struct ReadingListImportResult(int ImportedCount, int SkippedLocalCount, int SkippedInvalidCount, int TotalEntries);
}