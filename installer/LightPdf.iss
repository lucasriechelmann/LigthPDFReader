; Per-user installer for LightPdf (no admin rights). Built by tools/Package.ps1, which passes
; /DAppVersion=x.y.z and expects the AOT publish output in ..\publish.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B367DDCD-1E78-42D5-BB40-64449D6A1D1B}
AppName=LightPdf
AppVersion={#AppVersion}
AppVerName=LightPdf {#AppVersion}
AppPublisher=Lucas Riechelmann Ramos
AppPublisherURL=https://github.com/lucasriechelmann/LigthPDFReader
DefaultDirName={localappdata}\Programs\LightPdf
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=LightPdf-{#AppVersion}-setup
SetupIconFile=..\src\LightPdf.App\Assets\app.ico
UninstallDisplayIcon={app}\LightPdf.exe
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Close a running LightPdf before replacing its files.
CloseApplications=yes

[Tasks]
Name: "fileassoc"; Description: "Add LightPdf to ""Open with"" for PDF files (choose the default in Windows Settings)"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\LightPdf"; Filename: "{app}\LightPdf.exe"
Name: "{autodesktop}\LightPdf"; Filename: "{app}\LightPdf.exe"; Tasks: desktopicon

[Run]
; Same code path as the in-app "Make LightPdf the default PDF app" (per-user registry, never overrides the current default).
Filename: "{app}\LightPdf.exe"; Parameters: "--register"; Tasks: fileassoc; Flags: runhidden
Filename: "{app}\LightPdf.exe"; Description: "Launch LightPdf"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\LightPdf.exe"; Parameters: "--unregister"; Flags: runhidden; RunOnceId: "UnregisterPdf"
