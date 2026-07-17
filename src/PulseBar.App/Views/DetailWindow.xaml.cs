using System.Windows;
using System.Windows.Threading;
using PulseBar.App.Services;
using PulseBar.App.ViewModels;

namespace PulseBar.App.Views;

public partial class DetailWindow : Window
{
    private readonly DetailViewModel _viewModel;
    private readonly UserActions _actions;
    private readonly DispatcherTimer _timer;

    public DetailWindow(DetailViewModel viewModel, UserActions actions)
    {
        _viewModel = viewModel;
        _actions = actions;
        DataContext = viewModel;
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => _viewModel.Tick();

        Loaded += (_, _) =>
        {
            _viewModel.Tick();
            _timer.Start();
            PositionNearTray();
        };
        Closed += (_, _) => _timer.Stop();
    }

    public void SelectAiTab() => Tabs.SelectedIndex = 1;

    private void PositionNearTray()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    private async void OnCodexLoginClick(object sender, RoutedEventArgs e)
        => await _actions.CodexLoginAsync();

    private async void OnClaudeLoginClick(object sender, RoutedEventArgs e)
        => await _actions.ClaudeConnectAsync();
}
