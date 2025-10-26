using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace MSCS.Services
{
    public partial class LocalLibraryService
    {
        private static string NormalizeArchiveEntryKey(string entryKey)
        {
            if (string.IsNullOrEmpty(entryKey))
            {
                return string.Empty;
            }

            var normalized = entryKey.Replace('\\', '/');

            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            normalized = normalized.TrimStart('/');

            if (normalized.Contains("../", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return normalized;
        }



        private static ArchiveStreamFactoryResult CreateArchiveEntryStreamFactory(ArchiveEntryDescriptor descriptor)
        {
            var coreFactory = descriptor.ArchiveType switch
            {
                ArchiveType.Zip => CreateZipArchiveStreamFactoryCore(descriptor),
                _ => CreateSharpCompressStreamFactoryCore(descriptor),
            };

            if (descriptor.RequiresBufferedCopy)
            {
                var buffered = new BufferedArchiveEntryFactory(coreFactory);
                return new ArchiveStreamFactoryResult(buffered.CreateStream, buffered.Dispose);
            }

            return new ArchiveStreamFactoryResult(coreFactory, null);
        }

        private static Func<Stream> CreateSharpCompressStreamFactoryCore(ArchiveEntryDescriptor descriptor)
        {
            return () =>
            {
                FileStream? fileStream = null;
                IArchive? archive = null;
                try
                {
                    fileStream = File.OpenRead(descriptor.ArchivePath);
                    archive = ArchiveFactory.Open(fileStream);

                    var entry = archive.Entries
                        .FirstOrDefault(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key) &&
                                             string.Equals(NormalizeArchiveEntryKey(e.Key!.Replace('\\', '/')),
                                                 descriptor.NormalizedKey,
                                                 StringComparison.OrdinalIgnoreCase));

                    if (entry == null)
                    {
                        throw new FileNotFoundException(
                            $"Entry '{descriptor.NormalizedKey}' was not found in '{descriptor.ArchivePath}'.",
                            descriptor.NormalizedKey);
                    }

                    var entryStream = entry.OpenEntryStream();
                    return new ArchiveEntryStream(entryStream, archive, fileStream);
                }
                catch
                {
                    archive?.Dispose();
                    fileStream?.Dispose();
                    throw;
                }
            };
        }

        private static Func<Stream> CreateZipArchiveStreamFactoryCore(ArchiveEntryDescriptor descriptor)
        {
            return () =>
            {
                FileStream? fileStream = null;
                ZipArchive? zipArchive = null;
                try
                {
                    fileStream = File.OpenRead(descriptor.ArchivePath);
                    zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

                    var entry = zipArchive.GetEntry(descriptor.NormalizedKey) ??
                                zipArchive.GetEntry(descriptor.ArchiveEntryKey) ??
                                zipArchive.Entries.FirstOrDefault(e =>
                                    string.Equals(NormalizeArchiveEntryKey(e.FullName),
                                        descriptor.NormalizedKey,
                                        StringComparison.OrdinalIgnoreCase));

                    if (entry == null)
                    {
                        throw new FileNotFoundException(
                            $"Entry '{descriptor.NormalizedKey}' was not found in '{descriptor.ArchivePath}'.",
                            descriptor.NormalizedKey);
                    }

                    var entryStream = entry.Open();
                    return new ArchiveEntryStream(entryStream, zipArchive, fileStream);
                }
                catch
                {
                    zipArchive?.Dispose();
                    fileStream?.Dispose();
                    throw;
                }
            };
        }

        private readonly struct ArchiveStreamFactoryResult
        {
            public ArchiveStreamFactoryResult(Func<Stream> streamFactory, Action? cleanup)
            {
                StreamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
                Cleanup = cleanup;
            }

            public Func<Stream> StreamFactory { get; }
            public Action? Cleanup { get; }
        }

        private sealed class BufferedArchiveEntryFactory : IDisposable
        {
            private readonly Func<Stream> _streamFactory;
            private readonly object _syncRoot = new();
            private string? _bufferedPath;

            public BufferedArchiveEntryFactory(Func<Stream> streamFactory)
            {
                _streamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
            }

            public Stream CreateStream()
            {
                EnsureBuffered();
                return new FileStream(_bufferedPath!, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            private void EnsureBuffered()
            {
                if (_bufferedPath != null && File.Exists(_bufferedPath))
                {
                    return;
                }

                lock (_syncRoot)
                {
                    if (_bufferedPath != null && File.Exists(_bufferedPath))
                    {
                        return;
                    }

                    var tempDirectory = Path.Combine(Path.GetTempPath(), "MSCS", "ReaderCache");
                    Directory.CreateDirectory(tempDirectory);
                    var tempPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N"));

                    using var source = _streamFactory();
                    using var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    source.CopyTo(destination);
                    destination.Flush();

                    _bufferedPath = tempPath;
                }
            }

            public void Dispose()
            {
                lock (_syncRoot)
                {
                    if (string.IsNullOrWhiteSpace(_bufferedPath))
                    {
                        return;
                    }

                    try
                    {
                        if (File.Exists(_bufferedPath))
                        {
                            File.Delete(_bufferedPath);
                        }
                    }
                    catch
                    {
                        // ignored - best effort cleanup
                    }
                    finally
                    {
                        _bufferedPath = null;
                    }
                }
            }
        }

        private sealed record ArchiveEntryDescriptor(
            string ArchivePath,
            string NormalizedKey,
            string ArchiveEntryKey,
            ArchiveType ArchiveType,
            bool RequiresBufferedCopy);

        private sealed class ArchiveEntryStream : Stream
        {
            private readonly Stream _inner;
            private readonly IDisposable[] _disposables;
            private bool _disposed;

            public ArchiveEntryStream(Stream inner, params IDisposable?[] disposables)
            {
                _inner = inner;
                _disposables = disposables
                    .Where(d => d is not null)
                    .Cast<IDisposable>()
                    .ToArray();
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

            protected override void Dispose(bool disposing)
            {
                if (!_disposed && disposing)
                {
                    _inner.Dispose();
                    foreach (var disposable in _disposables)
                    {
                        disposable.Dispose();
                    }
                }

                _disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}