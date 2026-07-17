namespace PulseBar.Core.Localization;

/// <summary>
/// UI string tables. Both tables must contain exactly the same keys
/// (enforced by a unit test). Keep keys sorted by area prefix.
/// </summary>
public static class LocalizedStrings
{
    public static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["App_Name"] = "PulseBar",

        ["Tray_Refresh"] = "Refresh",
        ["Tray_DisplayItems"] = "Display items",
        ["Tray_Settings"] = "Settings",
        ["Tray_StartupRegister"] = "Start with Windows",
        ["Tray_OpenLogs"] = "Open log folder",
        ["Tray_Exit"] = "Exit",

        ["Otel_Installed"] = "Claude telemetry configured. Restart Claude Code to apply.",
        ["Otel_AlreadyInstalled"] = "Telemetry is already configured.",
        ["Otel_Conflict"] = "Existing OTel settings found; nothing was changed. Manual merge required.",
        ["Otel_Unreadable"] = "Could not parse Claude settings.json; nothing was changed.",
        ["Otel_NoProfile"] = "No Claude profile configured.",
        ["Otel_ReceiverDown"] = "The telemetry receiver has not started yet.",

        ["Tooltip_Today"] = "Today",
        ["Tooltip_SevenDays"] = "Last 7 days",
        ["Tooltip_Resets"] = "resets {0}",

        ["Detail_Title"] = "PulseBar Details",
        ["Detail_SystemTab"] = "System",
        ["Detail_AiTab"] = "AI Usage",
        ["Detail_CpuHistory"] = "CPU — last 60 s",
        ["Detail_CodexLogin"] = "Codex login",
        ["Detail_ClaudeLogin"] = "Claude login",
        ["Detail_Account"] = "Account",
        ["Detail_Plan"] = "Plan",
        ["Detail_Credits"] = "Credits",

        ["Statusline_WrapConfirm"] =
            "An existing statusline was found. Wrap it with the PulseBar bridge? " +
            "Your current HUD keeps working (passthrough) and settings.json is backed up first.",
        ["Statusline_Installed"] = "Claude usage connected. Restart Claude Code to apply.",
        ["Statusline_AlreadyInstalled"] = "Claude usage is already connected.",
        ["Statusline_Failed"] = "Statusline install failed — nothing was modified. See logs.",
        ["Statusline_Skipped"] = "Cancelled — your Claude settings were not modified.",
        ["Statusline_Unavailable"] = "Claude connection unavailable (no Claude CLI detected or bridge missing).",
        ["Otel_Failed"] = "Telemetry setup failed. See logs.",
        ["Label_Usage"] = "Usage",
        ["Label_Telemetry"] = "Telemetry",
        ["Codex_LoginOpened"] = "Complete the Codex login in your browser.",
        ["Codex_LoginUnavailable"] = "No Codex connection yet; cannot start login.",

        ["Settings_Export"] = "Export settings",
        ["Settings_Import"] = "Import settings",
        ["Settings_ExportDone"] = "Settings exported.",
        ["Settings_ImportDone"] = "Settings imported and applied.",
        ["Settings_ImportFailed"] = "Import failed: the file is not a valid PulseBar config.",

        ["Settings_Title"] = "PulseBar Settings",
        ["Settings_General"] = "General",
        ["Settings_Language"] = "Language",
        ["Settings_Language_Korean"] = "한국어",
        ["Settings_Language_English"] = "English",
        ["Settings_Layout"] = "Layout",
        ["Settings_Layout_OneLine"] = "One line",
        ["Settings_Layout_TwoLine"] = "Two lines",
        ["Settings_Layout_UltraCompact"] = "Ultra compact",
        ["Settings_Providers"] = "AI Providers",
        ["Settings_Metrics"] = "System Metrics",
        ["Settings_Save"] = "Save",
        ["Settings_Cancel"] = "Cancel",

        ["Freshness_Live"] = "Live",
        ["Freshness_Fresh"] = "Fresh",
        ["Freshness_Stale"] = "Stale",
        ["Freshness_Unavailable"] = "Unavailable",
        ["Freshness_AuthenticationRequired"] = "Login required",
        ["Freshness_Error"] = "Error",

