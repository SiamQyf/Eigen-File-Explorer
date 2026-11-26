using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Eigen.Helpers;
using Eigen.Models;
using Eigen.Services;

namespace Eigen.ViewModels;

public sealed class ExplorerPaneViewModel : ObservableObject, IDisposable
{
    private readonly FileSystemService _fileSystem;
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private readonly Func<IReadOnlyList<string>> _getPinnedFolders;
    private readonly Func<IReadOnlyList<string>> _getRecentPaths;
    private readonly Func<string, bool, bool, bool, (ImageSource? Icon, string TypeName)>? _iconResolver;

    private List<FileSystemItem> _rawItems = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _watcherDebounceCts;
    private CancellationTokenSource? _typeAheadResetCts;
    private FileSystemWatcher? _watcher;

    private string _currentPath = FormatHelper.HomeLocation;
    private string _addressBarText = "Home";
    private string _searchQuery = string.Empty;
    private string _typeAheadQuery = string.Empty;
    private DateTime _lastTypeAheadUtc = DateTime.MinValue;
    private string _statusSummary = string.Empty;
    private string? _errorMessage;
    private string? _pendingSelectPath;
    private bool _isActivePane;
    private bool _isAddressEditing;
    private bool _isDeepSearch;
    private bool _isLoading;
    private bool _showHiddenFiles;
    private bool _preferLargeListIcons;
    private SortField _sortField = SortField.Name;
    private bool _sortAscending = true;
    private ExplorerViewMode _viewMode = ExplorerViewMode.Details;
    private FileSystemItem? _selectedItem;
    private ImageSource? _currentIcon;

    public ExplorerPaneViewModel(
        FileSystemService fileSystem,
        Func<IReadOnlyList<string>> getPinnedFolders,
        Func<IReadOnlyList<string>> getRecentPaths,
        ExplorerViewMode initialViewMode,
        bool showHiddenFiles,
        Func<string, bool, bool, bool, (ImageSource? Icon, string TypeName)>? iconResolver = null)
    {
        _fileSystem = fileSystem;
        _getPinnedFolders = getPinnedFolders;
        _getRecentPaths = getRecentPaths;
        _viewMode = initialViewMode;
        _showHiddenFiles = showHiddenFiles;
        _iconResolver = iconResolver;

        GoBackCommand = new RelayCommand(GoBack, () => CanGoBack);
        GoForwardCommand = new RelayCommand(GoForward, () => CanGoForward);
        GoUpCommand = new RelayCommand(GoUp, () => CanGoUp);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        ActivateAddressEditCommand = new RelayCommand(BeginAddressEdit);
        CommitAddressEditCommand = new RelayCommand(CommitAddressEdit);
        CancelAddressEditCommand = new RelayCommand(CancelAddressEdit);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        ExecuteDeepSearchCommand = new RelayCommand(() => _ = RunDeepSearchAsync());
        SortByCommand = new RelayCommand<object>(param =>
        {
            if (param is SortField sf) SetSort(sf);
            else if (param is string s && Enum.TryParse<SortField>(s, true, out var parsed)) SetSort(parsed);
        });
        NavigateCommand = new RelayCommand<string>(path =>
        {
            if (!string.IsNullOrWhiteSpace(path))
                _ = NavigateToAsync(path);
        });
        OpenItemCommand = new RelayCommand<FileSystemItem>(OpenItem);
    }

    public event Action<ExplorerPaneViewModel>? Activated;
    public event Action<string>? NavigationSucceeded;
    public event Action<FileSystemItem?>? SelectionChanged;
    public event Action<FileSystemItem>? RequestInlineRename;
    public event Action<FileSystemItem>? RequestScrollAndSelect;
    public event Action<IReadOnlyList<FileSystemItem>>? ItemsPreparing;
    public event Action? ItemsUpdated;

    public BulkObservableCollection<FileSystemItem> Items { get; } = [];
    public ObservableCollection<FileSystemItem> SelectedItems { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];
    public ObservableCollection<DriveCardItem> Drives { get; } = [];
    public ObservableCollection<SidebarNode> HomePinnedFolders { get; } = [];
    public ObservableCollection<string> RecentLocations { get; } = [];

