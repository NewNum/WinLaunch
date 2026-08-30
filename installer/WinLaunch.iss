; WinLaunch installer (Inno Setup 6)
; Build with: .\installer\build-installer.ps1

#ifndef MyAppVersion
  #define MyAppVersion "0.7.4.2"
#endif

#ifndef RuntimeCheckEnabled
  #define RuntimeCheckEnabled "1"
#endif

#define MyAppName "WinLaunch"
#define MyAppPublisher "WinLaunch"
#define MyAppURL "https://github.com/NewNum/WinLaunch"
#define MyAppExeName "WinLaunch.exe"
#define PublishDir "..\publish\win-x64"

[Setup]
AppId={{A7C4E2F1-8B3D-4A9E-9F6C-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=output
OutputBaseFilename=WinLaunch-Setup-{#MyAppVersion}
SetupIconFile=..\WinLaunch\Images\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function DesktopRuntime10Installed: Boolean;
var
  Version: String;
begin
  if RegQueryStringValue(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', '10.0', Version) then
  begin
    Result := True;
    Exit;
  end;

  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', '10.0', Version) then
  begin
    Result := True;
    Exit;
  end;

  Result := False;
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if '{#RuntimeCheckEnabled}' = '1' then
  begin
    if not DesktopRuntime10Installed then
    begin
      if MsgBox('未检测到 .NET 10 桌面运行时。' + #13#10 + #13#10 +
        'WinLaunch 需要该运行时才能运行。是否打开下载页面？',
        mbConfirmation, MB_YESNO) = IDYES then
      begin
        ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOW, ewNoWait, ResultCode);
      end;
      Result := False;
    end;
  end;
end;
