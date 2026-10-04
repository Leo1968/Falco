; Falco GUI 2.0 安装包（C#/WPF 版，自包含 .NET 8 + ReadyToRun，无运行时依赖）
#define MyAppName "Falco"
#define MyAppVersion "2.4.10"
#define MyAppPublisher "Falco"
#define MyAppExeName "Falco.exe"

[Setup]
AppId={{8F3A7C21-6D5B-4E89-A4C7-2B9F1D6E5A30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Falco
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=falco.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=dist
OutputBaseFilename=Falco-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardImageStretch=no
ChangesAssociations=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoDescription=Falco Windows system optimizer and monitor
VersionInfoProductName=Falco

[Files]
Source: "src\Falco.App\release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "falco.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\earth.png"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "assets\earth-map.jpg"; DestDir: "{app}\assets"; Flags: ignoreversion
Source: "assets\nga.json.gz"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\falco.ico"; Comment: "Falco Windows 系统优化与监控"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\falco.ico"; Tasks: desktopicon; Comment: "Falco Windows 系统优化与监控"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 Falco"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Falco.log"
