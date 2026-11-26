using System.Windows.Input;
using System.Windows.Media;
using Eigen.Helpers;
using Eigen.Models;
using Eigen.Services;

namespace Eigen.ViewModels;

public sealed class ExplorerTabViewModel : ObservableObject, IDisposable
{
    private readonly Func<string, ExplorerPaneViewModel> _paneFactory;
    private ExplorerPaneViewModel _activePane;
    private ExplorerPaneViewModel? _secondaryPane;
    private bool _isSplitView;
    private bool _isSelected;

    public ExplorerTabViewModel(
        Func<string, ExplorerPaneViewModel> paneFactory,
        string initialPath)
    {
        _paneFactory = paneFactory;
        PrimaryPane = _paneFactory(initialPath);
        _activePane = PrimaryPane;
        _activePane.IsActivePane = true;

        WirePaneEvents(PrimaryPane);

        ToggleSplitViewCommand = new RelayCommand(ToggleSplitView);
        SwapPanesCommand = new RelayCommand(SwapPanes, () => IsSplitView && SecondaryPane is not null);
    }

    public event Action<ExplorerTabViewModel>? TabUpdated;

    public Guid Id { get; } = Guid.NewGuid();
    public ExplorerPaneViewModel PrimaryPane { get; }

    public ExplorerPaneViewModel? SecondaryPane
    {
        get => _secondaryPane;
        private set => SetProperty(ref _secondaryPane, value);
    }

    public ExplorerPaneViewModel ActivePane
    {
        get => _activePane;
        private set
        {
            if (ReferenceEquals(_activePane, value)) return;
            _activePane.IsActivePane = false;
            _activePane = value;
            _activePane.IsActivePane = true;
            OnPropertyChanged();
            TabUpdated?.Invoke(this);
        }
    }

    public bool IsSplitView
    {
        get => _isSplitView;
        set
        {
            if (SetProperty(ref _isSplitView, value))
            {
                if (value && SecondaryPane is null)
                {
                    var pane = _paneFactory(PrimaryPane.CurrentPath);
                    WirePaneEvents(pane);
                    SecondaryPane = pane;
                    _ = pane.NavigateToAsync(PrimaryPane.CurrentPath, recordHistory: false);
                }
                else if (!value)
                {
                    ActivePane = PrimaryPane;
                }

                ((RelayCommand)SwapPanesCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(TabTitle));
                TabUpdated?.Invoke(this);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string TabTitle => IsSplitView && SecondaryPane is not null
        ? $"{PrimaryPane.Title}  |  {SecondaryPane.Title}"
        : PrimaryPane.Title;

    public ImageSource? TabIcon => PrimaryPane.CurrentIcon;

    public ICommand ToggleSplitViewCommand { get; }
    public ICommand SwapPanesCommand { get; }

    public ExplorerPaneViewModel? GetOtherPane(ExplorerPaneViewModel pane)
    {
        if (!IsSplitView || SecondaryPane is null) return null;
        return ReferenceEquals(pane, PrimaryPane) ? SecondaryPane : PrimaryPane;
    }

    public void ToggleSplitView()
    {
        IsSplitView = !IsSplitView;
    }

    public void OpenInSecondaryPane(string path)
    {
        if (SecondaryPane is null)
        {
            var pane = _paneFactory(path);
            WirePaneEvents(pane);
            SecondaryPane = pane;
        }
        IsSplitView = true;
        ActivePane = SecondaryPane!;
        _ = SecondaryPane!.NavigateToAsync(path);
    }

    public async void SwapPanes()
    {
        if (!IsSplitView || SecondaryPane is null) return;
        try
        {
            var leftPath = PrimaryPane.CurrentPath;
            var rightPath = SecondaryPane.CurrentPath;
            await Task.WhenAll(
                PrimaryPane.NavigateToAsync(rightPath),
                SecondaryPane.NavigateToAsync(leftPath));
        }
        catch
        {
            // Ignore navigation errors during pane swap
        }
    }

    private void WirePaneEvents(ExplorerPaneViewModel pane)
    {
        pane.Activated += p => ActivePane = p;
        pane.NavigationSucceeded += _ =>
        {
            OnPropertyChanged(nameof(TabTitle));
            OnPropertyChanged(nameof(TabIcon));
            TabUpdated?.Invoke(this);
        };
        pane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerPaneViewModel.Title) or nameof(ExplorerPaneViewModel.CurrentIcon))
            {
                OnPropertyChanged(nameof(TabTitle));
                OnPropertyChanged(nameof(TabIcon));
            }
        };
    }

    public void Dispose()
    {
        PrimaryPane.Dispose();
        SecondaryPane?.Dispose();
    }
}
