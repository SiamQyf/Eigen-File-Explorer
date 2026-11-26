using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Eigen.Services;

public static class NativeShellService
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const uint SHGFI_TYPENAME = 0x000000400;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const int SHIL_EXTRALARGE = 2;
    private const uint ILD_TRANSPARENT = 0x00000001;

    private static readonly Guid IImageListGuid = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOW = 5;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Windows 11 Acrylic
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    private static readonly Guid DownloadsFolderGuid = new("374DE290-123F-4565-9164-39C4925E467B");

    private static readonly ConcurrentDictionary<string, (ImageSource? Icon, string TypeName)> IconCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, string?> AssociatedAppCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> ThirdPartyArchiverCache =
        new(StringComparer.OrdinalIgnoreCase);

    private const uint ASSOCSTR_EXECUTABLE = 2;
    private const uint ASSOCSTR_FRIENDLYAPPNAME = 4;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryStringW(
        uint flags,
        uint str,
        string pszAssoc,
        string? pszExtra,
        [Out] System.Text.StringBuilder? pszOut,
        ref uint pcchOut);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFOW psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW lpExecInfo);

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr pszPath);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

    [DllImport("comctl32.dll")]
    private static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, uint flags);

    public static (ImageSource? Icon, string TypeName) GetIconAndType(string path, bool isDirectory, bool isDrive = false, bool largeIcon = false)
    {
        var ext = isDirectory ? "<DIR>" : Path.GetExtension(path);
        var useRealPath = isDrive || (!isDirectory && (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                                                 || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                                                 || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)
                                                 || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)));

        var cacheKey = $"{(largeIcon ? "L" : "S")}|{(useRealPath ? path : ext)}";
        if (IconCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var shfi = new SHFILEINFOW();
        var flags = SHGFI_ICON | SHGFI_TYPENAME | (largeIcon ? (SHGFI_LARGEICON | SHGFI_SYSICONINDEX) : SHGFI_SMALLICON);
        uint attrs = 0;

        if (!useRealPath)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        }

        var queryTarget = useRealPath ? path : (isDirectory ? "folder" : $"file{ext}");
        var res = SHGetFileInfoW(queryTarget, attrs, ref shfi, (uint)Marshal.SizeOf<SHFILEINFOW>(), flags);
        ImageSource? iconSource = null;

        IntPtr hIconToUse = IntPtr.Zero;
        if (largeIcon && res != IntPtr.Zero && shfi.iIcon >= 0)
        {
            try
            {
                var iid = IImageListGuid;
                if (SHGetImageList(SHIL_EXTRALARGE, ref iid, out var himl) == 0 && himl != IntPtr.Zero)
                {
                    var extraLarge = ImageList_GetIcon(himl, shfi.iIcon, ILD_TRANSPARENT);
                    Marshal.Release(himl);
                    if (extraLarge != IntPtr.Zero)
                    {
                        if (shfi.hIcon != IntPtr.Zero) DestroyIcon(shfi.hIcon);
                        hIconToUse = extraLarge;
                    }
                }
            }
            catch
            {
                hIconToUse = IntPtr.Zero;
            }
        }

        if (hIconToUse == IntPtr.Zero)
            hIconToUse = shfi.hIcon;

        if (hIconToUse != IntPtr.Zero)
        {
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                    hIconToUse,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                iconSource = bmp;
            }
            catch
            {
                iconSource = null;
            }
            finally
            {
                DestroyIcon(hIconToUse);
            }
        }

        var typeName = !string.IsNullOrWhiteSpace(shfi.szTypeName)
            ? shfi.szTypeName
            : (isDirectory ? "File folder" : string.IsNullOrEmpty(ext) ? "File" : $"{ext.TrimStart('.').ToUpperInvariant()} File");

        var value = (iconSource, typeName);
        if (IconCache.Count < 4096)
            IconCache.TryAdd(cacheKey, value);

        return value;
    }

    public static (BitmapSource? Bitmap, bool IsThumbnail) ExtractShellPreviewBitmap(
        string path,
        int pixelSize,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || ct.IsCancellationRequested)
            return (null, false);

        (BitmapSource? Bitmap, bool IsThumbnail) result = (null, false);
        using var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            IShellItemImageFactory? factory = null;
            try
            {
                if (ct.IsCancellationRequested)
                    return;

                var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItemImageFactory, out factory);
                if (hr != 0 || factory is null)
                    return;

                // 1. Try genuine visual thumbnail first (PDF page, video frame, Office doc, SVG, album art, etc.)
                var thumbSize = new NATIVE_SIZE { cx = pixelSize, cy = pixelSize };
                if (factory.GetImage(thumbSize, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out var hThumb) == 0 &&
                    hThumb != IntPtr.Zero)
                {
                    try
                    {
                        if (HBitmapToImageSource(hThumb) is BitmapSource thumbBmp)
                        {
                            result = (thumbBmp, true);
                            return;
                        }
                    }
                    finally
                    {
                        DeleteObject(hThumb);
                    }
                }

                if (ct.IsCancellationRequested)
                    return;

                // 2. Fallback to high-resolution shell icon (up to 256px jumbo)
                var iconPx = Math.Clamp(pixelSize, 48, 256);
                var iconSize = new NATIVE_SIZE { cx = iconPx, cy = iconPx };
                if (factory.GetImage(iconSize, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hIconBmp) == 0 &&
                    hIconBmp != IntPtr.Zero)
                {
                    try
                    {
                        if (HBitmapToImageSource(hIconBmp) is BitmapSource iconBmp)
                            result = (iconBmp, false);
                    }
                    finally
                    {
                        DeleteObject(hIconBmp);
                    }
                }
            }
            catch
            {
                result = (null, false);
            }
            finally
            {
                if (factory is not null)
                {
                    try { Marshal.ReleaseComObject(factory); } catch { }
                }
                done.Set();
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            // Never allow a hung external shell codec/provider to stall cancellation
            WaitHandle.WaitAny([done.WaitHandle, ct.WaitHandle], millisecondsTimeout: 3500);
        }
        catch
        {
            // Return whatever completed cleanly before cancellation
        }

        return ct.IsCancellationRequested ? (null, false) : result;
    }

    public static string? GetAssociatedAppName(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith('.'))
            return null;

        if (AssociatedAppCache.TryGetValue(extension, out var cached))
            return cached;

        string? resolved = null;
        try
        {
            var sb = new System.Text.StringBuilder(260);
            uint len = (uint)sb.Capacity;
            var hr = AssocQueryStringW(0, ASSOCSTR_FRIENDLYAPPNAME, extension, null, sb, ref len);
            if (hr == 0 && sb.Length > 0)
            {
                var candidate = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !candidate.Equals("Pick an app", StringComparison.OrdinalIgnoreCase))
                {
                    resolved = candidate;
                }
            }
        }
        catch
        {
            resolved = null;
        }

        if (AssociatedAppCache.Count < 1024)
            AssociatedAppCache.TryAdd(extension, resolved);

        return resolved;
    }

    public static bool HasThirdPartyArchiverAssociation(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith('.'))
            return false;

        if (ThirdPartyArchiverCache.TryGetValue(extension, out var cached))
            return cached;

        var hasThirdParty = false;
        try
        {
            var sb = new System.Text.StringBuilder(260);
            uint len = (uint)sb.Capacity;
            var hr = AssocQueryStringW(0, ASSOCSTR_EXECUTABLE, extension, null, sb, ref len);
            if (hr == 0 && sb.Length > 0)
            {
                var exeName = Path.GetFileName(sb.ToString().Trim());
                if (!string.IsNullOrWhiteSpace(exeName) &&
                    !exeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                    !exeName.Equals("OpenWith.exe", StringComparison.OrdinalIgnoreCase))
                {
                    hasThirdParty = true;
                }
            }
        }
        catch
        {
            hasThirdParty = false;
        }

        if (ThirdPartyArchiverCache.Count < 1024)
            ThirdPartyArchiverCache.TryAdd(extension, hasThirdParty);

        return hasThirdParty;
    }

    public static string GetKnownDownloadsPath()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsFolderGuid, 0, IntPtr.Zero, out var ptr) == 0 && ptr != IntPtr.Zero)
            {
                try
                {
                    var result = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrWhiteSpace(result) && Directory.Exists(result))
                        return result;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }
        catch
        {
            // Fallback below
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public static void OpenItem(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void ShowProperties(string path)
    {
        var sei = new SHELLEXECUTEINFOW
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFOW>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW
        };
        ShellExecuteExW(ref sei);
    }

    public static void OpenInExplorer(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    public static void OpenInTerminal(string folderPath)
    {
        var targetDir = Directory.Exists(folderPath)
            ? folderPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{targetDir}\"") { UseShellExecute = true });
        }
        catch
        {
            var escaped = targetDir.Replace("'", "''");
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoExit -Command \"Set-Location -LiteralPath '{escaped}'\"")
            {
                UseShellExecute = true
            });
        }
    }

    public static string? FindGitBashPath()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "git-bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "git-bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "git-bash.exe")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool HasGitBash() => FindGitBashPath() is not null;

    public static void OpenInGitBash(string folderPath)
    {
        var gitBash = FindGitBashPath();
        if (gitBash is null) return;

        var targetDir = Directory.Exists(folderPath)
            ? folderPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Process.Start(new ProcessStartInfo(gitBash, $"--cd=\"{targetDir}\"")
        {
            UseShellExecute = true
        });
    }

    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0;
        }
        catch
        {
            // Ignore registry lookup errors
        }
        return true;
    }

    public static ImageSource? CreateSkinTaskbarIcon(Models.EigenSkin skin)
    {
        try
        {
            var accent = (Color)ColorConverter.ConvertFromString(skin.Accent);
            var accentHover = (Color)ColorConverter.ConvertFromString(skin.AccentHover);
            var accentSoft = (Color)ColorConverter.ConvertFromString(skin.AccentSoft);
            var onAccent = (Color)ColorConverter.ConvertFromString(skin.TextOnAccent);

            const int size = 64;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 256.0, size / 256.0));

                var backGeo = Geometry.Parse(
                    "M 34,24 L 98,24 C 110,24 116,29 123,37 L 134,48 C 139,53 145,55 153,55 L 222,55 C 236,55 246,65 246,79 L 246,204 C 246,218 236,228 222,228 L 34,228 C 20,228 10,218 10,204 L 10,48 C 10,34 20,24 34,24 Z");
                var backBrush = new SolidColorBrush(accentHover);
                dc.DrawGeometry(backBrush, null, backGeo);

                var sheetBrush = new SolidColorBrush(accentSoft);
                dc.DrawRoundedRectangle(sheetBrush, null, new Rect(24, 68, 208, 48), 14, 14);

                var frontBrush = new LinearGradientBrush(
                    Color.FromArgb(255, (byte)Math.Min(255, accent.R + 30), (byte)Math.Min(255, accent.G + 30), (byte)Math.Min(255, accent.B + 30)),
                    accentHover,
                    new Point(0.1, 0),
                    new Point(0.9, 1));
                dc.DrawRoundedRectangle(frontBrush, null, new Rect(10, 84, 236, 148), 24, 24);

                var rimBrush = new SolidColorBrush(Color.FromArgb(145, onAccent.R, onAccent.G, onAccent.B));
                dc.DrawRoundedRectangle(rimBrush, null, new Rect(34, 85, 188, 4), 2, 2);

                var claspBrush = new SolidColorBrush(Color.FromArgb(235, onAccent.R, onAccent.G, onAccent.B));
                dc.DrawRoundedRectangle(claspBrush, null, new Rect(72, 184, 112, 24), 12, 12);

                var slotBrush = new SolidColorBrush(accent);
                dc.DrawRoundedRectangle(slotBrush, null, new Rect(100, 193, 56, 6), 3, 3);

                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    public static void ApplyWindowTheme(Window window, bool isDark, bool isTranslucent = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var darkValue = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));

        var cornerPref = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = isTranslucent ? Colors.Transparent : Colors.Black;

        var margins = isTranslucent
            ? new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 }
            : default;
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);

        if (chrome is not null)
            chrome.GlassFrameThickness = isTranslucent ? new Thickness(-1) : new Thickness(0);

        var backdrop = isTranslucent ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }

    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_LAYERED = 0x00080000L;
    private const uint LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    public static void PrepareSmoothWindowEntrance(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Suppress the jarring Win32 DWM open/maximize slam before WPF finishes initial layout
        var disableTransitions = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disableTransitions, sizeof(int));

        var originalExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var hadLayered = (originalExStyle & WS_EX_LAYERED) != 0;
        if (!hadLayered)
            _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(originalExStyle | WS_EX_LAYERED));

        _ = SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);

        var started = false;
        void StartFade()
        {
            if (started) return;
            started = true;

            var sw = Stopwatch.StartNew();
            const double durationMs = 150.0;

            void OnRendering(object? _, EventArgs __)
            {
                var currentHwnd = new WindowInteropHelper(window).Handle;
                if (currentHwnd == IntPtr.Zero)
                {
                    CompositionTarget.Rendering -= OnRendering;
                    return;
                }

                var t = Math.Clamp(sw.Elapsed.TotalMilliseconds / durationMs, 0.0, 1.0);
                var eased = 1.0 - Math.Pow(1.0 - t, 3.0); // CubicEase EaseOut
                var alpha = (byte)Math.Round(eased * 255.0);
                _ = SetLayeredWindowAttributes(currentHwnd, 0, alpha, LWA_ALPHA);

                if (t >= 1.0)
                {
                    CompositionTarget.Rendering -= OnRendering;
                    if (!hadLayered)
                        _ = SetWindowLongPtr(currentHwnd, GWL_EXSTYLE, new IntPtr(originalExStyle));
                    var enableTransitions = 0;
                    _ = DwmSetWindowAttribute(currentHwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref enableTransitions, sizeof(int));
                }
            }

            CompositionTarget.Rendering += OnRendering;
        }

        window.ContentRendered += (_, _) => StartFade();
        window.Closed += (_, _) => started = true;
    }

    // ── Windows Explorer Shell Item Context Menu (IContextMenu / IContextMenu2 / IContextMenu3) ──

    private static Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu2 = new("000214F4-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu3 = new("BCFCE0A0-EC17-11d0-8D10-00A0C90F2719");

    private const uint CMF_NORMAL = 0x00000000;
    private const uint CMF_EXPLORE = 0x00000004;
    private const uint CMF_CANRENAME = 0x00000010;
    private const uint CMF_EXTENDEDVERBS = 0x00000100;

    private const uint GCS_VERBW = 0x00000004;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_TOPALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;

    private const uint CMIC_MASK_UNICODE = 0x00004000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;

    private const int WM_INITMENUPOPUP = 0x0117;
    private const int WM_DRAWITEM = 0x002B;
    private const int WM_MEASUREITEM = 0x002C;
    private const int WM_MENUCHAR = 0x0120;
    private const int VK_SHIFT = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E8-0000-0000-C000-000000000046")]
    internal interface IShellExtInit
    {
        [PreserveSig]
        int Initialize(IntPtr pidlFolder, IntPtr pdtobj, IntPtr hkeyProgID);
    }

    private static readonly Guid CLSID_NewMenu = new("D969A300-E7FF-11d0-A93B-00A0C90F2719");

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
    internal interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig]
        int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig]
        int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")]
    internal interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F4-0000-0000-C000-000000000046")]
    internal interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BCFCE0A0-EC17-11d0-8D10-00A0C90F2719")]
    internal interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv, out IntPtr ppidlLast);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMenuItemInfoW(IntPtr hMenu, uint uItem, [MarshalAs(UnmanagedType.Bool)] bool fByPosition, ref MENUITEMINFOW lpmii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetObjectW(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[]? lpvBits, ref BITMAPINFO lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int SIIGBF_BIGGERSIZEOK = 0x00000001;
    private const int SIIGBF_ICONONLY = 0x00000004;
    private const int SIIGBF_THUMBNAILONLY = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    private struct NATIVE_SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In] NATIVE_SIZE size, [In] int flags, out IntPtr phbm);
    }

    private const uint MIIM_STATE = 0x00000001;
    private const uint MIIM_ID = 0x00000002;
    private const uint MIIM_SUBMENU = 0x00000004;
    private const uint MIIM_STRING = 0x00000040;
    private const uint MIIM_BITMAP = 0x00000080;
    private const uint MIIM_FTYPE = 0x00000100;

    private const uint MFT_SEPARATOR = 0x00000800;
    private const uint MFS_DISABLED = 0x00000003;
    private const uint MFS_CHECKED = 0x00000008;
    private const uint MFS_DEFAULT = 0x00001000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MENUITEMINFOW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public IntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] bmiColors;
    }

    internal static bool TryBindContextMenu(
        IntPtr ownerHwnd,
        IReadOnlyList<string> paths,
        out List<IntPtr> pidlsToFree,
        out IShellFolder? parentFolder,
        out IContextMenu? contextMenu)
    {
        pidlsToFree = [];
        parentFolder = null;
        contextMenu = null;

        if (paths.Count == 0) return false;

        var childPidls = new List<IntPtr>(paths.Count);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var hr = SHParseDisplayName(path, IntPtr.Zero, out var fullPidl, 0, out _);
            if (hr != 0 || fullPidl == IntPtr.Zero) continue;

            pidlsToFree.Add(fullPidl);
            hr = SHBindToParent(fullPidl, ref IID_IShellFolder, out var folder, out var childPidl);
            if (hr != 0 || folder is null || childPidl == IntPtr.Zero)
            {
                if (folder is not null) Marshal.ReleaseComObject(folder);
                continue;
            }

            if (parentFolder is null)
                parentFolder = folder;
            else
                Marshal.ReleaseComObject(folder);

            childPidls.Add(childPidl);
        }

        if (parentFolder is null || childPidls.Count == 0)
            return false;

        var uiHr = parentFolder.GetUIObjectOf(
            ownerHwnd,
            (uint)childPidls.Count,
            childPidls.ToArray(),
            ref IID_IContextMenu,
            IntPtr.Zero,
            out var cmObj);

        if (uiHr != 0 || cmObj is not IContextMenu cm)
            return false;

        contextMenu = cm;
        return true;
    }

    public static bool CanBuildShellContextMenu(IReadOnlyList<string> paths)
    {
        List<IntPtr> pidlsToFree = [];
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        try
        {
            return TryBindContextMenu(IntPtr.Zero, paths, out pidlsToFree, out parentFolder, out contextMenu) &&
                   contextMenu is not null;
        }
        finally
        {
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
            if (parentFolder is not null) Marshal.ReleaseComObject(parentFolder);
            foreach (var pidl in pidlsToFree) ILFree(pidl);
        }
    }

    public static bool CanBuildShellNewMenu(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        object? newMenuObj = null;
        IntPtr pidl = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;
        try
        {
            var clsidType = Type.GetTypeFromCLSID(CLSID_NewMenu);
            if (clsidType is null) return false;
            newMenuObj = Activator.CreateInstance(clsidType);
            if (newMenuObj is not IShellExtInit extInit || newMenuObj is not IContextMenu cm)
                return false;

            if (SHParseDisplayName(folderPath, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
                return false;
            if (extInit.Initialize(pidl, IntPtr.Zero, IntPtr.Zero) != 0)
                return false;

            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return false;
            if (cm.QueryContextMenu(hMenu, 0, 1, 0x7FFF, 0) < 0)
                return false;

            var topCount = GetMenuItemCount(hMenu);
            for (uint i = 0; i < (uint)topCount; i++)
            {
                var mii = new MENUITEMINFOW
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                    fMask = MIIM_SUBMENU
                };
                if (GetMenuItemInfoW(hMenu, i, true, ref mii) && mii.hSubMenu != IntPtr.Zero)
                {
                    if (newMenuObj is IContextMenu3 cm3)
                        _ = cm3.HandleMenuMsg2(WM_INITMENUPOPUP, mii.hSubMenu, (IntPtr)i, out _);
                    else if (newMenuObj is IContextMenu2 cm2)
                        _ = cm2.HandleMenuMsg(WM_INITMENUPOPUP, mii.hSubMenu, (IntPtr)i);

                    return GetMenuItemCount(mii.hSubMenu) > 0;
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (pidl != IntPtr.Zero) ILFree(pidl);
            if (newMenuObj is not null) Marshal.ReleaseComObject(newMenuObj);
        }
    }

    public static bool PopulateShellNewMenuItems(
        System.Windows.Controls.ContextMenu hostContextMenu,
        System.Windows.Controls.ItemCollection targetItems,
        FrameworkElement ownerElement,
        string folderPath,
        Action? onNewFolderRequested = null,
        Action? onNewItemCreated = null,
        FontWeight? uiFontWeight = null,
        double? fontSize = null,
        double letterSpacing = 0.0)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        var owner = Window.GetWindow(ownerElement);
        var hwnd = owner is not null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;

        object? newMenuObj = null;
        IContextMenu? cm = null;
        IContextMenu2? cm2 = null;
        IContextMenu3? cm3 = null;
        IntPtr pidl = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;
        var cleanedUp = false;

        void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;

            if (hMenu != IntPtr.Zero)
            {
                DestroyMenu(hMenu);
                hMenu = IntPtr.Zero;
            }
            if (pidl != IntPtr.Zero)
            {
                ILFree(pidl);
                pidl = IntPtr.Zero;
            }
            if (newMenuObj is not null)
            {
                Marshal.ReleaseComObject(newMenuObj);
                newMenuObj = null;
            }
            cm3 = null;
            cm2 = null;
            cm = null;
        }

        try
        {
            var clsidType = Type.GetTypeFromCLSID(CLSID_NewMenu);
            if (clsidType is null) return false;
            newMenuObj = Activator.CreateInstance(clsidType);
            if (newMenuObj is not IShellExtInit extInit || newMenuObj is not IContextMenu contextMenu)
            {
                Cleanup();
                return false;
            }

            cm = contextMenu;
            cm2 = newMenuObj as IContextMenu2;
            cm3 = newMenuObj as IContextMenu3;

            if (SHParseDisplayName(folderPath, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            {
                Cleanup();
                return false;
            }
            if (extInit.Initialize(pidl, IntPtr.Zero, IntPtr.Zero) != 0)
            {
                Cleanup();
                return false;
            }

            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
            {
                Cleanup();
                return false;
            }

            const uint idCmdFirst = 1;
            const uint idCmdLast = 0x7FFF;
            if (cm.QueryContextMenu(hMenu, 0, idCmdFirst, idCmdLast, 0) < 0)
            {
                Cleanup();
                return false;
            }

            IntPtr subMenuHandle = IntPtr.Zero;
            var topCount = GetMenuItemCount(hMenu);
            for (uint i = 0; i < (uint)topCount; i++)
            {
                var mii = new MENUITEMINFOW
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                    fMask = MIIM_SUBMENU
                };
                if (GetMenuItemInfoW(hMenu, i, true, ref mii) && mii.hSubMenu != IntPtr.Zero)
                {
                    if (cm3 is not null)
                        _ = cm3.HandleMenuMsg2(WM_INITMENUPOPUP, mii.hSubMenu, (IntPtr)i, out _);
                    else if (cm2 is not null)
                        _ = cm2.HandleMenuMsg(WM_INITMENUPOPUP, mii.hSubMenu, (IntPtr)i);

                    subMenuHandle = mii.hSubMenu;
                    break;
                }
            }

            if (subMenuHandle == IntPtr.Zero || GetMenuItemCount(subMenuHandle) <= 0)
            {
                Cleanup();
                return false;
            }

            void InvokeNewCmd(uint cmd)
            {
                if (cm is null || cmd < idCmdFirst || cmd > idCmdLast) return;
                var offset = cmd - idCmdFirst;
                var verb = GetContextMenuVerb(cm, offset);
                if (onNewFolderRequested is not null &&
                    (string.Equals(verb, "NewFolder", StringComparison.OrdinalIgnoreCase) || offset == 0))
                {
                    onNewFolderRequested();
                    return;
                }

                var dirPtr = Marshal.StringToHGlobalUni(folderPath);
                try
                {
                    var ici = new CMINVOKECOMMANDINFOEX
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                        fMask = CMIC_MASK_UNICODE,
                        hwnd = hwnd,
                        lpVerb = (IntPtr)offset,
                        lpVerbW = (IntPtr)offset,
                        lpDirectoryW = dirPtr,
                        nShow = SW_SHOW
                    };
                    _ = cm.InvokeCommand(ref ici);
                    onNewItemCreated?.Invoke();
                }
                finally
                {
                    Marshal.FreeHGlobal(dirPtr);
                }
            }

            var appFont = Application.Current?.TryFindResource("AppFont") as FontFamily
                          ?? owner?.FontFamily
                          ?? new FontFamily("Segoe UI");
            var resolvedWeight = uiFontWeight ?? owner?.FontWeight ?? FontWeights.Normal;
            var resolvedSize = fontSize ?? 13.0;

            targetItems.Clear();
            PopulateWpfMenuItems(
                targetItems,
                subMenuHandle,
                idCmdFirst,
                idCmdLast,
                cm,
                cm2,
                cm3,
                appFont,
                resolvedWeight,
                resolvedSize,
                letterSpacing,
                InvokeNewCmd);

            if (targetItems.Count == 0)
            {
                Cleanup();
                return false;
            }

            hostContextMenu.Closed += OnHostClosed;
            void OnHostClosed(object? _, RoutedEventArgs __)
            {
                hostContextMenu.Closed -= OnHostClosed;
                ownerElement.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    Cleanup);
            }

            return true;
        }
        catch
        {
            Cleanup();
            return false;
        }
    }

    public static bool ShowShellContextMenu(
        FrameworkElement? placementTarget,
        IReadOnlyList<string> paths,
        Point screenPoint,
        Func<string, bool>? interceptVerb = null,
        Action<string>? onShellVerbInvoked = null,
        FontWeight? uiFontWeight = null,
        double? fontSize = null,
        double letterSpacing = 0.0,
        Action<System.Windows.Controls.ContextMenu>? customizeMenu = null)
    {
        if (placementTarget is null || paths.Count == 0) return false;
        var owner = Window.GetWindow(placementTarget);
        if (owner is null) return false;

        var hwnd = new WindowInteropHelper(owner).Handle;
        if (hwnd == IntPtr.Zero) return false;

        List<IntPtr> pidlsToFree = [];
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        IContextMenu2? contextMenu2 = null;
        IContextMenu3? contextMenu3 = null;
        IntPtr hMenu = IntPtr.Zero;
        var cleanedUp = false;

        void Cleanup()
        {
            if (cleanedUp) return;
            cleanedUp = true;

            if (hMenu != IntPtr.Zero)
            {
                DestroyMenu(hMenu);
                hMenu = IntPtr.Zero;
            }

            if (contextMenu3 is not null && !ReferenceEquals(contextMenu3, contextMenu))
                Marshal.ReleaseComObject(contextMenu3);
            if (contextMenu2 is not null && !ReferenceEquals(contextMenu2, contextMenu))
                Marshal.ReleaseComObject(contextMenu2);
            if (contextMenu is not null)
                Marshal.ReleaseComObject(contextMenu);
            if (parentFolder is not null)
                Marshal.ReleaseComObject(parentFolder);

            contextMenu3 = null;
            contextMenu2 = null;
            contextMenu = null;
            parentFolder = null;

            foreach (var pidl in pidlsToFree)
                ILFree(pidl);
            pidlsToFree.Clear();
        }

        try
        {
            if (!TryBindContextMenu(hwnd, paths, out pidlsToFree, out parentFolder, out contextMenu) ||
                contextMenu is null)
            {
                Cleanup();
                return false;
            }

            contextMenu2 = contextMenu as IContextMenu2;
            contextMenu3 = contextMenu as IContextMenu3;

            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
            {
                Cleanup();
                return false;
            }

            const uint idCmdFirst = 1;
            const uint idCmdLast = 0x7FFF;
            var shiftDown = (GetKeyState(VK_SHIFT) & 0x8000) != 0;
            var flags = CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME | (shiftDown ? CMF_EXTENDEDVERBS : 0);

            var queryHr = contextMenu.QueryContextMenu(hMenu, 0, idCmdFirst, idCmdLast, flags);
            if (queryHr < 0)
            {
                Cleanup();
                return false;
            }

            void InvokeCmd(uint cmd)
            {
                if (contextMenu is null || cmd < idCmdFirst || cmd > idCmdLast) return;
                var offset = cmd - idCmdFirst;
                var verb = GetContextMenuVerb(contextMenu, offset);

                if (interceptVerb is not null && !string.IsNullOrEmpty(verb) && interceptVerb(verb))
                    return;

                var ici = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE,
                    hwnd = hwnd,
                    lpVerb = (IntPtr)offset,
                    lpVerbW = (IntPtr)offset,
                    nShow = SW_SHOW,
                    ptInvoke = new POINT
                    {
                        X = (int)Math.Round(screenPoint.X),
                        Y = (int)Math.Round(screenPoint.Y)
                    }
                };

                _ = contextMenu.InvokeCommand(ref ici);
                onShellVerbInvoked?.Invoke(verb);
            }

            var appFont = Application.Current?.TryFindResource("AppFont") as FontFamily ?? owner.FontFamily;
            var resolvedWeight = uiFontWeight ?? owner.FontWeight;
            var resolvedFontSize = fontSize ?? 13.0;
            var wpfMenu = new System.Windows.Controls.ContextMenu
            {
                PlacementTarget = placementTarget,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                FontFamily = appFont,
                FontWeight = resolvedWeight,
                FontSize = resolvedFontSize
            };

            PopulateWpfMenuItems(
                wpfMenu.Items,
                hMenu,
                idCmdFirst,
                idCmdLast,
                contextMenu,
                contextMenu2,
                contextMenu3,
                appFont,
                resolvedWeight,
                resolvedFontSize,
                letterSpacing,
                InvokeCmd);

            customizeMenu?.Invoke(wpfMenu);

            if (wpfMenu.Items.Count == 0)
            {
                Cleanup();
                return false;
            }

            wpfMenu.Closed += (_, _) =>
                placementTarget.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    Cleanup);

            wpfMenu.IsOpen = true;
            return true;
        }
        catch
        {
            Cleanup();
            return false;
        }
    }

    private static void PopulateWpfMenuItems(
        System.Windows.Controls.ItemCollection targetItems,
        IntPtr hMenu,
        uint idCmdFirst,
        uint idCmdLast,
        IContextMenu contextMenu,
        IContextMenu2? contextMenu2,
        IContextMenu3? contextMenu3,
        FontFamily appFont,
        FontWeight uiFontWeight,
        double fontSize,
        double letterSpacing,
        Action<uint> onInvokeCmd)
    {
        var count = GetMenuItemCount(hMenu);
        if (count <= 0) return;

        var pendingSeparator = false;

        for (uint i = 0; i < (uint)count; i++)
        {
            const uint MIIM_CHECKMARKS = 0x00000008;
            var mii = new MENUITEMINFOW
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                fMask = MIIM_STATE | MIIM_ID | MIIM_SUBMENU | MIIM_CHECKMARKS | MIIM_FTYPE | MIIM_STRING | MIIM_BITMAP,
                dwTypeData = IntPtr.Zero,
                cch = 0
            };

            if (!GetMenuItemInfoW(hMenu, i, true, ref mii))
                continue;

            if ((mii.fType & MFT_SEPARATOR) != 0)
            {
                if (targetItems.Count > 0)
                    pendingSeparator = true;
                continue;
            }

            var rawText = string.Empty;
            if (mii.cch > 0)
            {
                var strBuf = Marshal.AllocHGlobal((int)((mii.cch + 1) * 2));
                try
                {
                    mii.fMask = MIIM_STRING;
                    mii.dwTypeData = strBuf;
                    mii.cch += 1;
                    if (GetMenuItemInfoW(hMenu, i, true, ref mii))
                        rawText = Marshal.PtrToStringUni(strBuf) ?? string.Empty;
                }
                finally
                {
                    Marshal.FreeHGlobal(strBuf);
                }
            }

            if (string.IsNullOrWhiteSpace(rawText))
                continue;

            if (pendingSeparator)
            {
                targetItems.Add(new System.Windows.Controls.Separator());
                pendingSeparator = false;
            }

            var (header, gesture) = FormatShellMenuText(rawText);
            var iconSource = HBitmapToImageSource(mii.hbmpItem)
                             ?? HBitmapToImageSource(mii.hbmpUnchecked)
                             ?? HBitmapToImageSource(mii.hbmpChecked);
            var verb = (iconSource is null && mii.wID >= idCmdFirst && mii.wID <= idCmdLast)
                ? GetContextMenuVerb(contextMenu, mii.wID - idCmdFirst)
                : string.Empty;

            if (iconSource is null && !string.IsNullOrEmpty(verb) && verb.StartsWith('.'))
                iconSource = GetIconAndType($"new{verb}", isDirectory: false, isDrive: false, largeIcon: false).Icon;

            if (string.IsNullOrEmpty(gesture) && !string.IsNullOrEmpty(verb))
                gesture = GetDefaultGestureForVerb(verb);

            var isEnabled = (mii.fState & MFS_DISABLED) == 0;
            var isChecked = (mii.fState & MFS_CHECKED) != 0;
            var isDefault = (mii.fState & MFS_DEFAULT) != 0;
            var itemWeight = isDefault ? FontWeights.SemiBold : uiFontWeight;

            var headerText = new Eigen.Controls.SpacedTextBlock
            {
                Text = header,
                FontFamily = appFont,
                FontWeight = itemWeight,
                FontSize = fontSize,
                LetterSpacing = letterSpacing,
                VerticalAlignment = VerticalAlignment.Center
            };

            var item = new System.Windows.Controls.MenuItem
            {
                Header = headerText,
                InputGestureText = gesture,
                IsEnabled = isEnabled,
                IsChecked = isChecked,
                FontFamily = appFont,
                FontWeight = itemWeight,
                FontSize = fontSize
            };

            if (iconSource is not null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source = iconSource,
                    Width = 16,
                    Height = 16,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                item.Icon = img;
            }
            else if (!string.IsNullOrEmpty(verb))
            {
                var glyph = GetFallbackGlyphForVerb(verb);
                if (glyph is not null)
                {
                    var iconFont = Application.Current?.TryFindResource("IconFont") as FontFamily
                                   ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
                    item.Icon = new System.Windows.Controls.TextBlock
                    {
                        FontFamily = iconFont,
                        Text = glyph,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                }
            }

            if (mii.hSubMenu != IntPtr.Zero)
            {
                var subMenuHandle = mii.hSubMenu;
                var pos = i;
                var subLoaded = false;
                item.Items.Add(new System.Windows.Controls.MenuItem { Header = "Loading...", IsEnabled = false });
                item.SubmenuOpened += (_, _) =>
                {
                    if (subLoaded) return;
                    subLoaded = true;

                    try
                    {
                        if (contextMenu3 is not null)
                            _ = contextMenu3.HandleMenuMsg2(WM_INITMENUPOPUP, subMenuHandle, (IntPtr)pos, out _);
                        else if (contextMenu2 is not null)
                            _ = contextMenu2.HandleMenuMsg(WM_INITMENUPOPUP, subMenuHandle, (IntPtr)pos);
                    }
                    catch
                    {
                        // Ignore shell extension submenu init errors
                    }

                    item.Items.Clear();
                    PopulateWpfMenuItems(
                        item.Items,
                        subMenuHandle,
                        idCmdFirst,
                        idCmdLast,
                        contextMenu,
                        contextMenu2,
                        contextMenu3,
                        appFont,
                        uiFontWeight,
                        fontSize,
                        letterSpacing,
                        onInvokeCmd);

                    if (item.Items.Count == 0)
                        item.Items.Add(new System.Windows.Controls.MenuItem { Header = "(Empty)", IsEnabled = false });
                };
            }
            else
            {
                var cmdId = mii.wID;
                item.Click += (_, _) => onInvokeCmd(cmdId);
            }

            targetItems.Add(item);
        }
    }

    private static (string Header, string Gesture) FormatShellMenuText(string raw)
    {
        var tabIdx = raw.IndexOf('\t');
        var label = tabIdx >= 0 ? raw[..tabIdx] : raw;
        var gesture = tabIdx >= 0 ? raw[(tabIdx + 1)..].Trim() : string.Empty;

        // Strip Win32 & accelerators cleanly for SpacedTextBlock rendering
        label = label
            .Replace("&&", "\u0001")
            .Replace("&", string.Empty)
            .Replace("\u0001", "&")
            .Trim();

        return (label, gesture);
    }

    private static string GetDefaultGestureForVerb(string verb) => verb.ToLowerInvariant() switch
    {
        "cut" => "Ctrl+X",
        "copy" => "Ctrl+C",
        "rename" => "F2",
        "delete" => "Del",
        "properties" => "Alt+Enter",
        _ => string.Empty
    };

    private static string? GetFallbackGlyphForVerb(string verb) => verb.ToLowerInvariant() switch
    {
        "open" => "\uE8E5",
        "opennew" => "\uE8A7",
        "pintohome" => "\uE718",
        "pintostartscreen" => "\uE80F",
        "windows.share" => "\uE72D",
        "previousversions" => "\uE81C",
        "newfolder" => "\uE8F4",
        "newlink" => "\uE71B",
        "cut" => "\uE8C6",
        "copy" => "\uE8C8",
        "copyaspath" => "\uE8C8",
        "delete" => "\uE74D",
        "rename" => "\uE8AC",
        "link" => "\uE71B",
        "properties" => "\uE946",
        "undo" => "\uE7A7",
        _ => null
    };

    public static bool IsMenuBitmapHandle(IntPtr hBitmap)
    {
        // Only 0..11 and -1 (HBMMENU_CALLBACK) are sentinel constants; valid GDI handles may have bit 31 set (< 0 in signed Int64)
        var raw = unchecked((ulong)hBitmap.ToInt64());
        return raw > 11 && raw != ulong.MaxValue;
    }

    private static ImageSource? HBitmapToImageSource(IntPtr hBitmap)
    {
        if (!IsMenuBitmapHandle(hBitmap))
            return null;

        try
        {
            if (GetObjectW(hBitmap, Marshal.SizeOf<BITMAP>(), out var bmp) != 0 &&
                bmp.bmWidth > 0 && bmp.bmHeight > 0 && bmp.bmBitsPixel == 32)
            {
                var width = bmp.bmWidth;
                var height = bmp.bmHeight;
                var stride = width * 4;
                var pixels = new byte[stride * height];
                var bmi = new BITMAPINFO
                {
                    bmiHeader = new BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // top-down
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0 // BI_RGB
                    },
                    bmiColors = new byte[4]
                };

                var hdc = GetDC(IntPtr.Zero);
                try
                {
                    if (GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref bmi, 0) != 0)
                    {
                        var hasAlpha = false;
                        for (var i = 3; i < pixels.Length; i += 4)
                        {
                            if (pixels[i] != 0)
                            {
                                hasAlpha = true;
                                break;
                            }
                        }

                        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
                        var source = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
                        source.Freeze();
                        return source;
                    }
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, hdc);
                }
            }

            var fallback = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }
        catch
        {
            return null;
        }
    }

    private static string GetContextMenuVerb(IContextMenu contextMenu, uint offset)
    {
        const int cch = 256;
        var ptr = Marshal.AllocHGlobal(cch * 2);
        try
        {
            Marshal.WriteInt16(ptr, 0);
            var hr = contextMenu.GetCommandString((UIntPtr)offset, GCS_VERBW, IntPtr.Zero, ptr, cch);
            if (hr == 0)
                return Marshal.PtrToStringUni(ptr)?.Trim() ?? string.Empty;
        }
        catch
        {
            // Not all shell extensions implement GetCommandStringW
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return string.Empty;
    }
}
