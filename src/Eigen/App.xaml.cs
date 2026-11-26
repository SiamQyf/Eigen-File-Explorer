using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eigen.Models;
using Eigen.Services;

namespace Eigen;

public partial class App : Application
{
    private ResourceDictionary? _currentThemeDict;

    private const double ScrollSpeedMultiplier = 0.6; // 40% slower than default
    private static readonly ConditionalWeakTable<ScrollViewer, SmoothScrollState> SmoothScrollStates = new();
    private static readonly List<WeakReference<ScrollViewer>> ActiveScrollViewers = [];
    private static readonly Stopwatch ScrollClock = Stopwatch.StartNew();
    private static long _lastScrollTickMs;
    private static bool _isScrollAnimating;

    private sealed class SmoothScrollState
    {
        public double CurrentVertical;
        public double TargetVertical;
        public double LastAppliedVertical;
        public bool HasVerticalTarget;

        public double CurrentHorizontal;
        public double TargetHorizontal;
        public double LastAppliedHorizontal;
        public bool HasHorizontalTarget;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            var profileDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Eigen");
            System.IO.Directory.CreateDirectory(profileDir);
            System.Runtime.ProfileOptimization.SetProfileRoot(profileDir);
            System.Runtime.ProfileOptimization.StartProfile("Eigen.startup.profile");
        }
        catch
        {
            // Ignore profile optimization failures on restricted environments
        }

        // Pre-warm font catalog on a background core while MainWindow XAML initializes
        _ = Task.Run(() => FontCatalogService.GetInstalledFonts());

