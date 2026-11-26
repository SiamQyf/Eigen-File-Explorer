using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
using Eigen.Models;
using Microsoft.VisualBasic.FileIO;

namespace Eigen.Services;

public sealed class FileSystemService
{
    private const int MaxPreviewTextBytes = 64 * 1024;

    public Task<List<FileSystemItem>> GetDirectoryItemsAsync(string path, bool showHidden, CancellationToken ct)
    {
        return Task.Run(() => GetDirectoryItemsCore(path, showHidden, ct), ct);
    }

    public List<FileSystemItem> GetDirectoryItemsCore(string path, bool showHidden, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Folder not found: {path}");

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = showHidden
                ? FileAttributes.System
                : FileAttributes.Hidden | FileAttributes.System
        };

        var result = new List<FileSystemItem>(64);
        var enumerable = new FileSystemEnumerable<FileSystemItem>(
            path,
            (ref FileSystemEntry entry) =>
            {
                var isDir = entry.IsDirectory;
                var attrs = entry.Attributes;
                var size = isDir ? 0L : entry.Length;
                var name = entry.FileName.ToString();
                var fullPath = entry.ToFullPath();
                var ext = isDir ? string.Empty : Path.GetExtension(name);

                return new FileSystemItem
                {
                    Name = name,
                    FullPath = fullPath,
                    ParentPath = path,
                    IsDirectory = isDir,
                    IsHidden = (attrs & FileAttributes.Hidden) != 0,
                    IsSystem = (attrs & FileAttributes.System) != 0,
                    IsReadOnly = (attrs & FileAttributes.ReadOnly) != 0,
                    SizeBytes = size,
                    SizeDisplay = isDir ? string.Empty : FormatHelper.FormatSize(size),
                    DateModified = entry.LastWriteTimeUtc.LocalDateTime,
                    Extension = ext,
                    TypeDescription = isDir
                        ? "File folder"
                        : (string.IsNullOrEmpty(ext) ? "File" : $"{ext.TrimStart('.').ToUpperInvariant()} File")
                };
            },
            options);

