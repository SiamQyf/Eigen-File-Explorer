using System.IO;
using Eigen.Models;

namespace Eigen.Services;

public static class FormatHelper
{
    public const string HomeLocation = "eigen://home";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".tif", ".tiff"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts", ".flv"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".nupkg", ".msix", ".appx", ".vsix", ".jar", ".whl", ".cbz", ".epub"
    };

    private static readonly HashSet<string> TextPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".jsonc", ".jsonl", ".ndjson", ".xml", ".yaml", ".yml",
        ".toml", ".ini", ".cfg", ".conf", ".log", ".csv", ".tsv", ".cs", ".xaml", ".csproj", ".sln",
        ".props", ".targets", ".pubxml", ".appxmanifest", ".py", ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
        ".html", ".htm", ".css", ".scss", ".less", ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".sh", ".bash",
        ".zsh", ".sql", ".rs", ".go", ".java", ".kt", ".kts", ".swift", ".dart", ".lua", ".rb", ".php",
        ".cpp", ".cxx", ".cc", ".c", ".h", ".hpp", ".r", ".tex", ".srt", ".vtt", ".reg", ".inf", ".iss",
        ".gitignore", ".gitattributes", ".gitmodules", ".editorconfig", ".npmrc", ".env", ".svg"
    };

    public static bool IsHomePath(string? path)
        => string.IsNullOrWhiteSpace(path)
           || string.Equals(path.Trim(), HomeLocation, StringComparison.OrdinalIgnoreCase)
           || string.Equals(path.Trim(), "Home", StringComparison.OrdinalIgnoreCase);

    public static bool IsImageExtension(string? ext)
        => !string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext);

    public static bool IsVideoExtension(string? ext)
        => !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);

    public static bool IsArchiveExtension(string? ext)
        => !string.IsNullOrEmpty(ext) && ArchiveExtensions.Contains(ext);

    public static bool IsTextPreviewExtension(string? ext, string? fileName = null)
    {
        if (!string.IsNullOrEmpty(ext) && TextPreviewExtensions.Contains(ext))
            return true;
        if (!string.IsNullOrEmpty(fileName))
        {
            var upper = fileName.ToUpperInvariant();
            return upper is "README" or "LICENSE" or "MAKEFILE" or "DOCKERFILE" or "CHANGELOG"
                or ".GITIGNORE" or ".GITATTRIBUTES" or ".ENV" or ".EDITORCONFIG";
        }
        return false;
    }

    public static bool IsLikelyTextBuffer(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
            return false;

        // UTF-16 BOM
        if (sample.Length >= 2 && ((sample[0] == 0xFF && sample[1] == 0xFE) || (sample[0] == 0xFE && sample[1] == 0xFF)))
            return true;

        var checkLen = Math.Min(sample.Length, 4096);
        var controlCount = 0;
        for (var i = 0; i < checkLen; i++)
        {
            var b = sample[i];
            if (b == 0)
                return false;
            if (b < 0x20 && b is not (byte)'\t' and not (byte)'\r' and not (byte)'\n' and not (byte)'\f')
                controlCount++;
        }

        return controlCount <= checkLen * 0.02;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return string.Empty;
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unitIndex = -1;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024.0;
            unitIndex++;
        }
        return value >= 100
            ? $"{value:0} {units[unitIndex]}"
            : value >= 10
                ? $"{value:0.#} {units[unitIndex]}"
                : $"{value:0.##} {units[unitIndex]}";
    }

    public static string FormatExactSize(long bytes)
        => bytes < 1024
            ? $"{bytes:N0} bytes"
            : $"{bytes:N0} bytes ({FormatSize(bytes)})";

    public static string ExpandNavigationPath(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return HomeLocation;

        var trimmed = rawInput.Trim().Trim('"');
        if (IsHomePath(trimmed) || string.Equals(trimmed, "This PC", StringComparison.OrdinalIgnoreCase))
            return HomeLocation;

        if (trimmed == "~" || trimmed.StartsWith("~\\", StringComparison.Ordinal) || trimmed.StartsWith("~/", StringComparison.Ordinal))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            trimmed = trimmed.Length == 1
                ? userProfile
                : Path.Combine(userProfile, trimmed[2..]);
        }

        trimmed = Environment.ExpandEnvironmentVariables(trimmed).Replace('/', '\\');

        if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            return trimmed + "\\";

        if (trimmed.Length > 3 && trimmed.EndsWith('\\') && !trimmed.StartsWith("\\\\", StringComparison.Ordinal))
            trimmed = trimmed.TrimEnd('\\');

        return trimmed;
    }

    public static string GetParentPath(string path)
    {
        if (IsHomePath(path))
            return HomeLocation;

        var normalized = ExpandNavigationPath(path);
        try
        {
            var root = Path.GetPathRoot(normalized);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(normalized.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return HomeLocation;
            }

            var parent = Directory.GetParent(normalized);
            return parent?.FullName ?? HomeLocation;
        }
        catch
        {
            return HomeLocation;
        }
    }

    public static List<BreadcrumbSegment> BuildBreadcrumbs(string path)
    {
        var list = new List<BreadcrumbSegment>
        {
            new()
            {
                Name = "Home",
                FullPath = HomeLocation,
                IsRoot = true
            }
        };

        if (IsHomePath(path))
        {
            list[0].IsLast = true;
            return list;
        }

        var normalized = ExpandNavigationPath(path);
        var root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
        {
            list.Add(new BreadcrumbSegment { Name = normalized, FullPath = normalized, IsLast = true });
            return list;
        }

        var rootDisplay = root.TrimEnd('\\');
        if (!root.StartsWith("\\\\", StringComparison.Ordinal) && rootDisplay.EndsWith(':'))
            rootDisplay = $"({rootDisplay})";

        list.Add(new BreadcrumbSegment
        {
            Name = rootDisplay,
            FullPath = root,
            IsRoot = true
        });

        var remainder = normalized[root.Length..].Trim('\\');
        if (!string.IsNullOrEmpty(remainder))
        {
            var current = root;
            foreach (var part in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                list.Add(new BreadcrumbSegment
                {
                    Name = part,
                    FullPath = current
                });
            }
        }

        list[^1].IsLast = true;
        return list;
    }

    public static string GetDisplayTitle(string path)
    {
        if (IsHomePath(path))
            return "Home";

        var normalized = ExpandNavigationPath(path);
        var root = Path.GetPathRoot(normalized);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(normalized.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            return $"Drive {root.TrimEnd('\\')}";
        }

        var name = Path.GetFileName(normalized.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? normalized : name;
    }

    public static string GenerateUniqueName(string directory, string baseName, string extension)
    {
        var candidate = string.IsNullOrEmpty(extension) ? baseName : $"{baseName}{extension}";
        var full = Path.Combine(directory, candidate);
        if (!File.Exists(full) && !Directory.Exists(full))
            return candidate;

        var counter = 2;
        while (true)
        {
            candidate = string.IsNullOrEmpty(extension)
                ? $"{baseName} ({counter})"
                : $"{baseName} ({counter}){extension}";
            full = Path.Combine(directory, candidate);
            if (!File.Exists(full) && !Directory.Exists(full))
                return candidate;
            counter++;
        }
    }

    public static bool IsSubPathOrSelf(string candidateChild, string parentFolder)
    {
        var childNorm = Path.GetFullPath(candidateChild).TrimEnd('\\') + "\\";
        var parentNorm = Path.GetFullPath(parentFolder).TrimEnd('\\') + "\\";
        return childNorm.StartsWith(parentNorm, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsValidFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = name.Trim();
        if (trimmed is "." or "..") return false;
        var invalid = Path.GetInvalidFileNameChars();
        return trimmed.IndexOfAny(invalid) < 0;
    }
}

public sealed class NaturalStringComparer : IComparer<string?>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var ix = 0;
        var iy = 0;

        while (ix < x.Length && iy < y.Length)
        {
            var cx = x[ix];
            var cy = y[iy];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                var startX = ix;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                var startY = iy;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                var spanX = x.AsSpan(startX, ix - startX);
                var spanY = y.AsSpan(startY, iy - startY);

                var trimX = spanX.TrimStart('0');
                var trimY = spanY.TrimStart('0');

                if (trimX.Length != trimY.Length)
                    return trimX.Length.CompareTo(trimY.Length);

                var cmpNum = trimX.CompareTo(trimY, StringComparison.Ordinal);
                if (cmpNum != 0)
                    return cmpNum;

                if (spanX.Length != spanY.Length)
                    return spanX.Length.CompareTo(spanY.Length);
            }
            else
            {
                var ux = char.ToUpperInvariant(cx);
                var uy = char.ToUpperInvariant(cy);
                if (ux != uy)
                    return ux.CompareTo(uy);
                ix++;
                iy++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}