        ["Scope_ServerAccount"] = "Server account",
        ["Scope_LocalMachine"] = "This PC only",
        ["Scope_LocalSession"] = "This session only",
        ["Scope_Estimated"] = "Estimated",

        ["State_Disabled"] = "Disabled",
        ["State_Detecting"] = "Detecting…",
        ["State_ExecutableMissing"] = "Executable not found",
        ["State_AuthenticationRequired"] = "Login required",
        ["State_Connecting"] = "Connecting…",
        ["State_Connected"] = "Connected",
        ["State_Stale"] = "Stale",
        ["State_Disconnected"] = "Disconnected",
        ["State_Error"] = "Error",

        ["Common_LastUpdated"] = "Last updated {0}",
        ["Common_JustNow"] = "just now",
        ["Common_MinutesAgo"] = "{0} min ago",
        ["Common_HoursAgo"] = "{0} h ago",
        ["Common_ResetsAt"] = "Resets at {0}",
        ["Common_NotConnected"] = "Setup required",

        ["Metric_Cpu"] = "CPU",
        ["Metric_Memory"] = "RAM",
        ["Metric_Gpu"] = "GPU",
        ["Metric_Vram"] = "VRAM",
        ["Metric_Disk"] = "Disk",
        ["Metric_Network"] = "Network",