        var initialPath = e.Args.Length > 0 ? e.Args[0] : null;
        if (TryForwardLaunchToExistingInstance(initialPath))
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnGlobalScrollViewerPreviewMouseWheel));

        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnGlobalComboBoxPreviewMouseWheel));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnGlobalScrollViewerStopInertia));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(OnGlobalScrollViewerKeyDown));

        var window = new MainWindow(initialPath);
        MainWindow = window;
        window.Show();

        StartWarmLaunchPipeServer();
    }

    private const string WarmLaunchPipeName = "Eigen.WarmLaunch.v1";

    private static bool TryForwardLaunchToExistingInstance(string? initialPath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", WarmLaunchPipeName, PipeDirection.Out);
            client.Connect(25);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(initialPath ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartWarmLaunchPipeServer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        WarmLaunchPipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var rawPath = await reader.ReadLineAsync();
                    var path = string.IsNullOrWhiteSpace(rawPath) ? null : rawPath.Trim();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        var win = new MainWindow(path);
                        win.Show();
                        win.Activate();
                    });
                }
                catch
                {
                    await Task.Delay(100);
                }
            }
        });
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogSafeException("Dispatcher", e.Exception);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogSafeException("Task", e.Exception);
        e.SetObserved();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogSafeException("Domain", ex);
    }

    private static void LogSafeException(string source, Exception ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Eigen");
            System.IO.Directory.CreateDirectory(dir);
            var logPath = System.IO.Path.Combine(dir, "crash.log");
            if (System.IO.File.Exists(logPath) && new System.IO.FileInfo(logPath).Length > 512 * 1024)
                System.IO.File.Delete(logPath);
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\r\n");
        }
        catch
        {
            // Never throw from exception handler
        }
    }

    private static void OnGlobalScrollViewerStopInertia(object sender, MouseButtonEventArgs e)
    {
        if (sender is ScrollViewer sv && SmoothScrollStates.TryGetValue(sv, out var state))
        {
            if (e.OriginalSource is DependencyObject dep &&
                FindParentScrollBar(dep) is not null)
            {
                state.HasVerticalTarget = false;
                state.HasHorizontalTarget = false;
            }
        }
    }

    private static void OnGlobalScrollViewerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down &&
            sender is ScrollViewer sv &&
            SmoothScrollStates.TryGetValue(sv, out var state))
        {
            state.HasVerticalTarget = false;
            state.HasHorizontalTarget = false;
        }
    }

    private static System.Windows.Controls.Primitives.ScrollBar? FindParentScrollBar(DependencyObject? dep)
    {
        while (dep is not null)
        {
            if (dep is System.Windows.Controls.Primitives.ScrollBar sb)
                return sb;
            dep = VisualTreeHelper.GetParent(dep);
        }
        return null;
    }

    private static void OnGlobalComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || sender is not ComboBox cb || !cb.IsDropDownOpen)
            return;

        if (cb.Template?.FindName("DropDownScrollViewer", cb) is ScrollViewer dropDownSv &&
            dropDownSv.ScrollableHeight > 0)
        {
            var step = (e.Delta / 120.0) * Math.Max(1, SystemParameters.WheelScrollLines) * 18.0;
            dropDownSv.ScrollToVerticalOffset(Math.Clamp(dropDownSv.VerticalOffset - step, 0, dropDownSv.ScrollableHeight));
            e.Handled = true;
        }
    }

    private static T? FindVisualOrLogicalAncestor<T>(DependencyObject? dep) where T : DependencyObject
    {
        while (dep is not null)
        {
            if (dep is T typed) return typed;
            dep = VisualTreeHelper.GetParent(dep) ?? LogicalTreeHelper.GetParent(dep);
        }
        return null;
    }

    private static void OnGlobalScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || sender is not ScrollViewer sv)
            return;

        if (sv.Name == "DropDownScrollViewer")
            return;

        if (e.OriginalSource is DependencyObject orig)
        {
            if (FindVisualOrLogicalAncestor<ComboBox>(orig) is { IsDropDownOpen: true })
                return;

            var innerSv = FindVisualOrLogicalAncestor<ScrollViewer>(orig);
            if (innerSv is not null && !ReferenceEquals(innerSv, sv) &&
                (innerSv.Name == "DropDownScrollViewer" || innerSv.ScrollableHeight > 0))
            {
                return;
            }
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            return;

        var notches = e.Delta / 120.0;
        var wheelLines = Math.Max(1, SystemParameters.WheelScrollLines);
        var state = SmoothScrollStates.GetOrCreateValue(sv);

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            if (sv.ScrollableWidth <= 0) return;

            if (!state.HasHorizontalTarget)
            {
                state.CurrentHorizontal = sv.HorizontalOffset;
                state.TargetHorizontal = sv.HorizontalOffset;
            }

            var hStep = notches * wheelLines * 16.0 * ScrollSpeedMultiplier;
            state.TargetHorizontal = Math.Clamp(state.TargetHorizontal - hStep, 0, sv.ScrollableWidth);
            state.HasHorizontalTarget = true;
            StartSmoothScrollLoop(sv);
            e.Handled = true;
            return;
        }

        if (sv.ScrollableHeight <= 0)
            return;

        if (!state.HasVerticalTarget)
        {
            state.CurrentVertical = sv.VerticalOffset;
            state.TargetVertical = sv.VerticalOffset;
        }

        var isItemMode = sv.CanContentScroll &&
                         VirtualizingPanel.GetScrollUnit(sv.TemplatedParent ?? sv) == ScrollUnit.Item;
        var vStep = isItemMode
            ? notches * wheelLines * ScrollSpeedMultiplier
            : notches * wheelLines * 16.0 * ScrollSpeedMultiplier;

        state.TargetVertical = Math.Clamp(state.TargetVertical - vStep, 0, sv.ScrollableHeight);
        state.HasVerticalTarget = true;
        StartSmoothScrollLoop(sv);
        e.Handled = true;
    }

    private static void StartSmoothScrollLoop(ScrollViewer sv)
    {
        if (!ActiveScrollViewers.Any(w => w.TryGetTarget(out var existing) && ReferenceEquals(existing, sv)))
            ActiveScrollViewers.Add(new WeakReference<ScrollViewer>(sv));

        if (_isScrollAnimating) return;
        _isScrollAnimating = true;
        _lastScrollTickMs = ScrollClock.ElapsedMilliseconds;
        CompositionTarget.Rendering += OnSmoothScrollRendering;
    }

    private static void OnSmoothScrollRendering(object? sender, EventArgs e)
    {
        var nowMs = ScrollClock.ElapsedMilliseconds;
        var dt = Math.Clamp((nowMs - _lastScrollTickMs) / 1000.0, 0.001, 0.05);
        _lastScrollTickMs = nowMs;

        var lerp = 1.0 - Math.Exp(-dt * 10.0);

        for (var i = ActiveScrollViewers.Count - 1; i >= 0; i--)
        {
            if (!ActiveScrollViewers[i].TryGetTarget(out var sv) || !sv.IsLoaded)
            {
                ActiveScrollViewers.RemoveAt(i);
                continue;
            }

            if (!SmoothScrollStates.TryGetValue(sv, out var state))
            {
                ActiveScrollViewers.RemoveAt(i);
                continue;
            }

            var stillActive = false;

            if (state.HasVerticalTarget)
            {
                state.TargetVertical = Math.Clamp(state.TargetVertical, 0, sv.ScrollableHeight);
                var diff = state.TargetVertical - state.CurrentVertical;
                if (Math.Abs(diff) < 0.15)
                {
                    state.CurrentVertical = state.TargetVertical;
                    state.HasVerticalTarget = false;
                }
                else
                {
                    state.CurrentVertical += diff * lerp;
                    stillActive = true;
                }
                state.LastAppliedVertical = state.CurrentVertical;
                sv.ScrollToVerticalOffset(state.CurrentVertical);
            }

            if (state.HasHorizontalTarget)
            {
                state.TargetHorizontal = Math.Clamp(state.TargetHorizontal, 0, sv.ScrollableWidth);
                var diff = state.TargetHorizontal - state.CurrentHorizontal;
                if (Math.Abs(diff) < 0.15)
                {
                    state.CurrentHorizontal = state.TargetHorizontal;
                    state.HasHorizontalTarget = false;
                }
                else
                {
                    state.CurrentHorizontal += diff * lerp;
                    stillActive = true;
                }
                state.LastAppliedHorizontal = state.CurrentHorizontal;
                sv.ScrollToHorizontalOffset(state.CurrentHorizontal);
            }

            if (!stillActive)
                ActiveScrollViewers.RemoveAt(i);
        }

        if (ActiveScrollViewers.Count == 0)
        {
            _isScrollAnimating = false;
            CompositionTarget.Rendering -= OnSmoothScrollRendering;
        }
    }

    public void ApplyTheme(bool isDark)
    {
        var skin = ThemeCatalogService.GetById(isDark
            ? ThemeCatalogService.DefaultDarkSkinId
            : ThemeCatalogService.DefaultLightSkinId);
        ApplySkin(skin);
    }

    public void ApplySkin(EigenSkin skin, string? fontFamilyOverride = null)
    {
        var merged = Resources.MergedDictionaries;
        if (_currentThemeDict is null && merged.Count > 0)
            _currentThemeDict = merged[0];

        var nextDict = ThemeCatalogService.CreateResourceDictionary(skin, fontFamilyOverride);

        if (_currentThemeDict is not null)
        {
            var idx = merged.IndexOf(_currentThemeDict);
            if (idx >= 0)
                merged[idx] = nextDict;
            else
                merged.Insert(0, nextDict);
        }
        else
        {
            merged.Insert(0, nextDict);
        }

        _currentThemeDict = nextDict;

        if (MainWindow is not null)
            NativeShellService.ApplyWindowTheme(MainWindow, skin.IsDark);
    }
}
