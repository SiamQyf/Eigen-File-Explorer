using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Eigen.Models;

namespace Eigen.Services;

public static class VectorIconPackService
{
    public enum IconCategory
    {
        Folder,
        Drive,
        Code,
        Image,
        Text,
        Archive,
        Audio,
        Video,
        Executable,
        Generic
    }

    private static readonly ConcurrentDictionary<string, ImageSource> Cache =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> CodeExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".py", ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
        ".html", ".htm", ".css", ".scss", ".json", ".xml", ".yaml", ".yml",
        ".toml", ".rs", ".go", ".java", ".cpp", ".c", ".h", ".hpp", ".ps1",
        ".sh", ".sql", ".csproj", ".sln", ".props", ".targets"
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico", ".svg", ".tif", ".tiff"
    };

    private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".pdf", ".doc", ".docx", ".rtf", ".csv", ".ini", ".cfg"
    };

    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso"
    };

    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".aac", ".m4a", ".wma"
    };

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v"
    };

    private static readonly HashSet<string> ExeExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".lnk", ".dll"
    };

    public static IconCategory Classify(string path, bool isDirectory, bool isDrive)
    {
        if (isDrive) return IconCategory.Drive;
        if (isDirectory) return IconCategory.Folder;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return IconCategory.Generic;
        if (CodeExts.Contains(ext)) return IconCategory.Code;
        if (ImageExts.Contains(ext)) return IconCategory.Image;
        if (TextExts.Contains(ext)) return IconCategory.Text;
        if (ArchiveExts.Contains(ext)) return IconCategory.Archive;
        if (AudioExts.Contains(ext)) return IconCategory.Audio;
        if (VideoExts.Contains(ext)) return IconCategory.Video;
        if (ExeExts.Contains(ext)) return IconCategory.Executable;
        return IconCategory.Generic;
    }

    public static ImageSource GetThemedIcon(IconPackStyle pack, string path, bool isDirectory, bool isDrive, EigenSkin skin)
    {
        var category = Classify(path, isDirectory, isDrive);
        var key = $"{pack}|{skin.Accent}|{skin.TextPrimary}|{skin.BgContent}|{category}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var image = RenderIcon(pack, category, skin);
        if (Cache.Count < 4096)
            Cache.TryAdd(key, image);
        return image;
    }

    public static ImageSource GetEigenArchiverIcon(EigenSkin skin)
    {
        var key = $"EigenArchiver|{skin.Accent}|{skin.AccentHover}|{skin.TextPrimary}|{skin.BgContent}|{skin.TextOnAccent}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var accent = (Color)ColorConverter.ConvertFromString(skin.Accent);
        var accentHover = (Color)ColorConverter.ConvertFromString(skin.AccentHover);
        var onAccent = (Color)ColorConverter.ConvertFromString(skin.TextOnAccent);
        var bgElevated = (Color)ColorConverter.ConvertFromString(skin.BgElevated);

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            Brushes.Transparent,
            null,
            new RectangleGeometry(new Rect(0, 0, 32, 32))));

        // Stacked 3-band Eigen Archiver box (distinctive archive binder look)
        var topBand = FrozenBrush(accentHover);
        var midBand = FrozenBrush(accent);
        var botBand = FrozenBrush(Color.FromRgb((byte)(accent.R * 0.72), (byte)(accent.G * 0.72), (byte)(accent.B * 0.72)));
        var claspBrush = FrozenBrush(onAccent);
        var rimPen = FrozenPen(Color.FromArgb(140, bgElevated.R, bgElevated.G, bgElevated.B), 1.0);

        group.Children.Add(new GeometryDrawing(botBand, rimPen, new RectangleGeometry(new Rect(4, 20, 24, 8), 2.5, 2.5)));
        group.Children.Add(new GeometryDrawing(midBand, rimPen, new RectangleGeometry(new Rect(4, 12, 24, 9), 2.5, 2.5)));
        group.Children.Add(new GeometryDrawing(topBand, rimPen, new RectangleGeometry(new Rect(4, 4, 24, 9), 2.5, 2.5)));

        // Center zipper clasp & teeth
        group.Children.Add(new GeometryDrawing(claspBrush, null, new RectangleGeometry(new Rect(13.5, 5.5, 5, 19), 1.5, 1.5)));
        group.Children.Add(new GeometryDrawing(midBand, null, new RectangleGeometry(new Rect(14.8, 12, 2.4, 5), 0.8, 0.8)));

        group.Freeze();
        var img = new DrawingImage(group);
        img.Freeze();

        if (Cache.Count < 4096)
            Cache.TryAdd(key, img);
        return img;
    }

    private static ImageSource RenderIcon(IconPackStyle pack, IconCategory category, EigenSkin skin)
    {
        var accent = (Color)ColorConverter.ConvertFromString(skin.Accent);
        var accentHover = (Color)ColorConverter.ConvertFromString(skin.AccentHover);
        var textPrimary = (Color)ColorConverter.ConvertFromString(skin.TextPrimary);
        var textSecondary = (Color)ColorConverter.ConvertFromString(skin.TextSecondary);
        var bgContent = (Color)ColorConverter.ConvertFromString(skin.BgContent);
        var bgElevated = (Color)ColorConverter.ConvertFromString(skin.BgElevated);
        var onAccent = (Color)ColorConverter.ConvertFromString(skin.TextOnAccent);

        var group = new DrawingGroup();
        // Transparent 32x32 bounding box so all icons share identical aspect/padding
        group.Children.Add(new GeometryDrawing(
            Brushes.Transparent,
            null,
            new RectangleGeometry(new Rect(0, 0, 32, 32))));

        switch (pack)
        {
            case IconPackStyle.NeonWire:
                DrawNeonWire(group, category, accent, accentHover, textPrimary, bgContent);
                break;
            case IconPackStyle.GlassFrost:
                DrawGlassFrost(group, category, accent, accentHover, textPrimary, onAccent);
                break;
            case IconPackStyle.RetroPixel:
                DrawRetroPixel(group, category, accent, accentHover, textPrimary, textSecondary, bgContent);
                break;
            case IconPackStyle.TactileBrutal:
                DrawTactileBrutal(group, category, accent, accentHover, textPrimary, bgElevated, onAccent);
                break;
            case IconPackStyle.Fluent3D:
                DrawFluent3D(group, category, accent, accentHover, textPrimary, bgElevated, onAccent);
                break;
            case IconPackStyle.SoftPastel:
                DrawSoftPastel(group, category, accent, accentHover, textPrimary, bgContent, onAccent);
                break;
            case IconPackStyle.MonochromeInk:
                DrawMonochromeInk(group, category, accent, textPrimary, textSecondary, bgContent);
                break;
            case IconPackStyle.BlueprintGrid:
                DrawBlueprintGrid(group, category, accent, accentHover, textPrimary, bgContent);
                break;
            case IconPackStyle.DuotoneMinimal:
            default:
                DrawDuotoneMinimal(group, category, accent, accentHover, textSecondary, onAccent);
                break;
        }

        group.Freeze();
        var img = new DrawingImage(group);
        img.Freeze();
        return img;
    }

    private static void DrawNeonWire(DrawingGroup g, IconCategory cat, Color accent, Color glow, Color text, Color bg)
    {
        var fillTint = FrozenBrush(Color.FromArgb(38, accent.R, accent.G, accent.B));
        var innerGlow = FrozenBrush(Color.FromArgb(65, glow.R, glow.G, glow.B));
        var wirePen = FrozenPen(accent, 1.75);
        var crispPen = FrozenPen(glow, 1.3);

        if (cat == IconCategory.Folder)
        {
            var folderGeo = Geometry.Parse("M 3,8 C 3,6.8 3.9,6 5,6 L 12.2,6 L 14.5,9 L 27,9 C 28.1,9 29,9.9 29,11 L 29,25 C 29,26.1 28.1,27 27,27 L 5,27 C 3.9,27 3,26.1 3,25 Z");
            g.Children.Add(new GeometryDrawing(fillTint, wirePen, folderGeo));
            g.Children.Add(new GeometryDrawing(null, crispPen, new LineGeometry(new Point(6, 13.5), new Point(26, 13.5))));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(fillTint, wirePen, new RectangleGeometry(new Rect(3, 8, 26, 16), 3, 3)));
            g.Children.Add(new GeometryDrawing(null, crispPen, new LineGeometry(new Point(6, 19), new Point(19, 19))));
            g.Children.Add(new GeometryDrawing(FrozenBrush(glow), null, new EllipseGeometry(new Point(24.5, 19), 2, 2)));
            return;
        }

        var docGeo = Geometry.Parse("M 6,3 L 20,3 L 26,9 L 26,28 C 26,28.6 25.6,29 25,29 L 7,29 C 6.4,29 6,28.6 6,28 Z");
        var secondary = cat == IconCategory.Generic ? text : accent;
        var docPen = FrozenPen(secondary, 1.65);
        g.Children.Add(new GeometryDrawing(innerGlow, docPen, docGeo));
        g.Children.Add(new GeometryDrawing(null, docPen, Geometry.Parse("M 20,3 L 20,9 L 26,9")));
        DrawCategoryGlyph(g, cat, glow, crispPen);
    }

    private static void DrawGlassFrost(DrawingGroup g, IconCategory cat, Color accent, Color accentHover, Color text, Color onAccent)
    {
        var backGlass = FrozenBrush(Color.FromArgb(145, accent.R, accent.G, accent.B));
        var frontGlass = FrozenBrush(Color.FromArgb(205, accentHover.R, accentHover.G, accentHover.B));
        var sheen = FrozenBrush(Color.FromArgb(85, 255, 255, 255));
        var rimPen = FrozenPen(Color.FromArgb(145, 255, 255, 255), 1.1);

        if (cat == IconCategory.Folder)
        {
            g.Children.Add(new GeometryDrawing(backGlass, rimPen, Geometry.Parse("M 3,8 C 3,6.5 4.2,5.5 5.6,5.5 L 12.2,5.5 L 14.8,8.5 L 26.5,8.5 C 27.9,8.5 29,9.6 29,11 L 29,24.5 C 29,25.9 27.9,27 26.5,27 L 5.5,27 C 4.1,27 3,25.9 3,24.5 Z")));
            g.Children.Add(new GeometryDrawing(frontGlass, rimPen, new RectangleGeometry(new Rect(3, 11.5, 26, 15.5), 4, 4)));
            g.Children.Add(new GeometryDrawing(sheen, null, Geometry.Parse("M 5,13 L 23,13 L 16,20 L 5,20 Z")));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(frontGlass, rimPen, new RectangleGeometry(new Rect(3, 8, 26, 16), 5, 5)));
            g.Children.Add(new GeometryDrawing(sheen, null, new RectangleGeometry(new Rect(5, 10, 22, 5), 2, 2)));
            g.Children.Add(new GeometryDrawing(FrozenBrush(onAccent), null, new EllipseGeometry(new Point(24, 19), 2.1, 2.1)));
            return;
        }

        var page = Geometry.Parse("M 6,4 C 6,3.4 6.4,3 7,3 L 19.5,3 L 26,9.5 L 26,27.5 C 26,28.3 25.3,29 24.5,29 L 7.5,29 C 6.7,29 6,28.3 6,27.5 Z");
        g.Children.Add(new GeometryDrawing(frontGlass, rimPen, page));
        g.Children.Add(new GeometryDrawing(sheen, null, Geometry.Parse("M 19.5,3 L 19.5,9.5 L 26,9.5 Z")));
        DrawCategoryGlyph(g, cat, onAccent, FrozenPen(onAccent, 1.5));
    }

    private static void DrawRetroPixel(DrawingGroup g, IconCategory cat, Color accent, Color glow, Color text, Color textSec, Color bg)
    {
        var body = FrozenBrush(accent);
        var light = FrozenBrush(glow);
        var dark = FrozenBrush(Color.FromArgb(220, bg.R, bg.G, bg.B));
        var ink = FrozenBrush(text);

        if (cat == IconCategory.Folder)
        {
            // Pixel folder steps (2px grid)
            g.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(4, 6, 10, 4))));
            g.Children.Add(new GeometryDrawing(body, null, new RectangleGeometry(new Rect(4, 10, 24, 16))));
            g.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(4, 10, 24, 2))));
            g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(6, 24, 22, 2))));
            g.Children.Add(new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(8, 15, 6, 2))));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(body, null, new RectangleGeometry(new Rect(4, 10, 24, 12))));
            g.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(4, 10, 24, 2))));
            g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(6, 18, 12, 2))));
            g.Children.Add(new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(22, 16, 4, 4))));
            return;
        }

        // Pixel page
        var pageBrush = cat == IconCategory.Generic ? FrozenBrush(textSec) : body;
        g.Children.Add(new GeometryDrawing(pageBrush, null, new RectangleGeometry(new Rect(6, 4, 16, 24))));
        g.Children.Add(new GeometryDrawing(pageBrush, null, new RectangleGeometry(new Rect(22, 8, 4, 20))));
        g.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(20, 4, 2, 4))));
        g.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(22, 6, 4, 2))));
        g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(10, 14, 12, 2))));
        g.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(10, 18, 8, 2))));
        if (cat == IconCategory.Code)
            g.Children.Add(new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(10, 10, 6, 2))));
        else if (cat == IconCategory.Image)
            g.Children.Add(new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(18, 10, 4, 4))));
    }

    private static void DrawTactileBrutal(DrawingGroup g, IconCategory cat, Color accent, Color accentHover, Color ink, Color elevated, Color onAccent)
    {
        var inkBrush = FrozenBrush(ink);
        var inkPen = FrozenPen(ink, 2.0);
        var fill = FrozenBrush(accent);
        var card = FrozenBrush(elevated);

        if (cat == IconCategory.Folder)
        {
            // Hard offset shadow block
            g.Children.Add(new GeometryDrawing(inkBrush, null, new RectangleGeometry(new Rect(6, 11, 24, 17))));
            g.Children.Add(new GeometryDrawing(FrozenBrush(accentHover), inkPen, Geometry.Parse("M 3,6 L 13,6 L 15,9.5 L 27,9.5 L 27,25.5 L 3,25.5 Z")));
            g.Children.Add(new GeometryDrawing(fill, inkPen, new RectangleGeometry(new Rect(3, 11.5, 24, 14))));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(inkBrush, null, new RectangleGeometry(new Rect(6, 11, 24, 14))));
            g.Children.Add(new GeometryDrawing(fill, inkPen, new RectangleGeometry(new Rect(3, 8, 24, 14))));
            g.Children.Add(new GeometryDrawing(FrozenBrush(onAccent), null, new RectangleGeometry(new Rect(19, 13.5, 4, 4))));
            return;
        }

        // Sharp neo-brutalist page with offset shadow
        g.Children.Add(new GeometryDrawing(inkBrush, null, new RectangleGeometry(new Rect(8, 6, 19, 23))));
        var docFill = cat == IconCategory.Generic ? card : fill;
        var glyphCol = cat == IconCategory.Generic ? ink : onAccent;
        g.Children.Add(new GeometryDrawing(docFill, inkPen, new RectangleGeometry(new Rect(5, 3, 19, 23))));
        DrawCategoryGlyph(g, cat, glyphCol, FrozenPen(glyphCol, 1.8));
    }

    private static void DrawDuotoneMinimal(DrawingGroup g, IconCategory cat, Color accent, Color accentHover, Color secondary, Color onAccent)
    {
        var primary = FrozenBrush(accent);
        var soft = FrozenBrush(Color.FromArgb(145, accentHover.R, accentHover.G, accentHover.B));
        var muted = FrozenBrush(Color.FromArgb(180, secondary.R, secondary.G, secondary.B));

        if (cat == IconCategory.Folder)
        {
            g.Children.Add(new GeometryDrawing(soft, null, Geometry.Parse("M 3,7.5 C 3,6.4 3.9,5.5 5,5.5 L 12.2,5.5 L 14.8,8.5 L 27,8.5 C 28.1,8.5 29,9.4 29,10.5 L 29,24.5 C 29,25.6 28.1,26.5 27,26.5 L 5,26.5 C 3.9,26.5 3,25.6 3,24.5 Z")));
            g.Children.Add(new GeometryDrawing(primary, null, new RectangleGeometry(new Rect(3, 11.5, 26, 15), 3.5, 3.5)));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(soft, null, new RectangleGeometry(new Rect(3, 8, 26, 16), 4, 4)));
            g.Children.Add(new GeometryDrawing(primary, null, new RectangleGeometry(new Rect(3, 15, 26, 9), 3, 3)));
            g.Children.Add(new GeometryDrawing(FrozenBrush(onAccent), null, new EllipseGeometry(new Point(24, 19.5), 1.8, 1.8)));
            return;
        }

        var page = Geometry.Parse("M 6,4.5 C 6,3.7 6.7,3 7.5,3 L 19.5,3 L 26,9.5 L 26,27.5 C 26,28.3 25.3,29 24.5,29 L 7.5,29 C 6.7,29 6,28.3 6,27.5 Z");
        var pageBrush = cat == IconCategory.Generic ? muted : primary;
        g.Children.Add(new GeometryDrawing(pageBrush, null, page));
        g.Children.Add(new GeometryDrawing(soft, null, Geometry.Parse("M 19.5,3 L 19.5,9.5 L 26,9.5 Z")));
        DrawCategoryGlyph(g, cat, onAccent, FrozenPen(onAccent, 1.55));
    }

    private static void DrawFluent3D(DrawingGroup g, IconCategory cat, Color accent, Color glow, Color text, Color bgElevated, Color onAccent)
    {
        var shadow = FrozenBrush(Color.FromArgb(65, 0, 0, 0));
        var backGrad = new LinearGradientBrush(accent, Color.FromRgb((byte)(accent.R * 0.72), (byte)(accent.G * 0.72), (byte)(accent.B * 0.72)), 90);
        backGrad.Freeze();
        var frontGrad = new LinearGradientBrush(glow, accent, 90);
        frontGrad.Freeze();
        var paper = FrozenBrush(Color.FromArgb(235, 250, 250, 252));
        var rimPen = FrozenPen(Color.FromArgb(110, 255, 255, 255), 0.9);

        if (cat == IconCategory.Folder)
        {
            g.Children.Add(new GeometryDrawing(shadow, null, new RectangleGeometry(new Rect(3.5, 11.5, 25.5, 16.5), 3.5, 3.5)));
            g.Children.Add(new GeometryDrawing(backGrad, null, Geometry.Parse("M 3,7.5 C 3,6.4 3.9,5.5 5,5.5 L 12.2,5.5 L 14.8,8.5 L 27,8.5 C 28.1,8.5 29,9.4 29,10.5 L 29,24.5 C 29,25.6 28.1,26.5 27,26.5 L 5,26.5 C 3.9,26.5 3,25.6 3,24.5 Z")));
            g.Children.Add(new GeometryDrawing(paper, null, new RectangleGeometry(new Rect(5.5, 9.8, 21, 5), 1.5, 1.5)));
            g.Children.Add(new GeometryDrawing(frontGrad, rimPen, new RectangleGeometry(new Rect(3, 12, 26, 14.5), 3.5, 3.5)));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(shadow, null, new RectangleGeometry(new Rect(3.5, 9, 25.5, 16), 4, 4)));
            g.Children.Add(new GeometryDrawing(frontGrad, rimPen, new RectangleGeometry(new Rect(3, 8, 26, 15.5), 4, 4)));
            g.Children.Add(new GeometryDrawing(FrozenBrush(Color.FromArgb(90, 0, 0, 0)), null, new RectangleGeometry(new Rect(5.5, 17.5, 14, 3), 1.5, 1.5)));
            g.Children.Add(new GeometryDrawing(FrozenBrush(onAccent), null, new EllipseGeometry(new Point(24, 19), 2, 2)));
            return;
        }

        g.Children.Add(new GeometryDrawing(shadow, null, new RectangleGeometry(new Rect(6.8, 4, 20, 25.5), 3, 3)));
        var page = Geometry.Parse("M 6,4.5 C 6,3.7 6.7,3 7.5,3 L 19.5,3 L 26,9.5 L 26,27.5 C 26,28.3 25.3,29 24.5,29 L 7.5,29 C 6.7,29 6,28.3 6,27.5 Z");
        var docGrad = cat == IconCategory.Generic
            ? FrozenBrush(bgElevated)
            : frontGrad;
        g.Children.Add(new GeometryDrawing(docGrad, rimPen, page));
        g.Children.Add(new GeometryDrawing(FrozenBrush(Color.FromArgb(130, 255, 255, 255)), null, Geometry.Parse("M 19.5,3 L 19.5,9.5 L 26,9.5 Z")));
        var glyphCol = cat == IconCategory.Generic ? text : onAccent;
        DrawCategoryGlyph(g, cat, glyphCol, FrozenPen(glyphCol, 1.6));
    }

    private static void DrawSoftPastel(DrawingGroup g, IconCategory cat, Color accent, Color glow, Color text, Color bg, Color onAccent)
    {
        var pastelFill = FrozenBrush(Color.FromArgb(72, glow.R, glow.G, glow.B));
        var pillFill = FrozenBrush(Color.FromArgb(200, accent.R, accent.G, accent.B));
        var softPen = FrozenPen(accent, 1.7);

        if (cat == IconCategory.Folder)
        {
            g.Children.Add(new GeometryDrawing(pastelFill, softPen, Geometry.Parse("M 3,8.5 C 3,6.8 4.4,5.5 6,5.5 L 12,5.5 L 14.5,8.5 L 26,8.5 C 27.7,8.5 29,9.8 29,11.5 L 29,24 C 29,25.7 27.7,27 26,27 L 6,27 C 4.3,27 3,25.7 3,24 Z")));
            g.Children.Add(new GeometryDrawing(pillFill, null, new RectangleGeometry(new Rect(5.5, 13.5, 21, 11), 4, 4)));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(pastelFill, softPen, new RectangleGeometry(new Rect(3, 8, 26, 16), 7, 7)));
            g.Children.Add(new GeometryDrawing(pillFill, null, new EllipseGeometry(new Point(22.5, 16), 3.2, 3.2)));
            g.Children.Add(new GeometryDrawing(null, softPen, new LineGeometry(new Point(7.5, 16), new Point(16.5, 16))));
            return;
        }

        var card = new RectangleGeometry(new Rect(5.5, 3, 21, 26), 6, 6);
        g.Children.Add(new GeometryDrawing(pastelFill, softPen, card));
        g.Children.Add(new GeometryDrawing(pillFill, null, new EllipseGeometry(new Point(22, 7.5), 2.3, 2.3)));
        DrawCategoryGlyph(g, cat, accent, FrozenPen(accent, 1.65));
    }

    private static void DrawMonochromeInk(DrawingGroup g, IconCategory cat, Color accent, Color text, Color textSec, Color bg)
    {
        var inkPen = FrozenPen(text, 1.75);
        var finePen = FrozenPen(textSec, 1.25);
        var paperFill = FrozenBrush(Color.FromArgb(24, text.R, text.G, text.B));
        var dotBrush = FrozenBrush(accent);

        if (cat == IconCategory.Folder)
        {
            g.Children.Add(new GeometryDrawing(paperFill, inkPen, Geometry.Parse("M 3,8 C 3,6.9 3.9,6 5,6 L 12,6 L 14.5,9 L 27,9 C 28.1,9 29,9.9 29,11 L 29,25 C 29,26.1 28.1,27 27,27 L 5,27 C 3.9,27 3,26.1 3,25 Z")));
            g.Children.Add(new GeometryDrawing(null, finePen, new LineGeometry(new Point(5.5, 13), new Point(26.5, 13))));
            g.Children.Add(new GeometryDrawing(dotBrush, null, new EllipseGeometry(new Point(24.5, 22.5), 1.8, 1.8)));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(paperFill, inkPen, new RectangleGeometry(new Rect(3, 8, 26, 16), 2.5, 2.5)));
            g.Children.Add(new GeometryDrawing(null, finePen, new LineGeometry(new Point(6, 18.5), new Point(20, 18.5))));
            g.Children.Add(new GeometryDrawing(dotBrush, null, new EllipseGeometry(new Point(24.5, 18.5), 1.9, 1.9)));
            return;
        }

        var page = Geometry.Parse("M 6,3 L 20,3 L 26,9 L 26,28 C 26,28.6 25.6,29 25,29 L 7,29 C 6.4,29 6,28.6 6,28 Z");
        g.Children.Add(new GeometryDrawing(paperFill, inkPen, page));
        g.Children.Add(new GeometryDrawing(null, finePen, Geometry.Parse("M 20,3 L 20,9 L 26,9")));
        DrawCategoryGlyph(g, cat, text, FrozenPen(text, 1.6));
    }

    private static void DrawBlueprintGrid(DrawingGroup g, IconCategory cat, Color accent, Color glow, Color text, Color bg)
    {
        var gridFill = FrozenBrush(Color.FromArgb(42, accent.R, accent.G, accent.B));
        var gridPen = FrozenPen(Color.FromArgb(85, glow.R, glow.G, glow.B), 0.75);
        var draftPen = FrozenPen(glow, 1.5);
        var tickPen = FrozenPen(accent, 1.2);

        if (cat == IconCategory.Folder)
        {
            g.Children.Add(new GeometryDrawing(gridFill, draftPen, Geometry.Parse("M 3,7.5 L 12,7.5 L 14.5,10 L 29,10 L 29,26.5 L 3,26.5 Z")));
            g.Children.Add(new GeometryDrawing(null, gridPen, Geometry.Parse("M 8,10 L 8,26.5 M 16,10 L 16,26.5 M 24,10 L 24,26.5 M 3,16 L 29,16 M 3,21.5 L 29,21.5")));
            return;
        }

        if (cat == IconCategory.Drive)
        {
            g.Children.Add(new GeometryDrawing(gridFill, draftPen, new RectangleGeometry(new Rect(3, 8, 26, 16), 1, 1)));
            g.Children.Add(new GeometryDrawing(null, gridPen, Geometry.Parse("M 10,8 L 10,24 M 18,8 L 18,24 M 3,16 L 29,16")));
            g.Children.Add(new GeometryDrawing(FrozenBrush(glow), null, new RectangleGeometry(new Rect(22.5, 18.5, 3, 3))));
            return;
        }

        var doc = Geometry.Parse("M 6,3 L 20.5,3 L 26,8.5 L 26,29 L 6,29 Z");
        g.Children.Add(new GeometryDrawing(gridFill, draftPen, doc));
        g.Children.Add(new GeometryDrawing(null, gridPen, Geometry.Parse("M 12.5,3 L 12.5,29 M 19.5,3 L 19.5,29 M 6,11 L 26,11 M 6,21 L 26,21")));
        g.Children.Add(new GeometryDrawing(null, tickPen, Geometry.Parse("M 20.5,3 L 20.5,8.5 L 26,8.5")));
        DrawCategoryGlyph(g, cat, glow, FrozenPen(glow, 1.5));
    }

    private static void DrawCategoryGlyph(DrawingGroup g, IconCategory cat, Color color, Pen pen)
    {
        var brush = FrozenBrush(color);
        switch (cat)
        {
            case IconCategory.Code:
                g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M 12.5,14.5 L 9.5,17.5 L 12.5,20.5 M 19.5,14.5 L 22.5,17.5 L 19.5,20.5 M 17,13.5 L 15,21.5")));
                break;
            case IconCategory.Image:
                g.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(12.5, 14.5), 1.6, 1.6)));
                g.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse("M 9.5,22.5 L 14,17 L 17.5,20.5 L 19.5,18.2 L 22.5,22.5 Z")));
                break;
            case IconCategory.Text:
                g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M 10,14.5 L 22,14.5 M 10,18.5 L 22,18.5 M 10,22.5 L 17,22.5")));
                break;
            case IconCategory.Archive:
                g.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(14.5, 11, 3, 2.5))));
                g.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(14.5, 15, 3, 2.5))));
                g.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(13.5, 19, 5, 5), 1, 1)));
                break;
            case IconCategory.Audio:
                g.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M 13,21.5 L 13,14 L 21,12.5 L 21,20")));
                g.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(11.5, 21.5), 2, 1.6)));
                g.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(19.5, 20), 2, 1.6)));
                break;
            case IconCategory.Video:
                g.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse("M 13,13.5 L 21.5,18 L 13,22.5 Z")));
                break;
            case IconCategory.Executable:
                g.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse("M 17,11.5 L 11.5,19 L 16,19 L 14.5,24.5 L 20.5,17 L 16,17 Z")));
                break;
        }
    }

    private static Brush FrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double thickness)
    {
        var p = new Pen(FrozenBrush(c), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        p.Freeze();
        return p;
    }
}
