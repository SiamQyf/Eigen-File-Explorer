using System.Windows.Media;

namespace Eigen.Models;

public sealed class EigenSkin
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Mood { get; init; }
    public required bool IsDark { get; init; }

    public required string BgWindow { get; init; }
    public required string BgSidebar { get; init; }
    public required string BgContent { get; init; }
    public required string BgElevated { get; init; }
    public required string BgInput { get; init; }
    public required string BgHover { get; init; }
    public required string BgPressed { get; init; }
    public required string BgSelection { get; init; }
    public required string BgSelectionHover { get; init; }

    public required string BorderSubtle { get; init; }
    public required string BorderStrong { get; init; }

    public required string Accent { get; init; }
    public required string AccentHover { get; init; }
    public required string AccentSoft { get; init; }

    public required string TextPrimary { get; init; }
    public required string TextSecondary { get; init; }
    public required string TextMuted { get; init; }
    public required string TextOnAccent { get; init; }

    public required string Danger { get; init; }
    public required string DangerHover { get; init; }
    public required string ProgressTrack { get; init; }
    public required string ScrollThumb { get; init; }
    public required string ScrollThumbHover { get; init; }

    public IconPackStyle IconPack { get; init; } = IconPackStyle.Native;
    public string FontFamilyName { get; init; } = "Segoe UI Variable Text, Segoe UI";
    public double ControlCornerRadius { get; init; } = 6.0;
    public double CardCornerRadius { get; init; } = 10.0;
    public double SurfaceBorderThickness { get; init; } = 1.0;
    public string? HeaderGradientEnd { get; init; }
    public bool IsTranslucent { get; init; }

    public string AestheticTag => IsTranslucent
        ? "Translucent • Acrylic"
        : IconPack switch
    {
        IconPackStyle.NeonWire => "Wire • Mono",
        IconPackStyle.GlassFrost => "Glass • Soft",
        IconPackStyle.RetroPixel => "Pixel • 8-Bit",
        IconPackStyle.TactileBrutal => "Brutal • Bold",
        IconPackStyle.DuotoneMinimal => "Duotone • Serif",
        _ => IsDark ? "Native • Dark" : "Native • Light"
    };

    public Brush PreviewBgBrush => CreateFrozenBrush(BgContent);
    public Brush PreviewSidebarBrush => CreateFrozenBrush(BgSidebar);
    public Brush PreviewAccentBrush => CreateFrozenBrush(Accent);
    public Brush PreviewTextBrush => CreateFrozenBrush(TextPrimary);

    private static Brush CreateFrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
