; Inno Setup script for Meet Desktop.
; Build: dotnet build -c Release (in the project folder), then compile this file with ISCC.exe.

#define AppName "Google Meet"
; CI passes /DAppVersion=x.y.z from the git tag; keep this in sync with <Version> in MeetApp.csproj.
#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif
#define AppExe "MeetApp.exe"
#define Bin "..\bin\Release\net48"

[Setup]
AppId={{2B2DD92F-7213-44FA-B2E0-E4D0002D8C20}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} Desktop {#AppVersion}
AppPublisher=Meet Desktop
DefaultDirName={autopf}\MeetApp
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default, so no admin prompt; users may choose all-users instead.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible or arm64
ArchitecturesInstallIn64BitMode=x64compatible or arm64
MinVersion=10.0
OutputDir=output
OutputBaseFilename=GoogleMeetSetup
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#Bin}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bin}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bin}\Microsoft.Web.WebView2.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bin}\Microsoft.Web.WebView2.WinForms.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Bin}\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs
; Only extracted (to temp) when WebView2 is missing.
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsWebView2

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Check: NeedsWebView2; Flags: waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; In-app updates run the installer with /SILENT /RELAUNCH=1: reopen the app afterwards (not elevated).
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser; Check: IsRelaunch

[Code]
const
  WebView2Key = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

function HasWebView2Under(Root: Integer; SubKey: String): Boolean;
var
  Ver: String;
begin
  Result := RegQueryStringValue(Root, SubKey, 'pv', Ver) and (Ver <> '') and (Ver <> '0.0.0.0');
end;

function IsRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function NeedsWebView2: Boolean;
begin
  Result := not (HasWebView2Under(HKLM, 'SOFTWARE\WOW6432Node\' + WebView2Key)
              or HasWebView2Under(HKLM, 'SOFTWARE\' + WebView2Key)
              or HasWebView2Under(HKCU, 'Software\' + WebView2Key));
end;

function InitializeSetup: Boolean;
var
  Release: Cardinal;
begin
  Result := True;
  // .NET Framework 4.8 = release 528040+ (built into Windows 10 1903 and later).
  if not (RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
          and (Release >= 528040)) then
  begin
    MsgBox('This app needs .NET Framework 4.8.' + #13#10 +
           'Please run Windows Update or install it from https://dotnet.microsoft.com/download/dotnet-framework/net48',
           mbError, MB_OK);
    Result := False;
  end;
end;
