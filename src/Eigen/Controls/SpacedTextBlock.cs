using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Eigen.Controls;

public sealed class SpacedTextBlock : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LetterSpacingProperty = DependencyProperty.Register(
        nameof(LetterSpacing),
        typeof(double),
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(
        nameof(TextWrapping),
        typeof(TextWrapping),
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(TextWrapping.NoWrap, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register(
        nameof(TextTrimming),
        typeof(TextTrimming),
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(TextTrimming.CharacterEllipsis, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment),
        typeof(TextAlignment),
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(TextAlignment.Left, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxLinesProperty = DependencyProperty.Register(
        nameof(MaxLines),
        typeof(int),
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(12.5,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(FontWeights.Normal,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(Brushes.White,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double LetterSpacing
    {
        get => (double)GetValue(LetterSpacingProperty);
        set => SetValue(LetterSpacingProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public int MaxLines
    {
        get => (int)GetValue(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? string.Empty;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (text.Length == 0)
        {
            var probe = CreateFormatted(" ", dpi);
            return new Size(0, probe.Height);
        }

        if (Math.Abs(LetterSpacing) < 0.01)
        {
            var ft = CreateFormatted(text, dpi);
            if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
            {
                if (TextWrapping == TextWrapping.Wrap)
                {
                    ft.MaxTextWidth = availableSize.Width;
                    ft.MaxLineCount = Math.Max(1, MaxLines);
                    ft.Trimming = TextTrimming;
                    return new Size(Math.Min(ft.Width, availableSize.Width), ft.Height);
                }

                return new Size(Math.Min(ft.Width, availableSize.Width), ft.Height);
            }
            return new Size(ft.Width, ft.Height);
        }

        var maxW = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(1, availableSize.Width);
        if (TryMeasureFastGlyphs(text, maxW, out var fastSize))
            return fastSize;

        var (lines, lineHeight) = LayoutSpacedLines(text, maxW, dpi);
        var maxLineW = lines.Count == 0 ? 0 : lines.Max(l => l.Width);
        return new Size(Math.Min(maxLineW, maxW), Math.Max(lineHeight, lines.Count * lineHeight));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0 || RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (Math.Abs(LetterSpacing) < 0.01)
        {
            var ft = CreateFormatted(text, dpi);
            ft.MaxTextWidth = Math.Max(1, RenderSize.Width);
            ft.MaxLineCount = TextWrapping == TextWrapping.Wrap ? Math.Max(1, MaxLines) : 1;
            ft.Trimming = TextTrimming;
            ft.TextAlignment = TextAlignment;

            var y = TextWrapping == TextWrapping.NoWrap
                ? Math.Max(0, (RenderSize.Height - ft.Height) / 2.0)
                : 0;
            dc.DrawText(ft, new Point(0, y));
            return;
        }

        if (TryRenderFastGlyphRuns(dc, text, RenderSize, dpi))
            return;

        var (lines, lineHeight) = LayoutSpacedLines(text, RenderSize.Width, dpi);
        var totalHeight = lines.Count * lineHeight;
        var startY = TextWrapping == TextWrapping.NoWrap
            ? Math.Max(0, (RenderSize.Height - totalHeight) / 2.0)
            : 0;

        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        try
        {
            for (var li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                var x = TextAlignment switch
                {
                    TextAlignment.Center => Math.Max(0, (RenderSize.Width - line.Width) / 2.0),
                    TextAlignment.Right => Math.Max(0, RenderSize.Width - line.Width),
                    _ => 0.0
                };
                var y = startY + li * lineHeight;

                foreach (var glyph in line.Glyphs)
                {
                    dc.DrawText(glyph.Formatted, new Point(x, y));
                    x += glyph.Advance;
                }
            }
        }
        finally
        {
            dc.Pop();
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(FontFamily Family, FontWeight Weight), GlyphTypeface?> GlyphTypefaceCache = new();

    private bool TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface, out double fontSize)
    {
        fontSize = Math.Max(6, FontSize);
        var key = (FontFamily, FontWeight);
        if (!GlyphTypefaceCache.TryGetValue(key, out glyphTypeface))
        {
            var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out glyphTypeface))
                glyphTypeface = null;
            if (GlyphTypefaceCache.Count < 64)
                GlyphTypefaceCache.TryAdd(key, glyphTypeface);
        }
        return glyphTypeface is not null;
    }

    private bool TryMeasureFastGlyphs(string text, double maxWidth, out Size size)
    {
        size = default;
        if (!TryGetGlyphTypeface(out var gtf, out var fontSize) || gtf is null)
            return false;

        if (!TryBuildFastLines(gtf, fontSize, text, maxWidth, out var lines, out var lineHeight))
            return false;

        double maxLineW = 0;
        foreach (var line in lines)
        {
            if (line.Width > maxLineW)
                maxLineW = line.Width;
        }

        size = new Size(Math.Min(maxLineW, maxWidth), Math.Max(lineHeight, lines.Count * lineHeight));
        return true;
    }

    private bool TryRenderFastGlyphRuns(DrawingContext dc, string text, Size renderSize, double dpi)
    {
        if (!TryGetGlyphTypeface(out var gtf, out var fontSize) || gtf is null)
            return false;

        if (!TryBuildFastLines(gtf, fontSize, text, renderSize.Width, out var lines, out var lineHeight))
            return false;

        var totalHeight = lines.Count * lineHeight;
        var startY = TextWrapping == TextWrapping.NoWrap
            ? Math.Max(0, (renderSize.Height - totalHeight) / 2.0)
            : 0;

        var brush = Foreground ?? Brushes.White;
        var baselineOffset = gtf.Baseline * fontSize;

        dc.PushClip(new RectangleGeometry(new Rect(renderSize)));
        try
        {
            for (var li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                if (line.GlyphIndices.Length == 0) continue;

                var x = TextAlignment switch
                {
                    TextAlignment.Center => Math.Max(0, (renderSize.Width - line.Width) / 2.0),
                    TextAlignment.Right => Math.Max(0, renderSize.Width - line.Width),
                    _ => 0.0
                };
                var baselineY = startY + li * lineHeight + baselineOffset;

                var run = new GlyphRun(
                    gtf,
                    0,
                    false,
                    fontSize,
                    (float)dpi,
                    line.GlyphIndices,
                    new Point(x, baselineY),
                    line.AdvanceWidths,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);

                dc.DrawGlyphRun(brush, run);
            }
        }
        finally
        {
            dc.Pop();
        }

        return true;
    }

    private bool TryBuildFastLines(
        GlyphTypeface gtf,
        double fontSize,
        string text,
        double maxWidth,
        out List<FastGlyphLine> lines,
        out double lineHeight)
    {
        lines = [];
        lineHeight = gtf.Height * fontSize;
        var spacing = LetterSpacing;
        var charMap = gtf.CharacterToGlyphMap;
        var advances = gtf.AdvanceWidths;

        var count = 0;
        var glyphIds = new ushort[text.Length];
        var widths = new double[text.Length];
        var isSpace = new bool[text.Length];

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is '\r' or '\n') continue;
            if (char.IsSurrogate(ch) || !charMap.TryGetValue(ch, out var gid))
                return false;

            glyphIds[count] = gid;
            widths[count] = advances[gid] * fontSize;
            isSpace[count] = ch == ' ';
            count++;
        }

        if (count == 0)
            return true;

        if (!charMap.TryGetValue('.', out var dotGid))
            return false;
        var dotW = advances[dotGid] * fontSize;
        var dotAdv = Math.Max(1.0, dotW + spacing);
        var ellipsisWidth = dotAdv * 2 + dotW;

        if (TextWrapping == TextWrapping.NoWrap || double.IsInfinity(maxWidth))
        {
            lines.Add(BuildFastTruncatedLine(glyphIds, widths, 0, count, maxWidth, spacing, dotGid, dotW, dotAdv, ellipsisWidth, TextTrimming != TextTrimming.None));
            return true;
        }

        var maxLines = Math.Max(1, MaxLines);
        var idx = 0;

        while (idx < count && lines.Count < maxLines)
        {
            var isLastAllowed = lines.Count == maxLines - 1;
            var end = idx;
            double widthSoFar = 0;
            var lastSpaceBreak = -1;

            while (end < count)
            {
                var w = widths[end];
                var nextW = end == idx ? w : widthSoFar + spacing + w;
                if (nextW > maxWidth && end > idx)
                    break;

                widthSoFar = nextW;
                if (isSpace[end])
                    lastSpaceBreak = end;
                end++;
            }

            if (end < count && isLastAllowed)
            {
                lines.Add(BuildFastTruncatedLine(glyphIds, widths, idx, count - idx, maxWidth, spacing, dotGid, dotW, dotAdv, ellipsisWidth, TextTrimming != TextTrimming.None));
                idx = count;
            }
            else
            {
                if (end < count && lastSpaceBreak > idx)
                    end = lastSpaceBreak + 1;

                var trimEnd = end;
                while (trimEnd > idx + 1 && isSpace[trimEnd - 1])
                    trimEnd--;

                lines.Add(BuildFastSlice(glyphIds, widths, idx, trimEnd - idx, spacing));
                idx = end;
                while (idx < count && isSpace[idx])
                    idx++;
            }
        }

        return true;
    }

    private static FastGlyphLine BuildFastSlice(ushort[] glyphIds, double[] widths, int start, int count, double spacing)
    {
        if (count <= 0) return new FastGlyphLine([], [], 0);
        var indices = new ushort[count];
        var advs = new double[count];
        double totalW = 0;
        for (var i = 0; i < count; i++)
        {
            indices[i] = glyphIds[start + i];
            var w = widths[start + i];
            if (i < count - 1)
            {
                var adv = Math.Max(1.0, w + spacing);
                advs[i] = adv;
                totalW += adv;
            }
            else
            {
                advs[i] = w;
                totalW += w;
            }
        }
        return new FastGlyphLine(indices, advs, Math.Max(0, totalW));
    }

    private static FastGlyphLine BuildFastTruncatedLine(
        ushort[] glyphIds,
        double[] widths,
        int start,
        int count,
        double maxWidth,
        double spacing,
        ushort dotGid,
        double dotW,
        double dotAdv,
        double ellipsisWidth,
        bool allowEllipsis)
    {
        double total = 0;
        for (var i = 0; i < count; i++)
            total += i == 0 ? widths[start + i] : spacing + widths[start + i];

        if (total <= maxWidth || double.IsInfinity(maxWidth))
            return BuildFastSlice(glyphIds, widths, start, count, spacing);

        if (!allowEllipsis || maxWidth <= ellipsisWidth)
            return new FastGlyphLine([dotGid, dotGid, dotGid], [dotAdv, dotAdv, dotW], ellipsisWidth);

        var budget = maxWidth - ellipsisWidth;
        var take = 0;
        double w = 0;
        for (var i = 0; i < count; i++)
        {
            var cw = widths[start + i];
            var next = i == 0 ? cw : w + spacing + cw;
            if (next > budget && take > 0)
                break;
            take++;
            w = next;
        }

        var len = take + 3;
        var indices = new ushort[len];
        var advs = new double[len];
        double totalW = 0;
        for (var i = 0; i < take; i++)
        {
            indices[i] = glyphIds[start + i];
            var adv = Math.Max(1.0, widths[start + i] + spacing);
            advs[i] = adv;
            totalW += adv;
        }
        indices[take] = dotGid;
        advs[take] = dotAdv;
        indices[take + 1] = dotGid;
        advs[take + 1] = dotAdv;
        indices[take + 2] = dotGid;
        advs[take + 2] = dotW;
        totalW += ellipsisWidth;

        return new FastGlyphLine(indices, advs, Math.Max(0, totalW));
    }

    private readonly record struct FastGlyphLine(ushort[] GlyphIndices, double[] AdvanceWidths, double Width);

    private FormattedText CreateFormatted(string s, double dpi)
    {
        return new FormattedText(
            s,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal),
            Math.Max(6, FontSize),
            Foreground ?? Brushes.White,
            dpi);
    }

    private (List<SpacedLine> Lines, double LineHeight) LayoutSpacedLines(string text, double maxWidth, double dpi)
    {
        var spacing = LetterSpacing;
        var glyphs = new List<GlyphRunItem>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        double lineHeight = 0;

        while (enumerator.MoveNext())
        {
            var elem = enumerator.GetTextElement();
            if (elem is "\r" or "\n") continue;
            var ft = CreateFormatted(elem, dpi);
            lineHeight = Math.Max(lineHeight, ft.Height);
            var w = ft.WidthIncludingTrailingWhitespace;
            var adv = Math.Max(1.0, w + spacing);
            glyphs.Add(new GlyphRunItem(elem, ft, w, adv));
        }

        if (lineHeight <= 0)
            lineHeight = CreateFormatted(" ", dpi).Height;

        var ellipsisFt = CreateFormatted("...", dpi);
        var ellipsisWidth = ellipsisFt.WidthIncludingTrailingWhitespace;

        if (TextWrapping == TextWrapping.NoWrap || double.IsInfinity(maxWidth))
        {
            var single = BuildTruncatedLine(glyphs, 0, glyphs.Count, maxWidth, spacing, ellipsisFt, ellipsisWidth, TextTrimming != TextTrimming.None);
            return ([single], lineHeight);
        }

        var lines = new List<SpacedLine>();
        var maxLines = Math.Max(1, MaxLines);
        var idx = 0;

        while (idx < glyphs.Count && lines.Count < maxLines)
        {
            var isLastAllowedLine = lines.Count == maxLines - 1;
            var end = idx;
            double widthSoFar = 0;
            var lastSpaceBreak = -1;

            while (end < glyphs.Count)
            {
                var g = glyphs[end];
                var nextW = end == idx ? g.Width : widthSoFar + spacing + g.Width;
                if (nextW > maxWidth && end > idx)
                    break;

                widthSoFar = nextW;
                if (g.Text == " ")
                    lastSpaceBreak = end;
                end++;
            }

            if (end < glyphs.Count && isLastAllowedLine)
            {
                var lastLine = BuildTruncatedLine(glyphs, idx, glyphs.Count - idx, maxWidth, spacing, ellipsisFt, ellipsisWidth, TextTrimming != TextTrimming.None);
                lines.Add(lastLine);
                idx = glyphs.Count;
            }
            else
            {
                if (end < glyphs.Count && lastSpaceBreak > idx)
                    end = lastSpaceBreak + 1;

                var trimEnd = end;
                while (trimEnd > idx + 1 && glyphs[trimEnd - 1].Text == " ")
                    trimEnd--;

                lines.Add(BuildExactSlice(glyphs, idx, trimEnd - idx, spacing));
                idx = end;
                while (idx < glyphs.Count && glyphs[idx].Text == " ")
                    idx++;
            }
        }

        return (lines, lineHeight);
    }

    private static SpacedLine BuildExactSlice(List<GlyphRunItem> source, int start, int count, double spacing)
    {
        var items = new List<GlyphRunItem>(count);
        double width = 0;
        for (var i = 0; i < count; i++)
        {
            var g = source[start + i];
            items.Add(g);
            width += i == 0 ? g.Width : spacing + g.Width;
        }
        return new SpacedLine(items, Math.Max(0, width));
    }

    private static SpacedLine BuildTruncatedLine(
        List<GlyphRunItem> source,
        int start,
        int count,
        double maxWidth,
        double spacing,
        FormattedText ellipsisFt,
        double ellipsisWidth,
        bool allowEllipsis)
    {
        var total = 0.0;
        for (var i = 0; i < count; i++)
            total += i == 0 ? source[start + i].Width : spacing + source[start + i].Width;

        if (total <= maxWidth || double.IsInfinity(maxWidth))
            return BuildExactSlice(source, start, count, spacing);

        if (!allowEllipsis || maxWidth <= ellipsisWidth)
            return new SpacedLine([new GlyphRunItem("...", ellipsisFt, ellipsisWidth, ellipsisWidth)], ellipsisWidth);

        var budget = maxWidth - ellipsisWidth;
        var items = new List<GlyphRunItem>();
        double w = 0;
        for (var i = 0; i < count; i++)
        {
            var g = source[start + i];
            var next = i == 0 ? g.Width : w + spacing + g.Width;
            if (next > budget && items.Count > 0)
                break;
            items.Add(g);
            w = next;
        }

        items.Add(new GlyphRunItem("...", ellipsisFt, ellipsisWidth, ellipsisWidth));
        var finalW = items.Count > 1 ? w + Math.Max(0, spacing * 0.5) + ellipsisWidth : ellipsisWidth;
        return new SpacedLine(items, finalW);
    }

    private sealed record GlyphRunItem(string Text, FormattedText Formatted, double Width, double Advance);
    private sealed record SpacedLine(List<GlyphRunItem> Glyphs, double Width);
}
