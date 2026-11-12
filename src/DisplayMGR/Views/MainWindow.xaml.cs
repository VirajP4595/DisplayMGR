using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DisplayMGR.ViewModels;

namespace DisplayMGR.Views;

/// <summary>The control panel. It opens above the tray and hides when focus moves away.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DateTime _hiddenAt = DateTime.MinValue;
    private bool _dialogOpen;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.Confirm = Confirm;
        viewModel.ShowPanelRequested += ShowPanel;
        viewModel.HidePanelRequested += HidePanel;

        SizeChanged += (_, _) => Reposition();
        Deactivated += (_, _) =>
        {
            if (!_viewModel.KeepPanelOpen && !_dialogOpen)
                HidePanel();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                HidePanel();
        };
    }

    /// <summary>Set before shutting down; otherwise closing only hides the panel.</summary>
    public bool AllowClose { get; set; }

    public void Toggle()
    {
        if (IsVisible)
            HidePanel();
        // Clicking the tray icon takes focus first, which hides the panel; do not reopen it at once.
        else if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds > 300)
            ShowPanel();
    }

    public void ShowPanel()
    {
        MaxHeight = SystemParameters.WorkArea.Height;
        Show();
        Reposition();
        Activate();
        _ = _viewModel.RefreshValuesAsync();
    }

    public void HidePanel()
    {
        if (!IsVisible)
            return;
        _hiddenAt = DateTime.UtcNow;
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            HidePanel();
        }
        base.OnClosing(e);
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;
    }

    private bool Confirm(string message)
    {
        _dialogOpen = true;
        try
        {
            return MessageBox.Show(this, message, "DisplayMGR", MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                MessageBoxResult.Cancel) == MessageBoxResult.OK;
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}
