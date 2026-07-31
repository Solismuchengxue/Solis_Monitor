#ifndef AppVersion
  #error AppVersion must be provided by tools\Build-Installer.ps1
#endif
#ifndef AppDisplayVersion
  #define AppDisplayVersion AppVersion
#endif
#ifndef VersionInfoVersion
  #error VersionInfoVersion must be provided by tools\Build-Installer.ps1
#endif
#ifndef SourceDir
  #error SourceDir must be provided by tools\Build-Installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by tools\Build-Installer.ps1
#endif
#ifndef SetupIconFile
  #error SetupIconFile must be provided by tools\Build-Installer.ps1
#endif

#define AppName "Solis Monitor"
#define AppExeName "SolisMonitor.exe"

[Setup]
AppId={{A25B1E83-9244-4D82-8846-A2B3B413886D}
AppName={#AppName}
AppVersion={#AppDisplayVersion}
AppVerName={#AppName} {#AppDisplayVersion}
AppPublisher=Solis Monitor
VersionInfoVersion={#VersionInfoVersion}
DefaultDirName={autopf}\Solis Monitor
DefaultGroupName=Solis Monitor
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=SolisMonitor-{#AppVersion}-win-x64-setup
SetupIconFile={#SetupIconFile}
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile={#SourceDir}\LICENSE
Compression=lzma2/fast
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern dynamic
CloseApplications=force
CloseApplicationsFilter=SolisMonitor.exe,SolisMonitor.NotificationHost.exe
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Solis Monitor"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Solis Monitor"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 Solis Monitor"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM SolisMonitor.exe /T /F"; Flags: runhidden waituntilterminated; RunOnceId: "StopSolisMonitor"
Filename: "{app}\NotificationHost\SolisMonitor.NotificationHost.exe"; Parameters: "--unregister-all"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterNotifications"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""SolisMonitor"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteCurrentStartupTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""LibreHardwareMonitor"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteLegacyStartupTask"
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""SolisMonitor"" /f"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteCurrentStartupValue"
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""LibreHardwareMonitor"" /f"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteLegacyStartupValue"