    public ICommand GoBackCommand { get; }
    public ICommand GoForwardCommand { get; }
    public ICommand GoUpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ActivateAddressEditCommand { get; }
    public ICommand CommitAddressEditCommand { get; }
    public ICommand CancelAddressEditCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ExecuteDeepSearchCommand { get; }
    public ICommand SortByCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand OpenItemCommand { get; }

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (SetProperty(ref _currentPath, value))
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(IsHome));
                OnPropertyChanged(nameof(CanGoUp));
                ((RelayCommand)GoUpCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string Title => FormatHelper.GetDisplayTitle(CurrentPath);
    public bool IsHome => FormatHelper.IsHomePath(CurrentPath);
    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public bool CanGoUp => !IsHome;

    public ImageSource? CurrentIcon
    {
        get => _currentIcon;
        private set => SetProperty(ref _currentIcon, value);
    }

    public bool IsActivePane
    {
        get => _isActivePane;
        set => SetProperty(ref _isActivePane, value);
    }

    public string AddressBarText
    {
        get => _addressBarText;
        set => SetProperty(ref _addressBarText, value);
    }

    public bool IsAddressEditing
    {
        get => _isAddressEditing;
        set => SetProperty(ref _isAddressEditing, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                OnPropertyChanged(nameof(HasSearchQuery));
                if (IsDeepSearch)
                {
                    if (string.IsNullOrWhiteSpace(value))
                        _ = RefreshAsync();
                }
                else
                {
                    ApplyFilterAndSort();
                }
            }
        }
    }

    public bool HasSearchQuery => !string.IsNullOrEmpty(SearchQuery);

    public string TypeAheadQuery
    {
        get => _typeAheadQuery;
        private set
        {
            if (SetProperty(ref _typeAheadQuery, value))
                OnPropertyChanged(nameof(HasTypeAheadQuery));
        }
    }

    public bool HasTypeAheadQuery => !string.IsNullOrEmpty(TypeAheadQuery);

    public bool IsDeepSearch
    {
        get => _isDeepSearch;
        set
        {
            if (SetProperty(ref _isDeepSearch, value))
            {
                if (!value)
                {
                    _searchCts?.Cancel();
                    _ = RefreshAsync();
                }
                else if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    _ = RunDeepSearchAsync();
                }
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string StatusSummary
    {
        get => _statusSummary;
        private set => SetProperty(ref _statusSummary, value);
    }

    public bool ShowHiddenFiles
    {
        get => _showHiddenFiles;
        set
        {
            if (SetProperty(ref _showHiddenFiles, value))
                _ = RefreshAsync();
        }
    }

    public SortField SortField
    {
        get => _sortField;
        set
        {
            if (SetProperty(ref _sortField, value))
                ApplyFilterAndSort();
        }
    }

    public bool SortAscending
    {
        get => _sortAscending;
        set
        {
            if (SetProperty(ref _sortAscending, value))
                ApplyFilterAndSort();
        }
    }

    public ExplorerViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (SetProperty(ref _viewMode, value))
            {
                if (value == ExplorerViewMode.Tiles)
                    _ = EnrichTilesAssetsAsync();
            }
        }
    }

    public bool PreferLargeListIcons
    {
        get => _preferLargeListIcons;
        set
        {
            if (SetProperty(ref _preferLargeListIcons, value) && value)
                _ = EnsureLargeIconsLoadedAsync();
        }
    }

    public Task EnsureLargeIconsLoadedAsync(CancellationToken ct = default)
        => EnrichLargeIconsAsync(ct);

    public FileSystemItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                UpdateStatusSummary();
                SelectionChanged?.Invoke(value);
            }
        }
    }

    public void Activate()
    {
        Activated?.Invoke(this);
    }

    public async Task NavigateToAsync(string rawPath, bool recordHistory = true, string? selectItemPath = null)
    {
        var trimmed = rawPath?.Trim() ?? string.Empty;
        if (trimmed.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("wt", StringComparison.OrdinalIgnoreCase))
        {
            var workDir = !IsHome && Directory.Exists(CurrentPath)
                ? CurrentPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = trimmed,
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            AddressBarText = IsHome ? "Home" : CurrentPath;
            IsAddressEditing = false;
            return;
        }

        var target = FormatHelper.ExpandNavigationPath(trimmed);
        if (!FormatHelper.IsHomePath(target) && !Directory.Exists(target))
        {
            if (File.Exists(target))
            {
                NativeShellService.OpenItem(target);
                return;
            }

            ErrorMessage = $"Could not find folder \"{target}\".";
            return;
        }

        if (recordHistory && !string.Equals(CurrentPath, target, StringComparison.OrdinalIgnoreCase))
        {
            _backStack.Push(CurrentPath);
            _forwardStack.Clear();
            RaiseHistoryChanged();
        }

        _pendingSelectPath = selectItemPath;
        ClearTypeAhead();
        _searchQuery = string.Empty;
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(HasSearchQuery));

        CurrentPath = target;
        AddressBarText = IsHome ? "Home" : target;
        IsAddressEditing = false;
        ErrorMessage = null;

        RebuildBreadcrumbs();
        UpdatePaneIcon();

        await LoadCurrentLocationAsync();
        NavigationSucceeded?.Invoke(CurrentPath);
    }

    public Task RefreshAsync(string? selectPath = null)
    {
        _pendingSelectPath = selectPath ?? SelectedItem?.FullPath;
        InvalidateFolderCache(CurrentPath);
        return LoadCurrentLocationAsync(forceRefresh: true);
    }

    public void GoBack()
    {
        if (_backStack.Count == 0) return;
        var prev = _backStack.Pop();
        _forwardStack.Push(CurrentPath);
        RaiseHistoryChanged();
        _ = NavigateToAsync(prev, recordHistory: false);
    }

    public void GoForward()
    {
        if (_forwardStack.Count == 0) return;
        var next = _forwardStack.Pop();
        _backStack.Push(CurrentPath);
        RaiseHistoryChanged();
        _ = NavigateToAsync(next, recordHistory: false);
    }

    public void GoUp()
    {
        if (IsHome) return;
        var current = CurrentPath;
        var parent = FormatHelper.GetParentPath(current);
        _ = NavigateToAsync(parent, recordHistory: true, selectItemPath: current);
    }

    public void BeginAddressEdit()
    {
        AddressBarText = IsHome ? string.Empty : CurrentPath;
        IsAddressEditing = true;
    }

    public void CommitAddressEdit()
    {
        var text = AddressBarText.Trim();
        IsAddressEditing = false;
        if (string.IsNullOrEmpty(text))
        {
            AddressBarText = IsHome ? "Home" : CurrentPath;
            return;
        }
        _ = NavigateToAsync(text);
    }

    public void CancelAddressEdit()
    {
        if (!IsAddressEditing) return;
        IsAddressEditing = false;
        AddressBarText = IsHome ? "Home" : CurrentPath;
    }

    public void ClearSearch()
    {
        SearchQuery = string.Empty;
        if (IsDeepSearch)
            IsDeepSearch = false;
    }

    public void SetSort(SortField field)
    {
        if (SortField == field)
            SortAscending = !SortAscending;
        else
        {
            _sortField = field;
            _sortAscending = true;
            OnPropertyChanged(nameof(SortField));
            OnPropertyChanged(nameof(SortAscending));
            ApplyFilterAndSort();
        }
    }

    public void LoadBreadcrumbSiblings(BreadcrumbSegment segment)
    {
        segment.SiblingFolders.Clear();
        foreach (var sibling in _fileSystem.GetSiblingDirectories(segment.FullPath, ShowHiddenFiles))
            segment.SiblingFolders.Add(sibling);
    }

    public void OpenItem(FileSystemItem? item)
    {
        if (item is null) return;
        if (item.IsDirectory)
        {
            _ = NavigateToAsync(item.FullPath);
        }
        else
        {
            try
            {
                NativeShellService.OpenItem(item.FullPath);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Unable to open \"{item.Name}\": {ex.Message}";
            }
        }
    }

    public void SyncSelectedItems(IEnumerable<FileSystemItem> selected)
    {
        SelectedItems.Clear();
        foreach (var item in selected)
            SelectedItems.Add(item);
        UpdateStatusSummary();
    }

    public void TriggerInlineRename(FileSystemItem item)
    {
        item.EditingName = item.Name;
        item.IsEditing = true;
        RequestInlineRename?.Invoke(item);
    }

    public void UpdateCutMarkers(IReadOnlySet<string> cutPaths)
    {
        foreach (var item in _rawItems)
            item.IsCut = cutPaths.Contains(item.FullPath);
    }

    private (ImageSource? Icon, string TypeName) ResolveIconAndType(string path, bool isDirectory, bool isDrive = false, bool largeIcon = false)
        => _iconResolver is not null
            ? _iconResolver(path, isDirectory, isDrive, largeIcon)
            : NativeShellService.GetIconAndType(path, isDirectory, isDrive, largeIcon);

    public void RefreshVisibleIcons()
    {
        UpdatePaneIcon();
        if (IsHome)
        {
            RefreshHomeData();
            return;
        }

        foreach (var item in _rawItems)
        {
            var (icon, typeName) = ResolveIconAndType(item.FullPath, item.IsDirectory, item.IsDrive, largeIcon: false);
            item.Icon = icon;
            if (!string.IsNullOrWhiteSpace(typeName))
                item.TypeDescription = typeName;

            if (item.LargeIcon is not null || ViewMode == ExplorerViewMode.Tiles || PreferLargeListIcons)
            {
                var (large, _) = ResolveIconAndType(item.FullPath, item.IsDirectory, item.IsDrive, largeIcon: true);
                item.LargeIcon = large;
            }
        }
    }

    public void RefreshHomeData()
    {
        if (!IsHome) return;

        Drives.Clear();
        foreach (var drive in _fileSystem.GetDrives())
        {
            var (icon, _) = ResolveIconAndType(drive.RootPath, isDirectory: true, isDrive: true, largeIcon: true);
            Drives.Add(new DriveCardItem
            {
                Name = drive.Name,
                RootPath = drive.RootPath,
                VolumeLabel = drive.VolumeLabel,
                DriveTypeLabel = drive.DriveTypeLabel,
                FileSystem = drive.FileSystem,
                TotalBytes = drive.TotalBytes,
                FreeBytes = drive.FreeBytes,
                SpaceSummary = drive.SpaceSummary,
                Icon = icon
            });
        }

        HomePinnedFolders.Clear();
        foreach (var path in _getPinnedFolders())
        {
            if (!Directory.Exists(path)) continue;
            var (icon, _) = ResolveIconAndType(path, isDirectory: true, isDrive: false, largeIcon: true);
            HomePinnedFolders.Add(new SidebarNode
            {
                Title = FormatHelper.GetDisplayTitle(path),
                Path = path,
                IsPinned = true,
                Icon = icon
            });
        }

        RecentLocations.Clear();
        foreach (var recent in _getRecentPaths().Where(Directory.Exists).Take(8))
            RecentLocations.Add(recent);
    }

    private sealed record CachedFolderEntry(
        List<FileSystemItem> Raw,
        List<FileSystemItem> Sorted,
        bool ShowHidden,
        SortField SortField,
        bool SortAscending);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedFolderEntry> FolderViewCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static void InvalidateFolderCache(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            FolderViewCache.TryRemove(path.TrimEnd('\\'), out _);
    }

    public async Task PrewarmFoldersAsync(IEnumerable<string> paths)
    {
        var showHidden = ShowHiddenFiles;
        var sortField = SortField;
        var sortAsc = SortAscending;
        await Task.Run(() =>
        {
            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath) || !Directory.Exists(rawPath)) continue;
                var key = rawPath.TrimEnd('\\');
                if (FolderViewCache.ContainsKey(key)) continue;

                try
                {
                    var raw = _fileSystem.GetDirectoryItemsCore(rawPath, showHidden, CancellationToken.None);
                    var ordered = SortItems(raw).ToList();
                    ResolveFastCategoryIcons(ordered, CancellationToken.None);
                    for (var i = 0; i < Math.Min(36, ordered.Count); i++)
                    {
                        var item = ordered[i];
                        var (icon, typeName) = ResolveIconAndType(item.FullPath, item.IsDirectory);
                        item.Icon = icon;
                        if (!string.IsNullOrWhiteSpace(typeName))
                            item.TypeDescription = typeName;
                    }
                    if (ordered.Count <= 4000)
                    {
                        if (FolderViewCache.Count >= 32)
                            FolderViewCache.Clear();
                        FolderViewCache[key] = new CachedFolderEntry(raw, ordered, showHidden, sortField, sortAsc);
                    }
                }
                catch
                {
                    // Ignore inaccessible prewarm paths
                }
            }
        });
    }

    private async Task LoadCurrentLocationAsync(bool forceRefresh = false)
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        StopWatcher();

        if (IsHome)
        {
            _rawItems = [];
            Items.Clear();
            SelectedItems.Clear();
            SelectedItem = null;
            IsLoading = false;
            RefreshHomeData();
            StatusSummary = $"{Drives.Count} drive(s)";
            ItemsUpdated?.Invoke();
            return;
        }

        var cacheKey = CurrentPath.TrimEnd('\\');
        var query = SearchQuery.Trim();
        if (!forceRefresh &&
            string.IsNullOrEmpty(query) &&
            !IsDeepSearch &&
            FolderViewCache.TryGetValue(cacheKey, out var cached) &&
            cached.ShowHidden == ShowHiddenFiles &&
            cached.SortField == SortField &&
            cached.SortAscending == SortAscending)
        {
            foreach (var it in cached.Sorted)
                it.IsEditing = false;

            _rawItems = cached.Raw;
            ItemsPreparing?.Invoke(cached.Sorted);
            Items.ReplaceAll(cached.Sorted);
            UpdateStatusSummary();
            ItemsUpdated?.Invoke();
            StartWatcher(CurrentPath);

            if (!string.IsNullOrEmpty(_pendingSelectPath))
            {
                var match = Items.FirstOrDefault(i => string.Equals(i.FullPath, _pendingSelectPath, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    SelectedItem = match;
                _pendingSelectPath = null;
            }

            IsLoading = false;
            _ = EnrichIconsBackgroundAsync(cached.Sorted, 36, ct);
            _ = SilentVerifyAndRefreshCacheAsync(CurrentPath, ShowHiddenFiles, SortField, SortAscending, cached.Raw.Count, ct);
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var path = CurrentPath;
            var showHidden = ShowHiddenFiles;
            var sortField = SortField;
            var sortAsc = SortAscending;
            var isDeep = IsDeepSearch;

            var (items, sorted) = await Task.Run(() =>
            {
                var raw = _fileSystem.GetDirectoryItemsCore(path, showHidden, ct);
                ct.ThrowIfCancellationRequested();

                IEnumerable<FileSystemItem> filtered = string.IsNullOrEmpty(query) || isDeep
                    ? raw
                    : raw.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
                var ordered = SortItems(filtered).ToList();
                return (raw, ordered);
            }, ct);
            if (ct.IsCancellationRequested) return;

            if (string.IsNullOrEmpty(query) && !isDeep)
            {
                if (sorted.Count <= 4000)
                {
                    if (FolderViewCache.Count >= 32)
                        FolderViewCache.Clear();
                    FolderViewCache[cacheKey] = new CachedFolderEntry(items, sorted, showHidden, sortField, sortAsc);
                }
            }

            _rawItems = items;
            ItemsPreparing?.Invoke(sorted);
            Items.ReplaceAll(sorted);
            UpdateStatusSummary();
            ItemsUpdated?.Invoke();
            StartWatcher(CurrentPath);

            if (!string.IsNullOrEmpty(_pendingSelectPath))
            {
                var match = Items.FirstOrDefault(i => string.Equals(i.FullPath, _pendingSelectPath, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    SelectedItem = match;
                _pendingSelectPath = null;
            }

            IsLoading = false;

            if (sorted.Count > 0)
                _ = EnrichIconsBackgroundAsync(sorted, 0, ct);
            if (ViewMode == ExplorerViewMode.Tiles)
                await EnrichTilesAssetsAsync(ct);
            else if (PreferLargeListIcons)
                await EnrichLargeIconsAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Navigation superseded
        }
        catch (Exception ex)
        {
            _rawItems = [];
            Items.Clear();
            ErrorMessage = ex.Message;
            StatusSummary = "0 items";
            ItemsUpdated?.Invoke();
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }

    public async Task RunDeepSearchAsync()
    {
        if (IsHome || string.IsNullOrWhiteSpace(SearchQuery))
        {
            await RefreshAsync();
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var root = CurrentPath;
        var query = SearchQuery.Trim();

        StopWatcher();
        IsLoading = true;
        ErrorMessage = null;
        _rawItems = [];
        Items.Clear();
        StatusSummary = $"Searching \"{query}\"...";

        var progress = new Progress<IReadOnlyList<FileSystemItem>>(batch =>
        {
            if (ct.IsCancellationRequested) return;
            foreach (var item in batch)
            {
                _rawItems.Add(item);
                var (icon, typeName) = ResolveIconAndType(item.FullPath, item.IsDirectory);
                item.Icon = icon;
                item.TypeDescription = typeName;
                Items.Add(item);
            }
            UpdateStatusSummary();
            ItemsUpdated?.Invoke();
        });

        try
        {
            await _fileSystem.SearchRecursiveAsync(root, query, ShowHiddenFiles, progress, ct);
            if (!ct.IsCancellationRequested)
            {
                ApplySortOnly();
                StatusSummary = $"{Items.Count} match(es) for \"{query}\"";
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }

    private void ApplyFilterAndSort()
    {
        var query = SearchQuery.Trim();
        IEnumerable<FileSystemItem> filtered = string.IsNullOrEmpty(query) || IsDeepSearch
            ? _rawItems
            : _rawItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        var sorted = SortItems(filtered).ToList();

        Items.ReplaceAll(sorted);

        UpdateStatusSummary();
        ItemsUpdated?.Invoke();
    }

    private void ApplySortOnly()
    {
        var sorted = SortItems(_rawItems).ToList();
        Items.ReplaceAll(sorted);
        ItemsUpdated?.Invoke();
    }

    private IEnumerable<FileSystemItem> SortItems(IEnumerable<FileSystemItem> source)
    {
        var ordered = source.OrderByDescending(i => i.IsDirectory);

        IOrderedEnumerable<FileSystemItem> result = SortField switch
        {
            SortField.DateModified => SortAscending
                ? ordered.ThenBy(i => i.DateModified)
                : ordered.ThenByDescending(i => i.DateModified),
            SortField.Type => SortAscending
                ? ordered.ThenBy(i => i.TypeDescription, StringComparer.OrdinalIgnoreCase)
                : ordered.ThenByDescending(i => i.TypeDescription, StringComparer.OrdinalIgnoreCase),
            SortField.Size => SortAscending
                ? ordered.ThenBy(i => i.SizeBytes)
                : ordered.ThenByDescending(i => i.SizeBytes),
            _ => SortAscending
                ? ordered.ThenBy(i => i.Name, NaturalStringComparer.Instance)
                : ordered.ThenByDescending(i => i.Name, NaturalStringComparer.Instance)
        };

        return result.ThenBy(i => i.Name, NaturalStringComparer.Instance);
    }

    private void ResolveFastCategoryIcons(List<FileSystemItem> ordered, CancellationToken ct)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ct.IsCancellationRequested) return;
            var item = ordered[i];
            if (item.Icon is not null) continue;
            if (!item.IsDirectory && HasPerFileIcon(item.Extension)) continue;

            var (icon, typeName) = ResolveIconAndType(item.FullPath, item.IsDirectory);
            item.Icon = icon;
            if (!string.IsNullOrWhiteSpace(typeName))
                item.TypeDescription = typeName;
        }
    }

    private static bool HasPerFileIcon(string ext) =>
        ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".url", StringComparison.OrdinalIgnoreCase);

    private async Task SilentVerifyAndRefreshCacheAsync(
        string path,
        bool showHidden,
        SortField sortField,
        bool sortAsc,
        int cachedCount,
        CancellationToken ct)
    {
        try
        {
            var fresh = await Task.Run(() =>
            {
                var raw = _fileSystem.GetDirectoryItemsCore(path, showHidden, ct);
                if (raw.Count == cachedCount)
                    return ((List<FileSystemItem> Raw, List<FileSystemItem> Sorted)?)null;
                var ordered = SortItems(raw).ToList();
                var preloadCount = Math.Min(36, ordered.Count);
                for (var i = 0; i < preloadCount; i++)
                {
                    if (ct.IsCancellationRequested) break;
                    var item = ordered[i];
                    var (icon, typeName) = ResolveIconAndType(item.FullPath, item.IsDirectory);
                    item.Icon = icon;
                    if (!string.IsNullOrWhiteSpace(typeName))
                        item.TypeDescription = typeName;
                }
                return (raw, ordered);
            }, ct);

            if (fresh is null || ct.IsCancellationRequested) return;
            if (!string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;

            FolderViewCache[path.TrimEnd('\\')] = new CachedFolderEntry(fresh.Value.Raw, fresh.Value.Sorted, showHidden, sortField, sortAsc);
            _rawItems = fresh.Value.Raw;
            Items.ReplaceAll(fresh.Value.Sorted);
            UpdateStatusSummary();
            ItemsUpdated?.Invoke();
        }
        catch
        {
            // Ignore background verification errors
        }
    }

    private async Task EnrichIconsBackgroundAsync(IReadOnlyList<FileSystemItem> items, int startIndex, CancellationToken ct)
    {
        try
        {
            const int firstBatchSize = 48;
            const int chunkSize = 150;
            var index = startIndex;
            var batchSize = firstBatchSize;

            while (index < items.Count)
            {
                if (ct.IsCancellationRequested) return;
                var end = Math.Min(index + batchSize, items.Count);
                var start = index;

                var resolved = await Task.Run(() =>
                {
                    var list = new List<(FileSystemItem Item, ImageSource? Icon, string TypeName)>(end - start);
                    for (var i = start; i < end; i++)
                    {
                        if (ct.IsCancellationRequested) return list;
                        var item = items[i];
                        if (item.Icon is not null) continue;
                        var (icon, typeName) = ResolveIconAndType(item.FullPath, item.IsDirectory);
                        list.Add((item, icon, typeName));
                    }
                    return list;
                }, ct);

                if (ct.IsCancellationRequested) return;
                foreach (var (item, icon, typeName) in resolved)
                {
                    if (ct.IsCancellationRequested) return;
                    item.Icon = icon;
                    if (!string.IsNullOrWhiteSpace(typeName))
                        item.TypeDescription = typeName;
                }

                index = end;
                batchSize = chunkSize;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded
        }
    }

    private async Task EnrichLargeIconsAsync(CancellationToken ct = default)
    {
        var itemsNeedingLargeIcons = _rawItems.Where(i => i.LargeIcon is null).ToList();
        const int batchSize = 48;
        for (var i = 0; i < itemsNeedingLargeIcons.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) return;
            var start = i;
            var end = Math.Min(i + batchSize, itemsNeedingLargeIcons.Count);

            var resolved = await Task.Run(() =>
            {
                var list = new List<(FileSystemItem Item, ImageSource? LargeIcon)>(end - start);
                for (var j = start; j < end; j++)
                {
                    if (ct.IsCancellationRequested) return list;
                    var item = itemsNeedingLargeIcons[j];
                    var (largeIcon, _) = ResolveIconAndType(item.FullPath, item.IsDirectory, largeIcon: true);
                    list.Add((item, largeIcon));
                }
                return list;
            }, ct);

            if (ct.IsCancellationRequested) return;
            foreach (var (item, largeIcon) in resolved)
            {
                if (ct.IsCancellationRequested) return;
                item.LargeIcon = largeIcon;
            }
        }
    }

    private async Task EnrichTilesAssetsAsync(CancellationToken ct = default)
    {
        await EnrichLargeIconsAsync(ct);

        var imageItems = _rawItems
            .Where(i => !i.IsDirectory && i.Thumbnail is null && FormatHelper.IsImageExtension(i.Extension))
            .Take(160)
            .ToList();

        if (imageItems.Count == 0) return;

        var dispatcher = Application.Current?.Dispatcher;
        await Parallel.ForEachAsync(
            imageItems,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 6),
                CancellationToken = ct
            },
            async (item, token) =>
            {
                var thumb = FileSystemService.LoadThumbnail(item.FullPath, 140);
                if (thumb is not null && !token.IsCancellationRequested)
                {
                    if (dispatcher is not null)
                        await dispatcher.InvokeAsync(() => item.Thumbnail = thumb);
                    else
                        item.Thumbnail = thumb;
                }
            });
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        foreach (var seg in FormatHelper.BuildBreadcrumbs(CurrentPath))
            Breadcrumbs.Add(seg);
    }

    private void UpdatePaneIcon()
    {
        if (IsHome)
        {
            CurrentIcon = null;
            return;
        }
        var isDrive = string.Equals(
            CurrentPath.TrimEnd('\\'),
            Path.GetPathRoot(CurrentPath)?.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
        var (icon, _) = ResolveIconAndType(CurrentPath, isDirectory: true, isDrive: isDrive);
        CurrentIcon = icon;
    }

    private void UpdateStatusSummary()
    {
        if (IsHome)
        {
            StatusSummary = $"{Drives.Count} drive(s)";
            return;
        }

        var selectedCount = SelectedItems.Count;
        if (selectedCount > 1)
        {
            var selectedBytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.SizeBytes);
            StatusSummary = selectedBytes > 0
                ? $"{Items.Count} items  •  {selectedCount} selected ({FormatHelper.FormatSize(selectedBytes)})"
                : $"{Items.Count} items  •  {selectedCount} selected";
        }
        else if (selectedCount == 1 || SelectedItem is not null)
        {
            var sel = SelectedItems.FirstOrDefault() ?? SelectedItem!;
            StatusSummary = sel.IsDirectory
                ? $"{Items.Count} items  •  1 folder selected"
                : $"{Items.Count} items  •  1 selected ({FormatHelper.FormatSize(sel.SizeBytes)})";
        }
        else
        {
            StatusSummary = Items.Count == 1 ? "1 item" : $"{Items.Count} items";
        }
    }

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        ((RelayCommand)GoBackCommand).RaiseCanExecuteChanged();
        ((RelayCommand)GoForwardCommand).RaiseCanExecuteChanged();
    }

    private long _watcherGeneration;
    private readonly object _watcherLock = new();

    private void StartWatcher(string path)
    {
        var gen = Interlocked.Increment(ref _watcherGeneration);
        _ = Task.Run(() =>
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                lock (_watcherLock)
                {
                    if (gen != _watcherGeneration)
                    {
                        watcher.Dispose();
                        return;
                    }

                    DisposeCurrentWatcherLocked();
                    watcher.Created += OnFileSystemChanged;
                    watcher.Deleted += OnFileSystemChanged;
                    watcher.Renamed += OnFileSystemChanged;
                    _watcher = watcher;
                }
            }
            catch
            {
                // Ignore inaccessible watch paths
            }
        });
    }

    private void StopWatcher()
    {
        Interlocked.Increment(ref _watcherGeneration);
        _watcherDebounceCts?.Cancel();
        _ = Task.Run(() =>
        {
            lock (_watcherLock)
            {
                DisposeCurrentWatcherLocked();
            }
        });
    }

    private void DisposeCurrentWatcherLocked()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileSystemChanged;
            _watcher.Deleted -= OnFileSystemChanged;
            _watcher.Renamed -= OnFileSystemChanged;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        _watcherDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _watcherDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(260, cts.Token);
                if (cts.Token.IsCancellationRequested) return;
                var app = Application.Current;
                if (app?.Dispatcher is not null)
                {
                    await app.Dispatcher.InvokeAsync(() =>
                    {
                        if (!cts.Token.IsCancellationRequested && !IsDeepSearch && !Items.Any(i => i.IsEditing))
                            _ = RefreshAsync();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Debounced
            }
        });
    }

    public void ClearTypeAhead()
    {
        _typeAheadResetCts?.Cancel();
        _lastTypeAheadUtc = DateTime.MinValue;
        TypeAheadQuery = string.Empty;
    }

    public FileSystemItem? HandleTypeAheadChar(char ch, DateTime? utcNow = null)
    {
        if (IsHome || Items.Count == 0 || char.IsControl(ch))
            return null;

        var now = utcNow ?? DateTime.UtcNow;
        if ((now - _lastTypeAheadUtc).TotalMilliseconds > 1100)
            TypeAheadQuery = string.Empty;

        if (ch == ' ' && string.IsNullOrEmpty(TypeAheadQuery))
            return null;

        _lastTypeAheadUtc = now;
        ScheduleTypeAheadReset();

        var count = Items.Count;
        var currentIndex = SelectedItem is not null ? Items.IndexOf(SelectedItem) : -1;

        // Repeating the same single character (e.g. 'i' -> 'i') checks for "ii..." first,
        // otherwise cycles to the next item starting with that character.
        if (TypeAheadQuery.Length == 1 &&
            char.ToUpperInvariant(TypeAheadQuery[0]) == char.ToUpperInvariant(ch))
        {
            var doubled = TypeAheadQuery + ch;
            var doubledMatch = FindPrefixMatch(doubled, startIndex: 0);
            if (doubledMatch is not null)
            {
                TypeAheadQuery = doubled;
                SelectTypeAheadMatch(doubledMatch);
                return doubledMatch;
            }

            var cycleStart = currentIndex >= 0 ? (currentIndex + 1) % count : 0;
            var cycled = FindPrefixMatch(TypeAheadQuery, startIndex: cycleStart);
            if (cycled is not null)
            {
                SelectTypeAheadMatch(cycled);
                return cycled;
            }
            return null;
        }

        var candidate = TypeAheadQuery + ch;
        var searchStart = string.IsNullOrEmpty(TypeAheadQuery) || currentIndex < 0 ? 0 : currentIndex;
        var match = FindPrefixMatch(candidate, startIndex: searchStart);
        if (match is not null)
        {
            TypeAheadQuery = candidate;
            SelectTypeAheadMatch(match);
            return match;
        }

        return null;
    }

    private FileSystemItem? FindPrefixMatch(string prefix, int startIndex)
    {
        var count = Items.Count;
        for (var offset = 0; offset < count; offset++)
        {
            var item = Items[(startIndex + offset) % count];
            if (item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    private void SelectTypeAheadMatch(FileSystemItem item)
    {
        SelectedItem = item;
        RequestScrollAndSelect?.Invoke(item);
    }

    private void ScheduleTypeAheadReset()
    {
        _typeAheadResetCts?.Cancel();
        var cts = new CancellationTokenSource();
        _typeAheadResetCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1100, cts.Token);
                if (cts.Token.IsCancellationRequested) return;
                var app = Application.Current;
                if (app?.Dispatcher is not null)
                {
                    await app.Dispatcher.InvokeAsync(() =>
                    {
                        if (!cts.Token.IsCancellationRequested)
                            TypeAheadQuery = string.Empty;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by next keystroke
            }
        });
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _searchCts?.Cancel();
        _typeAheadResetCts?.Cancel();
        StopWatcher();
    }
}

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}
