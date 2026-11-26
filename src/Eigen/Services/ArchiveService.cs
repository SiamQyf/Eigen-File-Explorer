using System.IO;
using System.IO.Compression;
using Eigen.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;

namespace Eigen.Services;

public enum ArchiveCreateFormat
{
    Zip,
    TarGz,
    Tar,
    TarBz2
}

public enum ArchiveCompressionPreset
{
    Store,
    Fast,
    Normal,
    Maximum
}

public sealed class ArchiveEntryInfo
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string ParentFolder { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public long CompressedSize { get; init; }
    public DateTime? LastModified { get; init; }
    public bool IsEncrypted { get; init; }

    public string SizeDisplay => IsDirectory ? string.Empty : FormatHelper.FormatSize(Size);
    public string CompressedDisplay => IsDirectory || CompressedSize <= 0 ? string.Empty : FormatHelper.FormatSize(CompressedSize);
    public string ModifiedDisplay => LastModified is { } dt && dt != DateTime.MinValue ? dt.ToString("yyyy-MM-dd HH:mm") : string.Empty;
    public string TypeDisplay => IsDirectory ? "Folder" : (Path.GetExtension(Name) is { Length: > 0 } ext ? $"{ext.TrimStart('.').ToUpperInvariant()} File" : "File");
}

