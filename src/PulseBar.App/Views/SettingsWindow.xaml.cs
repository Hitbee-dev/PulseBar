using System.Windows;
using PulseBar.App.ViewModels;

namespace PulseBar.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "pulsebar-config.json",
            Filter = "JSON (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Export(dialog.FileName);
            MessageBox.Show(
                _viewModel.Loc["Settings_ExportDone"], _viewModel.Loc["App_Name"],
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json",
        };
        if (dialog.ShowDialog(this) == true)
        {
            var ok = _viewModel.Import(dialog.FileName);
            MessageBox.Show(
                _viewModel.Loc[ok ? "Settings_ImportDone" : "Settings_ImportFailed"],
                _viewModel.Loc["App_Name"],
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }
}