        ["Claude_FiveHour"] = "5h",
        ["Claude_SevenDay"] = "Week",
        ["Claude_FableTokens"] = "Fable tokens · this PC's Claude Code",
        ["Codex_FiveHour"] = "5h",
        ["Codex_SevenDay"] = "Week",
    };

    public static readonly IReadOnlyDictionary<string, string> Ko = new Dictionary<string, string>
    {
        ["App_Name"] = "PulseBar",

        ["Tray_Refresh"] = "새로고침",
        ["Tray_DisplayItems"] = "표시 항목",
        ["Tray_Settings"] = "설정",
        ["Tray_StartupRegister"] = "Windows 시작 시 실행",
        ["Tray_OpenLogs"] = "로그 폴더 열기",
        ["Tray_Exit"] = "종료",

        ["Otel_Installed"] = "Claude 텔레메트리 연동이 설치되었습니다. Claude Code 재시작 후 적용됩니다.",
        ["Otel_AlreadyInstalled"] = "이미 연동되어 있습니다.",
        ["Otel_Conflict"] = "기존 OTel 설정이 있어 변경하지 않았습니다. 수동 병합이 필요합니다.",
        ["Otel_Unreadable"] = "Claude settings.json을 읽을 수 없어 변경하지 않았습니다.",
        ["Otel_NoProfile"] = "Claude 프로필이 없습니다.",
        ["Otel_ReceiverDown"] = "수신기가 아직 시작되지 않았습니다.",

        ["Tooltip_Today"] = "오늘",
        ["Tooltip_SevenDays"] = "최근 7일",
        ["Tooltip_Resets"] = "{0} 리셋",

        ["Detail_Title"] = "PulseBar 상세",
        ["Detail_SystemTab"] = "시스템",
        ["Detail_AiTab"] = "AI 사용량",
        ["Detail_CpuHistory"] = "CPU — 최근 60초",
        ["Detail_CodexLogin"] = "Codex 로그인",
        ["Detail_ClaudeLogin"] = "Claude 로그인",
        ["Detail_Account"] = "계정",
        ["Detail_Plan"] = "플랜",
        ["Detail_Credits"] = "크레딧",

        ["Statusline_WrapConfirm"] =
            "기존 statusline이 발견되었습니다. PulseBar 래퍼로 감쌀까요? " +
            "기존 HUD는 passthrough로 계속 동작하며, settings.json은 먼저 백업됩니다.",
        ["Statusline_Installed"] = "Claude 사용량 연동이 설치되었습니다. Claude Code 재시작 후 적용됩니다.",
        ["Statusline_AlreadyInstalled"] = "이미 연동되어 있습니다.",
        ["Statusline_Failed"] = "statusline 설치에 실패했습니다 — 아무것도 변경되지 않았습니다. 로그를 확인하세요.",
        ["Statusline_Skipped"] = "취소되었습니다 — Claude 설정은 변경되지 않았습니다.",
        ["Statusline_Unavailable"] = "Claude 연동을 사용할 수 없습니다 (Claude CLI 미탐지 또는 브리지 없음).",
        ["Otel_Failed"] = "텔레메트리 설정 중 오류가 발생했습니다. 로그를 확인하세요.",
        ["Label_Usage"] = "사용량",
        ["Label_Telemetry"] = "토큰 수집",
        ["Codex_LoginOpened"] = "브라우저에서 Codex 로그인을 완료하세요.",
        ["Codex_LoginUnavailable"] = "Codex 연결이 아직 없어 로그인을 시작할 수 없습니다.",

        ["Settings_Export"] = "설정 내보내기",
        ["Settings_Import"] = "설정 가져오기",
        ["Settings_ExportDone"] = "설정을 내보냈습니다.",
        ["Settings_ImportDone"] = "설정을 가져와 적용했습니다.",
        ["Settings_ImportFailed"] = "가져오기 실패: 올바른 PulseBar 설정 파일이 아닙니다.",

        ["Settings_Title"] = "PulseBar 설정",
        ["Settings_General"] = "일반",
        ["Settings_Language"] = "언어",
        ["Settings_Language_Korean"] = "한국어",
        ["Settings_Language_English"] = "English",
        ["Settings_Layout"] = "레이아웃",
        ["Settings_Layout_OneLine"] = "한 줄",
        ["Settings_Layout_TwoLine"] = "두 줄",
        ["Settings_Layout_UltraCompact"] = "초압축",
        ["Settings_Providers"] = "AI 프로바이더",
        ["Settings_Metrics"] = "시스템 자원",
        ["Settings_Save"] = "저장",
        ["Settings_Cancel"] = "취소",

        ["Freshness_Live"] = "실시간",
        ["Freshness_Fresh"] = "최신",
        ["Freshness_Stale"] = "오래됨",
        ["Freshness_Unavailable"] = "사용 불가",
        ["Freshness_AuthenticationRequired"] = "로그인 필요",
        ["Freshness_Error"] = "오류",

        ["Scope_ServerAccount"] = "서버 계정",
        ["Scope_LocalMachine"] = "이 PC에서만",
        ["Scope_LocalSession"] = "이 세션에서만",
        ["Scope_Estimated"] = "추정",

        ["State_Disabled"] = "비활성",
        ["State_Detecting"] = "탐지 중…",
        ["State_ExecutableMissing"] = "실행 파일 없음",
        ["State_AuthenticationRequired"] = "로그인 필요",
        ["State_Connecting"] = "연결 중…",
        ["State_Connected"] = "연결됨",
        ["State_Stale"] = "오래됨",
        ["State_Disconnected"] = "연결 끊김",
        ["State_Error"] = "오류",

        ["Common_LastUpdated"] = "마지막 갱신 {0}",
        ["Common_JustNow"] = "방금",
        ["Common_MinutesAgo"] = "{0}분 전",
        ["Common_HoursAgo"] = "{0}시간 전",
        ["Common_ResetsAt"] = "{0} 리셋",
        ["Common_NotConnected"] = "연동 필요",

        ["Metric_Cpu"] = "CPU",
        ["Metric_Memory"] = "RAM",
        ["Metric_Gpu"] = "GPU",
        ["Metric_Vram"] = "VRAM",
        ["Metric_Disk"] = "디스크",
        ["Metric_Network"] = "네트워크",

        ["Claude_FiveHour"] = "5시간",
        ["Claude_SevenDay"] = "주간",
        ["Claude_FableTokens"] = "Fable 토큰 · 이 PC Claude Code 기준",
        ["Codex_FiveHour"] = "5시간",
        ["Codex_SevenDay"] = "주간",
    };
}
