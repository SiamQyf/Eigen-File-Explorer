using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Eigen.Models;

public sealed class FileSystemItem : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private ImageSource? _largeIcon;
    private ImageSource? _thumbnail;
    private bool _isCut;
    private bool _isEditing;
    private string _editingName = string.Empty;
    private string _name = string.Empty;
    private string _fullPath = string.Empty;
    private string _sizeDisplay = string.Empty;

    public required string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public required string FullPath
    {
        get => _fullPath;
        set
        {
            if (_fullPath != value)
            {
                _fullPath = value;
                OnPropertyChanged();
            }
        }
    }

    public string ParentPath { get; set; } = string.Empty;
    public bool IsDirectory { get; init; }
    public bool IsDrive { get; init; }
    public bool IsHidden { get; init; }
    public bool IsSystem { get; init; }
    public bool IsReadOnly { get; init; }
    public long SizeBytes { get; set; }

    public string SizeDisplay
    {
        get => _sizeDisplay;
        set
        {
            if (_sizeDisplay != value)
            {
                _sizeDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime DateModified { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime DateAccessed { get; init; }
    public string DateModifiedDisplay => DateModified == DateTime.MinValue ? string.Empty : DateModified.ToString("yyyy-MM-dd  HH:mm");
    public string TypeDescription { get; set; } = string.Empty;
    public string Extension { get; init; } = string.Empty;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (!Equals(_icon, value))
            {
                _icon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveListIcon));
                OnPropertyChanged(nameof(TilePreviewImage));
            }
        }
    }

    public ImageSource? LargeIcon
    {
        get => _largeIcon;
        set
        {
            if (!Equals(_largeIcon, value))
            {
                _largeIcon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveListIcon));
                OnPropertyChanged(nameof(TilePreviewImage));
            }
        }
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (!Equals(_thumbnail, value))
            {
                _thumbnail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TilePreviewImage));
            }
        }
    }

    public ImageSource? EffectiveListIcon => _largeIcon ?? _icon;
    public ImageSource? TilePreviewImage => _thumbnail ?? _largeIcon ?? _icon;

    public bool IsCut
    {
        get => _isCut;
        set
        {
            if (_isCut != value)
            {
                _isCut = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing != value)
            {
                _isEditing = value;
                OnPropertyChanged();
            }
        }
    }

    public string EditingName
    {
        get => _editingName;
        set
        {
            if (_editingName != value)
            {
                _editingName = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
