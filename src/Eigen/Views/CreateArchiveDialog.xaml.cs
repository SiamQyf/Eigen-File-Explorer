using System.IO;
using System.Windows;
using System.Windows.Controls;
using Eigen.Services;
using Microsoft.Win32;

namespace Eigen.Views;

public partial class CreateArchiveDialog : Window
{
    private readonly IReadOnlyList<string> _sourcePaths;

    public CreateArchiveDialog(IReadOnlyList<string> sourcePaths, string defaultDirectory)
    {
        InitializeComponent();
        _sourcePaths = sourcePaths;

        SourceSummaryText.Text = sourcePaths.Count == 1
            ? $"Compressing \"{Path.GetFileName(sourcePaths[0].TrimEnd('\\', '/'))}\""
            : $"Compressing {sourcePaths.Count:N0} selected items";

        ArchiveNameBox.Text = ArchiveService.GetDefaultArchiveName(sourcePaths, ArchiveCreateFormat.Zip);
        DestinationBox.Text = defaultDirectory;
        Loaded += (_, _) =>
        {
            ArchiveNameBox.Focus();
            var dot = ArchiveNameBox.Text.IndexOf('.');
            if (dot > 0) ArchiveNameBox.Select(0, dot);
        };
    }

    public string ResultArchivePath { get; private set; } = string.Empty;
    public ArchiveCreateFormat SelectedFormat { get; private set; } = ArchiveCreateFormat.Zip;
    public ArchiveCompressionPreset SelectedCompression { get; private set; } = ArchiveCompressionPreset.Normal;

    private ArchiveCreateFormat GetCurrentFormat()
    {
        if (FormatCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<ArchiveCreateFormat>(item.Tag?.ToString(), out var fmt))
        {
            return fmt;
        }
        return ArchiveCreateFormat.Zip;
    }

    private void OnFormatSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var fmt = GetCurrentFormat();
        var newExt = ArchiveService.GetExtensionForFormat(fmt);
        var current = ArchiveNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(current))
        {
            ArchiveNameBox.Text = ArchiveService.GetDefaultArchiveName(_sourcePaths, fmt);
            return;
        }

        var stem = ArchiveService.GetArchiveBaseName(current);
        ArchiveNameBox.Text = $"{stem}{newExt}";
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose Destination Folder",
            InitialDirectory = Directory.Exists(DestinationBox.Text) ? DestinationBox.Text : string.Empty
        };
        if (dlg.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            DestinationBox.Text = dlg.FolderName;
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var name = ArchiveNameBox.Text.Trim();
        var destDir = DestinationBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(destDir))
        {
            MessageBox.Show(this, "Please specify an archive name and destination folder.", "Eigen Archiver", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedFormat = GetCurrentFormat();
        var expectedExt = ArchiveService.GetExtensionForFormat(SelectedFormat);
        if (!name.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase))
            name += expectedExt;

        SelectedCompression = CompressionCombo.SelectedItem is ComboBoxItem cbi &&
                              Enum.TryParse<ArchiveCompressionPreset>(cbi.Tag?.ToString(), out var preset)
            ? preset
            : ArchiveCompressionPreset.Normal;

        ResultArchivePath = Path.Combine(destDir, name);
        DialogResult = true;
    }
}
