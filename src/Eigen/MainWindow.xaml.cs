using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eigen.Controls;
using Eigen.Models;
using Eigen.Services;
using Eigen.ViewModels;

namespace Eigen;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(string? initialPath = null)
    {
        InitializeComponent();

        _viewModel = new MainViewModel(initialPath, deferStartupCatalogs: true);
        DataContext = _viewModel;

        if (_viewModel.Settings.IsMaximized)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            WindowState = WindowState.Maximized;
        }
        else
        {
            Width = _viewModel.Settings.WindowWidth;
            Height = _viewModel.Settings.WindowHeight;
        }

        _viewModel.ThemeChanged += ApplyTheme;
        _viewModel.SkinChanged += ApplySkin;
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.ActiveTab) or nameof(MainViewModel.ActivePane))
            {
                _viewModel.IsFloatingPreviewOpen = false;
                UpdateSplitPaneLayout();
            }
            else if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed))
                UpdateSidebarLayout();
            else if (e.PropertyName == nameof(MainViewModel.IsPreviewPaneOpen))
                UpdatePreviewLayout();
            else if (e.PropertyName == nameof(MainViewModel.IsFloatingPreviewOpen))
                UpdateFloatingPreviewVisual(_viewModel.IsFloatingPreviewOpen);
        };
        Deactivated += (_, _) => _viewModel.IsFloatingPreviewOpen = false;
        PreviewMouseDown += OnWindowPreviewMouseDown;
        PreviewInspectorControl.VideoStateChanged += OnPreviewVideoStateChanged;
        _viewModel.ConfirmAction += (title, message) =>
            MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        ApplySkin(_viewModel.ActiveSkin);
        UpdateMaximizeChrome();
        UpdateSidebarLayout();
        UpdatePreviewLayout();
        UpdateSplitPaneLayout();

        SourceInitialized += (_, _) =>
        {
            NativeShellService.ApplyWindowTheme(this, _viewModel.ActiveSkin.IsDark, _viewModel.ActiveSkin.IsTranslucent);
            NativeShellService.PrepareSmoothWindowEntrance(this);
        };

        Loaded += (_, _) => PlaySmoothOpenAnimation();

        ContentRendered += async (_, _) =>
        {
            await _viewModel.CompleteDeferredStartupAsync();
            await Task.Delay(200);
            var pane = _viewModel.ActivePane;
            if (pane is not null)
            {
                await pane.PrewarmFoldersAsync(
                [
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    NativeShellService.GetKnownDownloadsPath(),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                ]);
            }
        };
    }

    private void PlaySmoothOpenAnimation()
    {
        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
        };
        var fadeDuration = TimeSpan.FromMilliseconds(110);
        var motionDuration = TimeSpan.FromMilliseconds(135);

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.65, 1.0, fadeDuration)
        {
            EasingFunction = ease,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        var scaleX = new System.Windows.Media.Animation.DoubleAnimation(0.992, 1.0, motionDuration)
        {
            EasingFunction = ease,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        var scaleY = new System.Windows.Media.Animation.DoubleAnimation(0.992, 1.0, motionDuration)
        {
            EasingFunction = ease,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        var slideY = new System.Windows.Media.Animation.DoubleAnimation(4.0, 0.0, motionDuration)
        {
            EasingFunction = ease,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };

        RootGrid.BeginAnimation(OpacityProperty, fadeIn);
        WindowOpenScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        WindowOpenScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        WindowOpenTranslate.BeginAnimation(TranslateTransform.YProperty, slideY);
    }

    private void UpdateSidebarLayout()
    {
        if (_viewModel.IsSidebarCollapsed)
        {
            SidebarColumn.MinWidth = 0;
            SidebarColumn.Width = new GridLength(0);
        }
        else
        {
            SidebarColumn.MinWidth = 170;
            SidebarColumn.MaxWidth = 460;
            SidebarColumn.Width = new GridLength(Math.Clamp(_viewModel.Settings.SidebarWidth, 170, 460));
        }
    }

    private void UpdatePreviewLayout()
    {
        if (!_viewModel.IsPreviewPaneOpen)
        {
            PreviewColumn.MinWidth = 0;
            PreviewColumn.Width = new GridLength(0);
        }
        else
        {
            PreviewColumn.MinWidth = 220;
            PreviewColumn.MaxWidth = 520;
            PreviewColumn.Width = new GridLength(Math.Clamp(_viewModel.Settings.PreviewPaneWidth, 220, 520));
        }
    }

    private void OnSidebarSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (!_viewModel.IsSidebarCollapsed && SidebarColumn.ActualWidth > 0)
            _viewModel.Settings.SidebarWidth = Math.Round(Math.Clamp(SidebarColumn.ActualWidth, 170, 460));
    }

    private void OnPreviewSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_viewModel.IsPreviewPaneOpen && PreviewColumn.ActualWidth > 0)
            _viewModel.Settings.PreviewPaneWidth = Math.Round(Math.Clamp(PreviewColumn.ActualWidth, 220, 520));
    }

    private void UpdateSplitPaneLayout()
    {
        var isSplit = _viewModel.ActiveTab?.IsSplitView == true;
        if (isSplit)
        {
            PrimaryPaneColumn.Width = new GridLength(1, GridUnitType.Star);
            SecondaryPaneColumn.MinWidth = 240;
            SecondaryPaneColumn.Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            PrimaryPaneColumn.Width = new GridLength(1, GridUnitType.Star);
            SecondaryPaneColumn.MinWidth = 0;
            SecondaryPaneColumn.Width = new GridLength(0);
        }
    }

    private void ApplyTheme(bool isDark)
    {
        if (Application.Current is App app)
            app.ApplyTheme(isDark);
        else
            NativeShellService.ApplyWindowTheme(this, isDark);
    }

    private string? _lastTaskbarIconSkinId;

    private void ApplySkin(EigenSkin skin)
    {
        if (Application.Current is App app)
            app.ApplySkin(skin, _viewModel.EffectiveFontFamilyName);

        NativeShellService.ApplyWindowTheme(this, skin.IsDark, skin.IsTranslucent);

        if (!string.Equals(_lastTaskbarIconSkinId, skin.Id, StringComparison.OrdinalIgnoreCase))
        {
            _lastTaskbarIconSkinId = skin.Id;
            var icon = NativeShellService.CreateSkinTaskbarIcon(skin);
            if (icon is not null)
                Icon = icon;
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) is not null)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaxRestoreClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnWindowStateChanged(object? sender, EventArgs e) => UpdateMaximizeChrome();

    private void UpdateMaximizeChrome()
    {
        if (WindowState == WindowState.Maximized)
        {
            RootFrameBorder.BorderThickness = new Thickness(0);
            RootGrid.Margin = new Thickness(7);
            MaxRestoreButton.Content = "\uE923";
            MaxRestoreButton.ToolTip = "Restore";
        }
        else
        {
            RootFrameBorder.BorderThickness = new Thickness(1);
            RootGrid.Margin = new Thickness(0);
            MaxRestoreButton.Content = "\uE922";
            MaxRestoreButton.ToolTip = "Maximize";
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Settings.IsMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _viewModel.Settings.WindowWidth = Width;
            _viewModel.Settings.WindowHeight = Height;
        }
        if (!_viewModel.IsSidebarCollapsed && SidebarColumn.ActualWidth > 0)
            _viewModel.Settings.SidebarWidth = Math.Round(Math.Clamp(SidebarColumn.ActualWidth, 170, 460));
        if (_viewModel.IsPreviewPaneOpen && PreviewColumn.ActualWidth > 0)
            _viewModel.Settings.PreviewPaneWidth = Math.Round(Math.Clamp(PreviewColumn.ActualWidth, 220, 520));
        _viewModel.SaveSettings();
        _viewModel.Dispose();
    }

    // Tab Interactions
    private void OnTabPillClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) is not null)
            return;

        if (sender is FrameworkElement { DataContext: ExplorerTabViewModel tab })
            _viewModel.ActiveTab = tab;
    }

    private void OnTabPillMiddleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: ExplorerTabViewModel tab })
        {
            _viewModel.CloseTab(tab);
            e.Handled = true;
        }
    }

    private void OnTabMenuNewClick(object sender, RoutedEventArgs e)
        => _viewModel.NewTabCommand.Execute(null);

    private void OnTabMenuDuplicateClick(object sender, RoutedEventArgs e)
        => _viewModel.DuplicateTabCommand.Execute(GetMenuTab(sender));

    private void OnTabMenuSplitClick(object sender, RoutedEventArgs e)
    {
        if (GetMenuTab(sender) is { } tab)
            tab.ToggleSplitView();
    }

    private void OnTabMenuCloseClick(object sender, RoutedEventArgs e)
        => _viewModel.CloseTabCommand.Execute(GetMenuTab(sender));

    private void OnTabMenuCloseOthersClick(object sender, RoutedEventArgs e)
        => _viewModel.CloseOtherTabsCommand.Execute(GetMenuTab(sender));

    private static ExplorerTabViewModel? GetMenuTab(object sender)
    {
        if (sender is MenuItem mi && FindParent<ContextMenu>(mi)?.PlacementTarget is FrameworkElement fe)
            return fe.DataContext as ExplorerTabViewModel;
        return null;
    }

    // Toolbar Menu Buttons
    private void OnNewMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.DataContext = btn.DataContext;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void OnNewItemMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || _viewModel.ActivePane is not { IsHome: false, IsDeepSearch: false } pane)
            return;

        var existingPaths = pane.Items
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var menuFontSize = Math.Clamp(_viewModel.ItemFontSize - 1.5, 12.5, 15.0);
        NativeShellService.PopulateShellNewMenuItems(
            menu,
            menu.Items,
            this,
            pane.CurrentPath,
            onNewFolderRequested: () => _viewModel.NewFolderCommand.Execute(null),
            onNewItemCreated: () => _ = RefreshCreatedShellItemAsync(pane, existingPaths),
            uiFontWeight: _viewModel.UiFontWeight,
            fontSize: menuFontSize,
            letterSpacing: _viewModel.LetterSpacing);
    }

    private static async Task RefreshCreatedShellItemAsync(ExplorerPaneViewModel pane, HashSet<string> existingPaths)
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

    private void OnSortMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.DataContext = btn.DataContext;
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    // Sidebar Interactions
    private void OnQuickAccessClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SidebarNode node })
            _viewModel.NavigateSidebarCommand.Execute(node);
    }

    private void OnTreeItemBoxClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep &&
            FindParent<System.Windows.Controls.Primitives.ToggleButton>(dep) is not null)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: SidebarNode node })
        {
            if (node.CanExpand)
                node.IsExpanded = !node.IsExpanded;
            _viewModel.NavigateSidebarCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnSidebarMiddleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            sender is FrameworkElement { DataContext: SidebarNode node } &&
            !string.IsNullOrEmpty(node.Path))
        {
            _viewModel.OpenNewTab(node.Path);
            e.Handled = true;
        }
    }

    private void OnSidebarTreeExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: SidebarNode node })
            _viewModel.LoadSidebarNodeChildren(node);
    }

    private void OnSidebarOpenNewTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && FindParent<ContextMenu>(mi)?.PlacementTarget is FrameworkElement { DataContext: SidebarNode node })
            _viewModel.OpenInNewTabCommand.Execute(node);
    }

    private void OnSidebarOpenSplitClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && FindParent<ContextMenu>(mi)?.PlacementTarget is FrameworkElement { DataContext: SidebarNode node })
            _viewModel.OpenInSplitPaneCommand.Execute(node);
    }

    private void OnSidebarUnpinClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && FindParent<ContextMenu>(mi)?.PlacementTarget is FrameworkElement { DataContext: SidebarNode node })
            _viewModel.UnpinFromSidebarCommand.Execute(node);
    }

    private void OnSidebarNodeDragOver(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SidebarNode node } &&
            !node.IsSpecialHome &&
            Directory.Exists(node.Path) &&
            e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = (e.KeyStates & DragDropKeyStates.ControlKey) != 0 ? DragDropEffects.Copy : DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private async void OnSidebarNodeDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: SidebarNode node } &&
                !node.IsSpecialHome &&
                Directory.Exists(node.Path) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var isMove = (e.KeyStates & DragDropKeyStates.ControlKey) == 0;
                await _viewModel.HandleDropAsync(files, node.Path, isMove);
                e.Handled = true;
            }
        }
        catch
        {
            // Ignore unexpected shell drop errors
        }
    }

    // Global Keyboard Shortcuts
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var isTyping = Keyboard.FocusedElement is TextBox;

        var activePaneView = GetActivePaneView();

        if (ctrl && shift && !alt && e.Key == Key.T)
        {
            _viewModel.ReopenClosedTabCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && !alt && e.Key == Key.T)
        {
            _viewModel.NewTabCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && !alt && (e.Key == Key.W || e.Key == Key.F4))
        {
            _viewModel.CloseTabCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && !alt && e.Key == Key.N)
        {
            var path = _viewModel.ActivePane is { IsHome: false } pane ? pane.CurrentPath : null;
            var newWin = new MainWindow(path);
            newWin.Show();
            newWin.Activate();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Tab)
        {
            if (shift) _viewModel.PreviousTabCommand.Execute(null);
            else _viewModel.NextTabCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Oem5) // Ctrl+\
        {
            _viewModel.ToggleSplitViewCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (alt && e.SystemKey == Key.P)
        {
            _viewModel.TogglePreviewPaneCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if ((ctrl && e.Key == Key.L) || (alt && e.SystemKey == Key.D) || (!ctrl && !alt && !shift && e.Key == Key.F4 && !isTyping))
        {
            activePaneView?.FocusAddressBar();
            e.Handled = true;
            return;
        }

        if ((ctrl && (e.Key == Key.F || e.Key == Key.E)) || (!ctrl && !alt && !shift && e.Key == Key.F3))
        {
            activePaneView?.FocusSearchBox();
            e.Handled = true;
            return;
        }

        if (alt && e.SystemKey == Key.Left)
        {
            _viewModel.ActivePane?.GoBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (alt && e.SystemKey == Key.Right)
        {
            _viewModel.ActivePane?.GoForwardCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (alt && e.SystemKey == Key.Up)
        {
            _viewModel.ActivePane?.GoUpCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 || (ctrl && !shift && !alt && e.Key == Key.R))
        {
            _viewModel.ActivePane?.RefreshCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6 && _viewModel.ActiveTab is { IsSplitView: true } splitTab && _viewModel.ActivePane is not null)
        {
            splitTab.GetOtherPane(_viewModel.ActivePane)?.Activate();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.N)
        {
            _viewModel.NewFolderCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.C)
        {
            _viewModel.CopyPathCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.H)
        {
            _viewModel.ToggleHiddenFilesCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (alt && e.SystemKey == Key.Enter)
        {
            _viewModel.ShowPropertiesCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (isTyping) return;

        if (e.Key == Key.Escape && _viewModel.IsFloatingPreviewOpen)
        {
            _viewModel.IsFloatingPreviewOpen = false;
            e.Handled = true;
            return;
        }

        if (_viewModel.IsFloatingPreviewOpen && !ctrl && !alt)
        {
            if (e.Key is Key.OemPlus or Key.Add)
            {
                SetFloatingZoom(_floatingZoom * 1.2);
                e.Handled = true;
                return;
            }
            if (e.Key is Key.OemMinus or Key.Subtract)
            {
                SetFloatingZoom(_floatingZoom / 1.2);
                e.Handled = true;
                return;
            }
            if (e.Key is Key.D0 or Key.NumPad0)
            {
                SetFloatingZoom(1.0);
                e.Handled = true;
                return;
            }
        }

        if (_viewModel.CurrentPreview is { IsVideo: true } && !ctrl && !alt && !IsTextOrMenuFocused())
        {
            if (e.Key == Key.K || (e.Key == Key.Space && (_viewModel.IsFloatingPreviewOpen || _viewModel.ActivePane is not { HasTypeAheadQuery: true })))
            {
                PreviewInspectorControl.ToggleVideoPlayPause();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.M)
            {
                PreviewInspectorControl.ToggleVideoMute();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.J)
            {
                PreviewInspectorControl.SeekVideoRelative(TimeSpan.FromSeconds(-10));
                e.Handled = true;
                return;
            }
            if (e.Key == Key.L)
            {
                PreviewInspectorControl.SeekVideoRelative(TimeSpan.FromSeconds(10));
                e.Handled = true;
                return;
            }
            if (_viewModel.IsFloatingPreviewOpen)
            {
                if (e.Key == Key.Left)
                {
                    PreviewInspectorControl.SeekVideoRelative(TimeSpan.FromSeconds(-5));
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Right)
                {
                    PreviewInspectorControl.SeekVideoRelative(TimeSpan.FromSeconds(5));
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Up)
                {
                    PreviewInspectorControl.AdjustVideoVolume(0.10);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Down)
                {
                    PreviewInspectorControl.AdjustVideoVolume(-0.10);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (e.Key == Key.Escape && _viewModel.ActivePane is { HasTypeAheadQuery: true } activeEscPane)
        {
            activeEscPane.ClearTypeAhead();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && !ctrl && !alt &&
            _viewModel.ActivePane is { IsHome: false, HasTypeAheadQuery: true } spacePane &&
            !IsTextOrMenuFocused())
        {
            spacePane.HandleTypeAheadChar(' ');
            e.Handled = true;
            return;
        }

        if (ctrl && shift)
        {
            if (e.Key is Key.D2 or Key.D3 or Key.D4)
            {
                _viewModel.SetViewModeCommand.Execute(ExplorerViewMode.Tiles);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D5)
            {
                _viewModel.SetViewModeCommand.Execute(ExplorerViewMode.Compact);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.D6)
            {
                _viewModel.SetViewModeCommand.Execute(ExplorerViewMode.Details);
                e.Handled = true;
                return;
            }
        }

        if (((shift && e.Key == Key.F10) || e.Key == Key.Apps) && !ctrl && !alt && !IsTextOrMenuFocused())
        {
            activePaneView?.OpenContextMenuFromKeyboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            _viewModel.RenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || (ctrl && !shift && !alt && e.Key == Key.D))
        {
            if (shift) _viewModel.PermanentDeleteCommand.Execute(null);
            else _viewModel.DeleteCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && !shift && !alt && e.Key == Key.A && !IsTextOrMenuFocused())
        {
            activePaneView?.SelectAllItems();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && !ctrl && !alt && !shift && _viewModel.ActivePane is { IsHome: false } backPane && !IsTextOrMenuFocused())
        {
            if (backPane.CanGoBack) backPane.GoBack();
            else backPane.GoUp();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.C)
        {
            _viewModel.CopyCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.X)
        {
            _viewModel.CutCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.V)
        {
            _viewModel.PasteCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Z)
        {
            _viewModel.UndoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            _viewModel.ActivePane?.GoBackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            _viewModel.ActivePane?.GoForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnWindowPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        if (_viewModel.IsFloatingPreviewOpen) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        if (IsTextOrMenuFocused()) return;

        var pane = _viewModel.ActivePane;
        if (pane is null || pane.IsHome) return;

        var ch = e.Text[0];
        if (char.IsControl(ch)) return;
        if (ch == ' ' && !pane.HasTypeAheadQuery) return;

        pane.HandleTypeAheadChar(ch);
        e.Handled = true;
    }

    private static bool IsTextOrMenuFocused()
    {
        return Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            || Keyboard.FocusedElement is System.Windows.Controls.MenuItem
            || Keyboard.FocusedElement is System.Windows.Controls.ContextMenu;
    }

    private ExplorerPaneView? GetActivePaneView()
    {
        if (_viewModel.ActiveTab is null) return PrimaryPaneView;
        return ReferenceEquals(_viewModel.ActivePane, _viewModel.ActiveTab.SecondaryPane)
            ? SecondaryPaneView
            : PrimaryPaneView;
    }

    private double _floatingZoom = 1.0;
    private ScrollViewer? _panScrollViewer;
    private Point _panStartPoint;
    private double _panStartH;
    private double _panStartV;

    private void SetFloatingZoom(double zoom)
    {
        _floatingZoom = Math.Clamp(zoom, 1.0, 5.0);
        if (Math.Abs(_floatingZoom - 1.0) < 0.03)
            _floatingZoom = 1.0;

        FloatingImageZoom.ScaleX = _floatingZoom;
        FloatingImageZoom.ScaleY = _floatingZoom;
        FloatingVideoZoom.ScaleX = _floatingZoom;
        FloatingVideoZoom.ScaleY = _floatingZoom;
        FloatingTextZoom.ScaleX = _floatingZoom;
        FloatingTextZoom.ScaleY = _floatingZoom;
        FloatingZoomResetButton.Content = $"{Math.Round(_floatingZoom * 100):0}%";

        var panCursor = _floatingZoom > 1.01 ? Cursors.SizeAll : Cursors.Arrow;
        FloatingImageScrollViewer.Cursor = panCursor;
        FloatingVideoScrollViewer.Cursor = panCursor;
    }

    private void OnCloseFloatingPreviewClick(object sender, RoutedEventArgs e) => _viewModel.IsFloatingPreviewOpen = false;

    private void OnFloatingBackdropClick(object sender, MouseButtonEventArgs e) => _viewModel.IsFloatingPreviewOpen = false;

    private void OnFloatingZoomInClick(object sender, RoutedEventArgs e) => SetFloatingZoom(_floatingZoom * 1.25);

    private void OnFloatingZoomOutClick(object sender, RoutedEventArgs e) => SetFloatingZoom(_floatingZoom / 1.25);

    private void OnFloatingZoomResetClick(object sender, RoutedEventArgs e) => SetFloatingZoom(1.0);

    private bool _isSyncingFloatingVideoUi;
    private bool _isFloatingVideoSeeking;

    private void OnPreviewVideoStateChanged(VideoTransportState state)
    {
        _isSyncingFloatingVideoUi = true;
        try
        {
            FloatingVideoPlayPauseButton.Content = state.IsPlaying ? "\uE769" : "\uE768";
            FloatingVideoMuteButton.Content = state.IsMuted ? "\uE74F" : "\uE767";
            FloatingVideoVolumeSlider.Value = state.IsMuted ? 0 : state.Volume;
            FloatingVideoSeekSlider.Maximum = Math.Max(1.0, state.DurationSeconds);
            if (!_isFloatingVideoSeeking)
                FloatingVideoSeekSlider.Value = Math.Clamp(state.PositionSeconds, 0, FloatingVideoSeekSlider.Maximum);
            FloatingVideoTimeText.Text = state.TimeLabel;
        }
        finally
        {
            _isSyncingFloatingVideoUi = false;
        }
    }

    private void OnFloatingVideoSeekStart(object sender, MouseButtonEventArgs e) => _isFloatingVideoSeeking = true;

    private void OnFloatingVideoSeekEnd(object sender, MouseButtonEventArgs e)
    {
        _isFloatingVideoSeeking = false;
        PreviewInspectorControl.SetVideoPositionSeconds(FloatingVideoSeekSlider.Value);
    }

    private void OnFloatingVideoSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingFloatingVideoUi || !_isFloatingVideoSeeking) return;
        PreviewInspectorControl.SetVideoPositionSeconds(e.NewValue);
    }

    private void OnFloatingVideoVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingFloatingVideoUi || !IsLoaded) return;
        PreviewInspectorControl.SetVideoVolume(e.NewValue);
    }

    private void OnFloatingVideoPlayPauseClick(object sender, RoutedEventArgs e) => PreviewInspectorControl.ToggleVideoPlayPause();

    private void OnFloatingVideoBackClick(object sender, RoutedEventArgs e) => PreviewInspectorControl.SeekVideoRelative(TimeSpan.FromSeconds(-10));

    private void OnFloatingVideoForwardClick(object sender, RoutedEventArgs e) => PreviewInspectorControl.SeekVideoRelative(TimeSpan.FromSeconds(10));

    private void OnFloatingVideoMuteClick(object sender, RoutedEventArgs e) => PreviewInspectorControl.ToggleVideoMute();

    private void OnFloatingStagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // On text previews, allow normal scrolling unless Ctrl is held or already zooming
        if (ReferenceEquals(sender, FloatingTextScrollViewer) &&
            (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        var factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        SetFloatingZoom(_floatingZoom * factor);
        e.Handled = true;
    }

    private void OnFloatingPanStart(object sender, MouseButtonEventArgs e)
    {
        if (_floatingZoom <= 1.01 || sender is not ScrollViewer sv) return;
        _panScrollViewer = sv;
        _panStartPoint = e.GetPosition(sv);
        _panStartH = sv.HorizontalOffset;
        _panStartV = sv.VerticalOffset;
        sv.CaptureMouse();
        e.Handled = true;
    }

    private void OnFloatingPanMove(object sender, MouseEventArgs e)
    {
        if (_panScrollViewer is null || !ReferenceEquals(sender, _panScrollViewer)) return;
        var pos = e.GetPosition(_panScrollViewer);
        _panScrollViewer.ScrollToHorizontalOffset(_panStartH - (pos.X - _panStartPoint.X));
        _panScrollViewer.ScrollToVerticalOffset(_panStartV - (pos.Y - _panStartPoint.Y));
    }

    private void OnFloatingPanEnd(object sender, MouseButtonEventArgs e)
    {
        if (_panScrollViewer is null) return;
        _panScrollViewer.ReleaseMouseCapture();
        _panScrollViewer = null;
    }

    private void ApplyFloatingPreviewSize()
    {
        var targetW = Math.Round(SystemParameters.PrimaryScreenWidth * 0.625);
        var targetH = Math.Round(SystemParameters.PrimaryScreenHeight * 0.625);
        if (ActualWidth > 120)
            targetW = Math.Min(targetW, Math.Max(320, ActualWidth - 32));
        if (ActualHeight > 120)
            targetH = Math.Min(targetH, Math.Max(240, ActualHeight - 32));

        FloatingPreviewCard.Width = targetW;
        FloatingPreviewCard.Height = targetH;
    }

    private void UpdateFloatingPreviewVisual(bool isOpen)
    {
        try
        {
            if (isOpen)
            {
                ApplyFloatingPreviewSize();
                SetFloatingZoom(1.0);
            }
            FloatingPreviewOverlay.BeginAnimation(OpacityProperty, null);
            FloatingPreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            FloatingPreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            if (isOpen)
            {
                FloatingPreviewOverlay.Visibility = Visibility.Visible;

                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(135),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };
                var scaleIn = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.93,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(155),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                    }
                };

                FloatingPreviewOverlay.BeginAnimation(OpacityProperty, fadeIn);
                FloatingPreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
                FloatingPreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn.Clone());
            }
            else
            {
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(105),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
                    }
                };
                fadeOut.Completed += (_, _) =>
                {
                    if (!_viewModel.IsFloatingPreviewOpen)
                        FloatingPreviewOverlay.Visibility = Visibility.Collapsed;
                };

                var scaleOut = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = 0.95,
                    Duration = TimeSpan.FromMilliseconds(105)
                };

                FloatingPreviewOverlay.BeginAnimation(OpacityProperty, fadeOut);
                FloatingPreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
                FloatingPreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut.Clone());
            }
        }
        catch
        {
            FloatingPreviewOverlay.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            FloatingPreviewOverlay.Opacity = isOpen ? 1.0 : 0.0;
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed) return typed;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
