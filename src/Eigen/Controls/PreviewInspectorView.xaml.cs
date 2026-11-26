using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Eigen.Models;
using Eigen.ViewModels;

namespace Eigen.Controls;

public sealed record VideoTransportState(
    bool IsPlaying,
    bool IsMuted,
    double Volume,
    double PositionSeconds,
    double DurationSeconds,
    string TimeLabel);

public partial class PreviewInspectorView : UserControl
{
    private readonly DispatcherTimer _videoTimer;
    private MainViewModel? _subscribedVm;
    private bool _isPlaying;
    private bool _isMuted = true;
    private double _volume = 0.85;
    private bool _isUserSeeking;
    private bool _isSyncingUi;
    private Uri? _activeVideoSource;

    public event Action<VideoTransportState>? VideoStateChanged;

    public PreviewInspectorView()
    {
        InitializeComponent();
        _videoTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _videoTimer.Tick += (_, _) => PublishVideoState();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => StopAndReleaseVideo();
    }

    public MediaElement VideoPlayerElement => PreviewVideoPlayer;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedVm = e.NewValue as MainViewModel;
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;

        SyncVideoPlayback();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentPreview) or nameof(MainViewModel.IsPreviewPaneOpen))
            SyncVideoPlayback();
    }

    private void SyncVideoPlayback()
    {
        if (_subscribedVm is not { IsPreviewPaneOpen: true, CurrentPreview: { Kind: PreviewKind.Video, VideoSource: { } uri } })
        {
            StopAndReleaseVideo();
            return;
        }

        if (Equals(_activeVideoSource, uri))
            return;

        StopAndReleaseVideo();
        _activeVideoSource = uri;
        _isMuted = true;
        PreviewVideoFallbackImage.Visibility = Visibility.Collapsed;
        PreviewVideoPlayer.Visibility = Visibility.Visible;
        PreviewVideoPlayer.Volume = _volume;
        PreviewVideoPlayer.IsMuted = _isMuted;
        PreviewVideoPlayer.Source = uri;
        PreviewVideoPlayer.Play();
        _isPlaying = true;
        _videoTimer.Start();
        PublishVideoState();
    }

    public void StopAndReleaseVideo()
    {
        _videoTimer.Stop();
        _activeVideoSource = null;
        _isPlaying = false;
        _isUserSeeking = false;
        try
        {
            PreviewVideoPlayer.Stop();
            PreviewVideoPlayer.Close();
            PreviewVideoPlayer.Source = null;
        }
        catch
        {
            // Ignore media teardown errors
        }
        PublishVideoState();
    }

    public void ToggleVideoPlayPause() => TogglePlayPause();

    public void ToggleVideoMute()
    {
        _isMuted = !_isMuted;
        PreviewVideoPlayer.IsMuted = _isMuted;
        if (!_isMuted)
        {
            if (_volume <= 0.02) _volume = 0.85;
            PreviewVideoPlayer.Volume = _volume;
        }
        PublishVideoState();
    }

    public void SeekVideoRelative(TimeSpan delta)
    {
        if (_activeVideoSource is null) return;
        try
        {
            var next = PreviewVideoPlayer.Position + delta;
            if (next < TimeSpan.Zero) next = TimeSpan.Zero;
            if (PreviewVideoPlayer.NaturalDuration.HasTimeSpan && next > PreviewVideoPlayer.NaturalDuration.TimeSpan)
                next = PreviewVideoPlayer.NaturalDuration.TimeSpan;
            PreviewVideoPlayer.Position = next;
            PublishVideoState();
        }
        catch
        {
            // Ignore seek before media is ready
        }
    }

    public void SetVideoPositionSeconds(double seconds)
    {
        if (_activeVideoSource is null) return;
        try
        {
            var max = PreviewVideoPlayer.NaturalDuration.HasTimeSpan
                ? PreviewVideoPlayer.NaturalDuration.TimeSpan.TotalSeconds
                : seconds;
            PreviewVideoPlayer.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, max));
            PublishVideoState();
        }
        catch
        {
            // Ignore seek errors
        }
    }

    public void SetVideoVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
        try
        {
            PreviewVideoPlayer.Volume = _volume;
            if (_volume > 0.01 && _isMuted)
            {
                _isMuted = false;
                PreviewVideoPlayer.IsMuted = false;
            }
            else if (_volume <= 0.01 && !_isMuted)
            {
                _isMuted = true;
                PreviewVideoPlayer.IsMuted = true;
            }
        }
        catch
        {
            // Ignore volume errors
        }
        PublishVideoState();
    }

    public void AdjustVideoVolume(double delta) => SetVideoVolume(_volume + delta);

    private void PublishVideoState()
    {
        if (VideoPlayPauseButton is null || VideoMuteButton is null ||
            VideoVolumeSlider is null || VideoSeekSlider is null || VideoStatusText is null)
            return;

        double pos = 0;
        double dur = 0;
        try
        {
            pos = PreviewVideoPlayer.Position.TotalSeconds;
            if (PreviewVideoPlayer.NaturalDuration.HasTimeSpan)
                dur = PreviewVideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
        }
        catch { }

        var timeLabel = dur > 0
            ? $"{FormatTime(pos)} / {FormatTime(dur)}"
            : FormatTime(pos);

        _isSyncingUi = true;
        try
        {
            VideoPlayPauseButton.Content = _isPlaying ? "\uE769" : "\uE768";
            VideoMuteButton.Content = _isMuted ? "\uE74F" : "\uE767";
            VideoVolumeSlider.Value = _isMuted ? 0 : _volume;
            VideoSeekSlider.Maximum = Math.Max(1.0, dur);
            if (!_isUserSeeking)
                VideoSeekSlider.Value = Math.Clamp(pos, 0, VideoSeekSlider.Maximum);
            VideoStatusText.Text = timeLabel;
        }
        finally
        {
            _isSyncingUi = false;
        }

        VideoStateChanged?.Invoke(new VideoTransportState(_isPlaying, _isMuted, _volume, pos, dur, timeLabel));
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    private void TogglePlayPause()
    {
        if (_activeVideoSource is null) return;
        if (_isPlaying)
        {
            PreviewVideoPlayer.Pause();
            _isPlaying = false;
            _videoTimer.Stop();
        }
        else
        {
            PreviewVideoPlayer.Volume = _volume;
            PreviewVideoPlayer.IsMuted = _isMuted;
            PreviewVideoPlayer.Play();
            _isPlaying = true;
            _videoTimer.Start();
        }
        PublishVideoState();
    }

    private void OnVideoSurfaceClick(object sender, MouseButtonEventArgs e) => TogglePlayPause();

    private void OnVideoPlayPauseClick(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void OnVideoMuteClick(object sender, RoutedEventArgs e) => ToggleVideoMute();

    private void OnVideoSeekStart(object sender, MouseButtonEventArgs e) => _isUserSeeking = true;

    private void OnVideoSeekEnd(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = false;
        SetVideoPositionSeconds(VideoSeekSlider.Value);
    }

    private void OnVideoSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi || !_isUserSeeking) return;
        SetVideoPositionSeconds(e.NewValue);
    }

    private void OnVideoVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSyncingUi || !IsLoaded) return;
        SetVideoVolume(e.NewValue);
    }

    private void OnVideoMediaOpened(object sender, RoutedEventArgs e)
    {
        // Re-apply audio settings after MediaOpened so WMP/DirectShow stream honors unmute/volume
        PreviewVideoPlayer.Volume = _volume;
        PreviewVideoPlayer.IsMuted = _isMuted;
        PublishVideoState();
    }

    private void OnVideoMediaEnded(object sender, RoutedEventArgs e)
    {
        if (_activeVideoSource is null || !_isPlaying) return;
        PreviewVideoPlayer.Position = TimeSpan.Zero;
        PreviewVideoPlayer.Play();
        PublishVideoState();
    }

    private void OnVideoMediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _videoTimer.Stop();
        _isPlaying = false;
        PreviewVideoPlayer.Visibility = Visibility.Collapsed;
        PreviewVideoFallbackImage.Visibility = Visibility.Visible;
        VideoStatusText.Text = "Codec unsupported";
        VideoPlayPauseButton.Content = "\uE768";
    }

    private void OnPreviewSurfaceMouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.CurrentPreview is { CanZoom: true })
            vm.IsFloatingPreviewOpen = true;
    }
}
