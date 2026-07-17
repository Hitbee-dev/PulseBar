using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using PulseBar.Core.Localization;
using PulseBar.Providers.Claude.Statusline;

namespace PulseBar.App.Services;

/// <summary>
/// User-initiated flows shared by the tray menu and detail popup:
/// Codex browser login, and the combined Claude connect flow
/// (statusline bridge with wrap-consent + OTel token telemetry).
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

    /// <summary>
    /// One-stop Claude connection: statusline bridge (with wrap-consent for an
    /// existing HUD) plus OTel token telemetry, reported in a single dialog.
    /// Declining the wrap consent aborts the whole flow — settings.json is then
    /// never touched, telemetry included.
    /// </summary>
    public async Task ClaudeConnectAsync()
    {
        // Settings I/O can hit \\wsl.localhost (slow, may cold-start the WSL VM);
        // keep the whole flow off the UI thread. Dialogs marshal back themselves.
        var message = await Task.Run(BuildClaudeConnectMessageAsync);
        ShowMessage(message);
    }

    private async Task<string> BuildClaudeConnectMessageAsync()
    {
        var (statuslineMessage, aborted) = await RunStatuslineFlowAsync();
        if (aborted)
        {
            return statuslineMessage;
        }

        var otelMessage = await RunOtelFlowAsync();
        return $"{_loc["Label_Usage"]}: {statuslineMessage}\n{_loc["Label_Telemetry"]}: {otelMessage}";
    }

    private async Task<(string Message, bool Aborted)> RunStatuslineFlowAsync()
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
                    return (_loc["Statusline_Skipped"], Aborted: true);
                }

                result = await _providers.InstallStatuslineAsync(wrapExisting: true, CancellationToken.None);
            }

            return (result switch
            {
                StatuslineInstallResult.Installed => _loc["Statusline_Installed"],
                StatuslineInstallResult.AlreadyInstalled => _loc["Statusline_AlreadyInstalled"],
                null => _loc["Statusline_Unavailable"],
                _ => _loc["Statusline_Failed"],
            }, Aborted: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Statusline install flow failed.");
            return (_loc["Statusline_Failed"], Aborted: false);
        }
    }

    private async Task<string> RunOtelFlowAsync()
    {
        try
        {
            var resultKey = await _providers.InstallOtelEnvAsync(
                _receiver.Endpoint, _receiver.Secret, CancellationToken.None);
            return _loc[resultKey];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OTel install flow failed.");
            return _loc["Otel_Failed"];
        }
    }

    private void ShowMessage(string text)
        => Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(text, _loc["App_Name"], MessageBoxButton.OK, MessageBoxImage.Information));

    private MessageBoxResult ShowConfirm(string text)
        => Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(text, _loc["App_Name"], MessageBoxButton.YesNo, MessageBoxImage.Question));
}
