using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Eigen.Models;
using Eigen.Services;
using Microsoft.Win32;

namespace Eigen.Views;

public partial class EigenArchiverWindow : Window
{
    private readonly string _archivePath;
    private readonly Func<Task>? _onExtractedOrModified;
    private List<ArchiveEntryInfo> _allEntries = [];
    private string _currentFolder = string.Empty;

    public EigenArchiverWindow(string archivePath, Func<Task>? onExtractedOrModified = null)
    {
        InitializeComponent();
        _archivePath = archivePath;
        _onExtractedOrModified = onExtractedOrModified;

        var fileName = Path.GetFileName(archivePath);
        Title = $"Eigen Archiver — {fileName}";
        ArchiveTitleBadge.Text = fileName;
        AddFilesButton.IsEnabled = Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);

        Loaded += (_, _) => LoadArchiveEntries();
    }

    private void LoadArchiveEntries()
    {
        try
        {
            _allEntries = ArchiveService.ListEntries(_archivePath);
            RefreshView();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open archive:\n{ex.Message}", "Eigen Archiver", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshView()
    {
        var filter = FilterBox.Text.Trim();
        List<ArchiveEntryInfo> visible;
        if (!string.IsNullOrEmpty(filter))
        {
            visible = _allEntries
                .Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, NaturalStringComparer.Instance)
                .ToList();
        }
        else
        {
            visible = _allEntries
                .Where(e => string.Equals(e.ParentFolder, _currentFolder, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, NaturalStringComparer.Instance)
                .ToList();
        }

        EntriesListView.ItemsSource = visible;
        UpButton.IsEnabled = !string.IsNullOrEmpty(_currentFolder);
        InternalPathText.Text = string.IsNullOrEmpty(_currentFolder)
            ? $"{Path.GetFileName(_archivePath)} \\"
            : $"{Path.GetFileName(_archivePath)} \\ {_currentFolder.Replace('/', '\\')}";

        var files = _allEntries.Where(e => !e.IsDirectory).ToList();
        var totalSize = files.Sum(f => Math.Max(0, f.Size));
        var compSize = files.Sum(f => Math.Max(0, f.CompressedSize));
        var ratioText = totalSize > 0 && compSize > 0 && compSize <= totalSize
            ? $" • {Math.Round((1.0 - (double)compSize / totalSize) * 100):0}% saved"
            : string.Empty;
        StatusText.Text = $"{visible.Count:N0} shown ({files.Count:N0} total files, {FormatHelper.FormatSize(totalSize)}{ratioText})";
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => RefreshView();

    private void OnUpClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFolder)) return;
        var idx = _currentFolder.LastIndexOf('/');
        _currentFolder = idx >= 0 ? _currentFolder[..idx] : string.Empty;
        RefreshView();
    }

    private void OnEntryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EntriesListView.SelectedItem is ArchiveEntryInfo entry && entry.IsDirectory)
        {
            FilterBox.Text = string.Empty;
            _currentFolder = entry.Key;
            RefreshView();
        }
    }

    private async void OnExtractHereClick(object sender, RoutedEventArgs e)
    {
        var dest = Path.GetDirectoryName(_archivePath);
        if (string.IsNullOrEmpty(dest)) return;
        await RunExtractAsync(dest, createSubfolder: false);
    }

    private async void OnExtractToClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Select Extraction Folder",
            InitialDirectory = Path.GetDirectoryName(_archivePath) ?? string.Empty
        };
        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FolderName))
            return;

        await RunExtractAsync(dlg.FolderName, createSubfolder: false);
    }

    private async Task RunExtractAsync(string destinationFolder, bool createSubfolder)
    {
        var selectedKeys = EntriesListView.SelectedItems
            .OfType<ArchiveEntryInfo>()
            .Select(i => i.Key)
            .ToList();

        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.Value = 0;
        var progress = new Progress<FileOperationProgress>(p =>
        {
            OperationProgress.Value = p.ProgressPercent;
            StatusText.Text = $"Extracting {p.CurrentItem} ({p.CompletedItems:N0}/{p.TotalItems:N0})";
        });

        try
        {
            var count = await ArchiveService.ExtractAsync(
                _archivePath,
                destinationFolder,
                createSubfolder,
                selectedKeys.Count > 0 ? selectedKeys : null,
                progress: progress);

            StatusText.Text = $"Extracted {count:N0} file(s) to {destinationFolder}";
            if (_onExtractedOrModified is not null)
                await _onExtractedOrModified();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Extraction failed:\n{ex.Message}", "Eigen Archiver", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OperationProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add Files to Archive",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) != true || dlg.FileNames.Length == 0)
            return;

        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.IsIndeterminate = true;
        try
        {
            var added = await ArchiveService.AddFilesToZipAsync(_archivePath, dlg.FileNames);
            LoadArchiveEntries();
            StatusText.Text = $"Added {added:N0} file(s) to {Path.GetFileName(_archivePath)}";
            if (_onExtractedOrModified is not null)
                await _onExtractedOrModified();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not add files:\n{ex.Message}", "Eigen Archiver", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OperationProgress.IsIndeterminate = false;
            OperationProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnTestArchiveClick(object sender, RoutedEventArgs e)
    {
        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.IsIndeterminate = true;
        StatusText.Text = "Testing archive integrity...";
        try
        {
            var (count, bytes) = await ArchiveService.TestArchiveAsync(_archivePath);
            StatusText.Text = $"No errors found • Verified {count:N0} file(s) ({FormatHelper.FormatSize(bytes)})";
            MessageBox.Show(this, $"Archive test passed with no errors.\n\nVerified {count:N0} file(s) ({FormatHelper.FormatSize(bytes)}).", "Eigen Archiver", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Test failed: {ex.Message}";
            MessageBox.Show(this, $"Archive test failed:\n{ex.Message}", "Eigen Archiver", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            OperationProgress.IsIndeterminate = false;
            OperationProgress.Visibility = Visibility.Collapsed;
        }
    }
}
