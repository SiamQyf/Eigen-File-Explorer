using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eigen.Models;
using Eigen.ViewModels;

namespace Eigen.Controls;

public partial class ExplorerPaneView : UserControl
{
    private Point _dragStartPoint;
    private bool _isDraggingCandidate;

    private bool _isMarqueePending;
    private bool _isMarqueeActive;
    private bool _marqueeCtrlHeld;
    private ListBox? _marqueeListBox;
    private ScrollViewer? _marqueeScrollViewer;
    private Point _marqueeStartViewport;
    private double _marqueeStartScrollH;
    private double _marqueeStartScrollV;
    private HashSet<FileSystemItem> _marqueeBaseSelection = [];
    private System.Windows.Threading.DispatcherTimer? _marqueeAutoScrollTimer;
    private Point _marqueeLastViewport;

    private ExplorerPaneViewModel? _hookedVm;
    private MainViewModel? _hookedHostVm;
    private ScrollViewer? _detailsScrollViewer;
    private bool _isSyncingColumns;
    private bool _columnsPreparedThisPass;

    public ExplorerPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnPaneDataContextChanged;
        Loaded += OnPaneLoaded;
        Unloaded += OnPaneUnloaded;

        foreach (var col in new[] { NameCol, DateCol, TypeCol })
        {
            if (col is System.ComponentModel.INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnColumnPropertyChanged;
        }
    }

    private ExplorerPaneViewModel? ViewModel => DataContext as ExplorerPaneViewModel;

    private void OnPaneLoaded(object sender, RoutedEventArgs e)
    {
        EnsureHostViewModelHooked();
        SyncDetailsColumnWidths();
    }

    private void EnsureHostViewModelHooked()
    {
        var host = HostViewModel;
        if (!ReferenceEquals(_hookedHostVm, host))
        {
            if (_hookedHostVm is not null)
                _hookedHostVm.PropertyChanged -= OnHostPropertyChanged;
            _hookedHostVm = host;
            if (_hookedHostVm is not null)
            {
                _hookedHostVm.PropertyChanged += OnHostPropertyChanged;
                DetailsListView.Tag = _hookedHostVm;
            }
        }
    }

