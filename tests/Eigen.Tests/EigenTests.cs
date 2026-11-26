using System.Globalization;
using System.IO;
using System.Windows;
using Eigen.Converters;
using Eigen.Models;
using Eigen.Services;
using Eigen.ViewModels;

namespace Eigen.Tests;

public class EigenTests : IDisposable
{
    private readonly string _tempRoot;

    public EigenTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"EigenTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup
        }
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(2684354560, "2.5 GB")]
    public void FormatSize_FormatsHumanReadableUnits(long bytes, string expected)
    {
        Assert.Equal(expected, FormatHelper.FormatSize(bytes));
    }

    [Fact]
    public void NaturalStringComparer_SortsNumbersLogically()
    {
        var input = new[] { "file10.txt", "file2.txt", "file1.txt", "file20.txt", "Alpha9.md", "alpha12.md" };
        var sorted = input.OrderBy(x => x, NaturalStringComparer.Instance).ToArray();

        Assert.Equal(["Alpha9.md", "alpha12.md", "file1.txt", "file2.txt", "file10.txt", "file20.txt"], sorted);
    }

    [Fact]
    public void BuildBreadcrumbs_ParsesWindowsAndHomePaths()
    {
        var homeCrumbs = FormatHelper.BuildBreadcrumbs(FormatHelper.HomeLocation);
        Assert.Single(homeCrumbs);
        Assert.Equal("Home", homeCrumbs[0].Name);
        Assert.True(homeCrumbs[0].IsLast);

        var dirCrumbs = FormatHelper.BuildBreadcrumbs(@"C:\Users\Siam\Documents");
        Assert.Equal(5, dirCrumbs.Count);
        Assert.Equal("Home", dirCrumbs[0].Name);
        Assert.Equal("(C:)", dirCrumbs[1].Name);
        Assert.Equal(@"C:\", dirCrumbs[1].FullPath);
        Assert.Equal("Users", dirCrumbs[2].Name);
        Assert.Equal(@"C:\Users", dirCrumbs[2].FullPath);
        Assert.Equal("Documents", dirCrumbs[4].Name);
        Assert.True(dirCrumbs[4].IsLast);
        Assert.False(dirCrumbs[2].IsLast);
    }

    [Fact]
    public void ExpandNavigationPath_HandlesDriveRootAndTilde()
    {
        Assert.Equal(@"C:\", FormatHelper.ExpandNavigationPath("C:"));
        Assert.Equal(@"C:\Temp", FormatHelper.ExpandNavigationPath("  \"C:/Temp/\"  "));
        Assert.Equal(FormatHelper.HomeLocation, FormatHelper.ExpandNavigationPath("home"));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(profile, FormatHelper.ExpandNavigationPath("~"));
        Assert.Equal(Path.Combine(profile, "Desktop"), FormatHelper.ExpandNavigationPath(@"~\Desktop"));
    }

    [Fact]
    public void GetParentPath_ReturnsHomeAtDriveRoot()
    {
        Assert.Equal(FormatHelper.HomeLocation, FormatHelper.GetParentPath(@"C:\"));
        Assert.Equal(@"C:\", FormatHelper.GetParentPath(@"C:\Windows"));
        Assert.Equal(FormatHelper.HomeLocation, FormatHelper.GetParentPath(FormatHelper.HomeLocation));
    }

    [Fact]
    public void GenerateUniqueName_AvoidsCollisions()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "notes.txt"), "a");
        File.WriteAllText(Path.Combine(_tempRoot, "notes (2).txt"), "b");

        var next = FormatHelper.GenerateUniqueName(_tempRoot, "notes", ".txt");
        Assert.Equal("notes (3).txt", next);
    }

    [Fact]
    public void IsSubPathOrSelf_DetectsSelfAndNestedFolders()
    {
        var parent = Path.Combine(_tempRoot, "Projects");
        var child = Path.Combine(parent, "Eigen", "Sub");
        var sibling = Path.Combine(_tempRoot, "ProjectsBackup");

        Assert.True(FormatHelper.IsSubPathOrSelf(parent, parent));
        Assert.True(FormatHelper.IsSubPathOrSelf(child, parent));
        Assert.False(FormatHelper.IsSubPathOrSelf(sibling, parent));
    }

    [Fact]
    public async Task FileSystemService_CreateRenameCopyMoveAndSearch_WorksEndToEnd()
    {
        var svc = new FileSystemService();

        var folderA = svc.CreateFolder(_tempRoot, "Alpha");
        var folderB = svc.CreateFolder(_tempRoot, "Beta");
        var file1 = svc.CreateFile(folderA, "readme", ".md");
        await File.WriteAllTextAsync(file1, "# Eigen\nLine 2");

        // Rename including case-only rename
        var renamed = svc.RenameItem(file1, "Guide.md");
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(file1));
        var caseRenamed = svc.RenameItem(renamed, "guide.md");
        Assert.Equal("guide.md", Path.GetFileName(caseRenamed));

        // Copy with KeepBoth conflict handling
        var firstCopy = await svc.CopyOrMoveItemsAsync([caseRenamed], folderB, isMove: false, ConflictAction.KeepBoth, null, CancellationToken.None);
        Assert.Single(firstCopy);
        var secondCopy = await svc.CopyOrMoveItemsAsync([caseRenamed], folderB, isMove: false, ConflictAction.KeepBoth, null, CancellationToken.None);
        Assert.Single(secondCopy);
        Assert.Equal("guide (2).md", Path.GetFileName(secondCopy[0].DestinationPath));

        // Prevent copying folder into itself
        var nested = svc.CreateFolder(folderA, "Inner");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CopyOrMoveItemsAsync([folderA], nested, isMove: false, ConflictAction.KeepBoth, null, CancellationToken.None));

        // Recursive search
        var found = new List<FileSystemItem>();
        var progress = new SyncProgress<IReadOnlyList<FileSystemItem>>(batch => found.AddRange(batch));
        await svc.SearchRecursiveAsync(_tempRoot, "guide", showHidden: false, progress, CancellationToken.None);
        Assert.Equal(3, found.Count);

        // Preview text file
        var items = await svc.GetDirectoryItemsAsync(folderA, showHidden: false, CancellationToken.None);
        var mdItem = items.Single(i => i.Name.Equals("guide.md", StringComparison.OrdinalIgnoreCase));
        var preview = await svc.BuildPreviewAsync(mdItem, CancellationToken.None);
        Assert.Equal(PreviewKind.Text, preview.Kind);
        Assert.Contains("Eigen", preview.TextContent);
    }

    [Fact]
    public async Task FileSystemService_HiddenFilesToggle_RespectsHiddenAttribute()
    {
        var svc = new FileSystemService();
        var visible = Path.Combine(_tempRoot, "visible.txt");
        var hidden = Path.Combine(_tempRoot, "secret.txt");
        await File.WriteAllTextAsync(visible, "1");
        await File.WriteAllTextAsync(hidden, "2");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var withoutHidden = await svc.GetDirectoryItemsAsync(_tempRoot, showHidden: false, CancellationToken.None);
        Assert.Single(withoutHidden);
        Assert.Equal("visible.txt", withoutHidden[0].Name);

        var withHidden = await svc.GetDirectoryItemsAsync(_tempRoot, showHidden: true, CancellationToken.None);
        Assert.Equal(2, withHidden.Count);
    }

    [Fact]
    public void SettingsService_SavesLoadsAndFallsBackOnCorruption()
    {
        var path = Path.Combine(_tempRoot, "settings.json");
        var svc = new SettingsService(path);

        var settings = new AppSettings
        {
            ThemeMode = EigenThemeMode.Light,
            DefaultViewMode = ExplorerViewMode.Tiles,
            ShowHiddenFiles = true,
            DetailsVerticalGap = 7,
            TilesVerticalGap = 15,
            CompactVerticalGap = 5,
            SidebarVerticalGap = 9,
            HomeVerticalGap = 20,
            FontFamilyOverride = "Consolas",
            FontWeightValue = 500,
            LetterSpacing = 1.25,
            PinnedFolders = [_tempRoot]
        };
        svc.Save(settings);

        var reloaded = svc.Load();
        Assert.Equal(EigenThemeMode.Light, reloaded.ThemeMode);
        Assert.Equal(ExplorerViewMode.Tiles, reloaded.DefaultViewMode);
        Assert.True(reloaded.ShowHiddenFiles);
        Assert.Equal(7, reloaded.DetailsVerticalGap);
        Assert.Equal(15, reloaded.TilesVerticalGap);
        Assert.Equal(5, reloaded.CompactVerticalGap);
        Assert.Equal(9, reloaded.SidebarVerticalGap);
        Assert.Equal(20, reloaded.HomeVerticalGap);
        Assert.Equal("Consolas", reloaded.FontFamilyOverride);
        Assert.Equal(500, reloaded.FontWeightValue);
        Assert.Equal(1.25, reloaded.LetterSpacing);
        Assert.Contains(_tempRoot, reloaded.PinnedFolders);

        // Legacy single ItemVerticalGap JSON migrates cleanly into separate case gaps
        File.WriteAllText(path, "{ \"ItemVerticalGap\": 6.0 }");
        var migrated = svc.Load();
        Assert.Equal(6.0, migrated.DetailsVerticalGap);
        Assert.Equal(10.0, migrated.TilesVerticalGap);
        Assert.Equal(6.0, migrated.CompactVerticalGap);
        Assert.Equal(6.0, migrated.SidebarVerticalGap);
        Assert.Equal(24.0, migrated.HomeVerticalGap);
        Assert.Null(migrated.ItemVerticalGap);

        File.WriteAllText(path, "{ broken json");
        var fallback = svc.Load();
        Assert.Equal(EigenThemeMode.Light, fallback.ThemeMode);
        Assert.Equal("default", fallback.SkinId);
        Assert.Equal(IconPackStyle.NeonWire, fallback.IconPackOverride);
        Assert.False(fallback.ShowPreviewPane);
        Assert.True(fallback.IsMaximized);
        Assert.Equal(3.0, fallback.DetailsVerticalGap);
        Assert.Equal(13.0, fallback.TilesVerticalGap);
        Assert.Equal(3.0, fallback.CompactVerticalGap);
        Assert.Equal(1.0, fallback.SidebarVerticalGap);
        Assert.Equal(24.0, fallback.HomeVerticalGap);
        Assert.Equal(15.0, fallback.ItemIconSize);
        Assert.Equal(8.0, fallback.IconTextHorizontalGap);
        Assert.Equal(6.0, fallback.IconTextVerticalGap);
        Assert.Equal(14.0, fallback.ItemFontSize);
    }

    [Fact]
    public async Task ExplorerPaneAndTabViewModel_NavigateFilterSortAndSplitWork()
    {
        var svc = new FileSystemService();
        var sub = svc.CreateFolder(_tempRoot, "SubDir");
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "item10.txt"), "1234567890");
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "item2.txt"), "12");

        var tab = new ExplorerTabViewModel(
            _ => new ExplorerPaneViewModel(svc, () => [], () => [], ExplorerViewMode.Details, false),
            _tempRoot);

        await tab.PrimaryPane.NavigateToAsync(_tempRoot, recordHistory: false);
        Assert.Equal(3, tab.PrimaryPane.Items.Count);
        // Folder first, then natural sort: SubDir, item2.txt, item10.txt
        Assert.Equal(["SubDir", "item2.txt", "item10.txt"], tab.PrimaryPane.Items.Select(i => i.Name).ToArray());

        // Filter
        tab.PrimaryPane.SearchQuery = "10";
        Assert.Single(tab.PrimaryPane.Items);
        Assert.Equal("item10.txt", tab.PrimaryPane.Items[0].Name);
        tab.PrimaryPane.ClearSearch();
        Assert.Equal(3, tab.PrimaryPane.Items.Count);

        // Navigate into SubDir & test Back/Forward/Up
        await tab.PrimaryPane.NavigateToAsync(sub);
        Assert.True(tab.PrimaryPane.CanGoBack);
        tab.PrimaryPane.GoBack();
        Assert.Equal(_tempRoot, tab.PrimaryPane.CurrentPath);
        Assert.True(tab.PrimaryPane.CanGoForward);
        tab.PrimaryPane.GoForward();
        Assert.Equal(sub, tab.PrimaryPane.CurrentPath);

        // Split view & swap
        tab.ToggleSplitView();
        Assert.True(tab.IsSplitView);
        Assert.NotNull(tab.SecondaryPane);
        await tab.SecondaryPane!.NavigateToAsync(_tempRoot);
        tab.SwapPanes();
        await Task.Delay(50);
        Assert.Equal(_tempRoot, tab.PrimaryPane.CurrentPath);
        Assert.Equal(sub, tab.SecondaryPane.CurrentPath);

        tab.Dispose();
    }

    [Fact]
    public void Converters_HandleCountsBooleansAndEnums()
    {
        var nullConv = new NullToVisibilityConverter();
        var invNullConv = new NullToVisibilityConverter { Invert = true };
        Assert.Equal(Visibility.Collapsed, nullConv.Convert(0, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, nullConv.Convert(3, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, invNullConv.Convert(0, typeof(Visibility), null!, CultureInfo.InvariantCulture));

        var boolConv = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, boolConv.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, boolConv.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture));

        var enumConv = new EnumEqualsConverter();
        Assert.Equal(true, enumConv.Convert(ExplorerViewMode.Tiles, typeof(bool), ExplorerViewMode.Tiles, CultureInfo.InvariantCulture));
        Assert.Equal(false, enumConv.Convert(ExplorerViewMode.Details, typeof(bool), ExplorerViewMode.Tiles, CultureInfo.InvariantCulture));

        var enumVisConv = new EnumToVisibilityConverter();
        Assert.Equal(Visibility.Visible, enumVisConv.Convert(ExplorerViewMode.Details, typeof(Visibility), ExplorerViewMode.Details, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, enumVisConv.Convert(ExplorerViewMode.Details, typeof(Visibility), ExplorerViewMode.Tiles, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, enumVisConv.Convert(ExplorerViewMode.Details, typeof(Visibility), ExplorerViewMode.Compact, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, enumVisConv.Convert(PreviewKind.Text, typeof(Visibility), PreviewKind.Text, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, enumVisConv.Convert(PreviewKind.Text, typeof(Visibility), PreviewKind.Image, CultureInfo.InvariantCulture));

        var (smallIcon, dirType) = NativeShellService.GetIconAndType(_tempRoot, isDirectory: true, largeIcon: false);
        var (largeIcon, _) = NativeShellService.GetIconAndType(_tempRoot, isDirectory: true, largeIcon: true);
        Assert.NotNull(smallIcon);
        Assert.NotNull(largeIcon);
        Assert.False(string.IsNullOrWhiteSpace(dirType));
        Assert.True(largeIcon.Width >= 32);
    }

    [Fact]
    public void ExplorerPaneView_OnlyShowsActiveViewModeControl()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenDark.xaml")
                });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenStyles.xaml")
                });

                var svc = new FileSystemService();
                var paneVm = new ExplorerPaneViewModel(svc, () => [], () => [], ExplorerViewMode.Details, false);
                paneVm.NavigateToAsync(_tempRoot, recordHistory: false).GetAwaiter().GetResult();

                var view = new Eigen.Controls.ExplorerPaneView { DataContext = paneVm };
                view.Measure(new Size(900, 600));
                view.Arrange(new Rect(0, 0, 900, 600));
                view.UpdateLayout();

                var details = (System.Windows.Controls.ListView)view.FindName("DetailsListView");
                var tiles = (System.Windows.Controls.ListBox)view.FindName("TilesListBox");
                var compact = (System.Windows.Controls.ListBox)view.FindName("CompactListBox");

                Assert.Equal(Visibility.Visible, details.Visibility);
                Assert.Equal(Visibility.Collapsed, tiles.Visibility);
                Assert.Equal(Visibility.Collapsed, compact.Visibility);

                paneVm.ViewMode = ExplorerViewMode.Tiles;
                view.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, details.Visibility);
                Assert.Equal(Visibility.Visible, tiles.Visibility);
                Assert.Equal(Visibility.Collapsed, compact.Visibility);

                // Background context menu contains View / Sort by / Refresh / Paste / Undo / New / Properties
                var bgMenu = Assert.IsType<System.Windows.Controls.ContextMenu>(view.FindResource("PaneBackgroundContextMenu"));
                var headers = bgMenu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Header?.ToString()).ToList();
                Assert.Contains("View", headers);
                Assert.Contains("Sort by", headers);
                Assert.Contains("Refresh", headers);
                Assert.Contains("Paste", headers);
                Assert.Contains("New", headers);
                Assert.Contains("Properties", headers);

                paneVm.ViewMode = ExplorerViewMode.Compact;
                view.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, details.Visibility);
                Assert.Equal(Visibility.Collapsed, tiles.Visibility);
                Assert.Equal(Visibility.Visible, compact.Visibility);

                // Verify address edit mode shows full folder path in AddressTextBox
                var addressBox = (System.Windows.Controls.TextBox)view.FindName("AddressTextBox");
                Assert.Equal(Visibility.Collapsed, addressBox.Visibility);
                paneVm.BeginAddressEdit();
                view.UpdateLayout();
                Assert.True(paneVm.IsAddressEditing);
                Assert.Equal(_tempRoot, paneVm.AddressBarText);
                Assert.Equal(Visibility.Visible, addressBox.Visibility);
                Assert.Equal(_tempRoot, addressBox.Text);
                paneVm.CancelAddressEdit();
                view.UpdateLayout();
                Assert.False(paneVm.IsAddressEditing);
                Assert.Equal(Visibility.Collapsed, addressBox.Visibility);

                paneVm.Dispose();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("STA WPF test failed", threadEx);
    }

    [Fact]
    public async Task ExplorerPaneViewModel_TypeAheadJumpSearch_NarrowsCyclesAndResets()
    {
        var svc = new FileSystemService();
        svc.CreateFolder(_tempRoot, "alpha");
        svc.CreateFolder(_tempRoot, "iac_notes");
        svc.CreateFolder(_tempRoot, "icon");
        svc.CreateFolder(_tempRoot, "images");
        svc.CreateFolder(_tempRoot, "notes");

        var pane = new ExplorerPaneViewModel(svc, () => [], () => [], ExplorerViewMode.Details, false);
        await pane.NavigateToAsync(_tempRoot, recordHistory: false);

        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Incremental prefix narrowing: 'i' -> 'iac_notes', then 'c' -> 'icon', then 'o' -> 'icon'
        Assert.Equal("iac_notes", pane.HandleTypeAheadChar('i', t0)?.Name);
        Assert.Equal("i", pane.TypeAheadQuery);
        Assert.Equal("icon", pane.HandleTypeAheadChar('c', t0.AddMilliseconds(150))?.Name);
        Assert.Equal("ic", pane.TypeAheadQuery);
        Assert.Equal("icon", pane.HandleTypeAheadChar('o', t0.AddMilliseconds(300))?.Name);
        Assert.Equal("ico", pane.TypeAheadQuery);

        // Timeout reset after > 1100 ms starts a fresh jump
        Assert.Equal("notes", pane.HandleTypeAheadChar('n', t0.AddMilliseconds(1600))?.Name);
        Assert.Equal("n", pane.TypeAheadQuery);

        // Same-letter cycling: 'i' -> 'iac_notes' -> 'icon' -> 'images' -> 'iac_notes'
        var t1 = t0.AddSeconds(5);
        Assert.Equal("iac_notes", pane.HandleTypeAheadChar('i', t1)?.Name);
        Assert.Equal("icon", pane.HandleTypeAheadChar('i', t1.AddMilliseconds(150))?.Name);
        Assert.Equal("images", pane.HandleTypeAheadChar('i', t1.AddMilliseconds(300))?.Name);
        Assert.Equal("iac_notes", pane.HandleTypeAheadChar('i', t1.AddMilliseconds(450))?.Name);

        // Clear resets query
        pane.ClearTypeAhead();
        Assert.False(pane.HasTypeAheadQuery);
        Assert.Equal(string.Empty, pane.TypeAheadQuery);

        pane.Dispose();
    }

    [Fact]
    public void ThemeCatalogService_Has100UniqueReadableSkinsWithAesthetics()
    {
        var skins = ThemeCatalogService.Skins;
        Assert.Equal(105, skins.Count);
        Assert.Equal("default", skins[0].Id);
        Assert.Equal("Default", skins[0].Name);
        Assert.Equal(105, skins.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(105, skins.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var skin in skins)
        {
            var dict = ThemeCatalogService.CreateResourceDictionary(skin);
            Assert.True(dict.Count >= 30, $"{skin.Name} missing aesthetic resources");
            Assert.True(dict.Contains("AppFont"));
            Assert.True(dict.Contains("ControlCornerRadius"));
            Assert.True(dict.Contains("CardCornerRadius"));
            Assert.True(dict.Contains("HeaderSurfaceBrush"));

            var contentContrast = ThemeCatalogService.ContrastRatio(skin.TextPrimary, skin.BgContent);
            var sidebarContrast = ThemeCatalogService.ContrastRatio(skin.TextPrimary, skin.BgSidebar);
            var selectionContrast = ThemeCatalogService.ContrastRatio(skin.TextPrimary, skin.BgSelection);
            var onAccentContrast = ThemeCatalogService.ContrastRatio(skin.TextOnAccent, skin.Accent);

            Assert.True(contentContrast >= 4.5, $"{skin.Name} content contrast too low: {contentContrast:0.00}");
            Assert.True(sidebarContrast >= 4.5, $"{skin.Name} sidebar contrast too low: {sidebarContrast:0.00}");
            Assert.True(selectionContrast >= 4.0, $"{skin.Name} selection contrast too low: {selectionContrast:0.00}");
            Assert.True(onAccentContrast >= 3.0, $"{skin.Name} on-accent contrast too low: {onAccentContrast:0.00}");
        }
    }

    [Fact]
    public void VectorIconPackService_GeneratesFrozenIconsForAllPacksAndCategories()
    {
        var packs = new[]
        {
            IconPackStyle.NeonWire,
            IconPackStyle.GlassFrost,
            IconPackStyle.RetroPixel,
            IconPackStyle.DuotoneMinimal,
            IconPackStyle.TactileBrutal
        };
        var skin = ThemeCatalogService.GetById("tron-grid");

        foreach (var pack in packs)
        {
            var folderIcon = VectorIconPackService.GetThemedIcon(pack, _tempRoot, isDirectory: true, isDrive: false, skin);
            var driveIcon = VectorIconPackService.GetThemedIcon(pack, @"C:\", isDirectory: true, isDrive: true, skin);
            var codeIcon = VectorIconPackService.GetThemedIcon(pack, "Program.cs", isDirectory: false, isDrive: false, skin);
            var imageIcon = VectorIconPackService.GetThemedIcon(pack, "banner.png", isDirectory: false, isDrive: false, skin);

            Assert.NotNull(folderIcon);
            Assert.NotNull(driveIcon);
            Assert.NotNull(codeIcon);
            Assert.NotNull(imageIcon);
            Assert.True(folderIcon.IsFrozen);
            Assert.NotSame(folderIcon, codeIcon);
        }
    }

    [Fact]
    public async Task MainViewModel_CustomizationBoundsAndDrawer_StaySafeAndConsistent()
    {
        var svc = new FileSystemService();
        svc.CreateFolder(_tempRoot, "SampleFolder");
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "code.cs"), "// test");

        var vm = new MainViewModel(_tempRoot, Path.Combine(_tempRoot, "vm-settings.json"));
        await Task.Delay(120);

        // Fresh default theme is Default (Catppuccin Latte) at start of list and default icon pack is NeonWire
        Assert.Equal("default", vm.ActiveSkin.Id);
        Assert.Equal("Default", vm.ActiveSkin.Name);
        Assert.Equal("default", vm.FilteredSkins[0].Id);
        Assert.Equal(EigenThemeMode.Light, vm.ThemeMode);
        Assert.False(vm.IsDarkTheme);
        Assert.False(vm.IsPreviewPaneOpen);
        Assert.Equal(IconPackStyle.NeonWire, vm.IconPackOverride);
        Assert.Equal(IconPackStyle.NeonWire, vm.EffectiveIconPack);
        Assert.Equal(3.0, vm.DetailsVerticalGap);
        Assert.Equal(13.0, vm.TilesVerticalGap);
        Assert.Equal(3.0, vm.CompactVerticalGap);
        Assert.Equal(1.0, vm.SidebarVerticalGap);
        Assert.Equal(24.0, vm.HomeVerticalGap);
        Assert.Equal(15.0, vm.ItemIconSize);
        Assert.Equal(4.0, vm.IconLeftGap);
        Assert.Equal(14.0, vm.ItemFontSize);
        Assert.Equal("Auto (Skin Default)", vm.SelectedFontOption.ToString());

        // Clamp lower bounds for all separate vertical gaps & sizing
        vm.DetailsVerticalGap = -10;
        vm.TilesVerticalGap = -10;
        vm.CompactVerticalGap = -10;
        vm.SidebarVerticalGap = -10;
        vm.HomeVerticalGap = -10;
        vm.ItemIconSize = 4;
        vm.IconLeftGap = -5;
        vm.ItemFontSize = 3;
        Assert.Equal(MainViewModel.MinDetailsVerticalGap, vm.DetailsVerticalGap);
        Assert.Equal(MainViewModel.MinTilesVerticalGap, vm.TilesVerticalGap);
        Assert.Equal(MainViewModel.MinCompactVerticalGap, vm.CompactVerticalGap);
        Assert.Equal(MainViewModel.MinSidebarVerticalGap, vm.SidebarVerticalGap);
        Assert.Equal(MainViewModel.MinHomeVerticalGap, vm.HomeVerticalGap);
        Assert.Equal(MainViewModel.MinIconSize, vm.ItemIconSize);
        Assert.Equal(MainViewModel.MinIconLeftGap, vm.IconLeftGap);
        Assert.Equal(MainViewModel.MinFontSize, vm.ItemFontSize);
        Assert.True(vm.DetailsRowHeight >= vm.ItemIconSize + 10);
        Assert.True(vm.TileCardWidth > vm.TileIconSize);
        Assert.True(vm.TileCardHeight > vm.TileIconSize);

        // Clamp upper bounds
        vm.DetailsVerticalGap = 99;
        vm.TilesVerticalGap = 99;
        vm.CompactVerticalGap = 99;
        vm.SidebarVerticalGap = 99;
        vm.HomeVerticalGap = 99;
        vm.ItemIconSize = 200;
        vm.IconLeftGap = 99;
        vm.ItemFontSize = 60;
        Assert.Equal(MainViewModel.MaxDetailsVerticalGap, vm.DetailsVerticalGap);
        Assert.Equal(MainViewModel.MaxTilesVerticalGap, vm.TilesVerticalGap);
        Assert.Equal(MainViewModel.MaxCompactVerticalGap, vm.CompactVerticalGap);
        Assert.Equal(MainViewModel.MaxSidebarVerticalGap, vm.SidebarVerticalGap);
        Assert.Equal(MainViewModel.MaxHomeVerticalGap, vm.HomeVerticalGap);
        Assert.Equal(MainViewModel.MaxIconSize, vm.ItemIconSize);
        Assert.Equal(MainViewModel.MaxIconLeftGap, vm.IconLeftGap);
        Assert.Equal(MainViewModel.MaxIconLeftGap, vm.IconLeftGapMargin.Left);
        Assert.Equal(MainViewModel.MaxFontSize, vm.ItemFontSize);
        Assert.True(vm.DetailsRowHeight >= vm.ItemIconSize + 10 + vm.DetailsVerticalGap);
        Assert.Equal(vm.TilesVerticalGap / 2.0, vm.TileItemMargin.Top);
        Assert.Equal(vm.CompactVerticalGap / 2.0, vm.CompactItemMargin.Top);
        Assert.Equal(vm.SidebarVerticalGap / 2.0, vm.SidebarTreeItemMargin.Top);
        Assert.Equal(vm.HomeVerticalGap, vm.HomeCardMargin.Bottom);
        Assert.True(vm.HomeHeroMargin.Bottom > vm.HomeCardMargin.Bottom);
        Assert.True(vm.TileCardHeight >= vm.TileIconSize + vm.ItemFontSize * 3.8);
        Assert.True(vm.CompactCardHeight >= vm.ItemIconSize + 8);

        // Changing one vertical gap leaves the other contexts unaffected
        vm.ResetLayoutCustomizationCommand.Execute(null);
        vm.DetailsVerticalGap = 11;
        Assert.Equal(11, vm.DetailsVerticalGap);
        Assert.Equal(MainViewModel.DefaultTilesVerticalGap, vm.TilesVerticalGap);
        Assert.Equal(MainViewModel.DefaultCompactVerticalGap, vm.CompactVerticalGap);
        Assert.Equal(MainViewModel.DefaultSidebarVerticalGap, vm.SidebarVerticalGap);
        Assert.Equal(MainViewModel.DefaultHomeVerticalGap, vm.HomeVerticalGap);

        // Reset defaults
        vm.ResetLayoutCustomizationCommand.Execute(null);
        Assert.Equal(MainViewModel.DefaultDetailsVerticalGap, vm.DetailsVerticalGap);
        Assert.Equal(MainViewModel.DefaultTilesVerticalGap, vm.TilesVerticalGap);
        Assert.Equal(MainViewModel.DefaultCompactVerticalGap, vm.CompactVerticalGap);
        Assert.Equal(MainViewModel.DefaultSidebarVerticalGap, vm.SidebarVerticalGap);
        Assert.Equal(MainViewModel.DefaultHomeVerticalGap, vm.HomeVerticalGap);
        Assert.Equal(MainViewModel.DefaultIconSize, vm.ItemIconSize);
        Assert.Equal(MainViewModel.DefaultIconLeftGap, vm.IconLeftGap);
        Assert.Equal(MainViewModel.DefaultFontSize, vm.ItemFontSize);

        // Skin category filter + aesthetic icon pack switching
        Assert.Equal(105, vm.FilteredSkins.Count);
        vm.SelectIconPackCommand.Execute(IconPackStyle.Auto);
        vm.SelectSkinCategoryCommand.Execute("Glass");
        Assert.Equal(10, vm.FilteredSkins.Count);
        vm.SelectSkinCommand.Execute(vm.FilteredSkins[0]);
        Assert.Equal(vm.FilteredSkins[0].Id, vm.ActiveSkin.Id);
        Assert.Equal(IconPackStyle.GlassFrost, vm.EffectiveIconPack);

        vm.SelectSkinCategoryCommand.Execute("Cyber");
        vm.SelectSkinCommand.Execute(vm.FilteredSkins[0]);
        Assert.Equal(IconPackStyle.NeonWire, vm.EffectiveIconPack);

        vm.SelectIconPackCommand.Execute(IconPackStyle.RetroPixel);
        Assert.Equal(IconPackStyle.RetroPixel, vm.EffectiveIconPack);

        // Sidebar selection highlighter syncs cleanly between Quick Access and This PC drives
        var homeNode = vm.QuickAccessNodes.First(n => n.IsSpecialHome);
        var driveNode = vm.DriveTreeNodes.First();
        vm.SyncSidebarSelection(driveNode.Path);
        Assert.True(driveNode.IsSelected);
        Assert.False(homeNode.IsSelected);
        vm.SyncSidebarSelection(homeNode.Path);
        Assert.True(homeNode.IsSelected);
        Assert.False(driveNode.IsSelected);

        // Mutually exclusive drawer behavior
        vm.IsPreviewPaneOpen = true;
        vm.IsCustomizePaneOpen = true;
        Assert.True(vm.IsCustomizePaneOpen);
        Assert.False(vm.IsPreviewPaneOpen);

        // Font Weight (100..900 snapped to 100) & Letter Spacing (-2.0..8.0 snapped to 0.25)
        vm.FontWeightValue = 50;
        Assert.Equal(MainViewModel.MinFontWeightValue, vm.FontWeightValue);
        Assert.Equal(FontWeights.Thin, vm.UiFontWeight);
        vm.FontWeightValue = 950;
        Assert.Equal(MainViewModel.MaxFontWeightValue, vm.FontWeightValue);
        Assert.Equal(FontWeights.Black, vm.UiFontWeight);
        vm.FontWeightValue = 540;
        Assert.Equal(500, vm.FontWeightValue);

        vm.LetterSpacing = -5.0;
        Assert.Equal(MainViewModel.MinLetterSpacing, vm.LetterSpacing);
        vm.LetterSpacing = 12.0;
        Assert.Equal(MainViewModel.MaxLetterSpacing, vm.LetterSpacing);
        vm.LetterSpacing = 1.18;
        Assert.Equal(1.25, vm.LetterSpacing);

        // Font Family override + layout width protection
        vm.FontFamilyOverride = "Segoe UI";
        var baseDateCol = vm.DetailsDateColWidth;
        vm.FontFamilyOverride = "Consolas";
        Assert.Equal("Consolas", vm.SelectedFontOption.Id);
        Assert.True(vm.DetailsDateColWidth > baseDateCol);
        vm.FontFamilyOverride = "NonExistentFont";
        Assert.Equal("Auto", vm.SelectedFontOption.Id);

        // Enumerates installed system fonts
        Assert.True(vm.InstalledFontCount > 20, $"Expected >20 installed fonts, got {vm.InstalledFontCount}");
        Assert.Contains(vm.AvailableFonts, f => f.DisplayName.Equals("Arial", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.AvailableFonts, f => f.DisplayName.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.AvailableFonts, f => f.DisplayName.Equals("Wingdings", StringComparison.OrdinalIgnoreCase));

        // Name -> Date modified column distance uses shortest item name width + 300px
        var shortFolderItems = new[]
        {
            new FileSystemItem { Name = "a", FullPath = Path.Combine(_tempRoot, "a"), IsDirectory = true },
            new FileSystemItem { Name = "Very Long Folder Name That Should Not Dominate", FullPath = Path.Combine(_tempRoot, "long"), IsDirectory = true }
        };
        var longFolderItems = new[]
        {
            new FileSystemItem { Name = "long-document-filename-one.txt", FullPath = Path.Combine(_tempRoot, "l1.txt") },
            new FileSystemItem { Name = "another-long-filename-two.txt", FullPath = Path.Combine(_tempRoot, "l2.txt") }
        };
        var shortNameColWidth = vm.CalculateDetailsNameColumnWidth(shortFolderItems);
        var longNameColWidth = vm.CalculateDetailsNameColumnWidth(longFolderItems);
        var expectedShortWidth = Math.Round(vm.IconLeftGap
            + vm.DetailsIconColumnWidth.Value
            + FontCatalogService.MeasureTextWidth("a", vm.EffectiveFontFamilyName, vm.ItemFontSize, vm.UiFontWeight, vm.LetterSpacing)
            + MainViewModel.DetailsNamePaddingGap);
        Assert.Equal(expectedShortWidth, shortNameColWidth);
        Assert.True(longNameColWidth > shortNameColWidth);

        vm.ResetLayoutCustomizationCommand.Execute(null);
        Assert.Equal(MainViewModel.DefaultFontFamilyOverride, vm.FontFamilyOverride);
        Assert.Equal(MainViewModel.DefaultFontWeightValue, vm.FontWeightValue);
        Assert.Equal(MainViewModel.DefaultLetterSpacing, vm.LetterSpacing);

        // Icon-to-text horizontal and vertical gaps clamp safely and scale column/card metrics
        vm.IconTextHorizontalGap = -5;
        vm.IconTextVerticalGap = -5;
        Assert.Equal(MainViewModel.MinIconTextHorizontalGap, vm.IconTextHorizontalGap);
        Assert.Equal(MainViewModel.MinIconTextVerticalGap, vm.IconTextVerticalGap);
        Assert.Equal(vm.ItemIconSize + MainViewModel.MinIconTextHorizontalGap, vm.DetailsIconColumnWidth.Value);
        Assert.Equal(0.0, vm.TileIconMargin.Bottom);

        vm.IconTextHorizontalGap = 99;
        vm.IconTextVerticalGap = 99;
        Assert.Equal(MainViewModel.MaxIconTextHorizontalGap, vm.IconTextHorizontalGap);
        Assert.Equal(MainViewModel.MaxIconTextVerticalGap, vm.IconTextVerticalGap);
        Assert.Equal(vm.ItemIconSize + MainViewModel.MaxIconTextHorizontalGap, vm.CompactIconColumnWidth.Value);
        Assert.Equal(16.0 + MainViewModel.MaxIconTextHorizontalGap, vm.SidebarIconColumnWidth.Value);
        Assert.Equal(MainViewModel.MaxIconTextVerticalGap, vm.TileIconMargin.Bottom);
        Assert.True(vm.TileCardHeight >= vm.TileIconSize + vm.IconTextVerticalGap + vm.ItemFontSize * 3.8);

        vm.ResetLayoutCustomizationCommand.Execute(null);
        Assert.Equal(MainViewModel.DefaultIconTextHorizontalGap, vm.IconTextHorizontalGap);
        Assert.Equal(MainViewModel.DefaultIconTextVerticalGap, vm.IconTextVerticalGap);

        vm.Dispose();
    }

    [Fact]
    public void SpacedTextBlock_MeasuresAndConstrainsAcrossSpacingAndWrap()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var ctrl = new Eigen.Controls.SpacedTextBlock
                {
                    Text = "2026-11-26  03:54",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    LetterSpacing = -0.5,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                ctrl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var tightWidth = ctrl.DesiredSize.Width;

                ctrl.LetterSpacing = 0.0;
                ctrl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var normalWidth = ctrl.DesiredSize.Width;

                ctrl.LetterSpacing = 2.5;
                ctrl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var wideWidth = ctrl.DesiredSize.Width;

                Assert.True(tightWidth > 0 && tightWidth < normalWidth);
                Assert.True(wideWidth > normalWidth);

                // Constrained container width should never overflow
                ctrl.Measure(new Size(90, 40));
                Assert.True(ctrl.DesiredSize.Width <= 90.01);

                // Wrap mode with MaxLines = 2
                ctrl.Text = "Very Long Folder Name That Wraps Cleanly Across Two Lines";
                ctrl.TextWrapping = TextWrapping.Wrap;
                ctrl.MaxLines = 2;
                ctrl.Measure(new Size(140, 100));
                Assert.True(ctrl.DesiredSize.Width <= 140.01);
                Assert.True(ctrl.DesiredSize.Height > 0 && ctrl.DesiredSize.Height <= 100);

                // Ensure CustomizeDrawerView XAML instantiates and measures cleanly
                var app = Application.Current ?? new Application();
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenDark.xaml")
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenStyles.xaml")
                    });
                }
                var vm = new Eigen.ViewModels.MainViewModel(_tempRoot, Path.Combine(_tempRoot, "drawer-settings.json"));
                var drawer = new Eigen.Controls.CustomizeDrawerView { DataContext = vm };
                drawer.Measure(new Size(360, 700));
                drawer.Arrange(new Rect(0, 0, 360, 700));
                drawer.UpdateLayout();
                Assert.True(drawer.DesiredSize.Height > 0);

                var skinsCtrl = (System.Windows.Controls.ItemsControl)drawer.FindName("SkinsItemsControl");
                var card0 = (UIElement)skinsCtrl.ItemContainerGenerator.ContainerFromIndex(0);
                var card1 = (UIElement)skinsCtrl.ItemContainerGenerator.ContainerFromIndex(1);
                var p0 = card0.TranslatePoint(new Point(0, 0), skinsCtrl);
                var p1 = card1.TranslatePoint(new Point(0, 0), skinsCtrl);
                Assert.True(Math.Abs(p0.Y - p1.Y) < 1.0, $"First two skin cards should align horizontally on row 0 (y0={p0.Y}, y1={p1.Y})");
                Assert.True(p1.X > p0.X + 80, $"Second skin card must sit side-by-side to the right (x0={p0.X}, x1={p1.X})");
                vm.Dispose();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("SpacedTextBlock STA test failed", threadEx);
    }

    [Fact]
    public void MainWindow_SidebarScrollViewer_ScrollsOverTreeWithoutNestedScrollViewer()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            Eigen.ViewModels.MainViewModel? vm = null;
            try
            {
                var app = Application.Current ?? new Application();
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenLight.xaml")
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenStyles.xaml")
                    });
                }

                var win = new Eigen.MainWindow(_tempRoot);
                vm = (Eigen.ViewModels.MainViewModel)win.DataContext;

                // Populate many expanded nested nodes so sidebar overflows both vertically and horizontally
                var rootNode = new SidebarNode
                {
                    Title = "DeepDrive",
                    Path = _tempRoot,
                    IsExpanded = true,
                    CanExpand = true,
                    IsLoaded = true
                };
                var current = rootNode;
                for (var level = 0; level < 8; level++)
                {
                    var child = new SidebarNode
                    {
                        Title = $"Level_{level}_Very_Long_Folder_Name_That_Exceeds_Sidebar_Width",
                        Path = _tempRoot,
                        IsExpanded = true,
                        CanExpand = true,
                        IsLoaded = true
                    };
                    current.Children.Add(child);
                    current = child;
                }
                for (var i = 0; i < 20; i++)
                {
                    current.Children.Add(new SidebarNode { Title = $"Subfolder_{i:00}", Path = _tempRoot });
                }
                vm.DriveTreeNodes.Add(rootNode);

                var assetsDir = Directory.CreateDirectory(Path.Combine(_tempRoot, "Assets Diary")).FullName;
                Directory.CreateDirectory(Path.Combine(assetsDir, "SubFolderA"));
                var assetsNode = new SidebarNode
                {
                    Title = "Assets Diary",
                    Path = assetsDir,
                    IsExpanded = false,
                    CanExpand = true
                };
                assetsNode.Children.Add(new SidebarNode { Title = "Loading..." });
                vm.DriveTreeNodes.Add(assetsNode);

                var rootVisual = (UIElement)win.Content;
                rootVisual.Measure(new Size(1100, 260));
                rootVisual.Arrange(new Rect(0, 0, 1100, 260));
                rootVisual.UpdateLayout();

                var sidebarSv = (System.Windows.Controls.ScrollViewer)win.FindName("SidebarScrollViewer");
                var driveTree = (System.Windows.Controls.TreeView)win.FindName("SidebarDriveTree");
                var sidebarCol = (System.Windows.Controls.ColumnDefinition)win.FindName("SidebarColumn");
                var previewCol = (System.Windows.Controls.ColumnDefinition)win.FindName("PreviewColumn");
                var sidebarSplitter = (System.Windows.Controls.GridSplitter)win.FindName("SidebarSplitter");
                var previewSplitter = (System.Windows.Controls.GridSplitter)win.FindName("PreviewSplitter");

                Assert.Equal(System.Windows.Controls.GridResizeDirection.Columns, sidebarSplitter.ResizeDirection);
                Assert.Equal(System.Windows.Controls.GridResizeDirection.Columns, previewSplitter.ResizeDirection);
                Assert.True(sidebarCol.Width.Value >= 170 && sidebarCol.Width.Value <= 460);

                vm.IsSidebarCollapsed = true;
                Assert.Equal(0.0, sidebarCol.Width.Value);
                vm.IsSidebarCollapsed = false;
                Assert.True(sidebarCol.Width.Value >= 170);

                vm.IsPreviewPaneOpen = true;
                Assert.True(previewCol.Width.Value >= 220);
                vm.IsPreviewPaneOpen = false;
                Assert.Equal(0.0, previewCol.Width.Value);

                Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Auto, sidebarSv.VerticalScrollBarVisibility);
                Assert.Equal(System.Windows.Controls.ScrollBarVisibility.Disabled, sidebarSv.HorizontalScrollBarVisibility);
                Assert.True(sidebarSv.ScrollableHeight > 0, $"Expected ScrollableHeight > 0, got {sidebarSv.ScrollableHeight}");
                Assert.Equal(0.0, sidebarSv.ScrollableWidth);

                // Ensure TreeView template does not trap wheel events inside a hidden inner ScrollViewer
                static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
                {
                    var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
                    for (var i = 0; i < count; i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                        if (child is T typed && (predicate is null || predicate(typed)))
                            return typed;
                        var nested = FindDescendant(child, predicate);
                        if (nested is not null)
                            return nested;
                    }
                    return null;
                }
                Assert.Null(FindDescendant<System.Windows.Controls.ScrollViewer>(driveTree));

                // MouseWheel over the TreeView itself must scroll SidebarScrollViewer vertically
                var wheelArgs = new System.Windows.Input.MouseWheelEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount,
                    -240)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = driveTree
                };
                driveTree.RaiseEvent(wheelArgs);
                rootVisual.UpdateLayout();
                Assert.True(wheelArgs.Handled, "MouseWheel over DriveTree must be handled by SidebarScrollViewer");
                Assert.True(sidebarSv.VerticalOffset > 0, $"MouseWheel over DriveTree must scroll SidebarScrollViewer (VerticalOffset={sidebarSv.VerticalOffset}, ScrollableHeight={sidebarSv.ScrollableHeight})");

                // Horizontal scrolling is disabled on the sidebar
                sidebarSv.ScrollToHorizontalOffset(40);
                rootVisual.UpdateLayout();
                Assert.Equal(0.0, sidebarSv.HorizontalOffset);

                // Clicking anywhere in a TreeViewItem header box (Bd) expands/collapses it and loads children
                var assetsContainer = (System.Windows.Controls.TreeViewItem)driveTree.ItemContainerGenerator.ContainerFromItem(assetsNode);
                Assert.NotNull(assetsContainer);
                var bd = FindDescendant<System.Windows.Controls.Border>(assetsContainer, b => b.Name == "Bd");
                Assert.NotNull(bd);

                var clickExpandArgs = new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount,
                    System.Windows.Input.MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                    Source = bd
                };
                bd!.RaiseEvent(clickExpandArgs);
                rootVisual.UpdateLayout();

                Assert.True(assetsNode.IsExpanded, "Clicking header box must expand collapsed sidebar tree node");
                Assert.True(assetsNode.IsLoaded);
                Assert.Contains(assetsNode.Children, c => c.Title == "SubFolderA");

                // Clicking again toggles collapse cleanly
                var clickCollapseArgs = new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount,
                    System.Windows.Input.MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                    Source = bd
                };
                bd.RaiseEvent(clickCollapseArgs);
                Assert.False(assetsNode.IsExpanded, "Clicking expanded header box must collapse it");
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                vm?.Dispose();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("Sidebar scroll STA test failed", threadEx);
    }

    [Fact]
    public void ExplorerPaneView_SizeColumnFillsRemainingWidth()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            Eigen.ViewModels.MainViewModel? vm = null;
            try
            {
                var app = Application.Current ?? new Application();
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenLight.xaml")
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenStyles.xaml")
                    });
                }

                System.Threading.SynchronizationContext.SetSynchronizationContext(
                    new System.Windows.Threading.DispatcherSynchronizationContext(
                        System.Windows.Threading.Dispatcher.CurrentDispatcher));

                var win = new Eigen.MainWindow(_tempRoot);
                vm = (Eigen.ViewModels.MainViewModel)win.DataContext;
                vm.IsPreviewPaneOpen = false;
                vm.IsCustomizePaneOpen = false;
                var pane = vm.ActivePane!;
                pane.ViewMode = ExplorerViewMode.Details;

                var frame = new System.Windows.Threading.DispatcherFrame();
                var navTask = pane.NavigateToAsync(_tempRoot, recordHistory: false);
                navTask.ContinueWith(_ => frame.Continue = false, System.Threading.Tasks.TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                navTask.GetAwaiter().GetResult();

                var rootVisual = (UIElement)win.Content;
                rootVisual.Measure(new Size(1360, 760));
                rootVisual.Arrange(new Rect(0, 0, 1360, 760));
                rootVisual.UpdateLayout();

                var paneView = (Eigen.Controls.ExplorerPaneView)win.FindName("PrimaryPaneView");
                var nameCol = (System.Windows.Controls.GridViewColumn)paneView.FindName("NameCol");
                var dateCol = (System.Windows.Controls.GridViewColumn)paneView.FindName("DateCol");
                var typeCol = (System.Windows.Controls.GridViewColumn)paneView.FindName("TypeCol");
                var sizeCol = (System.Windows.Controls.GridViewColumn)paneView.FindName("SizeCol");

                Assert.True(sizeCol.Width > vm.DetailsSizeColWidth,
                    $"Expected SizeCol to expand beyond minimum {vm.DetailsSizeColWidth}, got {sizeCol.Width}");
                Assert.Equal(0.0, vm.DetailsRowMargin.Left);
                Assert.Equal(0.0, vm.DetailsRowMargin.Right);

                var totalCols = nameCol.Width + dateCol.Width + typeCol.Width + sizeCol.Width;
                var listView = (System.Windows.Controls.ListView)paneView.FindName("DetailsListView");
                Assert.True(totalCols >= listView.ActualWidth - 20 && totalCols <= listView.ActualWidth,
                    $"Total columns {totalCols} should fill viewport {listView.ActualWidth}");

                // Widening the window makes SizeCol expand further
                var widthBefore = sizeCol.Width;
                rootVisual.Measure(new Size(1600, 760));
                rootVisual.Arrange(new Rect(0, 0, 1600, 760));
                rootVisual.UpdateLayout();
                Assert.True(sizeCol.Width > widthBefore, $"Expected SizeCol to grow from {widthBefore}, got {sizeCol.Width}");

                // Narrowing never shrinks SizeCol below minimum readable width
                rootVisual.Measure(new Size(860, 760));
                rootVisual.Arrange(new Rect(0, 0, 860, 760));
                rootVisual.UpdateLayout();
                Assert.True(sizeCol.Width >= vm.DetailsSizeColWidth);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                vm?.Dispose();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("SizeCol fill STA test failed", threadEx);
    }

    [Fact]
    public void NativeShellService_CanBuildShellNewMenu_PopulatesRegisteredTypes()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.True(NativeShellService.CanBuildShellNewMenu(_tempRoot));
                Assert.False(NativeShellService.CanBuildShellNewMenu(Path.Combine(_tempRoot, "missing_folder_xyz")));
                Assert.False(NativeShellService.IsMenuBitmapHandle(IntPtr.Zero));
                Assert.False(NativeShellService.IsMenuBitmapHandle(new IntPtr(11)));
                Assert.False(NativeShellService.IsMenuBitmapHandle(new IntPtr(-1)));
                Assert.True(NativeShellService.IsMenuBitmapHandle(new IntPtr(-1526393622)));
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("ShellNew STA test failed", threadEx);
    }

    [Fact]
    public void NativeShellService_CanBuildShellContextMenu_BindsRealItemsAndRejectsEmpty()
    {
        var folder = Path.Combine(_tempRoot, "CtxFolder");
        var file = Path.Combine(_tempRoot, "ctx.txt");
        Directory.CreateDirectory(folder);
        File.WriteAllText(file, "hello");

        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.True(NativeShellService.CanBuildShellContextMenu([folder]));
                Assert.True(NativeShellService.CanBuildShellContextMenu([folder, file]));
                Assert.False(NativeShellService.CanBuildShellContextMenu([]));
                Assert.False(NativeShellService.CanBuildShellContextMenu([Path.Combine(_tempRoot, "missing_xyz_123.bin")]));
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("Shell context menu STA test failed", threadEx);
    }

    [Fact]
    public void DetailsListView_ScrollViewer_SupportsSubItemPixelScrolling()
    {
        for (var i = 0; i < 60; i++)
            Directory.CreateDirectory(Path.Combine(_tempRoot, $"Dir_{i:D3}"));

        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            Eigen.ViewModels.MainViewModel? vm = null;
            try
            {
                var app = Application.Current ?? new Application();
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenLight.xaml")
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenStyles.xaml")
                    });
                }

                System.Threading.SynchronizationContext.SetSynchronizationContext(
                    new System.Windows.Threading.DispatcherSynchronizationContext(
                        System.Windows.Threading.Dispatcher.CurrentDispatcher));

                var win = new Eigen.MainWindow(_tempRoot);
                vm = (Eigen.ViewModels.MainViewModel)win.DataContext;
                vm.IsPreviewPaneOpen = false;
                vm.IsCustomizePaneOpen = false;
                var pane = vm.ActivePane!;
                pane.ViewMode = ExplorerViewMode.Details;

                var frame = new System.Windows.Threading.DispatcherFrame();
                var navTask = pane.NavigateToAsync(_tempRoot, recordHistory: false);
                navTask.ContinueWith(_ => frame.Continue = false, System.Threading.Tasks.TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                navTask.GetAwaiter().GetResult();

                var rootVisual = (UIElement)win.Content;
                rootVisual.Measure(new Size(1200, 500));
                rootVisual.Arrange(new Rect(0, 0, 1200, 500));
                rootVisual.UpdateLayout();

                var paneView = (Eigen.Controls.ExplorerPaneView)win.FindName("PrimaryPaneView");
                var lv = (System.Windows.Controls.ListView)paneView.FindName("DetailsListView");
                Assert.Equal(System.Windows.Controls.ScrollUnit.Pixel, System.Windows.Controls.VirtualizingPanel.GetScrollUnit(lv));

                var sv = FindVisualDescendant<System.Windows.Controls.ScrollViewer>(lv);
                Assert.NotNull(sv);
                Assert.True(sv!.ScrollableHeight > 200, $"Expected pixel ScrollableHeight > 200, got {sv.ScrollableHeight}");

                sv.ScrollToVerticalOffset(5.5);
                rootVisual.UpdateLayout();
                Assert.InRange(sv.VerticalOffset, 4.5, 6.5);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                vm?.Dispose();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("Pixel scroll STA test failed", threadEx);

        static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is T t) return t;
                var nested = FindVisualDescendant<T>(c);
                if (nested is not null) return nested;
            }
            return null;
        }
    }

    [Fact]
    public void MainViewModel_FolderColumnWidths_PersistPerFolderAcrossRestarts()
    {
        var folderA = Path.Combine(_tempRoot, "FolderA");
        var folderB = Path.Combine(_tempRoot, "FolderB");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);
        var settingsPath = Path.Combine(_tempRoot, "col-settings.json");

        using (var vm1 = new MainViewModel(folderA, settingsPath))
        {
            vm1.SaveFolderColumnWidths(folderA + Path.DirectorySeparatorChar, 412, 165, 145);
            vm1.SaveFolderColumnWidths(folderB, 280, 140, 120);
        }

        using var vm2 = new MainViewModel(folderA, settingsPath);
        Assert.True(vm2.TryGetFolderColumnWidths(folderA, out var loadedA));
        Assert.NotNull(loadedA);
        Assert.Equal(412, loadedA!.Name);
        Assert.Equal(165, loadedA.Date);
        Assert.Equal(145, loadedA.Type);

        Assert.True(vm2.TryGetFolderColumnWidths(folderB.ToLowerInvariant(), out var loadedB));
        Assert.NotNull(loadedB);
        Assert.Equal(280, loadedB!.Name);
        Assert.Equal(140, loadedB.Date);
        Assert.Equal(120, loadedB.Type);

        Assert.False(vm2.TryGetFolderColumnWidths(Path.Combine(_tempRoot, "Other"), out _));
    }

    [Fact]
    public async Task FileSystemService_BuildPreviewAsync_SupportsAllFileTypes()
    {
        var svc = new FileSystemService();

        // 1. Auto-detected UTF-8 text file with custom extension
        var customTextPath = Path.Combine(_tempRoot, "service.customlog");
        await File.WriteAllTextAsync(customTextPath, "line-one=ok\nline-two=ready\n");

        // 2. Real .zip archive
        var zipPath = Path.Combine(_tempRoot, "bundle.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("docs/readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("Archived file contents");
        }

        // 3. Binary MZ/PE-like file + generic non-text bytes
        var binPath = Path.Combine(_tempRoot, "payload.bin");
        var binBytes = new byte[128];
        binBytes[0] = 0x4D; // M
        binBytes[1] = 0x5A; // Z
        binBytes[60] = 64;  // PE header offset
        binBytes[64] = 0x50; // P
        binBytes[65] = 0x45; // E
        binBytes[68] = 0x64; // 0x8664 (x64)
        binBytes[69] = 0x86;
        await File.WriteAllBytesAsync(binPath, binBytes);

        // 4. Empty zero-byte binary file
        var emptyPath = Path.Combine(_tempRoot, "zero.dat");
        await File.WriteAllBytesAsync(emptyPath, []);

        // 5. Real PNG image
        var pngPath = Path.Combine(_tempRoot, "tiny.png");
        var wb = System.Windows.Media.Imaging.BitmapSource.Create(
            12, 8, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, new byte[12 * 8 * 4], 12 * 4);
        using (var fs = File.Create(pngPath))
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(wb));
            encoder.Save(fs);
        }

        var items = await svc.GetDirectoryItemsAsync(_tempRoot, showHidden: false, CancellationToken.None);

        var textPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "service.customlog"), CancellationToken.None);
        Assert.Equal(PreviewKind.Text, textPreview.Kind);
        Assert.Contains("line-two=ready", textPreview.TextContent);

        var zipPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "bundle.zip"), CancellationToken.None);
        Assert.Equal("Archive Contents", zipPreview.PreviewBadge);
        Assert.Contains("docs/readme.txt", zipPreview.TextContent);
        Assert.Contains("1 entries", zipPreview.TextStats);

        var binPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "payload.bin"), CancellationToken.None);
        Assert.Equal(PreviewKind.Generic, binPreview.Kind);
        Assert.Contains("PE Executable (x64)", binPreview.TextContent);
        Assert.DoesNotContain("0000: 4D 5A", binPreview.TextContent);

        var emptyPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "zero.dat"), CancellationToken.None);
        Assert.Equal(PreviewKind.Generic, emptyPreview.Kind);
        Assert.Contains("Empty file (0 bytes)", emptyPreview.TextContent);

        var imgPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "tiny.png"), CancellationToken.None);
        Assert.Equal(PreviewKind.Image, imgPreview.Kind);
        Assert.True(imgPreview.IsVisualThumbnail);
        Assert.NotNull(imgPreview.PreviewImage);
        Assert.Contains("12 × 8 px", imgPreview.ImageDimensions);

        var (missingBmp, missingThumb) = NativeShellService.ExtractShellPreviewBitmap(Path.Combine(_tempRoot, "missing.xyz"), 256);
        Assert.Null(missingBmp);
        Assert.False(missingThumb);
    }

    [Fact]
    public async Task FileSystemService_BuildPreviewAsync_PreviewsPdfOfficeRtfAndFonts()
    {
        var svc = new FileSystemService();

        // 1. Minimal valid single-page PDF 1.4
        var pdfPath = Path.Combine(_tempRoot, "sample.pdf");
        await File.WriteAllBytesAsync(pdfPath, BuildMinimalSinglePagePdfBytes("Eigen PDF Preview"));

        // 2. Minimal valid .docx (OOXML)
        var docxPath = Path.Combine(_tempRoot, "report.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(docxPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var docEntry = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(docEntry.Open());
            w.Write("""
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Quarterly Eigen Summary</w:t></w:r></w:p>
                  </w:body>
                </w:document>
                """);
        }

        // 3. Minimal valid .rtf
        var rtfPath = Path.Combine(_tempRoot, "notes.rtf");
        await File.WriteAllTextAsync(rtfPath, @"{\rtf1\ansi\deff0 {\fonttbl {\f0 Segoe UI;}} \f0\fs24 Rich Text Preview Check}");

        var items = await svc.GetDirectoryItemsAsync(_tempRoot, showHidden: false, CancellationToken.None);

        var pdfPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "sample.pdf"), CancellationToken.None);
        Assert.Equal(PreviewKind.Generic, pdfPreview.Kind);
        Assert.False(pdfPreview.IsVisualThumbnail);
        Assert.False(pdfPreview.CanZoom);
        Assert.False(string.IsNullOrWhiteSpace(pdfPreview.OpenPromptLabel));
        Assert.Contains("PDF 1.4 Document", pdfPreview.TextContent);
        Assert.True(
            pdfPreview.TextContent!.Contains("Open with:", StringComparison.Ordinal) ||
            pdfPreview.TextContent.Contains("No default application", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(NativeShellService.GetAssociatedAppName(".txt")));

        // Video file preview descriptor
        Assert.True(FormatHelper.IsVideoExtension(".mp4"));
        Assert.True(FormatHelper.IsVideoExtension(".mkv"));
        Assert.False(FormatHelper.IsVideoExtension(".png"));

        var videoPath = Path.Combine(_tempRoot, "clip.mp4");
        await File.WriteAllBytesAsync(videoPath, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
        var videoItem = new FileSystemItem
        {
            Name = "clip.mp4",
            FullPath = videoPath,
            ParentPath = _tempRoot,
            Extension = ".mp4",
            SizeBytes = 8,
            TypeDescription = "MP4 Video"
        };
        var videoPreview = await svc.BuildPreviewAsync(videoItem, CancellationToken.None);
        Assert.Equal(PreviewKind.Video, videoPreview.Kind);
        Assert.True(videoPreview.IsVideo);
        Assert.Equal("Video Player", videoPreview.PreviewBadge);
        Assert.Equal(new Uri(videoPath, UriKind.Absolute), videoPreview.VideoSource);

        var docxPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "report.docx"), CancellationToken.None);
        Assert.Equal("Word Document", docxPreview.PreviewBadge);
        Assert.Contains("Quarterly Eigen Summary", docxPreview.TextContent);

        var rtfPreview = await svc.BuildPreviewAsync(items.Single(i => i.Name == "notes.rtf"), CancellationToken.None);
        Assert.Equal(PreviewKind.Text, rtfPreview.Kind);
        Assert.Equal("Rich Text Document", rtfPreview.PreviewBadge);
        Assert.Contains("Rich Text Preview Check", rtfPreview.TextContent);

        var segoeFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (File.Exists(segoeFont))
        {
            var fontItem = new FileSystemItem
            {
                Name = "segoeui.ttf",
                FullPath = segoeFont,
                ParentPath = Path.GetDirectoryName(segoeFont)!,
                Extension = ".ttf",
                SizeBytes = new FileInfo(segoeFont).Length,
                TypeDescription = "TrueType font file"
            };
            var fontPreview = await svc.BuildPreviewAsync(fontItem, CancellationToken.None);
            Assert.Equal(PreviewKind.Image, fontPreview.Kind);
            Assert.True(fontPreview.IsVisualThumbnail);
            Assert.Equal("Font Specimen", fontPreview.PreviewBadge);
            Assert.NotNull(fontPreview.PreviewImage);
        }
    }

    private static byte[] BuildMinimalSinglePagePdfBytes(string text)
    {
        var objects = new[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n",
            $"4 0 obj\n<< /Length {18 + text.Length} >>\nstream\nBT /F1 18 Tf 20 120 Td ({text}) Tj ET\nendstream\nendobj\n",
            "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"
        };

        var sb = new System.Text.StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        foreach (var obj in objects)
        {
            offsets.Add(System.Text.Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(obj);
        }

        var xrefOffset = System.Text.Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n");
        sb.Append("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Length; i++)
            sb.Append($"{offsets[i]:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void MainWindow_FloatingPreviewOverlay_CentersAndRespondsToHoverState()
    {
        var sampleFile = Path.Combine(_tempRoot, "preview-check.txt");
        File.WriteAllText(sampleFile, "Hello floating centered preview!");

        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            Eigen.ViewModels.MainViewModel? vm = null;
            try
            {
                var app = Application.Current ?? new Application();
                if (app.Resources.MergedDictionaries.Count == 0)
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenLight.xaml")
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Eigen;component/Themes/EigenStyles.xaml")
                    });
                }

                System.Threading.SynchronizationContext.SetSynchronizationContext(
                    new System.Windows.Threading.DispatcherSynchronizationContext(
                        System.Windows.Threading.Dispatcher.CurrentDispatcher));

                var win = new Eigen.MainWindow(_tempRoot);
                vm = (Eigen.ViewModels.MainViewModel)win.DataContext;
                vm.IsPreviewPaneOpen = false;
                vm.IsCustomizePaneOpen = false;
                var pane = vm.ActivePane!;

                var navFrame = new System.Windows.Threading.DispatcherFrame();
                var navTask = pane.NavigateToAsync(_tempRoot, recordHistory: false);
                navTask.ContinueWith(_ => navFrame.Continue = false, System.Threading.Tasks.TaskScheduler.Default);
                System.Windows.Threading.Dispatcher.PushFrame(navFrame);
                navTask.GetAwaiter().GetResult();

                // Guard: floating preview cannot open while preview pane is closed or CurrentPreview is null
                var paneView = (Eigen.Controls.ExplorerPaneView)win.FindName("PrimaryPaneView");
                var marqueeCanvas = (System.Windows.Controls.Canvas)paneView.FindName("MarqueeCanvas");
                Assert.NotNull(marqueeCanvas);
                Assert.False(marqueeCanvas.IsHitTestVisible);
                Assert.Equal(Visibility.Collapsed, marqueeCanvas.Visibility);

                vm.IsFloatingPreviewOpen = true;
                Assert.False(vm.IsFloatingPreviewOpen);

                pane.SelectedItem = pane.Items.Single(i => i.Name == "preview-check.txt");
                vm.IsPreviewPaneOpen = true;

                // Pump dispatcher briefly for async BuildPreviewAsync completion
                var deadline = DateTime.UtcNow.AddSeconds(3);
                while (vm.CurrentPreview is null && DateTime.UtcNow < deadline)
                {
                    var tick = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => tick.Continue = false));
                    System.Windows.Threading.Dispatcher.PushFrame(tick);
                    Thread.Sleep(15);
                }

                Assert.NotNull(vm.CurrentPreview);

                var overlay = (System.Windows.Controls.Grid)win.FindName("FloatingPreviewOverlay");
                var card = (System.Windows.Controls.Border)win.FindName("FloatingPreviewCard");
                var closeBtn = (System.Windows.Controls.Button)win.FindName("CloseFloatingPreviewButton");
                var zoomResetBtn = (System.Windows.Controls.Button)win.FindName("FloatingZoomResetButton");
                Assert.True(overlay.IsHitTestVisible);
                Assert.NotNull(closeBtn);
                Assert.Equal("100%", zoomResetBtn.Content);
                Assert.Equal(Visibility.Collapsed, overlay.Visibility);
                Assert.Equal(3, System.Windows.Controls.Grid.GetRowSpan(overlay));
                Assert.Equal(HorizontalAlignment.Center, card.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, card.VerticalAlignment);

                vm.IsFloatingPreviewOpen = true;
                Assert.True(vm.IsFloatingPreviewOpen);
                Assert.Equal(Visibility.Visible, overlay.Visibility);
                Assert.Equal(Math.Round(SystemParameters.PrimaryScreenWidth * 0.625), card.Width);
                Assert.Equal(Math.Round(SystemParameters.PrimaryScreenHeight * 0.625), card.Height);

                // Verify cross button closes floating preview
                closeBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.False(vm.IsFloatingPreviewOpen);

                vm.IsFloatingPreviewOpen = true;
                vm.IsPreviewPaneOpen = false;
                Assert.False(vm.IsFloatingPreviewOpen);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
            finally
            {
                vm?.Dispose();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new Exception("Floating preview STA test failed", threadEx);
    }

    [Fact]
    public void VectorIconPackService_RendersAllIconPacksAcrossCategories()
    {
        var skin = ThemeCatalogService.GetById("default");
        var customPacks = Enum.GetValues<IconPackStyle>()
            .Where(p => p is not IconPackStyle.Auto and not IconPackStyle.Native)
            .ToList();

        Assert.Contains(IconPackStyle.Fluent3D, customPacks);
        Assert.Contains(IconPackStyle.SoftPastel, customPacks);
        Assert.Contains(IconPackStyle.MonochromeInk, customPacks);
        Assert.Contains(IconPackStyle.BlueprintGrid, customPacks);

        foreach (var pack in customPacks)
        {
            var folderIcon = VectorIconPackService.GetThemedIcon(pack, @"C:\Test", isDirectory: true, isDrive: false, skin);
            var driveIcon = VectorIconPackService.GetThemedIcon(pack, @"C:\", isDirectory: true, isDrive: true, skin);
            var codeIcon = VectorIconPackService.GetThemedIcon(pack, @"C:\Test\app.cs", isDirectory: false, isDrive: false, skin);
            var videoIcon = VectorIconPackService.GetThemedIcon(pack, @"C:\Test\clip.mp4", isDirectory: false, isDrive: false, skin);

            Assert.NotNull(folderIcon);
            Assert.NotNull(driveIcon);
            Assert.NotNull(codeIcon);
            Assert.NotNull(videoIcon);
            Assert.True(folderIcon.IsFrozen);
        }
    }

    [Fact]
    public async Task ArchiveService_CreatesListsTestsExtractsAndInjectsMenu()
    {
        Assert.True(ArchiveService.IsArchivePath(@"C:\test\bundle.zip"));
        Assert.True(ArchiveService.IsArchivePath(@"C:\test\bundle.rar"));
        Assert.True(ArchiveService.IsArchivePath(@"C:\test\bundle.7z"));
        Assert.True(ArchiveService.IsArchivePath(@"C:\test\bundle.tar.gz"));
        Assert.False(ArchiveService.IsArchivePath(@"C:\test\readme.txt"));

        var srcDir = Directory.CreateDirectory(Path.Combine(_tempRoot, "PackSrc")).FullName;
        var nestedDir = Directory.CreateDirectory(Path.Combine(srcDir, "sub")).FullName;
        var fileA = Path.Combine(srcDir, "alpha.txt");
        var fileB = Path.Combine(nestedDir, "beta.txt");
        await File.WriteAllTextAsync(fileA, "Alpha payload");
        await File.WriteAllTextAsync(fileB, "Beta payload");

        // 1. Create ZIP and TAR.GZ
        var zipPath = Path.Combine(_tempRoot, "out.zip");
        var packedZip = await ArchiveService.CreateArchiveAsync([srcDir], zipPath, ArchiveCreateFormat.Zip, ArchiveCompressionPreset.Normal);
        Assert.Equal(2, packedZip);
        Assert.True(File.Exists(zipPath));

        var tgzPath = Path.Combine(_tempRoot, "out.tar.gz");
        var packedTgz = await ArchiveService.CreateArchiveAsync([srcDir], tgzPath, ArchiveCreateFormat.TarGz, ArchiveCompressionPreset.Normal);
        Assert.Equal(2, packedTgz);
        Assert.True(File.Exists(tgzPath));

        // 2. List & Test archive integrity
        var entries = ArchiveService.ListEntries(zipPath);
        Assert.Contains(entries, e => !e.IsDirectory && e.Name == "alpha.txt");
        Assert.Contains(entries, e => !e.IsDirectory && e.Name == "beta.txt");

        var (testedCount, testedBytes) = await ArchiveService.TestArchiveAsync(zipPath);
        Assert.Equal(2, testedCount);
        Assert.True(testedBytes > 0);

        // 3. Extract to subfolder
        var extractRoot = Directory.CreateDirectory(Path.Combine(_tempRoot, "Extracted")).FullName;
        var extracted = await ArchiveService.ExtractAsync(zipPath, extractRoot, createSubfolder: true);
        Assert.Equal(2, extracted);
        Assert.Equal("Beta payload", await File.ReadAllTextAsync(Path.Combine(extractRoot, "out", "PackSrc", "sub", "beta.txt")));

        // 4. Add extra file to ZIP
        var extraFile = Path.Combine(_tempRoot, "gamma.txt");
        await File.WriteAllTextAsync(extraFile, "Gamma payload");
        var added = await ArchiveService.AddFilesToZipAsync(zipPath, [extraFile]);
        Assert.Equal(1, added);
        Assert.Contains(ArchiveService.ListEntries(zipPath), e => !e.IsDirectory && e.Name == "gamma.txt");

        // 4b. Verify Eigen Archiver fallback icon & third-party archiver association check
        var archiverIcon = VectorIconPackService.GetEigenArchiverIcon(ThemeCatalogService.GetById("default"));
        Assert.NotNull(archiverIcon);
        Assert.True(archiverIcon.IsFrozen);
        Assert.False(NativeShellService.HasThirdPartyArchiverAssociation(null));
        Assert.False(NativeShellService.HasThirdPartyArchiverAssociation(".definitely_unassociated_ext_999"));

        // 5. Verify right-click menu injection in STA
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var paneView = new Eigen.Controls.ExplorerPaneView();
                var menu = new System.Windows.Controls.ContextMenu();
                var rarItem = new FileSystemItem { Name = "archive.rar", FullPath = Path.Combine(_tempRoot, "archive.rar") };
                paneView.InjectEigenArchiverMenu(menu, [rarItem.FullPath], rarItem);

                var archiverMenu = menu.Items.OfType<System.Windows.Controls.MenuItem>()
                    .FirstOrDefault(m => string.Equals(m.Header?.ToString(), "Eigen Archiver", StringComparison.Ordinal));
                Assert.NotNull(archiverMenu);
                var subHeaders = archiverMenu!.Items.OfType<System.Windows.Controls.MenuItem>()
                    .Select(m => m.Header?.ToString())
                    .ToList();
                Assert.Contains("Open in Eigen Archiver", subHeaders);
                Assert.Contains("Extract Here", subHeaders);
                Assert.Contains("Extract to \"archive\\\"", subHeaders);
                Assert.Contains("Add to archive...", subHeaders);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (threadEx is not null)
            throw new Exception("Eigen Archiver menu STA test failed", threadEx);
    }

    [Fact]
    public void MainViewModel_WindowBrightness_ClampsAndPersists()
    {
        var settingsPath = Path.Combine(_tempRoot, "brightness-settings.json");
        using (var vm1 = new MainViewModel(_tempRoot, settingsPath))
        {
            Assert.Equal(1.0, vm1.WindowBrightness);
            Assert.Equal(0.0, vm1.BrightnessOverlayOpacity);
            Assert.Equal("100%", vm1.WindowBrightnessPercentText);

            vm1.WindowBrightness = 0.1; // Clamps to 0.40
            Assert.Equal(0.40, vm1.WindowBrightness);
            Assert.Equal("40%", vm1.WindowBrightnessPercentText);
            Assert.True(vm1.BrightnessOverlayOpacity > 0.4);

            vm1.WindowBrightness = 0.75;
            Assert.Equal("75%", vm1.WindowBrightnessPercentText);
        }

        using var vm2 = new MainViewModel(_tempRoot, settingsPath);
        Assert.Equal(0.75, vm2.WindowBrightness);
        vm2.ResetBrightnessCommand.Execute(null);
        Assert.Equal(1.0, vm2.WindowBrightness);
        Assert.Equal(0.0, vm2.BrightnessOverlayOpacity);
    }

    [Fact]
    public async Task MainViewModel_AllCustomizations_PersistToDiskImmediately()
    {
        var settingsPath = Path.Combine(_tempRoot, "custom-persist.json");
        using var vm1 = new MainViewModel(_tempRoot, settingsPath);
        vm1.ActiveSkin = ThemeCatalogService.GetById("cobalt-concrete");
        vm1.IconPackOverride = IconPackStyle.Fluent3D;
        vm1.DetailsVerticalGap = 7;
        vm1.IconLeftGap = 14;
        vm1.ItemIconSize = 22;
        vm1.TileIconSize = 84;
        vm1.ItemFontSize = 16;
        vm1.FontWeightValue = 600;
        vm1.LetterSpacing = 0.5;
        vm1.IconTransparency = 0.35;
        vm1.TextTransparency = 0.25;
        vm1.WindowBrightness = 0.8;

        // Wait past debounce without disposing vm1 to verify live disk persistence
        await Task.Delay(450);

        using var vm2 = new MainViewModel(_tempRoot, settingsPath);
        Assert.Equal("cobalt-concrete", vm2.ActiveSkin.Id);
        Assert.Equal(IconPackStyle.Fluent3D, vm2.IconPackOverride);
        Assert.Equal(7, vm2.DetailsVerticalGap);
        Assert.Equal(14, vm2.IconLeftGap);
        Assert.Equal(22, vm2.ItemIconSize);
        Assert.Equal(84, vm2.TileIconSize);
        Assert.Equal(16, vm2.ItemFontSize);
        Assert.Equal(600, vm2.FontWeightValue);
        Assert.Equal(0.5, vm2.LetterSpacing);
        Assert.Equal(0.35, vm2.IconTransparency);
        Assert.Equal(0.25, vm2.TextTransparency);
        Assert.Equal(0.65, vm2.IconOpacity, 2);
        Assert.Equal(0.75, vm2.TextOpacity, 2);
        Assert.Equal("35%", vm2.IconTransparencyPercentText);
        Assert.Equal("25%", vm2.TextTransparencyPercentText);
        Assert.Equal(0.8, vm2.WindowBrightness);

        // Quick Access and This PC icons align on the exact same vertical X line
        var quickAccessIconLeft = vm2.SidebarItemMargin.Left + vm2.SidebarItemPadding.Left;
        var treeIconLeft = vm2.SidebarTreeItemMargin.Left + vm2.SidebarTreeItemPadding.Left + 18.0;
        Assert.Equal(treeIconLeft, quickAccessIconLeft);

        // IconSize and TileIconSize are independent
        vm2.ItemIconSize = 30;
        Assert.Equal(84, vm2.TileIconSize);
        vm2.TileIconSize = 110;
        Assert.Equal(30, vm2.ItemIconSize);

        vm2.ResetLayoutCustomizationCommand.Execute(null);
        Assert.Equal(MainViewModel.DefaultIconSize, vm2.ItemIconSize);
        Assert.Equal(MainViewModel.DefaultTileIconSize, vm2.TileIconSize);
        Assert.Equal(0.0, vm2.IconTransparency);
        Assert.Equal(0.0, vm2.TextTransparency);
        Assert.Equal(1.0, vm2.IconOpacity);
        Assert.Equal(1.0, vm2.TextOpacity);

        Assert.Equal(vm2.AvailableIconPacks.Count, vm2.IconPackOptions.Count);
        Assert.All(vm2.IconPackOptions, opt =>
        {
            Assert.NotNull(opt.FolderPreview);
            Assert.NotNull(opt.CodePreview);
            Assert.NotNull(opt.ImagePreview);
        });
        Assert.Contains("Translucent", ThemeCatalogService.Categories);
        var obsidian = ThemeCatalogService.GetById("acrylic-obsidian");
        var frost = ThemeCatalogService.GetById("acrylic-frost");
        Assert.True(obsidian.IsTranslucent);
        Assert.True(frost.IsTranslucent);
        Assert.Equal("Translucent • Acrylic", obsidian.AestheticTag);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
