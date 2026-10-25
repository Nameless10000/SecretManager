#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{6D0F5C26-8EE1-4D1B-B00E-A0DF579FBBF8}
AppName=SecretManager
AppVersion={#AppVersion}
AppPublisher=Nameless10000
AppPublisherURL=https://github.com/Nameless10000/SecretManager
AppSupportURL=https://github.com/Nameless10000/SecretManager/issues
DefaultDirName={localappdata}\Programs\SecretManager
DefaultGroupName=SecretManager
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=SecretManager-Setup
SetupIconFile=..\src\SecretManager.App\Assets\app.ico
UninstallDisplayIcon={app}\Assets\app.ico
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
CloseApplications=yes
RestartApplications=no
AppMutex=Local\SecretManager.6D0F5C26
SetupMutex=SecretManager.Setup
VersionInfoVersion={#AppVersion}
VersionInfoDescription=SecretManager for Windows

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\SecretManager"; Filename: "{app}\SecretManager.App.exe"; IconFilename: "{app}\Assets\app.ico"
Name: "{autodesktop}\SecretManager"; Filename: "{app}\SecretManager.App.exe"; IconFilename: "{app}\Assets\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\SecretManager.App.exe"; Description: "Запустить SecretManager"; Flags: nowait postinstall skipifsilent

; Intentionally no UninstallDelete for LocalAppData\SecretManager: user vaults survive uninstall.
