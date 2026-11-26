using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Eigen.Models;

public sealed class DriveCardItem
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public string VolumeLabel { get; init; } = string.Empty;
    public string DriveTypeLabel { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPercentage => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0.0;
    public string SpaceSummary { get; init; } = string.Empty;
    public ImageSource? Icon { get; init; }
}

public sealed class SidebarNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _canExpand;
    private ImageSource? _icon;

    public required string Title { get; init; }
    public string Path { get; init; } = string.Empty;
    public string Glyph { get; init; } = "\uE8B7";
    public bool IsPinned { get; init; }
    public bool IsSpecialHome { get; init; }
    public bool IsDrive { get; init; }
    public bool IsLoaded { get; set; }

    public bool CanExpand
    {
        get => _canExpand;
        set { if (_canExpand != value) { _canExpand = value; OnPropertyChanged(); } }
    }

    public ImageSource? Icon
    {
        get => _icon;
        set { if (!Equals(_icon, value)) { _icon = value; OnPropertyChanged(); } }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<SidebarNode> Children { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class BreadcrumbSegment
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsRoot { get; init; }
    public bool IsLast { get; set; }
    public ObservableCollection<BreadcrumbSegment> SiblingFolders { get; } = [];
}

public sealed class PreviewInfo
{
    public PreviewKind Kind { get; init; } = PreviewKind.None;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public ImageSource? PreviewImage { get; init; }
    public Uri? VideoSource { get; init; }
    public bool IsVideo => Kind == PreviewKind.Video && VideoSource is not null;
    public bool IsVisualThumbnail { get; init; }
    public bool CanZoom { get; init; } = true;
    public string? OpenPromptLabel { get; init; }
    public string? PreviewBadge { get; init; }
    public string? ImageDimensions { get; init; }
    public string? TextContent { get; init; }
    public string? TextStats { get; init; }
    public bool IsTruncated { get; init; }
    public string ExactSizeText { get; init; } = string.Empty;
    public string CreatedText { get; init; } = string.Empty;
    public string ModifiedText { get; init; } = string.Empty;
    public string AccessedText { get; init; } = string.Empty;
    public string AttributesText { get; init; } = string.Empty;
    public int? FolderFileCount { get; init; }
    public int? FolderSubdirCount { get; init; }
}

public sealed class FileOperationProgress
{
    public string OperationName { get; init; } = string.Empty;
    public string CurrentItem { get; init; } = string.Empty;
    public int CompletedItems { get; init; }
    public int TotalItems { get; init; }
    public long BytesProcessed { get; init; }
    public long TotalBytes { get; init; }
    public double ProgressPercent => TotalBytes > 0
        ? Math.Clamp((double)BytesProcessed / TotalBytes * 100.0, 0.0, 100.0)
        : (TotalItems > 0 ? Math.Clamp((double)CompletedItems / TotalItems * 100.0, 0.0, 100.0) : 0.0);
}

public sealed record FileTransferEntry(string SourcePath, string DestinationPath, bool IsDirectory);

public sealed class UndoRecord
{
    public required string Description { get; init; }
    public required Func<CancellationToken, Task> UndoAsync { get; init; }
}

public sealed class AppSettings
{
    public EigenThemeMode ThemeMode { get; set; } = EigenThemeMode.Light;
    public string SkinId { get; set; } = Eigen.Services.ThemeCatalogService.DefaultSkinId;
    public IconPackStyle IconPackOverride { get; set; } = IconPackStyle.NeonWire;
    public ExplorerViewMode DefaultViewMode { get; set; } = ExplorerViewMode.Details;
    public bool ShowHiddenFiles { get; set; }
    public bool ShowPreviewPane { get; set; }
    public bool StartInDualPane { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? ItemVerticalGap { get; set; }
    public double DetailsVerticalGap { get; set; } = 3.0;
    public double TilesVerticalGap { get; set; } = 13.0;
    public double CompactVerticalGap { get; set; } = 3.0;
    public double SidebarVerticalGap { get; set; } = 1.0;
    public double HomeVerticalGap { get; set; } = 24.0;
    public double ItemIconSize { get; set; } = 15.0;
    public double TileIconSize { get; set; } = 48.0;
    public double IconLeftGap { get; set; } = 4.0;
    public double IconTextHorizontalGap { get; set; } = 8.0;
    public double IconTextVerticalGap { get; set; } = 6.0;
    public double ItemFontSize { get; set; } = 14.0;
    public string FontFamilyOverride { get; set; } = "Auto";
    public int FontWeightValue { get; set; } = 400;
    public double LetterSpacing { get; set; } = 0.0;
    public double IconTransparency { get; set; } = 0.0;
    public double TextTransparency { get; set; } = 0.0;
    public double WindowBrightness { get; set; } = 1.0;
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 840;
    public double SidebarWidth { get; set; } = 246;
    public double PreviewPaneWidth { get; set; } = 310;
    public bool IsMaximized { get; set; } = true;
    public List<string> PinnedFolders { get; set; } = [];
    public List<string> RecentPaths { get; set; } = [];
    public Dictionary<string, FolderColumnWidths> FolderColumnLayout { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class FolderColumnWidths
{
    public double Name { get; set; }
    public double Date { get; set; }
    public double Type { get; set; }
}

public sealed class IconPackOption
{
    public IconPackStyle Style { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ImageSource? FolderPreview { get; init; }
    public ImageSource? CodePreview { get; init; }
    public ImageSource? ImagePreview { get; init; }
}
