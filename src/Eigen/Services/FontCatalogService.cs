using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Eigen.Models;

namespace Eigen.Services;

public static class FontCatalogService
{
    private static readonly Lazy<IReadOnlyList<FontOption>> CachedFonts = new(BuildInstalledFonts);
    private static readonly ConcurrentDictionary<string, double> WidthBoostCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lazy<double> BaselineWidth = new(() => MeasureSampleWidth("Segoe UI"));

    private static readonly HashSet<string> ExcludedSymbolFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Marlett",
        "Symbol",
        "Webdings",
        "Wingdings",
        "Wingdings 2",
        "Wingdings 3",
        "Segoe MDL2 Assets",
        "Segoe Fluent Icons",
        "HoloLens MDL2 Assets",
        "Segoe UI Emoji",
        "Segoe UI Symbol"
    };

    public static IReadOnlyList<FontOption> GetInstalledFonts() => CachedFonts.Value;

    public static double GetWidthBoost(string? fontFamilyName)
    {
        if (string.IsNullOrWhiteSpace(fontFamilyName) ||
            string.Equals(fontFamilyName, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return 0.0;
        }

        return WidthBoostCache.GetOrAdd(fontFamilyName, name =>
        {
            var width = MeasureSampleWidth(name);
            var delta = width - BaselineWidth.Value;
            return Math.Clamp(Math.Round(delta), 0.0, 90.0);
        });
    }

    private static IReadOnlyList<FontOption> BuildInstalledFonts()
    {
        var result = new List<FontOption>
        {
            new("Auto", "Auto (Skin Default)", "Auto", 0.0)
        };

        var families = Fonts.SystemFontFamilies.ToArray();
        var discoveredBag = new ConcurrentBag<FontOption>();

        Parallel.ForEach(families, family =>
        {
            var source = family.Source?.Trim();
            if (string.IsNullOrEmpty(source) || ExcludedSymbolFonts.Contains(source))
                return;

            if (!HasReadableAsciiGlyphs(family))
                return;

            discoveredBag.Add(new FontOption(source, source, source, 0.0));
        });

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discovered = new List<FontOption>(discoveredBag.Count);
        foreach (var opt in discoveredBag)
        {
            if (seen.Add(opt.Id))
                discovered.Add(opt);
        }

        discovered.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        result.AddRange(discovered);
        return result;
    }

    private static bool HasReadableAsciiGlyphs(FontFamily family)
    {
        try
        {
            foreach (var typeface in family.GetTypefaces())
            {
                if (typeface.TryGetGlyphTypeface(out var glyphTypeface))
                {
                    if (glyphTypeface.CharacterToGlyphMap.ContainsKey('A') &&
                        glyphTypeface.CharacterToGlyphMap.ContainsKey('a') &&
                        glyphTypeface.CharacterToGlyphMap.ContainsKey('0'))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    public static double MeasureTextWidth(
        string text,
        string? fontFamilyName,
        double fontSize,
        FontWeight fontWeight,
        double letterSpacing = 0.0)
    {
        if (string.IsNullOrEmpty(text))
            return 0.0;

        try
        {
            var primaryFont = string.IsNullOrWhiteSpace(fontFamilyName) ||
                              string.Equals(fontFamilyName, "Auto", StringComparison.OrdinalIgnoreCase)
                ? "Segoe UI Variable Text, Segoe UI"
                : fontFamilyName;
            var ft = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(primaryFont), FontStyles.Normal, fontWeight, FontStretches.Normal),
                Math.Max(1.0, fontSize),
                Brushes.White,
                1.0);
            var rawWidth = ft.WidthIncludingTrailingWhitespace;
            var spacingDelta = Math.Max(0, text.Length - 1) * letterSpacing;
            return Math.Max(rawWidth * 0.7, rawWidth + spacingDelta);
        }
        catch
        {
            return text.Length * Math.Max(6.0, fontSize * 0.55);
        }
    }

    private static double MeasureSampleWidth(string fontFamilyName)
        => MeasureTextWidth("2026-11-26  03:54", fontFamilyName, 12.5, FontWeights.Normal);
}
