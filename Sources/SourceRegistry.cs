using MSCS.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MSCS.Sources
{
    public static class SourceRegistry
    {
        private sealed record SourceRegistration(SourceDescriptor Descriptor, IMangaSource Source);

        private static readonly Dictionary<string, SourceRegistration> Sources = new();

        static SourceRegistry()
        {
            Register(SourceKeyConstants.MangaRead, new MangaReadSource(), "MangaRead");
            Register(SourceKeyConstants.MangaDex, new MangaDexSource(), "MangaDex");
        }

        public static void RegisterMadara(string key, Uri baseUri, string? displayName = null)
        {
            if (baseUri is null)
            {
                throw new ArgumentNullException(nameof(baseUri));
            }

            Register(key, new GenericMadaraSource(baseUri), displayName);
        }

        public static void Register(string key, IMangaSource source, string? displayName = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
            }

            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var descriptor = new SourceDescriptor(key, string.IsNullOrWhiteSpace(displayName) ? key : displayName);
            Sources[key] = new SourceRegistration(descriptor, source);
        }

        public static IMangaSource? Resolve(string key)
        {
            return Sources.TryGetValue(key, out var registration) ? registration.Source : null;
        }

        public static SourceDescriptor? GetDescriptor(string key)
        {
            return Sources.TryGetValue(key, out var registration) ? registration.Descriptor : null;
        }

        public static IReadOnlyList<SourceDescriptor> GetAllDescriptors()
        {
            return Sources.Values
                .Select(registration => registration.Descriptor)
                .OrderBy(descriptor => descriptor.DisplayName)
                .ToList();
        }
    }

    public sealed record SourceDescriptor(string Key, string DisplayName);
}