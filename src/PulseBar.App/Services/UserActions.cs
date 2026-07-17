using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Localization;
using PulseBar.Providers.Claude.Statusline;

namespace PulseBar.App.Services;

/// <summary>
/// User-initiated flows shared by the tray menu, overlay and detail popup:
/// Codex browser login, Claude terminal launch, statusline install (with
/// wrap-consent), and OTel opt-in.
/// </summary>
public sealed class UserActions
{
    private readonly ProviderManager _providers;
    private readonly OtelReceiverService _receiver;
    private readonly ILocalizationService _loc;
    private readonly ILogger<UserActions> _logger;

    public UserActions(
        ProviderManager providers,
        OtelReceiverService receiver,
        ILocalizationService loc,
        ILogger<UserActions> logger)
    {
        _providers = providers;
        _receiver = receiver;
        _loc = loc;
        _logger = logger;
    }

    public async Task CodexLoginAsync()
    {
        try
        {
            var authUrl = await _providers.BeginCodexLoginAsync(CancellationToken.None);
            if (authUrl is null)
            {
                ShowMessage(_loc["Codex_LoginUnavailable"]);
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });
            ShowMessage(_loc["Codex_LoginOpened"]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Codex login flow failed.");
            ShowMessage(_loc["Codex_LoginUnavailable"]);
        }
    }

    public void OpenClaude()
    {
        if (!_providers.OpenClaudeTerminal())
        {
            ShowMessage(_loc["Claude_OpenFailed"]);
        }
    }

    public async Task InstallStatuslineAsync()
    {
        try
        {
            var result = await _providers.InstallStatuslineAsync(wrapExisting: false, CancellationToken.None);
            if (result == StatuslineInstallResult.ExistingStatusLine)
            {
                // Wrapping replaces the statusLine entry — explicit consent required.
                var consent = ShowConfirm(_loc["Statusline_WrapConfirm"]);
                if (consent != MessageBoxResult.Yes)
                {
                    return;
                }

                result = await _providers.InstallStatuslineAsync(wrapExisting: true, CancellationToken.None);
            }

            ShowMessage(result switch
            {
                StatuslineInstallResult.Installed => _loc["Statusline_Installed"],
                StatuslineInstallResult.AlreadyInstalled => _loc["Statusline_AlreadyInstalled"],
                _ => _loc["Statusline_Failed"],
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Statusline install flow failed.");
            ShowMessage(_loc["Statusline_Failed"]);
        }
    }

    public async Task InstallOtelAsync()
    {
        var resultKey = await _providers.InstallOtelEnvAsync(
            _receiver.Endpoint, _receiver.Secret, CancellationToken.None);
        ShowMessage(_loc[resultKey]);
    }

    private void ShowMessage(string text)
        => Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(text, _loc["App_Name"], MessageBoxButton.OK, MessageBoxImage.Information));

    private MessageBoxResult ShowConfirm(string text)
        => Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(text, _loc["App_Name"], MessageBoxButton.YesNo, MessageBoxImage.Question));
}
