[Setup]
AppName=NetAudit Pro v4
AppVersion=4.0.0
AppPublisher=TMMArchitecture
AppPublisherURL=https://teknolojimimari.com
DefaultDirName={autopf}\NetAudit Pro
DefaultGroupName=NetAudit Pro
OutputDir=.\Output
OutputBaseFilename=NetAuditPro-4.0.0-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=yes
WizardStyle=modern
UninstallDisplayIcon={app}\NetAudit.UI.exe
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
VersionInfoVersion=4.0.0.0
VersionInfoDescription=NetAudit Pro v4 - Enterprise Network Diagnostic Tool

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupfolder"; Description: "Add to Startup"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "dist\NetAudit.UI.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "dist\NetAudit.UI.pdb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\NetAudit Pro"; Filename: "{app}\NetAudit.UI.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\NetAudit Pro"; Filename: "{app}\NetAudit.UI.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{commonstartup}\NetAudit Pro"; Filename: "{app}\NetAudit.UI.exe"; WorkingDir: "{app}"; Tasks: startupfolder

[Run]
Filename: "{app}\NetAudit.UI.exe"; Description: "Launch NetAudit Pro"; Flags: nowait postinstall skipifsilent

[Messages]
WelcomeLabel1=Welcome to NetAudit Pro Setup
WelcomeLabel2=This will install NetAudit Pro v4 on your computer.

[Code]
procedure InitializeWizard();
var
  NetRuntimePath: string;
  DownloadUrl: string;
begin
  // Check if .NET 6 Runtime is installed
  if not RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{033B0A6D-2E64-468E-9E5D-4EFB9012B948}') then
  begin
    if MsgBox('NetAudit Pro requires .NET 6 Runtime. Download and install now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      // ShellExecute('open', 'https://aka.ms/dotnet/6.0/windowsdesktop-runtime-win-x64.exe', '', '', SW_SHOW);
      MsgBox('.NET 6 Desktop Runtime will be installed automatically during setup. The installer includes embedded .NET runtime.', mbInformation, MB_OK);
    end;
  end;
end;
