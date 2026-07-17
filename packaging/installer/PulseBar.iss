; PulseBar per-user installer (Inno Setup 6).
; No admin rights: installs under %LOCALAPPDATA%\Programs\PulseBar (spec §13.2).
; Build with: packaging\installer\build-installer.ps1 (stages the publish output first).

#define MyAppName "PulseBar"
#define MyAppVersion "0.1.1"
#define MyAppExeName "PulseBar.exe"
#ifndef StageDir
  #define StageDir "..\..\artifacts\portable\PulseBar"
#endif

[Setup]
AppId={{9C4B7A31-5B0E-4F44-9D0B-3A6F2C81D5E7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\..\artifacts
OutputBaseFilename=PulseBar-setup-win-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SolidCompression=yes
Compression=lzma2
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "startup"; Description: "{cm:AutoStartProgram,{#MyAppName}}"; Flags: unchecked

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; Per-user autostart, matching the in-app toggle (HKCU Run, no admin).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "PulseBar"; ValueData: """{app}\{#MyAppExeName}"""; \
    Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillPulseBar"

; User data under %LOCALAPPDATA%\PulseBar (config, token history, logs) is
; intentionally preserved on uninstall; delete it manually to remove everything.