        foreach (var item in enumerable)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(item);
        }

        return result;
    }

    public async Task SearchRecursiveAsync(
        string rootPath,
        string query,
        bool showHidden,
        IProgress<IReadOnlyList<FileSystemItem>> batchProgress,
        CancellationToken ct)
    {
        await Task.Run(() =>
        {
            if (!Directory.Exists(rootPath) || string.IsNullOrWhiteSpace(query))
                return;

            var queue = new Queue<string>();
            queue.Enqueue(rootPath);
            var batch = new List<FileSystemItem>(32);

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = showHidden
                    ? FileAttributes.System
                    : FileAttributes.Hidden | FileAttributes.System
            };

            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = queue.Dequeue();

                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = new DirectoryInfo(current).EnumerateFileSystemInfos("*", options);
                }
                catch
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        batch.Add(CreateItem(entry));
                        if (batch.Count >= 24)
                        {
                            batchProgress.Report(batch.ToArray());
                            batch.Clear();
                        }
                    }

                    if (entry is DirectoryInfo subDir && (subDir.Attributes & FileAttributes.ReparsePoint) == 0)
                        queue.Enqueue(subDir.FullName);
                }
            }

            if (batch.Count > 0)
                batchProgress.Report(batch.ToArray());
        }, ct);
    }

    public List<BreadcrumbSegment> GetSiblingDirectories(string path, bool showHidden)
    {
        var results = new List<BreadcrumbSegment>();
        if (FormatHelper.IsHomePath(path))
        {
            foreach (var d in GetDrives())
            {
                results.Add(new BreadcrumbSegment
                {
                    Name = d.Name,
                    FullPath = d.RootPath,
                    IsRoot = true
                });
            }
            return results;
        }

        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = showHidden
                    ? FileAttributes.System
                    : FileAttributes.Hidden | FileAttributes.System
            };

            var dirInfo = new DirectoryInfo(path);
            if (!dirInfo.Exists) return results;

            foreach (var sub in dirInfo.EnumerateDirectories("*", options).OrderBy(d => d.Name, NaturalStringComparer.Instance).Take(80))
            {
                results.Add(new BreadcrumbSegment
                {
                    Name = sub.Name,
                    FullPath = sub.FullName
                });
            }
        }
        catch
        {
            // Ignore access errors
        }

        return results;
    }

    public List<FileSystemItem> GetChildDirectories(string path, bool showHidden)
    {
        var results = new List<FileSystemItem>();
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = showHidden
                    ? FileAttributes.System
                    : FileAttributes.Hidden | FileAttributes.System
            };

            foreach (var dir in new DirectoryInfo(path).EnumerateDirectories("*", options)
                         .OrderBy(d => d.Name, NaturalStringComparer.Instance)
                         .Take(200))
            {
                results.Add(CreateItem(dir));
            }
        }
        catch
        {
            // Ignore inaccessible folders in tree
        }
        return results;
    }

    public List<DriveCardItem> GetDrives()
    {
        var list = new List<DriveCardItem>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                var letter = drive.Name.TrimEnd('\\');
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? GetDriveTypeLabel(drive.DriveType) : drive.VolumeLabel;
                var displayName = $"{label} ({letter})";
                var free = drive.AvailableFreeSpace;
                var total = drive.TotalSize;

                list.Add(new DriveCardItem
                {
                    Name = displayName,
                    RootPath = drive.RootDirectory.FullName,
                    VolumeLabel = label,
                    DriveTypeLabel = GetDriveTypeLabel(drive.DriveType),
                    FileSystem = drive.DriveFormat,
                    TotalBytes = total,
                    FreeBytes = free,
                    SpaceSummary = $"{FormatHelper.FormatSize(free)} free of {FormatHelper.FormatSize(total)}"
                });
            }
            catch
            {
                // Skip unavailable drives
            }
        }
        return list;
    }

    public string CreateFolder(string parentPath, string? preferredName = null)
    {
        var name = FormatHelper.GenerateUniqueName(parentPath, preferredName ?? "New folder", string.Empty);
        var fullPath = Path.Combine(parentPath, name);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public string CreateFile(string parentPath, string baseName, string extension)
    {
        var name = FormatHelper.GenerateUniqueName(parentPath, baseName, extension);
        var fullPath = Path.Combine(parentPath, name);
        using (File.Create(fullPath)) { }
        return fullPath;
    }

    public string RenameItem(string fullPath, string newName)
    {
        if (!FormatHelper.IsValidFileName(newName))
            throw new ArgumentException("The file name contains invalid characters.", nameof(newName));

        var trimmed = newName.Trim();
        var parent = Path.GetDirectoryName(fullPath.TrimEnd('\\'))
            ?? throw new InvalidOperationException("Cannot determine parent folder.");
        var destination = Path.Combine(parent, trimmed);

        if (string.Equals(fullPath, destination, StringComparison.Ordinal))
            return fullPath;

        var caseOnlyChange = string.Equals(fullPath, destination, StringComparison.OrdinalIgnoreCase);

        if (Directory.Exists(fullPath))
        {
            if (!caseOnlyChange && (Directory.Exists(destination) || File.Exists(destination)))
                throw new IOException($"An item named '{trimmed}' already exists.");

            if (caseOnlyChange)
            {
                var temp = Path.Combine(parent, $".eigen_tmp_{Guid.NewGuid():N}");
                Directory.Move(fullPath, temp);
                Directory.Move(temp, destination);
            }
            else
            {
                Directory.Move(fullPath, destination);
            }
            return destination;
        }

        if (File.Exists(fullPath))
        {
            if (!caseOnlyChange && (File.Exists(destination) || Directory.Exists(destination)))
                throw new IOException($"An item named '{trimmed}' already exists.");

            if (caseOnlyChange)
            {
                var temp = Path.Combine(parent, $".eigen_tmp_{Guid.NewGuid():N}");
                File.Move(fullPath, temp);
                File.Move(temp, destination);
            }
            else
            {
                File.Move(fullPath, destination);
            }
            return destination;
        }

        throw new FileNotFoundException("Item no longer exists.", fullPath);
    }

    public void DeleteToRecycleBin(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else if (File.Exists(path))
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
        }
    }

    public void DeletePermanently(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
    }

    public async Task<IReadOnlyList<FileTransferEntry>> CopyOrMoveItemsAsync(
        IReadOnlyList<string> sourcePaths,
        string destinationFolder,
        bool isMove,
        ConflictAction conflictAction,
        IProgress<FileOperationProgress>? progress,
        CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            if (!Directory.Exists(destinationFolder))
                throw new DirectoryNotFoundException($"Destination folder not found: {destinationFolder}");

            var results = new List<FileTransferEntry>();
            var totalBytes = EstimateTotalBytes(sourcePaths, ct);
            long processedBytes = 0;
            var totalItems = sourcePaths.Count;

            for (var i = 0; i < sourcePaths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var src = sourcePaths[i].TrimEnd('\\');
                var isDir = Directory.Exists(src);
                var isFile = !isDir && File.Exists(src);
                if (!isDir && !isFile) continue;

                var itemName = Path.GetFileName(src);
                if (isDir && FormatHelper.IsSubPathOrSelf(destinationFolder, src))
                    throw new InvalidOperationException($"Cannot {(isMove ? "move" : "copy")} '{itemName}' into itself or a subfolder.");

                var dest = Path.Combine(destinationFolder, itemName);

                if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase))
                {
                    if (isMove)
                        continue;

                    var baseN = isDir ? itemName : Path.GetFileNameWithoutExtension(itemName);
                    var ext = isDir ? string.Empty : Path.GetExtension(itemName);
                    dest = Path.Combine(destinationFolder, FormatHelper.GenerateUniqueName(destinationFolder, $"{baseN} - Copy", ext));
                }
                else if (File.Exists(dest) || Directory.Exists(dest))
                {
                    if (conflictAction == ConflictAction.Skip)
                        continue;

                    if (conflictAction == ConflictAction.KeepBoth || conflictAction == ConflictAction.Ask)
                    {
                        var baseN = isDir ? itemName : Path.GetFileNameWithoutExtension(itemName);
                        var ext = isDir ? string.Empty : Path.GetExtension(itemName);
                        dest = Path.Combine(destinationFolder, FormatHelper.GenerateUniqueName(destinationFolder, baseN, ext));
                    }
                    else if (conflictAction == ConflictAction.Replace)
                    {
                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                        else if (File.Exists(dest)) File.Delete(dest);
                    }
                }

                progress?.Report(new FileOperationProgress
                {
                    OperationName = isMove ? "Moving items" : "Copying items",
                    CurrentItem = itemName,
                    CompletedItems = i,
                    TotalItems = totalItems,
                    BytesProcessed = processedBytes,
                    TotalBytes = totalBytes
                });

                if (isDir)
                {
                    if (isMove && SameVolume(src, dest))
                    {
                        Directory.Move(src, dest);
                    }
                    else
                    {
                        await CopyDirectoryRecursiveAsync(src, dest, ct, bytesDelta =>
                        {
                            processedBytes += bytesDelta;
                            progress?.Report(new FileOperationProgress
                            {
                                OperationName = isMove ? "Moving items" : "Copying items",
                                CurrentItem = itemName,
                                CompletedItems = i,
                                TotalItems = totalItems,
                                BytesProcessed = processedBytes,
                                TotalBytes = totalBytes
                            });
                        });
                        if (isMove)
                            Directory.Delete(src, recursive: true);
                    }
                }
                else
                {
                    var len = new FileInfo(src).Length;
                    if (isMove && SameVolume(src, dest))
                    {
                        File.Move(src, dest);
                        processedBytes += len;
                    }
                    else
                    {
                        await CopyFileBufferedAsync(src, dest, ct, bytesDelta =>
                        {
                            processedBytes += bytesDelta;
                            progress?.Report(new FileOperationProgress
                            {
                                OperationName = isMove ? "Moving items" : "Copying items",
                                CurrentItem = itemName,
                                CompletedItems = i,
                                TotalItems = totalItems,
                                BytesProcessed = processedBytes,
                                TotalBytes = totalBytes
                            });
                        });
                        if (isMove)
                            File.Delete(src);
                    }
                }

                results.Add(new FileTransferEntry(src, dest, isDir));
            }

            progress?.Report(new FileOperationProgress
            {
                OperationName = isMove ? "Moved items" : "Copied items",
                CurrentItem = "Completed",
                CompletedItems = totalItems,
                TotalItems = totalItems,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes
            });

            return results;
        }, ct);
    }

    public Task<PreviewInfo> BuildPreviewAsync(FileSystemItem item, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var attrs = BuildAttributesText(item);

            if (item.IsDirectory)
                return BuildFolderPreview(item, attrs, ct);

            var (_, shellType) = NativeShellService.GetIconAndType(item.FullPath, isDirectory: false);
            var subtitle = string.IsNullOrWhiteSpace(shellType) ? item.TypeDescription : shellType;

            if (FormatHelper.IsImageExtension(item.Extension))
            {
                var imgPreview = TryBuildRasterImagePreview(item, subtitle, attrs, ct);
                if (imgPreview is not null)
                    return imgPreview;
            }

            if (FormatHelper.IsVideoExtension(item.Extension))
            {
                return BuildVideoPreview(item, subtitle, attrs, ct);
            }

            if (IsOfficeOpenXmlExtension(item.Extension))
            {
                var officePreview = TryBuildOfficePreview(item, subtitle, attrs, ct);
                if (officePreview is not null)
                    return officePreview;
            }

            if (item.Extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                var rtfPreview = TryBuildRtfPreview(item, subtitle, attrs, ct);
                if (rtfPreview is not null)
                    return rtfPreview;
            }

            if (IsFontExtension(item.Extension))
            {
                var fontPreview = TryBuildFontPreview(item, subtitle, attrs, ct);
                if (fontPreview is not null)
                    return fontPreview;
            }

            if (FormatHelper.IsArchiveExtension(item.Extension))
            {
                var zipPreview = TryBuildArchivePreview(item, subtitle, attrs, ct);
                if (zipPreview is not null)
                    return zipPreview;
            }

            // SVG gets visual shell thumbnail when available, with XML source + fallback
            if (item.Extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var (svgThumb, isThumb) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 960, ct);
                if (isThumb && svgThumb is not null)
                {
                    TryReadTextChunk(item.FullPath, ct, out var svgText, out var lines, out var len, out var trunc);
                    return CreatePreview(
                        item,
                        PreviewKind.Image,
                        subtitle,
                        attrs,
                        previewImage: svgThumb,
                        isVisualThumbnail: true,
                        previewBadge: "Vector Graphic",
                        imageDimensions: $"{(int)svgThumb.PixelWidth} × {(int)svgThumb.PixelHeight} px • Rendered SVG",
                        textContent: svgText,
                        textStats: svgText is not null ? $"{lines:N0} lines{(trunc ? " (first 64 KB)" : "")} • {FormatHelper.FormatSize(len)}" : null,
                        isTruncated: trunc);
                }
            }

            if (FormatHelper.IsTextPreviewExtension(item.Extension, item.Name))
            {
                var textPreview = TryBuildTextPreview(item, subtitle, attrs, "Text / Code", ct);
                if (textPreview is not null)
                    return textPreview;
            }

            return BuildRichFallbackPreview(item, subtitle, attrs, ct);
        }, ct);
    }

    private static PreviewInfo BuildFolderPreview(FileSystemItem item, string attrs, CancellationToken ct)
    {
        int fileCount = 0;
        int dirCount = 0;
        var dirSamples = new List<string>(16);
        var fileSamples = new List<string>(16);
        const int maxSamples = 28;

        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true };
            foreach (var entry in new DirectoryInfo(item.FullPath).EnumerateFileSystemInfos("*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    dirCount++;
                    if (dirSamples.Count < maxSamples)
                        dirSamples.Add($"[DIR]   {entry.Name}/");
                }
                else
                {
                    fileCount++;
                    if (fileSamples.Count < maxSamples)
                    {
                        var len = entry is FileInfo fi ? SafeFileLength(fi) : 0;
                        fileSamples.Add($"        {entry.Name}  ({FormatHelper.FormatSize(len)})");
                    }
                }

                if (fileCount + dirCount >= 5000) break;
            }
        }
        catch
        {
            // Ignore access errors
        }

        var (folderBmp, isThumb) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 256, ct);
        var combined = dirSamples.Concat(fileSamples).Take(maxSamples).ToList();
        var listing = combined.Count > 0 ? string.Join(Environment.NewLine, combined) : "(Empty folder)";
        var shownOverflow = (fileCount + dirCount) > combined.Count;

        return CreatePreview(
            item,
            PreviewKind.Folder,
            item.TypeDescription,
            attrs,
            previewImage: folderBmp,
            isVisualThumbnail: isThumb,
            previewBadge: "Folder",
            textContent: listing,
            textStats: $"{dirCount:N0} folders, {fileCount:N0} files{(shownOverflow ? $" (showing first {combined.Count})" : "")}",
            isTruncated: shownOverflow,
            folderFileCount: fileCount,
            folderSubdirCount: dirCount);
    }

    private static PreviewInfo BuildVideoPreview(FileSystemItem item, string subtitle, string attrs, CancellationToken ct)
    {
        var (thumbBmp, isThumb) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 768, ct);
        var videoUri = new Uri(item.FullPath, UriKind.Absolute);
        var dims = isThumb && thumbBmp is not null
            ? $"{(int)thumbBmp.PixelWidth} × {(int)thumbBmp.PixelHeight} px"
            : null;

        return CreatePreview(
            item,
            PreviewKind.Video,
            subtitle,
            attrs,
            previewImage: thumbBmp,
            videoSource: videoUri,
            isVisualThumbnail: false,
            previewBadge: "Video Player",
            imageDimensions: dims);
    }

    private static PreviewInfo? TryBuildRasterImagePreview(FileSystemItem item, string subtitle, string attrs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var stream = new FileStream(item.FullPath, System.IO.FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int nativeWidth = 0;
            int nativeHeight = 0;
            try
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                var bestFrame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault();
                if (bestFrame is not null)
                {
                    nativeWidth = bestFrame.PixelWidth;
                    nativeHeight = bestFrame.PixelHeight;
                }
            }
            catch
            {
                // Fallback to BitmapImage dimensions directly
            }

            stream.Position = 0;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            if (nativeWidth > 1280)
                img.DecodePixelWidth = 1280;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();

            var displayW = nativeWidth > 0 ? nativeWidth : img.PixelWidth;
            var displayH = nativeHeight > 0 ? nativeHeight : img.PixelHeight;

            return CreatePreview(
                item,
                PreviewKind.Image,
                subtitle,
                attrs,
                previewImage: img,
                isVisualThumbnail: true,
                previewBadge: "Image",
                imageDimensions: $"{displayW:N0} × {displayH:N0} px");
        }
        catch
        {
            return null;
        }
    }

    private static PreviewInfo? TryBuildArchivePreview(FileSystemItem item, string subtitle, string attrs, CancellationToken ct)
    {
        try
        {
            using var archive = ZipFile.OpenRead(item.FullPath);
            var lines = new List<string>(Math.Min(archive.Entries.Count, 150));
            long totalUncompressed = 0;
            var entryCount = archive.Entries.Count;
            var index = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                totalUncompressed += Math.Max(0, entry.Length);
                if (index < 150)
                {
                    var isDir = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                    var sizeStr = isDir ? "[DIR]   " : $"{FormatHelper.FormatSize(entry.Length),8}";
                    lines.Add($"{sizeStr}  {entry.FullName}");
                }
                index++;
            }

            var (bmp, isThumb) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 256, ct);
            var listing = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "(Empty archive)";
            var truncated = entryCount > 150;

            return CreatePreview(
                item,
                PreviewKind.Generic,
                subtitle,
                attrs,
                previewImage: bmp,
                isVisualThumbnail: isThumb,
                previewBadge: "Archive Contents",
                imageDimensions: isThumb && bmp is not null ? $"{(int)bmp.PixelWidth} × {(int)bmp.PixelHeight} px" : null,
                textContent: listing,
                textStats: $"{entryCount:N0} entries{(truncated ? " (first 150 shown)" : "")} • {FormatHelper.FormatSize(totalUncompressed)} uncompressed",
                isTruncated: truncated);
        }
        catch
        {
            return null;
        }
    }

    private static PreviewInfo? TryBuildTextPreview(FileSystemItem item, string subtitle, string attrs, string badge, CancellationToken ct)
    {
        if (!TryReadTextChunk(item.FullPath, ct, out var text, out var lines, out var fileLength, out var truncated) || text is null)
            return null;

        var (iconBmp, _) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 192, ct);

        return CreatePreview(
            item,
            PreviewKind.Text,
            subtitle,
            attrs,
            previewImage: iconBmp,
            isVisualThumbnail: false,
            previewBadge: badge,
            textContent: text,
            textStats: $"{lines:N0} lines{(truncated ? " (first 64 KB)" : "")} • {FormatHelper.FormatSize(fileLength)}",
            isTruncated: truncated);
    }

    private static bool IsOfficeOpenXmlExtension(string ext) =>
        ext.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    private static bool IsFontExtension(string ext) =>
        ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase);

    private static PreviewInfo? TryBuildOfficePreview(FileSystemItem item, string subtitle, string attrs, CancellationToken ct)
    {
        try
        {
            using var zip = ZipFile.OpenRead(item.FullPath);
            BitmapSource? thumbBmp = null;
            var isVisualThumb = false;

            // Check embedded OOXML thumbnail first
            var thumbEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals("docProps/thumbnail.jpeg", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals("docProps/thumbnail.jpg", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.Equals("docProps/thumbnail.png", StringComparison.OrdinalIgnoreCase));
            if (thumbEntry is not null)
            {
                try
                {
                    using var ts = thumbEntry.Open();
                    using var ms = new MemoryStream();
                    ts.CopyTo(ms);
                    ms.Position = 0;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    thumbBmp = img;
                    isVisualThumb = true;
                }
                catch { }
            }

            if (thumbBmp is null)
                (thumbBmp, isVisualThumb) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 768, ct);

            var extracted = ExtractOfficeText(zip, item.Extension, ct);
            var badge = item.Extension.ToLowerInvariant() switch
            {
                ".docx" => "Word Document",
                ".pptx" => "PowerPoint Presentation",
                ".xlsx" => "Excel Workbook",
                _ => "Office Document"
            };

            if (isVisualThumb && thumbBmp is not null)
            {
                return CreatePreview(
                    item,
                    PreviewKind.Image,
                    subtitle,
                    attrs,
                    previewImage: thumbBmp,
                    isVisualThumbnail: true,
                    previewBadge: badge,
                    imageDimensions: $"{(int)thumbBmp.PixelWidth} × {(int)thumbBmp.PixelHeight} px • {badge}",
                    textContent: extracted,
                    textStats: extracted is not null ? $"{badge} text excerpt" : null);
            }

            if (!string.IsNullOrWhiteSpace(extracted))
            {
                var lineCount = extracted.Count(c => c == '\n') + 1;
                return CreatePreview(
                    item,
                    PreviewKind.Text,
                    subtitle,
                    attrs,
                    previewImage: thumbBmp,
                    isVisualThumbnail: false,
                    previewBadge: badge,
                    textContent: extracted,
                    textStats: $"{lineCount:N0} lines • {badge}");
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractOfficeText(ZipArchive zip, string ext, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };

        try
        {
            if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                var doc = zip.GetEntry("word/document.xml");
                if (doc is null) return null;
                using var s = doc.Open();
                using var xr = XmlReader.Create(s, readerSettings);
                var xdoc = XDocument.Load(xr);
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                foreach (var p in xdoc.Descendants(w + "p"))
                {
                    ct.ThrowIfCancellationRequested();
                    var line = string.Concat(p.Descendants(w + "t").Select(t => t.Value));
                    if (!string.IsNullOrWhiteSpace(line))
                        sb.AppendLine(line);
                    if (sb.Length > 32768) break;
                }
            }
            else if (ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            {
                var slides = zip.Entries
                    .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, NaturalStringComparer.Instance)
                    .Take(25);
                XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
                var idx = 1;
                foreach (var slide in slides)
                {
                    ct.ThrowIfCancellationRequested();
                    using var s = slide.Open();
                    using var xr = XmlReader.Create(s, readerSettings);
                    var xdoc = XDocument.Load(xr);
                    var texts = xdoc.Descendants(a + "t").Select(t => t.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                    if (texts.Count > 0)
                    {
                        sb.AppendLine($"--- Slide {idx} ---");
                        foreach (var t in texts)
                            sb.AppendLine(t);
                        sb.AppendLine();
                    }
                    idx++;
                    if (sb.Length > 32768) break;
                }
            }
            else if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                var wb = zip.GetEntry("xl/workbook.xml");
                if (wb is not null)
                {
                    using var ws = wb.Open();
                    using var wxr = XmlReader.Create(ws, readerSettings);
                    var wxdoc = XDocument.Load(wxr);
                    var sheets = wxdoc.Descendants().Where(el => el.Name.LocalName == "sheet")
                        .Select(el => el.Attribute("name")?.Value).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                    if (sheets.Count > 0)
                        sb.AppendLine($"Sheets ({sheets.Count}): {string.Join(", ", sheets)}").AppendLine();
                }

                var shared = zip.GetEntry("xl/sharedStrings.xml");
                if (shared is not null)
                {
                    using var ss = shared.Open();
                    using var sxr = XmlReader.Create(ss, readerSettings);
                    var sxdoc = XDocument.Load(sxr);
                    foreach (var si in sxdoc.Descendants().Where(el => el.Name.LocalName == "si"))
                    {
                        ct.ThrowIfCancellationRequested();
                        var val = string.Concat(si.Descendants().Where(el => el.Name.LocalName == "t").Select(t => t.Value));
                        if (!string.IsNullOrWhiteSpace(val))
                            sb.AppendLine(val);
                        if (sb.Length > 32768) break;
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    private static PreviewInfo? TryBuildRtfPreview(FileSystemItem item, string subtitle, string attrs, CancellationToken ct)
    {
        string? plainText = null;
        using var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                if (ct.IsCancellationRequested) return;
                using var fs = new FileStream(item.FullPath, System.IO.FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var doc = new FlowDocument();
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                range.Load(fs, DataFormats.Rtf);
                plainText = range.Text;
            }
            catch
            {
                plainText = null;
            }
            finally
            {
                done.Set();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        WaitHandle.WaitAny([done.WaitHandle, ct.WaitHandle], millisecondsTimeout: 2000);

        if (string.IsNullOrWhiteSpace(plainText))
            return null;

        var trimmed = plainText.Length > MaxPreviewTextBytes ? plainText[..MaxPreviewTextBytes] : plainText;
        var truncated = plainText.Length > MaxPreviewTextBytes;
        var lines = trimmed.Count(c => c == '\n') + 1;
        var (iconBmp, _) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 192, ct);

        return CreatePreview(
            item,
            PreviewKind.Text,
            subtitle,
            attrs,
            previewImage: iconBmp,
            isVisualThumbnail: false,
            previewBadge: "Rich Text Document",
            textContent: trimmed,
            textStats: $"{lines:N0} lines • Rich Text",
            isTruncated: truncated);
    }

    private static PreviewInfo? TryBuildFontPreview(FileSystemItem item, string subtitle, string attrs, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var uri = new Uri(item.FullPath);
            var glyph = new GlyphTypeface(uri);
            var family = Fonts.GetFontFamilies(uri).FirstOrDefault();
            var familyName = glyph.Win32FamilyNames.Values.FirstOrDefault()
                             ?? glyph.FamilyNames.Values.FirstOrDefault()
                             ?? Path.GetFileNameWithoutExtension(item.Name);

            if (family is null)
                return null;

            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            const int width = 760;
            const int height = 430;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(250, 250, 252)),
                    null,
                    new Rect(0, 0, width, height),
                    14, 14);

                var ink = new SolidColorBrush(Color.FromRgb(24, 24, 27));
                var muted = new SolidColorBrush(Color.FromRgb(113, 113, 122));
                var uiTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

                var header = new FormattedText(
                    $"{familyName}  •  {glyph.GlyphCount:N0} glyphs",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    uiTypeface,
                    16,
                    muted,
                    1.0);
                dc.DrawText(header, new Point(32, 26));

                var hero = new FormattedText(
                    "Aa Bb Cc Dd Ee Ff Gg",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    46,
                    ink,
                    1.0)
                {
                    MaxTextWidth = width - 64
                };
                dc.DrawText(hero, new Point(32, 74));

                var sample = new FormattedText(
                    "The quick brown fox jumps over the lazy dog.\nPACK MY BOX WITH FIVE DOZEN LIQUOR JUGS.\n0123456789   !@#$%^&*()_+-=[]{};':\",./<>?",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    24,
                    ink,
                    1.0)
                {
                    MaxTextWidth = width - 64,
                    LineHeight = 36
                };
                dc.DrawText(sample, new Point(32, 165));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            return CreatePreview(
                item,
                PreviewKind.Image,
                subtitle,
                attrs,
                previewImage: rtb,
                isVisualThumbnail: true,
                previewBadge: "Font Specimen",
                imageDimensions: $"{familyName} • {glyph.GlyphCount:N0} glyphs");
        }
        catch
        {
            return null;
        }
    }

    private static PreviewInfo BuildRichFallbackPreview(FileSystemItem item, string subtitle, string attrs, CancellationToken ct)
    {
        byte[]? rawBuffer = null;
        long fileLength = item.SizeBytes;
        try
        {
            using var fs = new FileStream(item.FullPath, System.IO.FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fileLength = fs.Length;
            var toRead = (int)Math.Min(fs.Length, MaxPreviewTextBytes);
            rawBuffer = new byte[toRead];
            var totalRead = 0;
            while (totalRead < toRead)
            {
                ct.ThrowIfCancellationRequested();
                var read = fs.Read(rawBuffer, totalRead, toRead - totalRead);
                if (read == 0) break;
                totalRead += read;
            }
            if (totalRead < toRead)
                rawBuffer = rawBuffer.AsSpan(0, totalRead).ToArray();
        }
        catch
        {
            rawBuffer = null;
        }

        // Auto-detect UTF-8 / UTF-16 / ASCII text files even for unfamiliar or missing extensions (never treat .pdf as raw text)
        if (!item.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) &&
            rawBuffer is { Length: > 0 } && FormatHelper.IsLikelyTextBuffer(rawBuffer))
        {
            var text = DecodeTextBuffer(rawBuffer);
            var lineCount = text.Count(c => c == '\n') + (text.Length > 0 ? 1 : 0);
            var truncated = fileLength > MaxPreviewTextBytes;
            var (textIcon, _) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, 192, ct);

            return CreatePreview(
                item,
                PreviewKind.Text,
                subtitle,
                attrs,
                previewImage: textIcon,
                isVisualThumbnail: false,
                previewBadge: "Text Document",
                textContent: text,
                textStats: $"{lineCount:N0} lines{(truncated ? " (first 64 KB)" : "")} • {FormatHelper.FormatSize(fileLength)}",
                isTruncated: truncated);
        }

        var isPdf = item.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
        var (shellBitmap, isVisualThumb) = NativeShellService.ExtractShellPreviewBitmap(item.FullPath, isPdf ? 256 : 768, ct);
        if (isPdf)
            isVisualThumb = false;

        var signature = rawBuffer is not null ? DetectBinarySignature(item.Extension, rawBuffer) : null;

        if (isVisualThumb && shellBitmap is not null)
        {
            return CreatePreview(
                item,
                PreviewKind.Image,
                subtitle,
                attrs,
                previewImage: shellBitmap,
                isVisualThumbnail: true,
                previewBadge: signature ?? "Shell Preview",
                imageDimensions: $"{(int)shellBitmap.PixelWidth} × {(int)shellBitmap.PixelHeight} px • {(signature ?? "Shell preview")}");
        }

        var appName = NativeShellService.GetAssociatedAppName(item.Extension);
        var extLabel = string.IsNullOrEmpty(item.Extension) ? "this file type" : $"{item.Extension} files";
        string guidance;
        if (rawBuffer is { Length: 0 })
        {
            guidance = appName is not null
                ? $"Empty file (0 bytes).\n\nOpen with: {appName}\nDouble-click or press Enter to open in {appName}."
                : "Empty file (0 bytes).";
        }
        else if (appName is not null)
        {
            guidance = $"{(signature is not null ? $"Format: {signature}\n" : "")}Open with: {appName}\n\nDouble-click or press Enter to open this file in {appName}.";
        }
        else
        {
            guidance = $"{(signature is not null ? $"Format: {signature}\n" : "")}No default application is associated with {extLabel}.\n\nRight-click and choose \"Open with\" to pick an app.";
        }

        var badge = appName is not null ? $"Open with {appName}" : (signature ?? "No Default App");
        var stats = appName is not null
            ? $"Default app: {appName}"
            : (signature is not null ? $"{signature} • No default app" : "No default app assigned");
        var openLabel = appName is not null ? $"Open with {appName}" : "Choose App to Open";

        return CreatePreview(
            item,
            PreviewKind.Generic,
            subtitle,
            attrs,
            previewImage: shellBitmap,
            isVisualThumbnail: false,
            canZoom: false,
            openPromptLabel: openLabel,
            previewBadge: badge,
            imageDimensions: shellBitmap is not null ? $"{(int)shellBitmap.PixelWidth} × {(int)shellBitmap.PixelHeight} px icon" : null,
            textContent: guidance,
            textStats: stats);
    }

    private static bool TryReadTextChunk(
        string path,
        CancellationToken ct,
        out string? text,
        out int lineCount,
        out long fileLength,
        out bool truncated)
    {
        text = null;
        lineCount = 0;
        fileLength = 0;
        truncated = false;

        try
        {
            using var fs = new FileStream(path, System.IO.FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fileLength = fs.Length;
            var toRead = (int)Math.Min(fs.Length, MaxPreviewTextBytes);
            var buffer = new byte[toRead];
            var totalRead = 0;
            while (totalRead < toRead)
            {
                ct.ThrowIfCancellationRequested();
                var read = fs.Read(buffer, totalRead, toRead - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            truncated = fs.Length > MaxPreviewTextBytes;
            text = DecodeTextBuffer(buffer.AsSpan(0, totalRead));
            lineCount = text.Count(c => c == '\n') + (text.Length > 0 ? 1 : 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? DetectBinarySignature(string ext, ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "PNG Image";
        if (data.Length >= 5 && data[0] == (byte)'%' && data[1] == (byte)'P' && data[2] == (byte)'D' && data[3] == (byte)'F' && data[4] == (byte)'-')
        {
            var verEnd = Math.Min(data.Length, 10);
            var ver = Encoding.ASCII.GetString(data[5..verEnd]).Split('\r', '\n', ' ')[0];
            return $"PDF {ver} Document";
        }
        if (data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A) // MZ
        {
            if (data.Length >= 64)
            {
                var peOffset = BitConverter.ToInt32(data[60..64]);
                if (peOffset > 0 && peOffset + 6 <= data.Length &&
                    data[peOffset] == 0x50 && data[peOffset + 1] == 0x45 && data[peOffset + 2] == 0 && data[peOffset + 3] == 0)
                {
                    var machine = BitConverter.ToUInt16(data[(peOffset + 4)..(peOffset + 6)]);
                    var arch = machine switch
                    {
                        0x8664 => "x64",
                        0x014C => "x86",
                        0xAA64 => "ARM64",
                        _ => $"0x{machine:X4}"
                    };
                    return $"PE Executable ({arch})";
                }
            }
            return "Windows Executable (MZ)";
        }
        if (data.Length >= 16 && Encoding.ASCII.GetString(data[..15]) == "SQLite format 3")
            return "SQLite 3 Database";
        if (data.Length >= 6 && data[0] == 0x37 && data[1] == 0x7A && data[2] == 0xBC && data[3] == 0xAF && data[4] == 0x27 && data[5] == 0x1C)
            return "7-Zip Archive";
        if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x61 && data[2] == 0x72 && data[3] == 0x21)
            return "RAR Archive";
        if (data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
            return ext.Equals(".docx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
                ? "Office Open XML Document"
                : "ZIP Container";
        if (data.Length >= 12 && data[4] == (byte)'f' && data[5] == (byte)'t' && data[6] == (byte)'y' && data[7] == (byte)'p')
            return "ISO Base Media / MP4";
        if (data.Length >= 4 && data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3)
            return "Matroska / WebM Video";
        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F')
            return Encoding.ASCII.GetString(data[8..12]) switch
            {
                "WAVE" => "WAVE Audio",
                "AVI " => "AVI Video",
                "WEBP" => "WebP Image",
                _ => "RIFF Media"
            };
        if (data.Length >= 3 && data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3')
            return "MP3 Audio (ID3)";
        if (data.Length >= 4 && data[0] == (byte)'f' && data[1] == (byte)'L' && data[2] == (byte)'a' && data[3] == (byte)'C')
            return "FLAC Lossless Audio";
        if (data.Length >= 4 && data[0] == (byte)'O' && data[1] == (byte)'g' && data[2] == (byte)'g' && data[3] == (byte)'S')
            return "Ogg Media Stream";
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
            return "GZIP Compressed Stream";
        if (data.Length >= 4 && data[0] == 0x7F && data[1] == (byte)'E' && data[2] == (byte)'L' && data[3] == (byte)'F')
            return "ELF Executable";
        if (data.Length >= 4 && data[0] == 0x00 && data[1] == (byte)'a' && data[2] == (byte)'s' && data[3] == (byte)'m')
            return "WebAssembly Module";
        if (data.Length >= 4 && data[0] == (byte)'8' && data[1] == (byte)'B' && data[2] == (byte)'P' && data[3] == (byte)'S')
            return "Adobe Photoshop Document";
        if (data.Length >= 4 && ((data[0] == 0x00 && data[1] == 0x01 && data[2] == 0x00 && data[3] == 0x00) || (data[0] == (byte)'O' && data[1] == (byte)'T' && data[2] == (byte)'T' && data[3] == (byte)'O')))
            return "TrueType / OpenType Font";
        return null;
    }

    private static string FormatHexDump(ReadOnlySpan<byte> data, int maxBytes)
    {
        var count = Math.Min(data.Length, maxBytes);
        var sb = new StringBuilder(count * 5);
        for (var offset = 0; offset < count; offset += 16)
        {
            if (offset > 0) sb.AppendLine();
            sb.Append($"{offset:X4}: ");
            var rowLen = Math.Min(16, count - offset);
            for (var i = 0; i < 16; i++)
            {
                if (i == 8) sb.Append(' ');
                if (i < rowLen) sb.Append($"{data[offset + i]:X2} ");
                else sb.Append("   ");
            }
            sb.Append(" |");
            for (var i = 0; i < rowLen; i++)
            {
                var b = data[offset + i];
                sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
            }
            sb.Append('|');
        }
        return sb.ToString();
    }

    private static PreviewInfo CreatePreview(
        FileSystemItem item,
        PreviewKind kind,
        string subtitle,
        string attrs,
        BitmapSource? previewImage = null,
        Uri? videoSource = null,
        bool isVisualThumbnail = false,
        bool canZoom = true,
        string? openPromptLabel = null,
        string? previewBadge = null,
        string? imageDimensions = null,
        string? textContent = null,
        string? textStats = null,
        bool isTruncated = false,
        int? folderFileCount = null,
        int? folderSubdirCount = null) => new()
    {
        Kind = kind,
        Title = item.Name,
        Subtitle = subtitle,
        FullPath = item.FullPath,
        PreviewImage = previewImage,
        VideoSource = videoSource,
        IsVisualThumbnail = isVisualThumbnail,
        CanZoom = canZoom,
        OpenPromptLabel = openPromptLabel,
        PreviewBadge = previewBadge,
        ImageDimensions = imageDimensions,
        TextContent = textContent,
        TextStats = textStats,
        IsTruncated = isTruncated,
        ExactSizeText = item.IsDirectory ? string.Empty : FormatHelper.FormatExactSize(item.SizeBytes),
        CreatedText = item.DateCreated.ToString("yyyy-MM-dd HH:mm"),
        ModifiedText = item.DateModifiedDisplay,
        AccessedText = item.DateAccessed.ToString("yyyy-MM-dd HH:mm"),
        AttributesText = attrs,
        FolderFileCount = folderFileCount,
        FolderSubdirCount = folderSubdirCount
    };

    public Task<(int Files, int Folders, long TotalBytes)> CalculateFolderStatsAsync(string folderPath, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            int files = 0;
            int folders = 0;
            long bytes = 0;
            var queue = new Queue<string>();
            queue.Enqueue(folderPath);
            var opts = new EnumerationOptions { IgnoreInaccessible = true };

            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                try
                {
                    foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos("*", opts))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (entry is DirectoryInfo sub)
                        {
                            folders++;
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                                queue.Enqueue(sub.FullName);
                        }
                        else if (entry is FileInfo f)
                        {
                            files++;
                            bytes += f.Length;
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible subfolders
                }
            }
            return (files, folders, bytes);
        }, ct);
    }

    public static BitmapImage? LoadThumbnail(string filePath, int decodeWidth = 140)
    {
        try
        {
            using var fs = new FileStream(filePath, System.IO.FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static FileSystemItem CreateItem(FileSystemInfo entry)
    {
        var isDir = entry is DirectoryInfo;
        var fileInfo = entry as FileInfo;
        var size = isDir ? 0L : SafeFileLength(fileInfo);
        var attrs = entry.Attributes;

        return new FileSystemItem
        {
            Name = entry.Name,
            FullPath = entry.FullName,
            ParentPath = Path.GetDirectoryName(entry.FullName) ?? string.Empty,
            IsDirectory = isDir,
            IsHidden = (attrs & FileAttributes.Hidden) != 0,
            IsSystem = (attrs & FileAttributes.System) != 0,
            IsReadOnly = (attrs & FileAttributes.ReadOnly) != 0,
            SizeBytes = size,
            SizeDisplay = isDir ? string.Empty : FormatHelper.FormatSize(size),
            DateModified = SafeDate(() => entry.LastWriteTime),
            DateCreated = SafeDate(() => entry.CreationTime),
            DateAccessed = SafeDate(() => entry.LastAccessTime),
            Extension = isDir ? string.Empty : entry.Extension,
            TypeDescription = isDir ? "File folder" : (string.IsNullOrEmpty(entry.Extension) ? "File" : $"{entry.Extension.TrimStart('.').ToUpperInvariant()} File")
        };
    }

    private static long SafeFileLength(FileInfo? file)
    {
        if (file is null) return 0;
        try { return file.Length; } catch { return 0; }
    }

    private static DateTime SafeDate(Func<DateTime> getter)
    {
        try { return getter(); } catch { return DateTime.MinValue; }
    }

    private static string BuildAttributesText(FileSystemItem item)
    {
        var parts = new List<string>();
        if (item.IsReadOnly) parts.Add("Read-only");
        if (item.IsHidden) parts.Add("Hidden");
        if (item.IsSystem) parts.Add("System");
        return parts.Count == 0 ? "Standard" : string.Join(", ", parts);
    }

    private static string DecodeTextBuffer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes[2..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool SameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private static long EstimateTotalBytes(IReadOnlyList<string> sourcePaths, CancellationToken ct)
    {
        long total = 0;
        foreach (var path in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                try { total += new FileInfo(path).Length; } catch { }
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    {
                        ct.ThrowIfCancellationRequested();
                        total += f.Length;
                    }
                }
                catch { }
            }
        }
        return total;
    }

    private static async Task CopyFileBufferedAsync(string src, string dest, CancellationToken ct, Action<long> onBytesCopied)
    {
        const int bufferSize = 128 * 1024;
        var buffer = new byte[bufferSize];
        await using var input = new FileStream(src, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var output = new FileStream(dest, System.IO.FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            onBytesCopied(read);
        }
    }

    private static async Task CopyDirectoryRecursiveAsync(string sourceDir, string destDir, CancellationToken ct, Action<long> onBytesCopied)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            var targetFile = Path.Combine(destDir, Path.GetFileName(file));
            await CopyFileBufferedAsync(file, targetFile, ct, onBytesCopied);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0) continue;
            var targetSub = Path.Combine(destDir, Path.GetFileName(subDir));
            await CopyDirectoryRecursiveAsync(subDir, targetSub, ct, onBytesCopied);
        }
    }

    private static string GetDriveTypeLabel(DriveType type) => type switch
    {
        DriveType.Fixed => "Local Disk",
        DriveType.Removable => "Removable Drive",
        DriveType.Network => "Network Drive",
        DriveType.CDRom => "Optical Drive",
        DriveType.Ram => "RAM Disk",
        _ => "Drive"
    };
}
