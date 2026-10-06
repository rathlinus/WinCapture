; The WinCapture setup program, compiled by scripts\release.ps1 with Inno Setup:
;
;   ISCC /DAppVersion=1.2.0 /DSourceDir=<artifacts\app> /DOutputDir=<artifacts\release> WinCapture.iss
;
; WinCapture is a plain folder of programs, so setup copies that folder for the
; current user, adds it to the Start menu and, if wanted, to the programs that
; start at sign-in. Its uninstaller gives Print Screen back to Windows and
; removes what WinCapture registered.

#ifndef AppVersion
  #error Pass the version with /DAppVersion=<major.minor.patch>
#endif
#ifndef SourceDir
  #error Pass the folder with WinCapture.exe and the Editor folder with /DSourceDir=<path>
#endif
#ifndef OutputDir
  #define OutputDir SourceDir
#endif

[Setup]
AppId={{6F1D2A8C-3B47-4E9A-A5C2-8D0B7E41F396}
AppName=WinCapture
AppVersion={#AppVersion}
AppVerName=WinCapture {#AppVersion}
AppPublisher=WinCapture
AppPublisherURL=https://github.com/rathlinus/WinCapture
AppSupportURL=https://github.com/rathlinus/WinCapture/issues
VersionInfoVersion={#AppVersion}
VersionInfoDescription=WinCapture Setup

OutputDir={#OutputDir}
OutputBaseFilename=WinCapture-{#AppVersion}-Setup
SetupIconFile=..\src\WinCapture.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\WinCapture.exe
WizardStyle=modern
WizardImageFile=installer-side.bmp
WizardSmallImageFile=installer-small.bmp

; Installed for the current user only: everything WinCapture changes, the Print
; Screen setting included, belongs to that user. No administrator is needed.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\WinCapture
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes

; A running WinCapture keeps its files locked. Setup asks it to close, and the
; finish page starts the new version.
CloseApplications=force
RestartApplications=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This installs [name/ver] on your computer.%n%nWinCapture takes over the Print Screen key from Snipping Tool. Uninstalling it gives the key back.
FinishedHeadingLabel=WinCapture is installed
FinishedLabelNoIcons=Press Print Screen to capture. WinCapture runs in the notification area.%n%nTo remove WinCapture later, open Settings, go to Apps, then Installed apps.
FinishedLabel=Press Print Screen to capture. WinCapture runs in the notification area.%n%nTo remove WinCapture later, open Settings, go to Apps, then Installed apps.

[Tasks]
Name: "autostart"; Description: "Start WinCapture when I sign in"

[InstallDelete]
; The editor's files differ from version to version; leftovers of an older one
; must not mix with the new.
Type: filesandordirs; Name: "{app}\Editor"

[Files]
Source: "{#SourceDir}\WinCapture.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\AppIcon.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Editor\*"; DestDir: "{app}\Editor"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\WinCapture"; Filename: "{app}\WinCapture.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinCapture"; ValueData: """{app}\WinCapture.exe"""; Tasks: autostart; Flags: uninsdeletevalue
; WinCapture writes these itself at every start; listing them here lets the
; uninstaller remove them.
Root: HKCU; Subkey: "Software\Classes\wincapture"; Flags: uninsdeletekey dontcreatekey
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\WinCapture"; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\WinCapture.exe"; Description: "Start WinCapture"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Without /F, taskkill asks the program to close, as Exit in its tray menu does,
; so the tray icon goes away with it.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM WinCapture.exe"; Flags: runhidden waituntilterminated; RunOnceId: "StopWinCapture"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM WinCapture.Editor.exe /F"; Flags: runhidden waituntilterminated; RunOnceId: "StopEditor"
Filename: "{app}\WinCapture.exe"; Parameters: "--restore-printscreen"; Flags: runhidden waituntilterminated; RunOnceId: "RestorePrintScreen"
; In case it had not finished closing by now; its files are deleted next.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM WinCapture.exe /F"; Flags: runhidden waituntilterminated; RunOnceId: "ForceStopWinCapture"
