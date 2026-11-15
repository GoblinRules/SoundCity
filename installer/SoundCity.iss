#ifndef AppVersion
  #error AppVersion must be supplied by Build-Release.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by Build-Release.ps1
#endif

[Setup]
AppId={{732BB69A-247E-438C-A1C4-97B0B3AA8F6E}
AppName=SoundCity
AppVersion={#AppVersion}
AppVerName=SoundCity {#AppVersion}
AppPublisher=SoundCity contributors
AppPublisherURL=https://github.com/GoblinRules/SoundCity
AppSupportURL=https://github.com/GoblinRules/SoundCity/issues
AppUpdatesURL=https://github.com/GoblinRules/SoundCity/releases
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\SoundCity
DefaultGroupName=SoundCity
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UninstallDisplayIcon={app}\SoundCity.exe
SetupIconFile={#ProjectRoot}\src\SoundCity.App\Assets\SoundCity.ico
LicenseFile={#PublishDir}\LICENSE.txt
OutputDir={#ReleaseDir}
OutputBaseFilename=SoundCity-{#AppVersion}-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dark
WizardImageFile={#ArtworkFile}
WizardSmallImageFile={#ProjectRoot}\assets\soundcity-icon.png
WizardImageBackColor=$100B09
WizardSmallImageBackColor=$100B09
DisableWelcomePage=no
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.log,*.sqlite,*.sqlite-*,*.db,*.db-*,.env,.env.*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SoundCity"; Filename: "{app}\SoundCity.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\SoundCity"; Filename: "{app}\SoundCity.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\SoundCity.exe"; Description: "Open SoundCity"; Flags: nowait postinstall skipifsilent

; User profiles and APO configuration are outside {app} and intentionally survive uninstall.
