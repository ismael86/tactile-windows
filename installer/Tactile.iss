; Inno Setup script for Tactile.
;
; Not built directly — run build-installer.ps1 from the repo root, which
; publishes the exe and passes /DAppVersion from <Version> in Tactile.csproj.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName    "Tactile"
#define AppExeName "Tactile.exe"
#define AppPublisher "Ismael"
#define AppUrl     "https://github.com/ismael86/tactile-windows"

[Setup]
; Never change AppId — it is what makes a new version replace the old install
; instead of stacking up a second Add/Remove Programs entry.
AppId={{8A3C1D6E-4F27-4B0A-9E51-2C7D6F0B8A34}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Per-user install. Two reasons, both load-bearing:
;   1. No UAC prompt.
;   2. Tactile writes tactile.json / layouts.json next to its own exe
;      (src/Program.cs, src/Layouts.cs). Program Files is not writable by a
;      normal user, so a per-machine install would break config and layouts.
; With PrivilegesRequired=lowest, {autopf} resolves to {localappdata}\Programs.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes

; The payload is win-x64. It runs fine under emulation on arm64, which
; x64compatible allows, but refuse 32-bit-only machines outright.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Tactile holds a single-instance mutex and locks its own exe while running.
; Restart Manager shuts it down first, so upgrades and uninstalls don't fail
; on a locked file.
CloseApplications=yes
RestartApplications=no

SetupIconFile=..\assets\tactile.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist
OutputBaseFilename=Tactile-Setup-{#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startuplogin"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Additional options:"

[Files]
Source: "..\publish\installer\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Tray app — a Start Menu entry is enough, a desktop icon would just be clutter.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Registry]
; The app manages this same value itself from its tray menu
; (TrayApp.ToggleStartAtLogin). The quoted-path format below has to match what
; the app writes, or the tray menu's checkmark won't reflect reality.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "Tactile"; ValueData: """{app}\{#AppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startuplogin

; Deletion-only entry: creates nothing, but guarantees the Run value is cleaned
; up on uninstall even when the user enabled it later from the tray menu
; rather than through the checkbox above.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "Tactile"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; \
    Flags: nowait postinstall skipifsilent

[Code]
// Tactile is a tray-only app (Application.Run on an ApplicationContext), so it
// has no top-level window to answer Restart Manager's graceful-shutdown
// request. RM correctly spots the locked Tactile.exe but then reports "some
// applications could not be shut down", and Setup aborts — which breaks every
// upgrade and uninstall while the app is running. So close it ourselves first.
//
// A force kill loses nothing: tactile.json and layouts.json are written
// synchronously at the moment the user changes them, never held dirty.
procedure CloseRunningTactile;
var
  ResultCode: Integer;
begin
  // Exit code 128 ("no such process") is the normal case; ignore it.
  if Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM Tactile.exe', '',
          SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Sleep(500);  // let the file handles actually drop before we touch the exe
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningTactile;
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningTactile;
  Result := True;
end;

// Inno only removes files it installed, so the config and layouts the app
// creates at runtime would otherwise be left behind as an orphan folder.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  Dir := ExpandConstant('{app}');
  if not (FileExists(Dir + '\tactile.json') or FileExists(Dir + '\layouts.json')) then
    Exit;

  // A silent uninstall must not block on a message box; keep the data.
  if UninstallSilent then
    Exit;

  if MsgBox('Also remove your Tactile settings and saved layouts?' + #13#10#13#10 +
            'Choose No to keep tactile.json and layouts.json for a future reinstall.',
            mbConfirmation, MB_YESNO) = IDYES then
  begin
    DeleteFile(Dir + '\tactile.json');
    DeleteFile(Dir + '\layouts.json');
    DeleteFile(Dir + '\layouts.json.bak');
  end;
end;