    private void OnPaneUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hookedHostVm is not null)
        {
            _hookedHostVm.PropertyChanged -= OnHostPropertyChanged;
            _hookedHostVm = null;
        }
    }

    private void OnHostPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ItemFontSize)
            or nameof(MainViewModel.ItemIconSize)
            or nameof(MainViewModel.IconLeftGap)
            or nameof(MainViewModel.IconTextHorizontalGap)
            or nameof(MainViewModel.LetterSpacing)
            or nameof(MainViewModel.FontWeightValue)
            or nameof(MainViewModel.SelectedFontOption)
            or nameof(MainViewModel.DetailsNameColWidth))
        {
            SyncDetailsColumnWidths();
        }
    }

    private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_isSyncingColumns && e.PropertyName == nameof(GridViewColumn.Width))
        {
            SyncDetailsSizeColumnWidth();
            if (ViewModel is { IsHome: false, IsDeepSearch: false } pane &&
                !double.IsNaN(NameCol.Width) && !double.IsNaN(DateCol.Width) && !double.IsNaN(TypeCol.Width))
            {
                _hookedHostVm?.SaveFolderColumnWidths(pane.CurrentPath, NameCol.Width, DateCol.Width, TypeCol.Width);
            }
        }
    }

    private void OnDetailsListViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 0.5)
            SyncDetailsSizeColumnWidth();
    }

    private void OnDetailsListViewScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ViewportWidthChange) > 0.5)
            SyncDetailsSizeColumnWidth();
    }

    private void SyncDetailsColumnWidths(IReadOnlyList<FileSystemItem>? itemsOverride = null)
    {
        EnsureHostViewModelHooked();
        if (_hookedHostVm is null)
        {
            SyncDetailsSizeColumnWidth();
            return;
        }

        _isSyncingColumns = true;
        try
        {
            if (ViewModel is { IsHome: false, IsDeepSearch: false } pane &&
                _hookedHostVm.TryGetFolderColumnWidths(pane.CurrentPath, out var saved) &&
                saved is not null)
            {
                NameCol.Width = Math.Max(120, saved.Name);
                DateCol.Width = Math.Max(80, saved.Date);
                TypeCol.Width = Math.Max(80, saved.Type);
            }
            else
            {
                NameCol.Width = _hookedHostVm.CalculateDetailsNameColumnWidth(itemsOverride ?? ViewModel?.Items);
                DateCol.Width = _hookedHostVm.DetailsDateColWidth;
                TypeCol.Width = _hookedHostVm.DetailsTypeColWidth;
            }
        }
        finally
        {
            _isSyncingColumns = false;
        }

        SyncDetailsSizeColumnWidth();
    }

    private void SyncDetailsSizeColumnWidth()
    {
        if (_isSyncingColumns) return;
        _isSyncingColumns = true;
        try
        {
            var minSizeCol = _hookedHostVm?.DetailsSizeColWidth ?? 96.0;
            _detailsScrollViewer ??= FindDescendant<ScrollViewer>(DetailsListView);
            var availableWidth = _detailsScrollViewer is not null && _detailsScrollViewer.ViewportWidth > 0
                ? _detailsScrollViewer.ViewportWidth
                : (DetailsListView.ActualWidth > 0 ? DetailsListView.ActualWidth - 12.0 : 0.0);

            if (availableWidth > 0)
            {
                var usedWidth = NameCol.Width + DateCol.Width + TypeCol.Width;
                var remaining = Math.Floor(availableWidth - 2.0 - usedWidth);
                SizeCol.Width = Math.Max(minSizeCol, remaining);
            }
            else
            {
                SizeCol.Width = minSizeCol;
            }
        }
        finally
        {
            _isSyncingColumns = false;
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) return typed;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void OnPaneItemsPreparing(IReadOnlyList<FileSystemItem> items)
    {
        SyncDetailsColumnWidths(items);
        _columnsPreparedThisPass = true;
    }

    private void OnPaneItemsUpdated()
    {
        if (_columnsPreparedThisPass)
        {
            _columnsPreparedThisPass = false;
            return;
        }
        SyncDetailsColumnWidths();
    }

    private void OnPaneDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_hookedVm is not null)
        {
            _hookedVm.RequestInlineRename -= OnRequestInlineRename;
            _hookedVm.RequestScrollAndSelect -= OnRequestScrollAndSelect;
            _hookedVm.ItemsPreparing -= OnPaneItemsPreparing;
            _hookedVm.ItemsUpdated -= OnPaneItemsUpdated;
        }

        _hookedVm = e.NewValue as ExplorerPaneViewModel;
        if (_hookedVm is not null)
        {
            _hookedVm.RequestInlineRename += OnRequestInlineRename;
            _hookedVm.RequestScrollAndSelect += OnRequestScrollAndSelect;
            _hookedVm.ItemsPreparing += OnPaneItemsPreparing;
            _hookedVm.ItemsUpdated += OnPaneItemsUpdated;
            SyncDetailsColumnWidths();
        }
    }

    private ListBox GetActiveItemsListBox() => ViewModel?.ViewMode switch
    {
        ExplorerViewMode.Tiles => TilesListBox,
        ExplorerViewMode.Compact => CompactListBox,
        _ => DetailsListView
    };

    private void OnRequestInlineRename(FileSystemItem item)
    {
        if (ViewModel is null) return;
        GetActiveItemsListBox().ScrollIntoView(item);
    }

    private void OnRequestScrollAndSelect(FileSystemItem item)
    {
        if (ViewModel is null) return;
        var activeList = GetActiveItemsListBox();
        if (activeList.SelectedItems.Count > 1 || !Equals(activeList.SelectedItem, item))
        {
            activeList.SelectedItems.Clear();
            activeList.SelectedItem = item;
        }
        activeList.ScrollIntoView(item);

        Dispatcher.BeginInvoke(() =>
        {
            if (activeList.ItemContainerGenerator.ContainerFromItem(item) is UIElement container &&
                Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
            {
                container.Focus();
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private MainViewModel? HostViewModel
        => Window.GetWindow(this)?.DataContext as MainViewModel;

    public void FocusAddressBar()
    {
        if (ViewModel is null) return;
        ViewModel.Activate();
        ViewModel.BeginAddressEdit();
        Dispatcher.BeginInvoke(() =>
        {
            AddressTextBox.Focus();
            AddressTextBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    public void FocusSearchBox()
    {
        if (ViewModel is null) return;
        ViewModel.Activate();
        Dispatcher.BeginInvoke(() =>
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        });
    }

    private void OnPanePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.Activate();
    }

    private void OnAddressBarPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is null || ViewModel.IsAddressEditing)
            return;

        if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) is not null)
            return;

        FocusAddressBar();
        e.Handled = true;
    }

    private void OnBreadcrumbSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BreadcrumbSegment seg } && ViewModel is not null)
            _ = ViewModel.NavigateToAsync(seg.FullPath);
    }

    private void OnBreadcrumbChevronClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not BreadcrumbSegment seg || ViewModel is null)
            return;

        ViewModel.LoadBreadcrumbSiblings(seg);
        if (seg.SiblingFolders.Count == 0) return;

        var menu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        foreach (var sibling in seg.SiblingFolders)
        {
            var mi = new MenuItem { Header = sibling.Name };
            var target = sibling.FullPath;
            mi.Click += (_, _) => _ = ViewModel.NavigateToAsync(target);
            menu.Items.Add(mi);
        }

        menu.IsOpen = true;
    }

    private void OnAddressTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.Key == Key.Enter)
        {
            ViewModel.CommitAddressEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.CancelAddressEdit();
            e.Handled = true;
        }
    }

    private void OnAddressTextBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is System.Windows.Controls.MenuItem or System.Windows.Controls.ContextMenu)
            return;
        ViewModel?.CancelAddressEdit();
    }

    private void OnSearchTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;
        if (e.Key == Key.Enter)
        {
            if (!ViewModel.IsDeepSearch && !string.IsNullOrWhiteSpace(ViewModel.SearchQuery))
                ViewModel.IsDeepSearch = true;
            else
                _ = ViewModel.RunDeepSearchAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.ClearSearch();
            e.Handled = true;
        }
    }

    private void OnDismissErrorClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
            ViewModel.ErrorMessage = null;
    }

    private void OnDriveCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DriveCardItem drive } && ViewModel is not null)
            _ = ViewModel.NavigateToAsync(drive.RootPath);
    }

    private void OnDriveCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: DriveCardItem drive })
        {
            HostViewModel?.OpenNewTab(drive.RootPath);
            e.Handled = true;
        }
    }

    private void OnHomePinnedCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SidebarNode node } && ViewModel is not null)
            _ = ViewModel.NavigateToAsync(node.Path);
    }

    private void OnHomePinnedCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: SidebarNode node })
        {
            HostViewModel?.OpenNewTab(node.Path);
            e.Handled = true;
        }
    }

    private void OnRecentLocationClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string path } && ViewModel is not null)
            _ = ViewModel.NavigateToAsync(path);
    }

    private void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && ViewModel is not null)
            ViewModel.SyncSelectedItems(lb.SelectedItems.OfType<FileSystemItem>());
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindParent<TextBox>(dep) is not null)
            return;

        if (e.OriginalSource is DependencyObject src && FindParent<ListBoxItem>(src)?.DataContext is FileSystemItem item)
        {
            ViewModel?.OpenItem(item);
            e.Handled = true;
        }
    }

    private void OnItemsMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            e.OriginalSource is DependencyObject src &&
            FindParent<ListBoxItem>(src)?.DataContext is FileSystemItem { IsDirectory: true } dir)
        {
            HostViewModel?.OpenNewTab(dir.FullPath);
            e.Handled = true;
        }
    }

    private void OnItemsKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;

        if (e.Key == Key.Enter && ViewModel?.SelectedItem is not null)
        {
            ViewModel.OpenItem(ViewModel.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Back && ViewModel is { IsHome: false })
        {
            ViewModel.GoUp();
            e.Handled = true;
        }
    }

    private void OnGridViewColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Tag: string tag } &&
            Enum.TryParse<SortField>(tag, out var sortField))
        {
            ViewModel?.SetSort(sortField);
        }
    }

    // Inline Rename handlers
    private void OnInlineRenameLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
            FocusInlineRenameTextBox(tb);
    }

    private void OnInlineRenameVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
            FocusInlineRenameTextBox(tb);
    }

    private void FocusInlineRenameTextBox(TextBox tb)
    {
        if (tb.DataContext is not FileSystemItem item) return;
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            if (!item.IsDirectory)
            {
                var stemLen = Path.GetFileNameWithoutExtension(item.EditingName).Length;
                tb.Select(0, stemLen > 0 ? stemLen : item.EditingName.Length);
            }
            else
            {
                tb.SelectAll();
            }
        });
    }

    private async void OnInlineRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not FileSystemItem item) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (HostViewModel is not null)
                await HostViewModel.CommitRenameAsync(item, tb.Text);
        }
        else if (e.Key == Key.Escape)
        {
            item.IsEditing = false;
            e.Handled = true;
        }
    }

    private async void OnInlineRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is FileSystemItem { IsEditing: true } item && HostViewModel is not null)
            await HostViewModel.CommitRenameAsync(item, tb.Text);
    }

    private bool IsInDetailsTrailingEmptySpace(ListBox lb, DependencyObject? source, Point posInListBox)
    {
        if (!ReferenceEquals(lb, DetailsListView) || source is null) return false;
        if (FindParent<ListViewItem>(source) is not { DataContext: FileSystemItem item }) return false;

        // Allow clicking anywhere on the Name, Date, or Type columns, plus the Size text area
        var contentLimitX = NameCol.Width + DateCol.Width + TypeCol.Width + (item.IsDirectory ? 24.0 : 72.0);
        var scrollOffset = FindChild<ScrollViewer>(DetailsListView)?.HorizontalOffset ?? 0.0;
        return posInListBox.X + scrollOffset > contentLimitX;
    }

    // Drag & Drop support
    private void OnItemsPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject dep) return;
        if (FindParent<TextBox>(dep) is not null ||
            FindParent<System.Windows.Controls.Primitives.ScrollBar>(dep) is not null ||
            FindParent<GridViewColumnHeader>(dep) is not null)
        {
            return;
        }

        if (sender is ListBox lb)
        {
            var lbi = FindParent<ListBoxItem>(dep);
            var inTrailingVoid = lbi is not null && IsInDetailsTrailingEmptySpace(lb, dep, e.GetPosition(lb));
            if (lbi is null || inTrailingVoid)
            {
                _marqueeCtrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (!_marqueeCtrlHeld)
                {
                    lb.SelectedItems.Clear();
                    lb.SelectedItem = null;
                }
                lb.Focus();
                _isDraggingCandidate = false;
                BeginMarqueeCandidate(lb, e.GetPosition(lb));
                if (inTrailingVoid)
                    e.Handled = true;
                return;
            }
        }

        _dragStartPoint = e.GetPosition(null);
        _isDraggingCandidate = true;
    }

    private void BeginMarqueeCandidate(ListBox lb, Point posInLb)
    {
        _marqueeListBox = lb;
        _marqueeScrollViewer = FindDescendant<ScrollViewer>(lb);
        _marqueeStartViewport = posInLb;
        _marqueeLastViewport = posInLb;
        _marqueeStartScrollH = _marqueeScrollViewer?.HorizontalOffset ?? 0;
        _marqueeStartScrollV = _marqueeScrollViewer?.VerticalOffset ?? 0;
        _marqueeBaseSelection = _marqueeCtrlHeld
            ? [.. lb.SelectedItems.OfType<FileSystemItem>()]
            : [];
        _isMarqueePending = true;
        _isMarqueeActive = false;
    }

    private void OnItemsPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndMarqueeSelection();
    }

    private void OnItemsLostMouseCapture(object sender, MouseEventArgs e)
    {
        EndMarqueeSelection();
    }

    private void EndMarqueeSelection()
    {
        _marqueeAutoScrollTimer?.Stop();
        var lb = _marqueeListBox;
        _isMarqueePending = false;
        _isMarqueeActive = false;
        _marqueeListBox = null;
        _marqueeScrollViewer = null;
        _marqueeBaseSelection.Clear();
        MarqueeCanvas.Visibility = Visibility.Collapsed;

        if (lb is not null && lb.IsMouseCaptured)
            lb.ReleaseMouseCapture();
    }

    private void OnItemsPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb || e.OriginalSource is not DependencyObject dep) return;
        if (FindParent<System.Windows.Controls.Primitives.ScrollBar>(dep) is not null ||
            FindParent<GridViewColumnHeader>(dep) is not null)
        {
            return;
        }

        var lbi = FindParent<ListBoxItem>(dep);
        var inTrailingVoid = lbi is not null && IsInDetailsTrailingEmptySpace(lb, dep, e.GetPosition(lb));
        if (lbi is null || inTrailingVoid)
        {
            lb.SelectedItems.Clear();
            lb.SelectedItem = null;
            if (inTrailingVoid)
                e.Handled = true;
        }
    }

    private void OnItemsPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if ((_isMarqueePending || _isMarqueeActive) && sender is ListBox marqueeLb && ReferenceEquals(marqueeLb, _marqueeListBox))
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndMarqueeSelection();
                return;
            }

            var current = e.GetPosition(marqueeLb);
            _marqueeLastViewport = current;

            if (!_isMarqueeActive)
            {
                if (Math.Abs(current.X - _marqueeStartViewport.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(current.Y - _marqueeStartViewport.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                _isMarqueePending = false;
                _isMarqueeActive = true;
                marqueeLb.CaptureMouse();
                EnsureMarqueeAutoScrollTimer();
                _marqueeAutoScrollTimer!.Start();
            }

            UpdateMarqueeSelection();
            e.Handled = true;
            return;
        }

        if (!_isDraggingCandidate || e.LeftButton != MouseButtonState.Pressed)
        {
            _isDraggingCandidate = false;
            return;
        }

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDraggingCandidate = false;
        if (sender is not ListBox lb || lb.SelectedItems.Count == 0) return;

        var paths = lb.SelectedItems.OfType<FileSystemItem>().Select(i => i.FullPath).ToArray();
        if (paths.Length == 0) return;

        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(lb, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void EnsureMarqueeAutoScrollTimer()
    {
        if (_marqueeAutoScrollTimer is not null) return;
        _marqueeAutoScrollTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _marqueeAutoScrollTimer.Tick += (_, _) =>
        {
            if (!_isMarqueeActive || _marqueeListBox is null || _marqueeScrollViewer is null)
                return;

            var headerH = GetDetailsHeaderHeight(_marqueeListBox);
            var h = _marqueeListBox.ActualHeight;
            double deltaY = 0;
            if (_marqueeLastViewport.Y < headerH + 14)
                deltaY = -Math.Clamp((headerH + 14 - _marqueeLastViewport.Y) * 0.7, 4, 28);
            else if (_marqueeLastViewport.Y > h - 14)
                deltaY = Math.Clamp((_marqueeLastViewport.Y - (h - 14)) * 0.7, 4, 28);

            if (Math.Abs(deltaY) > 0.1)
            {
                var nextOffset = Math.Clamp(_marqueeScrollViewer.VerticalOffset + deltaY, 0, _marqueeScrollViewer.ScrollableHeight);
                if (Math.Abs(nextOffset - _marqueeScrollViewer.VerticalOffset) > 0.1)
                {
                    _marqueeScrollViewer.ScrollToVerticalOffset(nextOffset);
                    UpdateMarqueeSelection();
                }
            }
        };
    }

    private double GetDetailsHeaderHeight(ListBox lb)
    {
        if (!ReferenceEquals(lb, DetailsListView)) return 0;
        var header = FindDescendant<GridViewHeaderRowPresenter>(lb);
        return header is { ActualHeight: > 0 } ? header.ActualHeight : 0;
    }

    private void UpdateMarqueeSelection()
    {
        var lb = _marqueeListBox;
        if (lb is null || !_isMarqueeActive) return;

        var curScrollH = _marqueeScrollViewer?.HorizontalOffset ?? 0;
        var curScrollV = _marqueeScrollViewer?.VerticalOffset ?? 0;
        var anchorX = _marqueeStartViewport.X - (curScrollH - _marqueeStartScrollH);
        var anchorY = _marqueeStartViewport.Y - (curScrollV - _marqueeStartScrollV);

        var headerH = GetDetailsHeaderHeight(lb);
        var maxX = _marqueeScrollViewer is not null && _marqueeScrollViewer.ViewportWidth > 0
            ? _marqueeScrollViewer.ViewportWidth
            : lb.ActualWidth;
        var maxY = lb.ActualHeight;

        var rawLeft = Math.Min(anchorX, _marqueeLastViewport.X);
        var rawRight = Math.Max(anchorX, _marqueeLastViewport.X);
        var rawTop = Math.Min(anchorY, _marqueeLastViewport.Y);
        var rawBottom = Math.Max(anchorY, _marqueeLastViewport.Y);

        var left = Math.Clamp(rawLeft, 0, maxX);
        var right = Math.Clamp(rawRight, 0, maxX);
        var top = Math.Clamp(rawTop, headerH, maxY);
        var bottom = Math.Clamp(rawBottom, headerH, maxY);

        var rectW = Math.Max(0, right - left);
        var rectH = Math.Max(0, bottom - top);

        MarqueeCanvas.Visibility = Visibility.Visible;
        var offsetInCanvas = lb.TranslatePoint(new Point(left, top), MarqueeCanvas);
        Canvas.SetLeft(MarqueeFill, offsetInCanvas.X);
        Canvas.SetTop(MarqueeFill, offsetInCanvas.Y);
        MarqueeFill.Width = rectW;
        MarqueeFill.Height = rectH;

        Canvas.SetLeft(MarqueeBorder, offsetInCanvas.X);
        Canvas.SetTop(MarqueeBorder, offsetInCanvas.Y);
        MarqueeBorder.Width = rectW;
        MarqueeBorder.Height = rectH;

        var marqueeRect = new Rect(left, top, rectW, rectH);
        var hitItems = new HashSet<FileSystemItem>();
        var isDetails = ReferenceEquals(lb, DetailsListView);
        var detailsContentLimitX = isDetails
            ? NameCol.ActualWidth + DateCol.ActualWidth + TypeCol.ActualWidth + SizeCol.ActualWidth + 12
            : double.MaxValue;

        double? estimatedRowHeight = null;
        if (isDetails)
        {
            for (var i = 0; i < lb.Items.Count; i++)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem sample && sample.ActualHeight > 0)
                {
                    estimatedRowHeight = sample.ActualHeight;
                    break;
                }
            }
        }

        for (var i = 0; i < lb.Items.Count; i++)
        {
            if (lb.Items[i] is not FileSystemItem fsItem) continue;

            var hit = false;
            if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container &&
                container.IsVisible && container.ActualWidth > 0 && container.ActualHeight > 0)
            {
                var pt = container.TranslatePoint(new Point(0, 0), lb);
                var rowWidth = isDetails ? Math.Min(container.ActualWidth, detailsContentLimitX) : container.ActualWidth;
                var itemRect = new Rect(pt.X, pt.Y, Math.Max(1, rowWidth), container.ActualHeight);
                hit = itemRect.IntersectsWith(marqueeRect);
            }
            else if (isDetails && estimatedRowHeight is > 0 && curScrollH + rawLeft <= detailsContentLimitX)
            {
                var rowTop = headerH + i * estimatedRowHeight.Value - curScrollV;
                var rowBottom = rowTop + estimatedRowHeight.Value;
                hit = rowBottom >= rawTop && rowTop <= rawBottom;
            }

            if (hit)
                hitItems.Add(fsItem);
        }

        var targetSelection = new HashSet<FileSystemItem>();
        if (_marqueeCtrlHeld)
        {
            foreach (var baseItem in _marqueeBaseSelection)
            {
                if (!hitItems.Contains(baseItem))
                    targetSelection.Add(baseItem);
            }
            foreach (var hitItem in hitItems)
            {
                if (!_marqueeBaseSelection.Contains(hitItem))
                    targetSelection.Add(hitItem);
            }
        }
        else
        {
            targetSelection = hitItems;
        }

        // Diff-apply selection to avoid flickering and redundant SelectionChanged storms
        var toRemove = lb.SelectedItems.OfType<FileSystemItem>().Where(x => !targetSelection.Contains(x)).ToList();
        foreach (var rem in toRemove)
            lb.SelectedItems.Remove(rem);

        foreach (var add in targetSelection)
        {
            if (!lb.SelectedItems.Contains(add))
                lb.SelectedItems.Add(add);
        }
    }

    private void OnPaneDragOver(object sender, DragEventArgs e)
    {
        if (ViewModel is { IsHome: false, IsDeepSearch: false } && e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = ResolveDropEffect(e, ViewModel.CurrentPath);
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnPaneDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is { IsHome: false, IsDeepSearch: false } &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            HostViewModel is not null)
        {
            var isMove = ResolveDropEffect(e, ViewModel.CurrentPath) == DragDropEffects.Move;
            await HostViewModel.HandleDropAsync(files, ViewModel.CurrentPath, isMove);
            e.Handled = true;
        }
    }

    private void OnFolderItemDragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileSystemItem { IsDirectory: true } folder } &&
            e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = ResolveDropEffect(e, folder.FullPath);
            e.Handled = true;
        }
    }

    private async void OnFolderItemDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileSystemItem { IsDirectory: true } folder } &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            HostViewModel is not null)
        {
            var isMove = ResolveDropEffect(e, folder.FullPath) == DragDropEffects.Move;
            await HostViewModel.HandleDropAsync(files, folder.FullPath, isMove);
            e.Handled = true;
        }
    }

    private static DragDropEffects ResolveDropEffect(DragEventArgs e, string targetFolder)
    {
        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
            return DragDropEffects.Copy;
        if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0)
            return DragDropEffects.Move;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var sameDrive = string.Equals(
                Path.GetPathRoot(files[0]),
                Path.GetPathRoot(targetFolder),
                StringComparison.OrdinalIgnoreCase);
            return sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
        }
        return DragDropEffects.Copy;
    }

    // Context Menu clicks
    private void OnContextOpenClick(object sender, RoutedEventArgs e)
        => ViewModel?.OpenItem(ViewModel.SelectedItem);

    private void OnContextOpenNewTabClick(object sender, RoutedEventArgs e)
        => HostViewModel?.OpenInNewTabCommand.Execute(ViewModel?.SelectedItem);

    private void OnContextOpenSplitPaneClick(object sender, RoutedEventArgs e)
        => HostViewModel?.OpenInSplitPaneCommand.Execute(ViewModel?.SelectedItem);

    private void OnContextCutClick(object sender, RoutedEventArgs e)
        => HostViewModel?.CutCommand.Execute(null);

    private void OnContextCopyClick(object sender, RoutedEventArgs e)
        => HostViewModel?.CopyCommand.Execute(null);

    private void OnContextCopyPathClick(object sender, RoutedEventArgs e)
        => HostViewModel?.CopyPathCommand.Execute(null);

    private void OnContextCopyNameClick(object sender, RoutedEventArgs e)
        => HostViewModel?.CopyNameCommand.Execute(null);

    private void OnContextCopyOtherPaneClick(object sender, RoutedEventArgs e)
        => HostViewModel?.CopyToOtherPaneCommand.Execute(null);

    private void OnContextMoveOtherPaneClick(object sender, RoutedEventArgs e)
        => HostViewModel?.MoveToOtherPaneCommand.Execute(null);

    private void OnContextPinClick(object sender, RoutedEventArgs e)
        => HostViewModel?.PinToSidebarCommand.Execute(ViewModel?.SelectedItem);

    private void OnContextRenameClick(object sender, RoutedEventArgs e)
        => HostViewModel?.RenameCommand.Execute(null);

    private void OnContextDeleteClick(object sender, RoutedEventArgs e)
        => HostViewModel?.DeleteCommand.Execute(null);

    private void OnContextPermanentDeleteClick(object sender, RoutedEventArgs e)
        => HostViewModel?.PermanentDeleteCommand.Execute(null);

    private void OnContextTerminalClick(object sender, RoutedEventArgs e)
        => HostViewModel?.OpenInTerminalCommand.Execute(null);

    private void OnContextWinExplorerClick(object sender, RoutedEventArgs e)
        => HostViewModel?.OpenInWindowsExplorerCommand.Execute(null);

    private void OnContextPropertiesClick(object sender, RoutedEventArgs e)
        => HostViewModel?.ShowPropertiesCommand.Execute(null);

    private void OnBackgroundContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var host = HostViewModel;
        var pane = ViewModel;

        if (LogicalTreeHelper.FindLogicalNode(menu, "BgViewDetailsItem") is MenuItem viewDetails)
            viewDetails.IsChecked = pane?.ViewMode == ExplorerViewMode.Details;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgViewTilesItem") is MenuItem viewTiles)
            viewTiles.IsChecked = pane?.ViewMode == ExplorerViewMode.Tiles;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgViewCompactItem") is MenuItem viewCompact)
            viewCompact.IsChecked = pane?.ViewMode == ExplorerViewMode.Compact;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgShowHiddenItem") is MenuItem showHidden)
            showHidden.IsChecked = host?.ShowHiddenFiles == true;

        if (LogicalTreeHelper.FindLogicalNode(menu, "BgSortNameItem") is MenuItem sortName)
            sortName.IsChecked = pane?.SortField == SortField.Name;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgSortDateItem") is MenuItem sortDate)
            sortDate.IsChecked = pane?.SortField == SortField.DateModified;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgSortTypeItem") is MenuItem sortType)
            sortType.IsChecked = pane?.SortField == SortField.Type;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgSortSizeItem") is MenuItem sortSize)
            sortSize.IsChecked = pane?.SortField == SortField.Size;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgSortAscItem") is MenuItem sortAsc)
            sortAsc.IsChecked = pane?.SortAscending == true;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgSortDescItem") is MenuItem sortDesc)
            sortDesc.IsChecked = pane?.SortAscending == false;

        if (LogicalTreeHelper.FindLogicalNode(menu, "BgPasteItem") is MenuItem pasteItem)
            pasteItem.IsEnabled = host?.PasteCommand.CanExecute(null) == true;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgUndoItem") is MenuItem undoItem)
        {
            undoItem.Header = host?.UndoLabel ?? "Undo";
            undoItem.IsEnabled = host?.CanUndo == true;
        }
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgGitBashItem") is MenuItem gitBashItem)
            gitBashItem.Visibility = Eigen.Services.NativeShellService.HasGitBash() ? Visibility.Visible : Visibility.Collapsed;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgNewMenuItem") is MenuItem newItem)
            newItem.IsEnabled = host?.NewFolderCommand.CanExecute(null) == true;
        if (LogicalTreeHelper.FindLogicalNode(menu, "BgPropertiesItem") is MenuItem propsItem)
            propsItem.IsEnabled = pane is { IsHome: false };
    }

    private void OnBgViewClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string mode })
            HostViewModel?.SetViewModeCommand.Execute(mode);
    }

    private void OnBgToggleHiddenClick(object sender, RoutedEventArgs e)
        => HostViewModel?.ToggleHiddenFilesCommand.Execute(null);

    private void OnBgSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string field })
            ViewModel?.SortByCommand.Execute(field);
    }

    private void OnBgSortOrderClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null || sender is not MenuItem { Tag: string dir }) return;
        ViewModel.SortAscending = string.Equals(dir, "Asc", StringComparison.OrdinalIgnoreCase);
    }

    private void OnBgUndoClick(object sender, RoutedEventArgs e)
        => HostViewModel?.UndoCommand.Execute(null);

    private void OnBgTerminalClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { IsHome: false } pane)
            Eigen.Services.NativeShellService.OpenInTerminal(pane.CurrentPath);
        else
            HostViewModel?.OpenInTerminalCommand.Execute(null);
    }

    private void OnBgGitBashClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { IsHome: false } pane)
            Eigen.Services.NativeShellService.OpenInGitBash(pane.CurrentPath);
    }

    private void OnBgWinExplorerClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { IsHome: false } pane)
            Eigen.Services.NativeShellService.OpenInExplorer(pane.CurrentPath);
    }

    private void OnBgPropertiesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { IsHome: false } pane)
            Eigen.Services.NativeShellService.ShowProperties(pane.CurrentPath);
    }

    private void OnBgNewSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem newItem || ViewModel is not { IsHome: false, IsDeepSearch: false } pane)
            return;

        var hostMenu = FindParent<ContextMenu>(newItem);
        if (hostMenu is null) return;

        var existingPaths = pane.Items
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var menuFontSize = HostViewModel is not null
            ? Math.Clamp(HostViewModel.ItemFontSize - 1.5, 12.5, 15.0)
            : 13.0;

        Eigen.Services.NativeShellService.PopulateShellNewMenuItems(
            hostMenu,
            newItem.Items,
            this,
            pane.CurrentPath,
            onNewFolderRequested: () => HostViewModel?.NewFolderCommand.Execute(null),
            onNewItemCreated: () => _ = RefreshAndRenameNewItemAsync(pane, existingPaths),
            uiFontWeight: HostViewModel?.UiFontWeight,
            fontSize: menuFontSize,
            letterSpacing: HostViewModel?.LetterSpacing ?? 0.0);
    }

    private async Task RefreshAndRenameNewItemAsync(ExplorerPaneViewModel pane, HashSet<string> existingPaths)
    {
        ExplorerPaneViewModel.InvalidateFolderCache(pane.CurrentPath);
        await pane.RefreshAsync();

        var created = pane.Items.FirstOrDefault(i => !existingPaths.Contains(i.FullPath));
        if (created is not null)
        {
            pane.SelectedItem = created;
            created.EditingName = created.Name;
            created.IsEditing = true;
        }
    }

    private void OnBgNewFolderClick(object sender, RoutedEventArgs e)
        => HostViewModel?.NewFolderCommand.Execute(null);

    private void OnBgNewTextFileClick(object sender, RoutedEventArgs e)
        => HostViewModel?.NewTextFileCommand.Execute(null);

    private void OnBgNewMarkdownFileClick(object sender, RoutedEventArgs e)
        => HostViewModel?.NewMarkdownFileCommand.Execute(null);

    private void OnBgPasteClick(object sender, RoutedEventArgs e)
        => HostViewModel?.PasteCommand.Execute(null);

    private void OnItemsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBox lb) return;
        if (e.OriginalSource is not DependencyObject src ||
            FindParent<ListBoxItem>(src) is not { DataContext: FileSystemItem clickedItem } lbi ||
            (e.CursorLeft >= 0 && e.CursorTop >= 0 && IsInDetailsTrailingEmptySpace(lb, src, Mouse.GetPosition(lb))))
        {
            return; // Empty space -> let PaneBackgroundContextMenu open
        }

        if (!lbi.IsSelected)
        {
            lb.SelectedItems.Clear();
            lbi.IsSelected = true;
            lb.SelectedItem = clickedItem;
        }

        var paths = lb.SelectedItems
            .OfType<FileSystemItem>()
            .Select(i => i.FullPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0 && !string.IsNullOrWhiteSpace(clickedItem.FullPath))
            paths = [clickedItem.FullPath];

        if (paths.Length == 0) return;

        var screenPt = e.CursorLeft >= 0 && e.CursorTop >= 0
            ? lb.PointToScreen(Mouse.GetPosition(lb))
            : lbi.PointToScreen(new Point(24, lbi.ActualHeight / 2));

        var menuFontSize = HostViewModel is not null
            ? Math.Clamp(HostViewModel.ItemFontSize - 1.5, 12.5, 15.0)
            : 13.0;
        var shown = Eigen.Services.NativeShellService.ShowShellContextMenu(
            lb,
            paths,
            screenPt,
            verb => InterceptShellVerb(verb, clickedItem, paths),
            OnShellVerbInvoked,
            HostViewModel?.UiFontWeight,
            menuFontSize,
            HostViewModel?.LetterSpacing ?? 0.0);

        if (shown)
        {
            e.Handled = true;
        }
        else if (TryFindResource("ItemContextMenu") is ContextMenu fallbackMenu)
        {
            fallbackMenu.PlacementTarget = lbi;
            fallbackMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private bool InterceptShellVerb(string verb, FileSystemItem clickedItem, IReadOnlyList<string> paths)
    {
        switch (verb.ToLowerInvariant())
        {
            case "open":
                if (paths.Count == 1 && clickedItem.IsDirectory)
                {
                    ViewModel?.OpenItem(clickedItem);
                    return true;
                }
                return false;
            case "rename":
                HostViewModel?.RenameCommand.Execute(null);
                return true;
            case "delete":
                HostViewModel?.DeleteCommand.Execute(null);
                return true;
            case "cut":
                HostViewModel?.CutCommand.Execute(null);
                return true;
            case "copy":
                HostViewModel?.CopyCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private void OnShellVerbInvoked(string verb)
    {
        if (string.Equals(verb, "properties", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(verb, "copyaspath", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.BeginInvoke(async () =>
        {
            if (ViewModel is { IsHome: false } pane)
                await pane.RefreshAsync();
        });
    }

    private void OnBgRefreshClick(object sender, RoutedEventArgs e)
        => ViewModel?.RefreshCommand.Execute(null);

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed) return typed;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static T? FindChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var nested = FindChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