public static class ArchiveService
{
    private static readonly HashSet<string> SupportedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz",
        ".nupkg", ".msix", ".appx", ".vsix", ".jar", ".whl", ".cbz", ".cbr", ".epub"
    };

    public static bool IsArchivePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedArchiveExtensions.Contains(ext);
    }

    public static string GetArchiveBaseName(string archivePath)
    {
        var name = Path.GetFileName(archivePath);
        foreach (var multiExt in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
        {
            if (name.EndsWith(multiExt, StringComparison.OrdinalIgnoreCase))
                return name[..^multiExt.Length];
        }
        return Path.GetFileNameWithoutExtension(archivePath);
    }

    public static string GetDefaultArchiveName(IReadOnlyList<string> paths, ArchiveCreateFormat format = ArchiveCreateFormat.Zip)
    {
        var ext = GetExtensionForFormat(format);
        if (paths.Count == 1)
        {
            var single = paths[0].TrimEnd('\\', '/');
            var baseName = Directory.Exists(single)
                ? Path.GetFileName(single)
                : Path.GetFileNameWithoutExtension(single);
            if (!string.IsNullOrWhiteSpace(baseName))
                return $"{baseName}{ext}";
        }

        var parent = Path.GetDirectoryName(paths[0].TrimEnd('\\', '/'));
        var folderName = !string.IsNullOrWhiteSpace(parent) ? Path.GetFileName(parent) : "Archive";
        return $"{(string.IsNullOrWhiteSpace(folderName) ? "Archive" : folderName)}{ext}";
    }

    public static string GetExtensionForFormat(ArchiveCreateFormat format) => format switch
    {
        ArchiveCreateFormat.Zip => ".zip",
        ArchiveCreateFormat.TarGz => ".tar.gz",
        ArchiveCreateFormat.Tar => ".tar",
        ArchiveCreateFormat.TarBz2 => ".tar.bz2",
        _ => ".zip"
    };

    public static List<ArchiveEntryInfo> ListEntries(string archivePath, string? password = null)
    {
        var readerOpts = string.IsNullOrEmpty(password) ? null : new ReaderOptions { Password = password };
        using var archive = ArchiveFactory.OpenArchive(archivePath, readerOpts);
        var entries = new List<ArchiveEntryInfo>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            var rawKey = (entry.Key ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(rawKey)) continue;

            var lastSlash = rawKey.LastIndexOf('/');
            var parent = lastSlash >= 0 ? rawKey[..lastSlash] : string.Empty;
            var name = lastSlash >= 0 ? rawKey[(lastSlash + 1)..] : rawKey;

            // Synthesize any intermediate directory entries omitted by some ZIP/RAR writers
            if (!string.IsNullOrEmpty(parent))
            {
                var parts = parent.Split('/');
                var cur = string.Empty;
                foreach (var p in parts)
                {
                    var prev = cur;
                    cur = string.IsNullOrEmpty(cur) ? p : $"{cur}/{p}";
                    if (seenDirs.Add(cur))
                    {
                        entries.Add(new ArchiveEntryInfo
                        {
                            Key = cur,
                            Name = p,
                            ParentFolder = prev,
                            IsDirectory = true
                        });
                    }
                }
            }

            if (entry.IsDirectory)
            {
                if (seenDirs.Add(rawKey))
                {
                    entries.Add(new ArchiveEntryInfo
                    {
                        Key = rawKey,
                        Name = name,
                        ParentFolder = parent,
                        IsDirectory = true,
                        LastModified = entry.LastModifiedTime
                    });
                }
                continue;
            }

            entries.Add(new ArchiveEntryInfo
            {
                Key = rawKey,
                Name = name,
                ParentFolder = parent,
                IsDirectory = false,
                Size = entry.Size,
                CompressedSize = entry.CompressedSize,
                LastModified = entry.LastModifiedTime,
                IsEncrypted = entry.IsEncrypted
            });
        }

        return entries;
    }

    public static Task<int> ExtractAsync(
        string archivePath,
        string destinationFolder,
        bool createSubfolder = false,
        IReadOnlyCollection<string>? selectedKeys = null,
        string? password = null,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var finalDest = createSubfolder
                ? Path.Combine(destinationFolder, GetArchiveBaseName(archivePath))
                : destinationFolder;
            Directory.CreateDirectory(finalDest);
            var fullBase = Path.GetFullPath(finalDest).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var readerOpts = string.IsNullOrEmpty(password) ? null : new ReaderOptions { Password = password };
            using var archive = ArchiveFactory.OpenArchive(archivePath, readerOpts);

            var targetEntries = archive.Entries
                .Where(e => !e.IsDirectory)
                .Where(e => MatchesSelection(e.Key, selectedKeys))
                .ToList();

            var total = Math.Max(1, targetEntries.Count);
            var totalBytes = targetEntries.Sum(e => Math.Max(0, e.Size));
            long processedBytes = 0;
            var completed = 0;

            foreach (var entry in targetEntries)
            {
                ct.ThrowIfCancellationRequested();
                var normalizedKey = (entry.Key ?? string.Empty).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(normalizedKey)) continue;

                var destPath = Path.GetFullPath(Path.Combine(finalDest, normalizedKey));
                if (!destPath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Blocked unsafe archive entry path: {entry.Key}");

                var dir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                progress?.Report(new FileOperationProgress
                {
                    OperationName = "Eigen Archiver • Extracting",
                    CurrentItem = Path.GetFileName(destPath),
                    CompletedItems = completed,
                    TotalItems = total,
                    BytesProcessed = processedBytes,
                    TotalBytes = totalBytes
                });

                using (var entryStream = entry.OpenEntryStream())
                using (var outStream = new FileStream(destPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    entryStream.CopyTo(outStream);
                }

                if (entry.LastModifiedTime is { } dt && dt != DateTime.MinValue)
                {
                    try { File.SetLastWriteTime(destPath, dt); } catch { }
                }

                completed++;
                processedBytes += Math.Max(0, entry.Size);
                progress?.Report(new FileOperationProgress
                {
                    OperationName = "Eigen Archiver • Extracting",
                    CurrentItem = Path.GetFileName(destPath),
                    CompletedItems = completed,
                    TotalItems = total,
                    BytesProcessed = processedBytes,
                    TotalBytes = totalBytes
                });
            }

            return completed;
        }, ct);
    }

    public static Task<int> CreateArchiveAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationArchivePath,
        ArchiveCreateFormat format = ArchiveCreateFormat.Zip,
        ArchiveCompressionPreset preset = ArchiveCompressionPreset.Normal,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (sourcePaths.Count == 0)
                throw new ArgumentException("No source items provided.", nameof(sourcePaths));

            var destDir = Path.GetDirectoryName(destinationArchivePath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            var filesToPack = CollectFilesWithRelativePaths(sourcePaths, ct);
            var total = Math.Max(1, filesToPack.Count);
            var totalBytes = filesToPack.Sum(f => f.Size);
            long processedBytes = 0;
            var completed = 0;

            var tempPath = destinationArchivePath + ".eigenpart";
            try
            {
                if (format == ArchiveCreateFormat.Zip)
                {
                    var level = preset switch
                    {
                        ArchiveCompressionPreset.Store => CompressionLevel.NoCompression,
                        ArchiveCompressionPreset.Fast => CompressionLevel.Fastest,
                        ArchiveCompressionPreset.Maximum => CompressionLevel.SmallestSize,
                        _ => CompressionLevel.Optimal
                    };

                    using var fs = new FileStream(tempPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None);
                    using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
                    foreach (var (fullPath, entryName, size) in filesToPack)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new FileOperationProgress
                        {
                            OperationName = "Eigen Archiver • Compressing",
                            CurrentItem = entryName,
                            CompletedItems = completed,
                            TotalItems = total,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes
                        });

                        var entry = zip.CreateEntry(entryName, level);
                        try { entry.LastWriteTime = File.GetLastWriteTime(fullPath); } catch { }
                        using var inStream = new FileStream(fullPath, System.IO.FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var entryStream = entry.Open();
                        inStream.CopyTo(entryStream);

                        completed++;
                        processedBytes += size;
                    }
                }
                else
                {
                    var scCompression = format switch
                    {
                        ArchiveCreateFormat.TarGz => CompressionType.GZip,
                        ArchiveCreateFormat.TarBz2 => CompressionType.BZip2,
                        _ => CompressionType.None
                    };
                    var writerOpts = new TarWriterOptions(scCompression, finalizeArchiveOnClose: true);
                    if (format == ArchiveCreateFormat.TarGz)
                    {
                        writerOpts.CompressionLevel = preset switch
                        {
                            ArchiveCompressionPreset.Store => 0,
                            ArchiveCompressionPreset.Fast => 1,
                            ArchiveCompressionPreset.Maximum => 9,
                            _ => 6
                        };
                    }

                    using var fs = new FileStream(tempPath, System.IO.FileMode.Create, FileAccess.Write, FileShare.None);
                    using var writer = WriterFactory.OpenWriter(fs, ArchiveType.Tar, writerOpts);
                    foreach (var (fullPath, entryName, size) in filesToPack)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new FileOperationProgress
                        {
                            OperationName = "Eigen Archiver • Compressing",
                            CurrentItem = entryName,
                            CompletedItems = completed,
                            TotalItems = total,
                            BytesProcessed = processedBytes,
                            TotalBytes = totalBytes
                        });

                        using var inStream = new FileStream(fullPath, System.IO.FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        DateTime? mtime = null;
                        try { mtime = File.GetLastWriteTime(fullPath); } catch { }
                        writer.Write(entryName, inStream, mtime);

                        completed++;
                        processedBytes += size;
                    }
                }

                if (File.Exists(destinationArchivePath))
                    File.Delete(destinationArchivePath);
                File.Move(tempPath, destinationArchivePath);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            progress?.Report(new FileOperationProgress
            {
                OperationName = "Eigen Archiver • Compressing",
                CurrentItem = Path.GetFileName(destinationArchivePath),
                CompletedItems = total,
                TotalItems = total,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes
            });

            return completed;
        }, ct);
    }

    public static Task<(int VerifiedCount, long TotalBytes)> TestArchiveAsync(
        string archivePath,
        string? password = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var readerOpts = string.IsNullOrEmpty(password) ? null : new ReaderOptions { Password = password };
            using var archive = ArchiveFactory.OpenArchive(archivePath, readerOpts);
            var count = 0;
            long totalBytes = 0;
            var discard = new byte[32 * 1024];

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory) continue;
                using var stream = entry.OpenEntryStream();
                int read;
                while ((read = stream.Read(discard, 0, discard.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    totalBytes += read;
                }
                count++;
            }

            return (count, totalBytes);
        }, ct);
    }

    public static Task<int> AddFilesToZipAsync(string zipPath, IReadOnlyList<string> sourcePaths, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var files = CollectFilesWithRelativePaths(sourcePaths, ct);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
            var added = 0;
            foreach (var (fullPath, entryName, _) in files)
            {
                ct.ThrowIfCancellationRequested();
                zip.GetEntry(entryName)?.Delete();
                zip.CreateEntryFromFile(fullPath, entryName, CompressionLevel.Optimal);
                added++;
            }
            return added;
        }, ct);
    }

    private static bool MatchesSelection(string? entryKey, IReadOnlyCollection<string>? selectedKeys)
    {
        if (selectedKeys is null || selectedKeys.Count == 0) return true;
        var norm = (entryKey ?? string.Empty).Replace('\\', '/').Trim('/');
        foreach (var sel in selectedKeys)
        {
            var s = sel.Replace('\\', '/').Trim('/');
            if (norm.Equals(s, StringComparison.OrdinalIgnoreCase) ||
                norm.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static List<(string FullPath, string EntryName, long Size)> CollectFilesWithRelativePaths(
        IReadOnlyList<string> sourcePaths,
        CancellationToken ct)
    {
        var results = new List<(string FullPath, string EntryName, long Size)>();
        foreach (var raw in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            var path = raw.TrimEnd('\\', '/');
            if (File.Exists(path))
            {
                var len = 0L;
                try { len = new FileInfo(path).Length; } catch { }
                results.Add((path, Path.GetFileName(path), len));
            }
            else if (Directory.Exists(path))
            {
                var rootParent = Path.GetDirectoryName(path) ?? path;
                var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", opts))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(rootParent, file.FullName).Replace('\\', '/');
                    results.Add((file.FullName, rel, file.Length));
                }
            }
        }
        return results;
    }
}
