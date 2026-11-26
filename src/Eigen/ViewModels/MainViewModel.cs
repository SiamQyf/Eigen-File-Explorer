using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Eigen.Helpers;
using Eigen.Models;
using Eigen.Services;

namespace Eigen.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    public const double MinDetailsVerticalGap = 0.0;
    public const double MaxDetailsVerticalGap = 40.0;
    public const double DefaultDetailsVerticalGap = 3.0;

    public const double MinTilesVerticalGap = 0.0;
    public const double MaxTilesVerticalGap = 48.0;
    public const double DefaultTilesVerticalGap = 13.0;

    public const double MinCompactVerticalGap = 0.0;
    public const double MaxCompactVerticalGap = 40.0;
    public const double DefaultCompactVerticalGap = 3.0;

    public const double MinSidebarVerticalGap = 0.0;
    public const double MaxSidebarVerticalGap = 32.0;
    public const double DefaultSidebarVerticalGap = 1.0;

    public const double MinHomeVerticalGap = 0.0;
    public const double MaxHomeVerticalGap = 48.0;
    public const double DefaultHomeVerticalGap = 24.0;

    public const double MinIconSize = 10.0;
    public const double MaxIconSize = 64.0;
    public const double DefaultIconSize = 15.0;
    public const double MinTileIconSize = 32.0;
    public const double MaxTileIconSize = 160.0;
    public const double DefaultTileIconSize = 48.0;
    public const double MinIconLeftGap = 0.0;
    public const double MaxIconLeftGap = 48.0;
    public const double DefaultIconLeftGap = 4.0;
    public const double MinIconTextHorizontalGap = 0.0;
    public const double MaxIconTextHorizontalGap = 48.0;
    public const double DefaultIconTextHorizontalGap = 8.0;
    public const double MinIconTextVerticalGap = 0.0;
    public const double MaxIconTextVerticalGap = 36.0;
    public const double DefaultIconTextVerticalGap = 6.0;
    public const double DetailsNamePaddingGap = 300.0;

    public const double MinFontSize = 9.0;
    public const double MaxFontSize = 28.0;
    public const double DefaultFontSize = 14.0;

    public const double MinFontWeightValue = 100.0;
    public const double MaxFontWeightValue = 900.0;
    public const int DefaultFontWeightValue = 400;

    public const double MinLetterSpacing = -2.0;
    public const double MaxLetterSpacing = 8.0;
    public const double DefaultLetterSpacing = 0.0;

    public const double MinTransparency = 0.0;
    public const double MaxTransparency = 0.90;
    public const double DefaultTransparency = 0.0;

    public const string DefaultFontFamilyOverride = "Auto";

    private readonly FileSystemService _fileSystem = new();
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Stack<UndoRecord> _undoStack = new();
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _folderStatsCts;
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _toastCts;

    private ExplorerTabViewModel? _activeTab;
    private EigenThemeMode _themeMode;
    private EigenSkin _activeSkin = null!;
    private IconPackStyle _iconPackOverride;
    private string _selectedSkinCategory = "All";
    private string _skinSearchQuery = string.Empty;
    private double _detailsVerticalGap;
    private double _tilesVerticalGap;
    private double _compactVerticalGap;
    private double _sidebarVerticalGap;
    private double _homeVerticalGap;
    private double _itemIconSize;
    private double _tileIconSize;
    private double _iconLeftGap;
    private double _iconTextHorizontalGap;
    private double _iconTextVerticalGap;
    private double _itemFontSize;
    private string _fontFamilyOverride = DefaultFontFamilyOverride;
    private int _fontWeightValue = DefaultFontWeightValue;
    private double _letterSpacing = DefaultLetterSpacing;
    private double _iconTransparency = DefaultTransparency;
    private double _textTransparency = DefaultTransparency;
    private double _windowBrightness = 1.0;
    private FontOption _selectedFontOption = null!;
    private bool _isDarkTheme;
    private bool _isPreviewPaneOpen;
    private bool _isCustomizePaneOpen;
    private bool _isSidebarCollapsed;
    private bool _showHiddenFiles;
    private PreviewInfo? _currentPreview;
    private bool _isPreviewLoading;
    private bool _isFloatingPreviewOpen;
    private string? _calculatedFolderSize;
    private bool _isCalculatingFolderSize;
    private FileOperationProgress? _activeOperationProgress;
    private bool _isOperationRunning;
    private string _toastMessage = string.Empty;
    private bool _isToastVisible;

    public MainViewModel(string? initialPath = null, string? settingsPath = null)
    {
        _settingsService = new SettingsService(settingsPath);
        _settings = _settingsService.Load();
        _themeMode = _settings.ThemeMode;
        _activeSkin = ThemeCatalogService.GetById(_settings.SkinId);
        _iconPackOverride = _settings.IconPackOverride;
        _isDarkTheme = _activeSkin.IsDark;
        _detailsVerticalGap = Math.Clamp(_settings.DetailsVerticalGap, MinDetailsVerticalGap, MaxDetailsVerticalGap);
        _tilesVerticalGap = Math.Clamp(_settings.TilesVerticalGap, MinTilesVerticalGap, MaxTilesVerticalGap);
        _compactVerticalGap = Math.Clamp(_settings.CompactVerticalGap, MinCompactVerticalGap, MaxCompactVerticalGap);
        _sidebarVerticalGap = Math.Clamp(_settings.SidebarVerticalGap, MinSidebarVerticalGap, MaxSidebarVerticalGap);
        _homeVerticalGap = Math.Clamp(_settings.HomeVerticalGap, MinHomeVerticalGap, MaxHomeVerticalGap);
        _itemIconSize = Math.Clamp(_settings.ItemIconSize, MinIconSize, MaxIconSize);
        _tileIconSize = Math.Clamp(_settings.TileIconSize, MinTileIconSize, MaxTileIconSize);
        _iconLeftGap = Math.Clamp(_settings.IconLeftGap, MinIconLeftGap, MaxIconLeftGap);
        _iconTextHorizontalGap = Math.Clamp(_settings.IconTextHorizontalGap, MinIconTextHorizontalGap, MaxIconTextHorizontalGap);
        _iconTextVerticalGap = Math.Clamp(_settings.IconTextVerticalGap, MinIconTextVerticalGap, MaxIconTextVerticalGap);
        _itemFontSize = Math.Clamp(_settings.ItemFontSize, MinFontSize, MaxFontSize);
        _fontWeightValue = ClampFontWeight(_settings.FontWeightValue);
        _letterSpacing = SnapLetterSpacing(_settings.LetterSpacing);
        _iconTransparency = Math.Clamp(_settings.IconTransparency, MinTransparency, MaxTransparency);
        _textTransparency = Math.Clamp(_settings.TextTransparency, MinTransparency, MaxTransparency);
        _windowBrightness = Math.Clamp(_settings.WindowBrightness, 0.40, 1.0);
        _selectedFontOption = ResolveFontOption(_settings.FontFamilyOverride);
        _fontFamilyOverride = _selectedFontOption.Id;
        _isPreviewPaneOpen = _settings.ShowPreviewPane;
        _showHiddenFiles = _settings.ShowHiddenFiles;

        ApplySkinFilter();
        InitializeCommands();
        RebuildIconPackOptions();
        RebuildSidebar();

        var startPath = !string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath)
            ? initialPath
            : FormatHelper.HomeLocation;

        var firstTab = CreateTab(startPath);
        if (_settings.StartInDualPane)
            firstTab.IsSplitView = true;

        Tabs.Add(firstTab);
        ActiveTab = firstTab;
        _ = firstTab.PrimaryPane.NavigateToAsync(startPath, recordHistory: false);
    }

    public event Action<bool>? ThemeChanged;
    public event Action<EigenSkin>? SkinChanged;
    public event Func<string, string, bool>? ConfirmAction;

    public ObservableCollection<ExplorerTabViewModel> Tabs { get; } = [];
    public ObservableCollection<SidebarNode> QuickAccessNodes { get; } = [];
    public ObservableCollection<SidebarNode> DriveTreeNodes { get; } = [];
    public ObservableCollection<EigenSkin> FilteredSkins { get; } = [];

    public IReadOnlyList<EigenSkin> AllSkins => ThemeCatalogService.Skins;
    public IReadOnlyList<string> SkinCategories => ThemeCatalogService.Categories;
    public IReadOnlyList<IconPackStyle> AvailableIconPacks { get; } =
    [
        IconPackStyle.Auto,
        IconPackStyle.Native,
        IconPackStyle.NeonWire,
        IconPackStyle.GlassFrost,
        IconPackStyle.RetroPixel,
        IconPackStyle.DuotoneMinimal,
        IconPackStyle.TactileBrutal,
        IconPackStyle.Fluent3D,
        IconPackStyle.SoftPastel,
        IconPackStyle.MonochromeInk,
        IconPackStyle.BlueprintGrid
    ];

    public ObservableCollection<IconPackOption> IconPackOptions { get; } = [];

    private void RebuildIconPackOptions()
    {
        IconPackOptions.Clear();
        var sysDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var style in AvailableIconPacks)
        {
            var effective = style == IconPackStyle.Auto ? ActiveSkin.IconPack : style;
            System.Windows.Media.ImageSource? folder;
            System.Windows.Media.ImageSource? code;
            System.Windows.Media.ImageSource? image;

            if (effective == IconPackStyle.Native)
            {
                (folder, _) = NativeShellService.GetIconAndType(sysDir, isDirectory: true, largeIcon: true);
                (code, _) = NativeShellService.GetIconAndType("sample.cs", isDirectory: false, largeIcon: true);
                (image, _) = NativeShellService.GetIconAndType("sample.png", isDirectory: false, largeIcon: true);
            }
            else
            {
                folder = VectorIconPackService.GetThemedIcon(effective, sysDir, isDirectory: true, isDrive: false, ActiveSkin);
                code = VectorIconPackService.GetThemedIcon(effective, "sample.cs", isDirectory: false, isDrive: false, ActiveSkin);
                image = VectorIconPackService.GetThemedIcon(effective, "sample.png", isDirectory: false, isDrive: false, ActiveSkin);
            }

            var label = style switch
            {
                IconPackStyle.Auto => "Auto (Skin)",
                IconPackStyle.Native => "Native Shell",
                IconPackStyle.NeonWire => "Neon Wire",
                IconPackStyle.GlassFrost => "Glass Frost",
                IconPackStyle.RetroPixel => "Retro Pixel",
                IconPackStyle.DuotoneMinimal => "Duotone",
                IconPackStyle.TactileBrutal => "Tactile Brutal",
                IconPackStyle.Fluent3D => "Fluent 3D",
                IconPackStyle.SoftPastel => "Soft Pastel",
                IconPackStyle.MonochromeInk => "Monochrome",
                IconPackStyle.BlueprintGrid => "Blueprint",
                _ => style.ToString()
            };

            IconPackOptions.Add(new IconPackOption
            {
                Style = style,
                DisplayName = label,
                FolderPreview = folder,
                CodePreview = code,
                ImagePreview = image
            });
        }
    }

    public IReadOnlyList<FontOption> AvailableFonts => FontCatalogService.GetInstalledFonts();
    public int InstalledFontCount => Math.Max(0, AvailableFonts.Count - 1);

    public AppSettings Settings => _settings;

    public ExplorerTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (ReferenceEquals(_activeTab, value)) return;
            if (_activeTab is not null) _activeTab.IsSelected = false;
            _activeTab = value;
            if (_activeTab is not null) _activeTab.IsSelected = true;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActivePane));
            SyncSidebarSelection(ActivePane?.CurrentPath);
            RefreshPreviewForActivePane();
            RaiseCommandStates();
        }
    }

    public ExplorerPaneViewModel? ActivePane => ActiveTab?.ActivePane;

    public EigenThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            if (SetProperty(ref _themeMode, value))
            {
                _settings.ThemeMode = value;
                var dark = ResolveIsDark(value);
                ActiveSkin = ThemeCatalogService.GetById(dark
                    ? ThemeCatalogService.DefaultDarkSkinId
                    : ThemeCatalogService.DefaultLightSkinId);
                ThemeChanged?.Invoke(dark);
                OnPropertyChanged(nameof(ThemeLabel));
                ScheduleSaveSettings();
            }
        }
    }

    public EigenSkin ActiveSkin
    {
        get => _activeSkin;
        set
        {
            if (value is null) return;
            if (!Equals(_activeSkin, value))
            {
                _activeSkin = value;
                _settings.SkinId = value.Id;
                IsDarkTheme = value.IsDark;
                _themeMode = value.IsDark ? EigenThemeMode.Dark : EigenThemeMode.Light;
                _settings.ThemeMode = _themeMode;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeMode));
                OnPropertyChanged(nameof(ThemeLabel));
                OnPropertyChanged(nameof(EffectiveIconPack));
                SkinChanged?.Invoke(value);
                RebuildIconPackOptions();
                RefreshWorkspaceIcons();
                SaveSettings();
            }
        }
    }

    public IconPackStyle IconPackOverride
    {
        get => _iconPackOverride;
        set
        {
            if (SetProperty(ref _iconPackOverride, value))
            {
                _settings.IconPackOverride = value;
                OnPropertyChanged(nameof(EffectiveIconPack));
                RefreshWorkspaceIcons();
                SaveSettings();
            }
        }
    }

    public IconPackStyle EffectiveIconPack
        => _iconPackOverride == IconPackStyle.Auto ? ActiveSkin.IconPack : _iconPackOverride;

    public string SelectedSkinCategory
    {
        get => _selectedSkinCategory;
        set
        {
            if (SetProperty(ref _selectedSkinCategory, value))
                ApplySkinFilter();
        }
    }

    public string SkinSearchQuery
    {
        get => _skinSearchQuery;
        set
        {
            if (SetProperty(ref _skinSearchQuery, value))
                ApplySkinFilter();
        }
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    public string ThemeLabel => ActiveSkin?.Name ?? "Eigen Amber";

    public bool IsPreviewPaneOpen
    {
        get => _isPreviewPaneOpen;
        set
        {
            if (SetProperty(ref _isPreviewPaneOpen, value))
            {
                _settings.ShowPreviewPane = value;
                if (value)
                {
                    if (IsCustomizePaneOpen) IsCustomizePaneOpen = false;
                    RefreshPreviewForActivePane();
                }
                else
                {
                    IsFloatingPreviewOpen = false;
                }
                ScheduleSaveSettings();
            }
        }
    }

    public bool IsCustomizePaneOpen
    {
        get => _isCustomizePaneOpen;
        set
        {
            if (SetProperty(ref _isCustomizePaneOpen, value) && value && IsPreviewPaneOpen)
                IsPreviewPaneOpen = false;
        }
    }

    private void SetVerticalGap(
        ref double field,
        double value,
        double min,
        double max,
        Action<double> persist,
        [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        var clamped = Math.Round(Math.Clamp(value, min, max), 1);
        if (SetProperty(ref field, clamped, propertyName))
        {
            persist(clamped);
            RaiseLayoutMetricsChanged();
        }
    }

    public double DetailsVerticalGap
    {
        get => _detailsVerticalGap;
        set => SetVerticalGap(ref _detailsVerticalGap, value, MinDetailsVerticalGap, MaxDetailsVerticalGap, v => _settings.DetailsVerticalGap = v);
    }

    public double TilesVerticalGap
    {
        get => _tilesVerticalGap;
        set => SetVerticalGap(ref _tilesVerticalGap, value, MinTilesVerticalGap, MaxTilesVerticalGap, v => _settings.TilesVerticalGap = v);
    }

    public double CompactVerticalGap
    {
        get => _compactVerticalGap;
        set => SetVerticalGap(ref _compactVerticalGap, value, MinCompactVerticalGap, MaxCompactVerticalGap, v => _settings.CompactVerticalGap = v);
    }

    public double SidebarVerticalGap
    {
        get => _sidebarVerticalGap;
        set => SetVerticalGap(ref _sidebarVerticalGap, value, MinSidebarVerticalGap, MaxSidebarVerticalGap, v => _settings.SidebarVerticalGap = v);
    }

    public double HomeVerticalGap
    {
        get => _homeVerticalGap;
        set => SetVerticalGap(ref _homeVerticalGap, value, MinHomeVerticalGap, MaxHomeVerticalGap, v => _settings.HomeVerticalGap = v);
    }

    public double IconLeftGap
    {
        get => _iconLeftGap;
        set => SetVerticalGap(ref _iconLeftGap, value, MinIconLeftGap, MaxIconLeftGap, v => _settings.IconLeftGap = v);
    }

    public double IconTextHorizontalGap
    {
        get => _iconTextHorizontalGap;
        set => SetVerticalGap(ref _iconTextHorizontalGap, value, MinIconTextHorizontalGap, MaxIconTextHorizontalGap, v => _settings.IconTextHorizontalGap = v);
    }

    public double IconTextVerticalGap
    {
        get => _iconTextVerticalGap;
        set => SetVerticalGap(ref _iconTextVerticalGap, value, MinIconTextVerticalGap, MaxIconTextVerticalGap, v => _settings.IconTextVerticalGap = v);
    }

    public double ItemIconSize
    {
        get => _itemIconSize;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, MinIconSize, MaxIconSize), 0);
            if (SetProperty(ref _itemIconSize, clamped))
            {
                _settings.ItemIconSize = clamped;
                var preferLarge = clamped > 20;
                foreach (var tab in Tabs)
                {
                    tab.PrimaryPane.PreferLargeListIcons = preferLarge;
                    if (tab.SecondaryPane is not null)
                        tab.SecondaryPane.PreferLargeListIcons = preferLarge;
                }
                RaiseLayoutMetricsChanged();
            }
        }
    }

    public double TileIconSize
    {
        get => _tileIconSize;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, MinTileIconSize, MaxTileIconSize), 0);
            if (SetProperty(ref _tileIconSize, clamped))
            {
                _settings.TileIconSize = clamped;
                RaiseLayoutMetricsChanged();
            }
        }
    }

    public double ItemFontSize
    {
        get => _itemFontSize;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, MinFontSize, MaxFontSize) * 2.0) / 2.0;
            if (SetProperty(ref _itemFontSize, clamped))
            {
                _settings.ItemFontSize = clamped;
                RaiseLayoutMetricsChanged();
            }
        }
    }

    public FontOption SelectedFontOption
    {
        get => _selectedFontOption;
        set
        {
            if (value is null) return;
            if (!Equals(_selectedFontOption, value))
            {
                _selectedFontOption = value;
                _fontFamilyOverride = value.Id;
                _settings.FontFamilyOverride = value.Id;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FontFamilyOverride));
                OnPropertyChanged(nameof(EffectiveFontFamilyName));
                SkinChanged?.Invoke(ActiveSkin);
                RaiseLayoutMetricsChanged();
            }
        }
    }

    public string FontFamilyOverride
    {
        get => _fontFamilyOverride;
        set => SelectedFontOption = ResolveFontOption(value);
    }

    public string EffectiveFontFamilyName
        => string.Equals(_fontFamilyOverride, DefaultFontFamilyOverride, StringComparison.OrdinalIgnoreCase)
            ? ActiveSkin.FontFamilyName
            : _selectedFontOption.FontFamilyValue;

    public int FontWeightValue
    {
        get => _fontWeightValue;
        set
        {
            var clamped = ClampFontWeight(value);
            if (SetProperty(ref _fontWeightValue, clamped))
            {
                _settings.FontWeightValue = clamped;
                OnPropertyChanged(nameof(UiFontWeight));
                OnPropertyChanged(nameof(FontWeightLabel));
                RaiseLayoutMetricsChanged();
            }
        }
    }

    public FontWeight UiFontWeight => FontWeight.FromOpenTypeWeight(_fontWeightValue);

    public string FontWeightLabel => _fontWeightValue switch
    {
        100 => "Thin (100)",
        200 => "ExtraLight (200)",
        300 => "Light (300)",
        500 => "Medium (500)",
        600 => "SemiBold (600)",
        700 => "Bold (700)",
        800 => "ExtraBold (800)",
        900 => "Black (900)",
        _ => "Normal (400)"
    };

    public double LetterSpacing
    {
        get => _letterSpacing;
        set
        {
            var clamped = SnapLetterSpacing(value);
            if (SetProperty(ref _letterSpacing, clamped))
            {
                _settings.LetterSpacing = clamped;
                OnPropertyChanged(nameof(LetterSpacingLabel));
                RaiseLayoutMetricsChanged();
            }
        }
    }

    public string LetterSpacingLabel => LetterSpacing >= 0
        ? $"+{LetterSpacing:0.00} px"
        : $"{LetterSpacing:0.00} px";

    private double EffectiveFontWidthBoost
    {
        get
        {
            var fontToMeasure = EffectiveFontFamilyName.Split(',')[0].Trim();
            var baseBoost = FontCatalogService.GetWidthBoost(fontToMeasure);
            var weightBoost = _fontWeightValue >= 500 ? (_fontWeightValue - 400) * 0.04 : 0.0;
            return baseBoost + weightBoost;
        }
    }

    private double PositiveLetterSpacing => Math.Max(0.0, LetterSpacing);

    public double SecondaryFontSize => Math.Max(10.5, ItemFontSize - 0.5);
    public double SidebarFontSize => Math.Clamp(ItemFontSize, 11.0, 15.5);
    public Thickness SidebarItemPadding => new(18 + _iconLeftGap, Math.Round(4 + SidebarVerticalGap * 0.35, 1), 10, Math.Round(4 + SidebarVerticalGap * 0.35, 1));
    public Thickness SidebarItemMargin => new(4, Math.Round(SidebarVerticalGap / 2.0, 1), 4, Math.Round(SidebarVerticalGap / 2.0, 1));
    public Thickness SidebarTreeItemPadding => new(_iconLeftGap, Math.Round(4 + SidebarVerticalGap * 0.35, 1), 4, Math.Round(4 + SidebarVerticalGap * 0.35, 1));
    public Thickness SidebarTreeItemMargin => new(4, Math.Round(SidebarVerticalGap / 2.0, 1), 4, Math.Round(SidebarVerticalGap / 2.0, 1));
    public GridLength SidebarIconColumnWidth => new(16 + IconTextHorizontalGap);

    public Thickness IconLeftGapMargin => new(_iconLeftGap, 0, 0, 0);
    public double DetailsRowHeight => Math.Round(Math.Max(ItemIconSize + 10, ItemFontSize * 1.7) + DetailsVerticalGap);
    public Thickness DetailsRowMargin => new(0, Math.Round(DetailsVerticalGap / 2.0, 1), 0, Math.Round(DetailsVerticalGap / 2.0, 1));
    public GridLength DetailsIconColumnWidth => new(ItemIconSize + IconTextHorizontalGap);
    public double DetailsNameColWidth => CalculateDetailsNameColumnWidth(ActivePane?.Items);

    public double CalculateDetailsNameColumnWidth(IEnumerable<FileSystemItem>? items)
    {
        var iconWidth = DetailsIconColumnWidth.Value;
        var minWidth = Math.Round(_iconLeftGap + iconWidth + DetailsNamePaddingGap);

        string? shortestName = null;
        if (items is not null)
        {
            var minLen = int.MaxValue;
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Name)) continue;
                if (item.Name.Length < minLen)
                {
                    minLen = item.Name.Length;
                    shortestName = item.Name;
                }
            }
        }

        var textWidth = string.IsNullOrEmpty(shortestName)
            ? FontCatalogService.MeasureTextWidth("Name", EffectiveFontFamilyName, ItemFontSize, UiFontWeight, LetterSpacing)
            : FontCatalogService.MeasureTextWidth(shortestName, EffectiveFontFamilyName, ItemFontSize, UiFontWeight, LetterSpacing);

        return Math.Round(Math.Clamp(_iconLeftGap + iconWidth + textWidth + DetailsNamePaddingGap, minWidth, 720.0));
    }
    public double DetailsDateColWidth => Math.Round(148 + Math.Max(0, ItemFontSize - 12.5) * 9 + PositiveLetterSpacing * 18 + EffectiveFontWidthBoost);
    public double DetailsTypeColWidth => Math.Round(155 + Math.Max(0, ItemFontSize - 12.5) * 9 + PositiveLetterSpacing * 16 + EffectiveFontWidthBoost);
    public double DetailsSizeColWidth => Math.Round(96 + Math.Max(0, ItemFontSize - 12.5) * 6 + PositiveLetterSpacing * 9 + (FontWeightValue >= 500 ? 6 : 0));

    public Thickness TileIconMargin => new(0, 4, 0, IconTextVerticalGap);
    public double TileCardWidth => Math.Round(Math.Max(122, TileIconSize + ItemFontSize * 5.6 + PositiveLetterSpacing * 14 + EffectiveFontWidthBoost));
    public double TileCardHeight => Math.Round(TileIconSize + IconTextVerticalGap + ItemFontSize * 3.8 + 12 + (FontWeightValue >= 600 ? 4 : 0));
    public Thickness TileItemMargin => new(4, Math.Round(TilesVerticalGap / 2.0, 1), 4, Math.Round(TilesVerticalGap / 2.0, 1));

    public double CompactCardWidth => Math.Round(Math.Max(200, _iconLeftGap + ItemIconSize + IconTextHorizontalGap + ItemFontSize * 14.0 + PositiveLetterSpacing * 18 + EffectiveFontWidthBoost));
    public double CompactCardHeight => Math.Round(Math.Max(ItemIconSize + 8, ItemFontSize * 1.65) + CompactVerticalGap * 0.5);
    public Thickness CompactItemMargin => new(4, Math.Round(CompactVerticalGap / 2.0, 1), 4, Math.Round(CompactVerticalGap / 2.0, 1));
    public GridLength CompactIconColumnWidth => new(ItemIconSize + IconTextHorizontalGap);

    public Thickness HomeHeroMargin => new(0, 0, 0, Math.Round(6 + HomeVerticalGap, 1));
    public Thickness HomeSectionMargin => new(0, 0, 0, Math.Round(10 + HomeVerticalGap, 1));
    public Thickness HomeCardMargin => new(0, 0, 12, HomeVerticalGap);
    public Thickness HomePinnedCardMargin => new(0, 0, 10, Math.Round(HomeVerticalGap * 0.85, 1));
    public Thickness HomeRecentItemMargin => new(0, 0, 0, Math.Round(HomeVerticalGap * 0.35, 1));

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set => SetProperty(ref _isSidebarCollapsed, value);
    }

    public bool ShowHiddenFiles
    {
        get => _showHiddenFiles;
        set
        {
            if (SetProperty(ref _showHiddenFiles, value))
            {
                _settings.ShowHiddenFiles = value;
                foreach (var tab in Tabs)
                {
                    tab.PrimaryPane.ShowHiddenFiles = value;
                    if (tab.SecondaryPane is not null)
                        tab.SecondaryPane.ShowHiddenFiles = value;
                }
                RebuildSidebar();
                ScheduleSaveSettings();
            }
        }
    }

    public PreviewInfo? CurrentPreview
    {
        get => _currentPreview;
        private set => SetProperty(ref _currentPreview, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set => SetProperty(ref _isPreviewLoading, value);
    }

    public bool IsFloatingPreviewOpen
    {
        get => _isFloatingPreviewOpen;
        set => SetProperty(ref _isFloatingPreviewOpen, value && CurrentPreview is { CanZoom: true } && IsPreviewPaneOpen);
    }

    public double WindowBrightness
    {
        get => _windowBrightness;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, 0.40, 1.0), 2);
            if (SetProperty(ref _windowBrightness, clamped))
            {
                _settings.WindowBrightness = clamped;
                ScheduleSaveSettings();
                OnPropertyChanged(nameof(BrightnessOverlayOpacity));
                OnPropertyChanged(nameof(WindowBrightnessPercentText));
            }
        }
    }

    public double BrightnessOverlayOpacity => Math.Clamp((1.0 - _windowBrightness) * 0.75, 0.0, 0.45);
    public string WindowBrightnessPercentText => $"{Math.Round(_windowBrightness * 100):0}%";

    public double IconTransparency
    {
        get => _iconTransparency;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, MinTransparency, MaxTransparency), 2);
            if (SetProperty(ref _iconTransparency, clamped))
            {
                _settings.IconTransparency = clamped;
                ScheduleSaveSettings();
                OnPropertyChanged(nameof(IconOpacity));
                OnPropertyChanged(nameof(IconTransparencyPercentText));
            }
        }
    }

    public double TextTransparency
    {
        get => _textTransparency;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, MinTransparency, MaxTransparency), 2);
            if (SetProperty(ref _textTransparency, clamped))
            {
                _settings.TextTransparency = clamped;
                ScheduleSaveSettings();
                OnPropertyChanged(nameof(TextOpacity));
                OnPropertyChanged(nameof(TextTransparencyPercentText));
            }
        }
    }

    public double IconOpacity => Math.Clamp(1.0 - _iconTransparency, 0.10, 1.0);
    public double TextOpacity => Math.Clamp(1.0 - _textTransparency, 0.10, 1.0);
    public string IconTransparencyPercentText => $"{Math.Round(_iconTransparency * 100):0}%";
    public string TextTransparencyPercentText => $"{Math.Round(_textTransparency * 100):0}%";

    public string? CalculatedFolderSize
    {
        get => _calculatedFolderSize;
        private set => SetProperty(ref _calculatedFolderSize, value);
    }

    public bool IsCalculatingFolderSize
    {
        get => _isCalculatingFolderSize;
        private set => SetProperty(ref _isCalculatingFolderSize, value);
    }

    public FileOperationProgress? ActiveOperationProgress
    {
        get => _activeOperationProgress;
        private set => SetProperty(ref _activeOperationProgress, value);
    }

    public bool IsOperationRunning
    {
        get => _isOperationRunning;
        private set => SetProperty(ref _isOperationRunning, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => SetProperty(ref _toastMessage, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => SetProperty(ref _isToastVisible, value);
    }

    public bool CanUndo => _undoStack.Count > 0;
    public string UndoLabel => _undoStack.Count > 0 ? $"Undo {_undoStack.Peek().Description}" : "Undo";

    public ICommand NewTabCommand { get; private set; } = null!;
    public ICommand CloseTabCommand { get; private set; } = null!;
    public ICommand DuplicateTabCommand { get; private set; } = null!;
    public ICommand CloseOtherTabsCommand { get; private set; } = null!;
    public ICommand NextTabCommand { get; private set; } = null!;
    public ICommand PreviousTabCommand { get; private set; } = null!;
    public ICommand CycleThemeCommand { get; private set; } = null!;
    public ICommand ToggleSplitViewCommand { get; private set; } = null!;
    public ICommand TogglePreviewPaneCommand { get; private set; } = null!;
    public ICommand ToggleCustomizePaneCommand { get; private set; } = null!;
    public ICommand SelectSkinCommand { get; private set; } = null!;
    public ICommand SelectSkinCategoryCommand { get; private set; } = null!;
    public ICommand SelectIconPackCommand { get; private set; } = null!;
    public ICommand RandomSkinCommand { get; private set; } = null!;
    public ICommand ResetLayoutCustomizationCommand { get; private set; } = null!;
    public ICommand ToggleSidebarCommand { get; private set; } = null!;
    public ICommand ToggleHiddenFilesCommand { get; private set; } = null!;
    public ICommand SetViewModeCommand { get; private set; } = null!;
    public ICommand NewFolderCommand { get; private set; } = null!;
    public ICommand NewTextFileCommand { get; private set; } = null!;
    public ICommand NewMarkdownFileCommand { get; private set; } = null!;
    public ICommand CopyCommand { get; private set; } = null!;
    public ICommand CutCommand { get; private set; } = null!;
    public ICommand PasteCommand { get; private set; } = null!;
    public ICommand RenameCommand { get; private set; } = null!;
    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand PermanentDeleteCommand { get; private set; } = null!;
    public ICommand UndoCommand { get; private set; } = null!;
    public ICommand CopyToOtherPaneCommand { get; private set; } = null!;
    public ICommand MoveToOtherPaneCommand { get; private set; } = null!;
    public ICommand SwapPanesCommand { get; private set; } = null!;
    public ICommand OpenInTerminalCommand { get; private set; } = null!;
    public ICommand OpenInWindowsExplorerCommand { get; private set; } = null!;
    public ICommand ShowPropertiesCommand { get; private set; } = null!;
    public ICommand CopyPathCommand { get; private set; } = null!;
    public ICommand CopyNameCommand { get; private set; } = null!;
    public ICommand OpenInNewTabCommand { get; private set; } = null!;
    public ICommand OpenInSplitPaneCommand { get; private set; } = null!;
    public ICommand PinToSidebarCommand { get; private set; } = null!;
    public ICommand UnpinFromSidebarCommand { get; private set; } = null!;
    public ICommand CalculateFolderSizeCommand { get; private set; } = null!;
    public ICommand OpenPreviewFileCommand { get; private set; } = null!;
    public ICommand ResetBrightnessCommand { get; private set; } = null!;
    public ICommand CancelOperationCommand { get; private set; } = null!;
    public ICommand NavigateSidebarCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        OpenPreviewFileCommand = new RelayCommand(() =>
        {
            if (CurrentPreview is { FullPath: { Length: > 0 } path })
            {
                try { NativeShellService.OpenItem(path); } catch { }
            }
        });
        ResetBrightnessCommand = new RelayCommand(() => WindowBrightness = 1.0);
        NewTabCommand = new RelayCommand<string>(path => OpenNewTab(path ?? FormatHelper.HomeLocation));
        CloseTabCommand = new RelayCommand<ExplorerTabViewModel>(tab => CloseTab(tab ?? ActiveTab));
        DuplicateTabCommand = new RelayCommand<ExplorerTabViewModel>(tab =>
        {
            var src = tab ?? ActiveTab;
            if (src is not null) OpenNewTab(src.ActivePane.CurrentPath);
        });
        CloseOtherTabsCommand = new RelayCommand<ExplorerTabViewModel>(tab =>
        {
            var keep = tab ?? ActiveTab;
            if (keep is null) return;
            foreach (var other in Tabs.Where(t => !ReferenceEquals(t, keep)).ToList())
                CloseTab(other);
        });
        NextTabCommand = new RelayCommand(() => CycleTab(1));
        PreviousTabCommand = new RelayCommand(() => CycleTab(-1));

        CycleThemeCommand = new RelayCommand(() =>
        {
            ActiveSkin = ThemeCatalogService.GetById(IsDarkTheme
                ? ThemeCatalogService.DefaultLightSkinId
                : ThemeCatalogService.DefaultDarkSkinId);
            ShowToast($"Skin: {ActiveSkin.Name}");
        });

        ToggleSplitViewCommand = new RelayCommand(() =>
        {
            if (ActiveTab is null) return;
            ActiveTab.ToggleSplitView();
            _settings.StartInDualPane = ActiveTab.IsSplitView;
            OnPropertyChanged(nameof(ActivePane));
            RaiseCommandStates();
        });

        TogglePreviewPaneCommand = new RelayCommand(() => IsPreviewPaneOpen = !IsPreviewPaneOpen);
        ToggleCustomizePaneCommand = new RelayCommand(() => IsCustomizePaneOpen = !IsCustomizePaneOpen);
        SelectSkinCommand = new RelayCommand<EigenSkin>(skin =>
        {
            if (skin is null) return;
            ActiveSkin = skin;
            ShowToast($"Skin: {skin.Name}");
        });
        SelectSkinCategoryCommand = new RelayCommand<string>(cat =>
        {
            if (!string.IsNullOrWhiteSpace(cat))
                SelectedSkinCategory = cat;
        });
        SelectIconPackCommand = new RelayCommand<object>(param =>
        {
            if (param is IconPackStyle pack)
            {
                IconPackOverride = pack;
                ShowToast($"Icons: {pack}");
            }
            else if (param is string s && Enum.TryParse<IconPackStyle>(s, true, out var parsed))
            {
                IconPackOverride = parsed;
                ShowToast($"Icons: {parsed}");
            }
        });
        RandomSkinCommand = new RelayCommand(() =>
        {
            var others = AllSkins.Where(s => s.Id != ActiveSkin.Id).ToList();
            if (others.Count == 0) return;
            var picked = others[Random.Shared.Next(others.Count)];
            ActiveSkin = picked;
            ShowToast($"Skin: {picked.Name}");
        });
        ResetLayoutCustomizationCommand = new RelayCommand(() =>
        {
            DetailsVerticalGap = DefaultDetailsVerticalGap;
            TilesVerticalGap = DefaultTilesVerticalGap;
            CompactVerticalGap = DefaultCompactVerticalGap;
            SidebarVerticalGap = DefaultSidebarVerticalGap;
            HomeVerticalGap = DefaultHomeVerticalGap;
            ItemIconSize = DefaultIconSize;
            TileIconSize = DefaultTileIconSize;
            IconLeftGap = DefaultIconLeftGap;
            IconTextHorizontalGap = DefaultIconTextHorizontalGap;
            IconTextVerticalGap = DefaultIconTextVerticalGap;
            ItemFontSize = DefaultFontSize;
            FontFamilyOverride = DefaultFontFamilyOverride;
            FontWeightValue = DefaultFontWeightValue;
            LetterSpacing = DefaultLetterSpacing;
            IconTransparency = DefaultTransparency;
            TextTransparency = DefaultTransparency;
            ShowToast("Layout & typography reset to defaults");
        });
        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        ToggleHiddenFilesCommand = new RelayCommand(() =>
        {
            ShowHiddenFiles = !ShowHiddenFiles;
            ShowToast(ShowHiddenFiles ? "Showing hidden items" : "Hiding hidden items");
        });

        SetViewModeCommand = new RelayCommand<object>(param =>
        {
            if (ActivePane is null) return;
            if (param is ExplorerViewMode mode)
            {
                ActivePane.ViewMode = mode;
                _settings.DefaultViewMode = mode;
            }
            else if (param is string s && Enum.TryParse<ExplorerViewMode>(s, true, out var parsed))
            {
                ActivePane.ViewMode = parsed;
                _settings.DefaultViewMode = parsed;
            }
        });

        NewFolderCommand = new RelayCommand(() => _ = CreateNewFolderAsync(), CanMutateActiveFolder);
        NewTextFileCommand = new RelayCommand(() => _ = CreateNewFileAsync("New Text Document", ".txt"), CanMutateActiveFolder);
        NewMarkdownFileCommand = new RelayCommand(() => _ = CreateNewFileAsync("New Note", ".md"), CanMutateActiveFolder);

        CopyCommand = new RelayCommand(() => CopySelectedToClipboard(isCut: false), HasSelection);
        CutCommand = new RelayCommand(() => CopySelectedToClipboard(isCut: true), HasSelection);
        PasteCommand = new RelayCommand(() => _ = PasteFromClipboardAsync(), CanMutateActiveFolder);

        RenameCommand = new RelayCommand(BeginRenameSelected, () => GetSelectedPaths().Count == 1);
        DeleteCommand = new RelayCommand(() => _ = DeleteSelectedAsync(permanent: false), HasSelection);
        PermanentDeleteCommand = new RelayCommand(() => _ = DeleteSelectedAsync(permanent: true), HasSelection);
        UndoCommand = new RelayCommand(() => _ = UndoLastAsync(), () => CanUndo);

        CopyToOtherPaneCommand = new RelayCommand(() => _ = TransferToOtherPaneAsync(isMove: false), CanTransferToOtherPane);
        MoveToOtherPaneCommand = new RelayCommand(() => _ = TransferToOtherPaneAsync(isMove: true), CanTransferToOtherPane);
        SwapPanesCommand = new RelayCommand(() => ActiveTab?.SwapPanes(), () => ActiveTab?.IsSplitView == true);

        OpenInTerminalCommand = new RelayCommand(() =>
        {
            if (ActivePane is null) return;
            var selectedDir = GetPrimarySelectedItem() is { IsDirectory: true } dir ? dir.FullPath : ActivePane.CurrentPath;
            NativeShellService.OpenInTerminal(FormatHelper.IsHomePath(selectedDir)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : selectedDir);
        });

        OpenInWindowsExplorerCommand = new RelayCommand(() =>
        {
            var sel = GetPrimarySelectedItem();
            if (sel is not null) NativeShellService.OpenInExplorer(sel.FullPath);
            else if (ActivePane is not null && !ActivePane.IsHome) NativeShellService.OpenInExplorer(ActivePane.CurrentPath);
        });

        ShowPropertiesCommand = new RelayCommand(() =>
        {
            var target = GetPrimarySelectedItem()?.FullPath ?? (ActivePane is { IsHome: false } ? ActivePane.CurrentPath : null);
            if (!string.IsNullOrEmpty(target))
                NativeShellService.ShowProperties(target);
        });

        CopyPathCommand = new RelayCommand(() =>
        {
            var paths = GetSelectedPaths();
            var text = paths.Count > 0
                ? string.Join(Environment.NewLine, paths)
                : (ActivePane is { IsHome: false } ? ActivePane.CurrentPath : string.Empty);
            if (!string.IsNullOrEmpty(text))
            {
                ClipboardService.CopyText(text);
                ShowToast(paths.Count > 1 ? $"Copied {paths.Count} paths" : "Copied path to clipboard");
            }
        });

        CopyNameCommand = new RelayCommand(() =>
        {
            var item = GetPrimarySelectedItem();
            if (item is not null)
            {
                ClipboardService.CopyText(item.Name);
                ShowToast("Copied name to clipboard");
            }
        }, () => GetPrimarySelectedItem() is not null);

        OpenInNewTabCommand = new RelayCommand<object>(param =>
        {
            var path = ExtractFolderPath(param);
            if (!string.IsNullOrEmpty(path)) OpenNewTab(path);
        });

        OpenInSplitPaneCommand = new RelayCommand<object>(param =>
        {
            var path = ExtractFolderPath(param);
            if (!string.IsNullOrEmpty(path) && ActiveTab is not null)
                ActiveTab.OpenInSecondaryPane(path);
        });

        PinToSidebarCommand = new RelayCommand<object>(param =>
        {
            var path = ExtractFolderPath(param);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) &&
                !_settings.PinnedFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _settings.PinnedFolders.Add(path);
                RebuildSidebar();
                ActivePane?.RefreshHomeData();
                ShowToast($"Pinned \"{FormatHelper.GetDisplayTitle(path)}\"");
            }
        });

        UnpinFromSidebarCommand = new RelayCommand<object>(param =>
        {
            var path = ExtractFolderPath(param);
            if (!string.IsNullOrEmpty(path))
            {
                _settings.PinnedFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                RebuildSidebar();
                ActivePane?.RefreshHomeData();
                ShowToast($"Unpinned \"{FormatHelper.GetDisplayTitle(path)}\"");
            }
        });

        CalculateFolderSizeCommand = new RelayCommand(() => _ = CalculateSelectedFolderSizeAsync());
        CancelOperationCommand = new RelayCommand(() => _operationCts?.Cancel());
        NavigateSidebarCommand = new RelayCommand<SidebarNode>(node =>
        {
            if (node is null || string.IsNullOrEmpty(node.Path) || ActivePane is null) return;
            _ = ActivePane.NavigateToAsync(node.Path);
        });
    }

    public void OpenNewTab(string path)
    {
        var tab = CreateTab(path);
        Tabs.Add(tab);
        ActiveTab = tab;
        _ = tab.PrimaryPane.NavigateToAsync(path, recordHistory: false);
    }

    public void CloseTab(ExplorerTabViewModel? tab)
    {
        if (tab is null || Tabs.Count <= 1) return;
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;

        Tabs.RemoveAt(idx);
        tab.Dispose();

        if (ReferenceEquals(ActiveTab, tab))
        {
            var nextIdx = Math.Clamp(idx - 1, 0, Tabs.Count - 1);
            ActiveTab = Tabs[nextIdx];
        }
    }

    public void RebuildSidebar()
    {
        QuickAccessNodes.Clear();

        QuickAccessNodes.Add(new SidebarNode
        {
            Title = "Home",
            Path = FormatHelper.HomeLocation,
            Glyph = "\uE80F",
            IsSpecialHome = true
        });

        var standardFolders = new List<(string Title, string Path, string Glyph)>
        {
            ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\uE8FC"),
            ("Downloads", NativeShellService.GetKnownDownloadsPath(), "\uE896"),
            ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "\uE8A5"),
            ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "\uEB9F"),
            ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "\uEC4F"),
            ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "\uE8B2")
        };

        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (title, path, glyph) in standardFolders)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !added.Add(path))
                continue;

            var (icon, _) = ResolveIconAndType(path, isDirectory: true);
            var isPinned = _settings.PinnedFolders.Contains(path, StringComparer.OrdinalIgnoreCase);
            QuickAccessNodes.Add(new SidebarNode
            {
                Title = title,
                Path = path,
                Glyph = glyph,
                Icon = icon,
                IsPinned = isPinned
            });
        }

        foreach (var pinned in _settings.PinnedFolders)
        {
            if (!Directory.Exists(pinned) || !added.Add(pinned))
                continue;

            var (icon, _) = ResolveIconAndType(pinned, isDirectory: true);
            QuickAccessNodes.Add(new SidebarNode
            {
                Title = FormatHelper.GetDisplayTitle(pinned),
                Path = pinned,
                Glyph = "\uE718",
                Icon = icon,
                IsPinned = true
            });
        }

        DriveTreeNodes.Clear();
        foreach (var drive in _fileSystem.GetDrives())
        {
            var (icon, _) = ResolveIconAndType(drive.RootPath, isDirectory: true, isDrive: true);
            var node = new SidebarNode
            {
                Title = drive.Name,
                Path = drive.RootPath,
                Glyph = "\uEDA2",
                Icon = icon,
                IsDrive = true,
                CanExpand = true
            };
            // Placeholder child so TreeView renders expand chevron
            node.Children.Add(new SidebarNode { Title = "Loading..." });
            DriveTreeNodes.Add(node);
        }

        SyncSidebarSelection(ActivePane?.CurrentPath);
    }

    public void SyncSidebarSelection(string? activePath)
    {
        var normalized = NormalizeSidebarPath(activePath);

        foreach (var node in QuickAccessNodes)
        {
            node.IsSelected = !string.IsNullOrEmpty(normalized)
                && string.Equals(NormalizeSidebarPath(node.Path), normalized, StringComparison.OrdinalIgnoreCase);
        }

        static void ApplyTree(IEnumerable<SidebarNode> nodes, string normalizedPath)
        {
            foreach (var node in nodes)
            {
                node.IsSelected = !string.IsNullOrEmpty(normalizedPath)
                    && string.Equals(NormalizeSidebarPath(node.Path), normalizedPath, StringComparison.OrdinalIgnoreCase);
                if (node.Children.Count > 0)
                    ApplyTree(node.Children, normalizedPath);
            }
        }

        ApplyTree(DriveTreeNodes, normalized);
    }

    private static string NormalizeSidebarPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        if (FormatHelper.IsHomePath(path))
            return FormatHelper.HomeLocation;

        var trimmed = path.Trim();
        if (trimmed.Length == 2 && trimmed[1] == ':')
            return trimmed + "\\";
        if (trimmed.Length == 3 && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/'))
            return trimmed[..2] + "\\";
        return Path.TrimEndingDirectorySeparator(trimmed);
    }

    public void LoadSidebarNodeChildren(SidebarNode node)
    {
        if (node.IsLoaded || string.IsNullOrEmpty(node.Path))
            return;

        node.IsLoaded = true;
        node.Children.Clear();

        foreach (var dir in _fileSystem.GetChildDirectories(node.Path, ShowHiddenFiles))
        {
            var (icon, _) = ResolveIconAndType(dir.FullPath, isDirectory: true);
            var child = new SidebarNode
            {
                Title = dir.Name,
                Path = dir.FullPath,
                Icon = icon,
                CanExpand = true
            };
            child.Children.Add(new SidebarNode { Title = "Loading..." });
            node.Children.Add(child);
        }

        node.CanExpand = node.Children.Count > 0;
        SyncSidebarSelection(ActivePane?.CurrentPath);
    }

    public async Task<bool> CommitRenameAsync(FileSystemItem item, string newName)
    {
        item.IsEditing = false;
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(item.Name, newName.Trim(), StringComparison.Ordinal))
            return false;

        var oldPath = item.FullPath;
        var oldName = item.Name;

        try
        {
            var newPath = _fileSystem.RenameItem(oldPath, newName);
            PushUndo(new UndoRecord
            {
                Description = $"Rename \"{oldName}\"",
                UndoAsync = _ =>
                {
                    _fileSystem.RenameItem(newPath, oldName);
                    return Task.CompletedTask;
                }
            });

            if (ActivePane is not null)
                await ActivePane.RefreshAsync(newPath);
            return true;
        }
        catch (Exception ex)
        {
            if (ActivePane is not null)
                ActivePane.ErrorMessage = ex.Message;
            return false;
        }
    }

    public async Task HandleDropAsync(IReadOnlyList<string> sourcePaths, string destinationFolder, bool isMove)
    {
        if (sourcePaths.Count == 0 || !Directory.Exists(destinationFolder))
            return;

        await ExecuteCopyOrMoveAsync(sourcePaths, destinationFolder, isMove);
    }

    public void SaveSettings() => _settingsService.Save(_settings);

    private ExplorerTabViewModel CreateTab(string initialPath)
    {
        var tab = new ExplorerTabViewModel(CreatePane, initialPath);
        tab.TabUpdated += _ =>
        {
            OnPropertyChanged(nameof(ActivePane));
            SyncSidebarSelection(ActivePane?.CurrentPath);
            RaiseCommandStates();
        };
        return tab;
    }

    private ExplorerPaneViewModel CreatePane(string initialPath)
    {
        var pane = new ExplorerPaneViewModel(
            _fileSystem,
            () => _settings.PinnedFolders,
            () => _settings.RecentPaths,
            _settings.DefaultViewMode,
            ShowHiddenFiles,
            ResolveIconAndType)
        {
            PreferLargeListIcons = ItemIconSize > 20
        };

        pane.Activated += p =>
        {
            OnPropertyChanged(nameof(ActivePane));
            SyncSidebarSelection(ActivePane?.CurrentPath);
            RefreshPreviewForActivePane();
            RaiseCommandStates();
        };

        pane.NavigationSucceeded += path =>
        {
            if (!FormatHelper.IsHomePath(path))
            {
                _settings.RecentPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                _settings.RecentPaths.Insert(0, path);
                if (_settings.RecentPaths.Count > 15)
                    _settings.RecentPaths.RemoveRange(15, _settings.RecentPaths.Count - 15);
            }
            pane.UpdateCutMarkers(_cutPaths);
            if (ReferenceEquals(pane, ActivePane))
            {
                SyncSidebarSelection(path);
                RefreshPreviewForActivePane();
            }
            RaiseCommandStates();
        };

        pane.SelectionChanged += item =>
        {
            if (ReferenceEquals(pane, ActivePane))
            {
                RefreshPreview(item);
                RaiseCommandStates();
            }
        };

        pane.ItemsUpdated += () =>
        {
            if (ReferenceEquals(pane, ActivePane))
                OnPropertyChanged(nameof(DetailsNameColWidth));
        };

        return pane;
    }

    private void CycleTab(int delta)
    {
        if (Tabs.Count <= 1 || ActiveTab is null) return;
        var idx = Tabs.IndexOf(ActiveTab);
        var next = (idx + delta + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[next];
    }

    private async Task CreateNewFolderAsync()
    {
        if (ActivePane is null || ActivePane.IsHome) return;
        try
        {
            var created = _fileSystem.CreateFolder(ActivePane.CurrentPath);
            PushUndo(new UndoRecord
            {
                Description = "New folder",
                UndoAsync = _ =>
                {
                    if (Directory.Exists(created))
                        _fileSystem.DeleteToRecycleBin([created]);
                    return Task.CompletedTask;
                }
            });

            await ActivePane.RefreshAsync(created);
            var item = ActivePane.Items.FirstOrDefault(i => string.Equals(i.FullPath, created, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
                ActivePane.TriggerInlineRename(item);
        }
        catch (Exception ex)
        {
            ActivePane.ErrorMessage = ex.Message;
        }
    }

    private async Task CreateNewFileAsync(string baseName, string ext)
    {
        if (ActivePane is null || ActivePane.IsHome) return;
        try
        {
            var created = _fileSystem.CreateFile(ActivePane.CurrentPath, baseName, ext);
            PushUndo(new UndoRecord
            {
                Description = $"New {ext.TrimStart('.')} file",
                UndoAsync = _ =>
                {
                    if (File.Exists(created))
                        _fileSystem.DeleteToRecycleBin([created]);
                    return Task.CompletedTask;
                }
            });

            await ActivePane.RefreshAsync(created);
            var item = ActivePane.Items.FirstOrDefault(i => string.Equals(i.FullPath, created, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
                ActivePane.TriggerInlineRename(item);
        }
        catch (Exception ex)
        {
            ActivePane.ErrorMessage = ex.Message;
        }
    }

    private void CopySelectedToClipboard(bool isCut)
    {
        var paths = GetSelectedPaths();
        if (paths.Count == 0) return;

        ClipboardService.CopyFilesToClipboard(paths, isCut);
        _cutPaths.Clear();
        if (isCut)
        {
            foreach (var p in paths)
                _cutPaths.Add(p);
        }

        UpdateCutMarkersAcrossPanes();
        ShowToast(isCut
            ? $"Cut {paths.Count} item(s) — ready to move"
            : $"Copied {paths.Count} item(s) to clipboard");
    }

    private async Task PasteFromClipboardAsync()
    {
        if (ActivePane is null || ActivePane.IsHome) return;
        var (paths, isCut) = ClipboardService.GetClipboardFiles();
        if (paths.Count == 0) return;

        await ExecuteCopyOrMoveAsync(paths, ActivePane.CurrentPath, isCut);

        if (isCut)
        {
            _cutPaths.Clear();
            UpdateCutMarkersAcrossPanes();
        }
    }

    private async Task TransferToOtherPaneAsync(bool isMove)
    {
        if (ActiveTab is null || ActivePane is null) return;
        var other = ActiveTab.GetOtherPane(ActivePane);
        if (other is null || other.IsHome) return;

        var paths = GetSelectedPaths();
        if (paths.Count == 0) return;

        await ExecuteCopyOrMoveAsync(paths, other.CurrentPath, isMove);
    }

    private async Task ExecuteCopyOrMoveAsync(IReadOnlyList<string> sourcePaths, string destFolder, bool isMove)
    {
        _operationCts?.Cancel();
        _operationCts = new CancellationTokenSource();
        var ct = _operationCts.Token;

        IsOperationRunning = true;
        var progress = new Progress<FileOperationProgress>(p => ActiveOperationProgress = p);

        try
        {
            var transferred = await _fileSystem.CopyOrMoveItemsAsync(
                sourcePaths,
                destFolder,
                isMove,
                ConflictAction.KeepBoth,
                progress,
                ct);

            if (transferred.Count > 0)
            {
                if (isMove)
                {
                    PushUndo(new UndoRecord
                    {
                        Description = $"Move {transferred.Count} item(s)",
                        UndoAsync = async token =>
                        {
                            var grouped = transferred.GroupBy(t => Path.GetDirectoryName(t.SourcePath) ?? string.Empty);
                            foreach (var grp in grouped)
                            {
                                if (!string.IsNullOrEmpty(grp.Key) && Directory.Exists(grp.Key))
                                    await _fileSystem.CopyOrMoveItemsAsync(grp.Select(x => x.DestinationPath).ToList(), grp.Key, true, ConflictAction.KeepBoth, null, token);
                            }
                        }
                    });
                }
                else
                {
                    PushUndo(new UndoRecord
                    {
                        Description = $"Copy {transferred.Count} item(s)",
                        UndoAsync = _ =>
                        {
                            _fileSystem.DeleteToRecycleBin(transferred.Select(t => t.DestinationPath));
                            return Task.CompletedTask;
                        }
                    });
                }

                await RefreshAllOpenPanesAsync(transferred[^1].DestinationPath);
                ShowToast(isMove ? $"Moved {transferred.Count} item(s)" : $"Copied {transferred.Count} item(s)");
            }
        }
        catch (OperationCanceledException)
        {
            ShowToast("Operation cancelled");
        }
        catch (Exception ex)
        {
            if (ActivePane is not null)
                ActivePane.ErrorMessage = ex.Message;
        }
        finally
        {
            IsOperationRunning = false;
            ActiveOperationProgress = null;
        }
    }

    private void BeginRenameSelected()
    {
        if (ActivePane is null) return;
        var item = GetPrimarySelectedItem();
        if (item is not null)
            ActivePane.TriggerInlineRename(item);
    }

    private async Task DeleteSelectedAsync(bool permanent)
    {
        if (ActivePane is null) return;
        var paths = GetSelectedPaths();
        if (paths.Count == 0) return;

        var summary = paths.Count == 1
            ? $"\"{Path.GetFileName(paths[0])}\""
            : $"{paths.Count} selected items";

        if (permanent)
        {
            var confirmed = ConfirmAction?.Invoke(
                "Permanent Delete",
                $"Permanently delete {summary}? This cannot be undone and skips the Recycle Bin.") ?? false;
            if (!confirmed) return;
        }

        try
        {
            if (permanent)
                _fileSystem.DeletePermanently(paths);
            else
                _fileSystem.DeleteToRecycleBin(paths);

            await RefreshAllOpenPanesAsync();
            ShowToast(permanent ? $"Permanently deleted {summary}" : $"Moved {summary} to Recycle Bin");
        }
        catch (Exception ex)
        {
            ActivePane.ErrorMessage = ex.Message;
        }
    }

    private async Task UndoLastAsync()
    {
        if (_undoStack.Count == 0) return;
        var record = _undoStack.Pop();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();

        try
        {
            await record.UndoAsync(CancellationToken.None);
            await RefreshAllOpenPanesAsync();
            ShowToast($"Undid {record.Description}");
        }
        catch (Exception ex)
        {
            if (ActivePane is not null)
                ActivePane.ErrorMessage = $"Undo failed: {ex.Message}";
        }
    }

    private void PushUndo(UndoRecord record)
    {
        _undoStack.Push(record);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        ((RelayCommand)UndoCommand).RaiseCanExecuteChanged();
    }

    private void RefreshPreviewForActivePane()
    {
        if (!IsPreviewPaneOpen) return;
        RefreshPreview(GetPrimarySelectedItem());
    }

    private async void RefreshPreview(FileSystemItem? item)
    {
        _previewCts?.Cancel();
        _folderStatsCts?.Cancel();
        CalculatedFolderSize = null;
        IsCalculatingFolderSize = false;

        if (!IsPreviewPaneOpen || item is null || ActivePane is null || ActivePane.IsHome)
        {
            CurrentPreview = null;
            IsPreviewLoading = false;
            IsFloatingPreviewOpen = false;
            return;
        }

        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;
        IsPreviewLoading = true;

        try
        {
            var info = await _fileSystem.BuildPreviewAsync(item, ct);
            if (!ct.IsCancellationRequested)
                CurrentPreview = info;
        }
        catch (OperationCanceledException)
        {
            // Superseded
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                CurrentPreview = null;
                IsFloatingPreviewOpen = false;
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsPreviewLoading = false;
        }
    }

    private async Task CalculateSelectedFolderSizeAsync()
    {
        var preview = CurrentPreview;
        if (preview is null || preview.Kind != PreviewKind.Folder || !Directory.Exists(preview.FullPath))
            return;

        _folderStatsCts?.Cancel();
        _folderStatsCts = new CancellationTokenSource();
        var ct = _folderStatsCts.Token;

        IsCalculatingFolderSize = true;
        CalculatedFolderSize = "Calculating...";

        try
        {
            var (files, folders, bytes) = await _fileSystem.CalculateFolderStatsAsync(preview.FullPath, ct);
            if (!ct.IsCancellationRequested)
                CalculatedFolderSize = $"{FormatHelper.FormatExactSize(bytes)}  •  {files:N0} files, {folders:N0} folders";
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsCalculatingFolderSize = false;
        }
    }

    private async Task RefreshAllOpenPanesAsync(string? selectPath = null)
    {
        foreach (var tab in Tabs)
        {
            await tab.PrimaryPane.RefreshAsync(ReferenceEquals(tab.PrimaryPane, ActivePane) ? selectPath : null);
            if (tab.SecondaryPane is not null)
                await tab.SecondaryPane.RefreshAsync(ReferenceEquals(tab.SecondaryPane, ActivePane) ? selectPath : null);
        }
    }

    private void UpdateCutMarkersAcrossPanes()
    {
        foreach (var tab in Tabs)
        {
            tab.PrimaryPane.UpdateCutMarkers(_cutPaths);
            tab.SecondaryPane?.UpdateCutMarkers(_cutPaths);
        }
    }

    private IReadOnlyList<string> GetSelectedPaths()
    {
        if (ActivePane is null) return [];
        if (ActivePane.SelectedItems.Count > 0)
            return ActivePane.SelectedItems.Select(i => i.FullPath).ToList();
        return ActivePane.SelectedItem is not null ? [ActivePane.SelectedItem.FullPath] : [];
    }

    private FileSystemItem? GetPrimarySelectedItem()
    {
        if (ActivePane is null) return null;
        return ActivePane.SelectedItems.FirstOrDefault() ?? ActivePane.SelectedItem;
    }

    private string? ExtractFolderPath(object? param)
    {
        if (param is FileSystemItem item && item.IsDirectory) return item.FullPath;
        if (param is SidebarNode node && !string.IsNullOrEmpty(node.Path)) return node.Path;
        if (param is string str && Directory.Exists(str)) return str;
        var sel = GetPrimarySelectedItem();
        return sel is { IsDirectory: true } ? sel.FullPath : null;
    }

    private bool HasSelection() => GetSelectedPaths().Count > 0;
    private bool CanMutateActiveFolder() => ActivePane is { IsHome: false, IsDeepSearch: false };
    private bool CanTransferToOtherPane()
    {
        if (ActiveTab is null || ActivePane is null || !ActiveTab.IsSplitView) return false;
        var other = ActiveTab.GetOtherPane(ActivePane);
        return other is { IsHome: false } && HasSelection();
    }

    private void RaiseCommandStates()
    {
        ((RelayCommand)NewFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NewTextFileCommand).RaiseCanExecuteChanged();
        ((RelayCommand)NewMarkdownFileCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CutCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PasteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RenameCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PermanentDeleteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyToOtherPaneCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MoveToOtherPaneCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SwapPanesCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyNameCommand).RaiseCanExecuteChanged();
    }

    public void ShowToast(string message)
    {
        _toastCts?.Cancel();
        var cts = new CancellationTokenSource();
        _toastCts = cts;

        ToastMessage = message;
        IsToastVisible = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2600, cts.Token);
                if (!cts.Token.IsCancellationRequested)
                {
                    var app = System.Windows.Application.Current;
                    if (app?.Dispatcher is not null)
                        await app.Dispatcher.InvokeAsync(() => IsToastVisible = false);
                }
            }
            catch (OperationCanceledException)
            {
                // Replaced by newer toast
            }
        });
    }

    private (System.Windows.Media.ImageSource? Icon, string TypeName) ResolveIconAndType(
        string path,
        bool isDirectory,
        bool isDrive = false,
        bool largeIcon = false)
    {
        var (nativeIcon, typeName) = NativeShellService.GetIconAndType(path, isDirectory, isDrive, largeIcon);

        if (!isDirectory && ArchiveService.IsArchivePath(path))
        {
            var ext = Path.GetExtension(path);
            if (NativeShellService.HasThirdPartyArchiverAssociation(ext))
                return (nativeIcon, typeName);

            return (VectorIconPackService.GetEigenArchiverIcon(ActiveSkin), typeName);
        }

        var pack = EffectiveIconPack;
        if (pack == IconPackStyle.Native)
            return (nativeIcon, typeName);

        var themed = VectorIconPackService.GetThemedIcon(pack, path, isDirectory, isDrive, ActiveSkin);
        return (themed, typeName);
    }

    private void RefreshWorkspaceIcons()
    {
        foreach (var node in QuickAccessNodes)
        {
            if (node.IsSpecialHome || string.IsNullOrEmpty(node.Path)) continue;
            var (icon, _) = ResolveIconAndType(node.Path, isDirectory: true);
            node.Icon = icon;
        }

        void RefreshTreeNodes(IEnumerable<SidebarNode> nodes)
        {
            foreach (var n in nodes)
            {
                if (!string.IsNullOrEmpty(n.Path))
                {
                    var (icon, _) = ResolveIconAndType(n.Path, isDirectory: true, isDrive: n.IsDrive);
                    n.Icon = icon;
                }
                if (n.IsLoaded && n.Children.Count > 0)
                    RefreshTreeNodes(n.Children);
            }
        }

        RefreshTreeNodes(DriveTreeNodes);

        foreach (var tab in Tabs)
        {
            tab.PrimaryPane.RefreshVisibleIcons();
            tab.SecondaryPane?.RefreshVisibleIcons();
        }
    }

    private void ApplySkinFilter()
    {
        var query = SkinSearchQuery.Trim();
        IEnumerable<EigenSkin> matches = ThemeCatalogService.Skins;

        if (!string.Equals(SelectedSkinCategory, "All", StringComparison.OrdinalIgnoreCase))
            matches = matches.Where(s => string.Equals(s.Category, SelectedSkinCategory, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(query))
            matches = matches.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                      || s.Mood.Contains(query, StringComparison.OrdinalIgnoreCase)
                                      || s.Category.Contains(query, StringComparison.OrdinalIgnoreCase));

        FilteredSkins.Clear();
        foreach (var skin in matches)
            FilteredSkins.Add(skin);
    }

    private void RaiseLayoutMetricsChanged()
    {
        ScheduleSaveSettings();
        OnPropertyChanged(nameof(SecondaryFontSize));
        OnPropertyChanged(nameof(SidebarFontSize));
        OnPropertyChanged(nameof(SidebarItemPadding));
        OnPropertyChanged(nameof(SidebarItemMargin));
        OnPropertyChanged(nameof(SidebarTreeItemPadding));
        OnPropertyChanged(nameof(SidebarTreeItemMargin));
        OnPropertyChanged(nameof(SidebarIconColumnWidth));
        OnPropertyChanged(nameof(IconLeftGapMargin));
        OnPropertyChanged(nameof(DetailsRowHeight));
        OnPropertyChanged(nameof(DetailsRowMargin));
        OnPropertyChanged(nameof(DetailsIconColumnWidth));
        OnPropertyChanged(nameof(DetailsNameColWidth));
        OnPropertyChanged(nameof(DetailsDateColWidth));
        OnPropertyChanged(nameof(DetailsTypeColWidth));
        OnPropertyChanged(nameof(DetailsSizeColWidth));
        OnPropertyChanged(nameof(TileIconSize));
        OnPropertyChanged(nameof(TileIconMargin));
        OnPropertyChanged(nameof(TileCardWidth));
        OnPropertyChanged(nameof(TileCardHeight));
        OnPropertyChanged(nameof(TileItemMargin));
        OnPropertyChanged(nameof(CompactCardWidth));
        OnPropertyChanged(nameof(CompactCardHeight));
        OnPropertyChanged(nameof(CompactItemMargin));
        OnPropertyChanged(nameof(CompactIconColumnWidth));
        OnPropertyChanged(nameof(HomeHeroMargin));
        OnPropertyChanged(nameof(HomeSectionMargin));
        OnPropertyChanged(nameof(HomeCardMargin));
        OnPropertyChanged(nameof(HomePinnedCardMargin));
        OnPropertyChanged(nameof(HomeRecentItemMargin));
    }

    private FontOption ResolveFontOption(string? idOrName)
    {
        if (!string.IsNullOrWhiteSpace(idOrName))
        {
            var match = AvailableFonts.FirstOrDefault(f =>
                string.Equals(f.Id, idOrName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.DisplayName, idOrName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }
        return AvailableFonts[0];
    }

    private static int ClampFontWeight(int weight)
    {
        var clamped = Math.Clamp((double)weight, MinFontWeightValue, MaxFontWeightValue);
        return (int)Math.Round(clamped / 100.0) * 100;
    }

    private static double SnapLetterSpacing(double spacing)
    {
        var clamped = Math.Clamp(spacing, MinLetterSpacing, MaxLetterSpacing);
        return Math.Round(clamped * 4.0) / 4.0;
    }

    public static bool ResolveIsDark(EigenThemeMode mode) => mode switch
    {
        EigenThemeMode.Dark => true,
        EigenThemeMode.Light => false,
        _ => NativeShellService.IsSystemDarkMode()
    };

    public bool TryGetFolderColumnWidths(string? folderPath, out FolderColumnWidths? widths)
    {
        widths = null;
        var key = NormalizeFolderKey(folderPath);
        if (key is null || _settings.FolderColumnLayout.Count == 0)
            return false;

        if (_settings.FolderColumnLayout.TryGetValue(key, out var exact))
        {
            widths = exact;
            return true;
        }

        // Handle JSON deserialization that may use case-sensitive default comparer
        foreach (var kvp in _settings.FolderColumnLayout)
        {
            if (string.Equals(NormalizeFolderKey(kvp.Key), key, StringComparison.OrdinalIgnoreCase))
            {
                widths = kvp.Value;
                return true;
            }
        }

        return false;
    }

    private CancellationTokenSource? _saveSettingsDebounceCts;
    private bool _hasPendingSettingsSave;

    public void SaveFolderColumnWidths(string? folderPath, double nameWidth, double dateWidth, double typeWidth)
    {
        var key = NormalizeFolderKey(folderPath);
        if (key is null) return;

        if (double.IsNaN(nameWidth) || double.IsNaN(dateWidth) || double.IsNaN(typeWidth))
            return;

        if (_settings.FolderColumnLayout.Comparer != StringComparer.OrdinalIgnoreCase)
            _settings.FolderColumnLayout = new Dictionary<string, FolderColumnWidths>(_settings.FolderColumnLayout, StringComparer.OrdinalIgnoreCase);

        if (_settings.FolderColumnLayout.Count >= 300 && !_settings.FolderColumnLayout.ContainsKey(key))
        {
            var oldestKey = _settings.FolderColumnLayout.Keys.FirstOrDefault();
            if (oldestKey is not null)
                _settings.FolderColumnLayout.Remove(oldestKey);
        }

        _settings.FolderColumnLayout[key] = new FolderColumnWidths
        {
            Name = Math.Round(Math.Max(120, nameWidth)),
            Date = Math.Round(Math.Max(80, dateWidth)),
            Type = Math.Round(Math.Max(80, typeWidth))
        };
        ScheduleSaveSettings();
    }

    private void ScheduleSaveSettings()
    {
        _hasPendingSettingsSave = true;
        _saveSettingsDebounceCts?.Cancel();
        _saveSettingsDebounceCts = new CancellationTokenSource();
        var token = _saveSettingsDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token);
                if (!token.IsCancellationRequested)
                {
                    _hasPendingSettingsSave = false;
                    SaveSettings();
                }
            }
            catch (OperationCanceledException)
            {
                // Debounced
            }
        }, token);
    }

    private static string? NormalizeFolderKey(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;
        var trimmed = folderPath.Trim();
        if (trimmed.Length <= 3 && trimmed.EndsWith(":\\", StringComparison.Ordinal))
            return trimmed;
        return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public void Dispose()
    {
        _saveSettingsDebounceCts?.Cancel();
        if (_hasPendingSettingsSave)
        {
            _hasPendingSettingsSave = false;
            SaveSettings();
        }
        _previewCts?.Cancel();
        _folderStatsCts?.Cancel();
        _operationCts?.Cancel();
        _toastCts?.Cancel();
        foreach (var tab in Tabs)
            tab.Dispose();
    }
}
