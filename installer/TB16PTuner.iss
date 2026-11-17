; Inno Setup script for TB16P Tuner. Built by release.ps1 from the self-contained publish
; (the exe carries its own .NET, so nothing else needs installing).
; iscc /DAppVersion=1.0.0 /DSourceDir=<publish folder> /O<output folder> TB16PTuner.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point at the release publish folder
#endif

[Setup]
AppId={{6E0B9C51-2B7D-4C8E-9A3E-7B16D2F4C0A1}
AppName=TB16P Tuner
AppVersion={#AppVersion}
AppVerName=TB16P Tuner {#AppVersion}
AppPublisher=nmhthien58
AppPublisherURL=https://github.com/nmhthien58/Thinkbook16PController
AppSupportURL=https://github.com/nmhthien58/Thinkbook16PController/issues
DefaultDirName={autopf}\TB16P Tuner
DefaultGroupName=TB16P Tuner
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputBaseFilename=TB16PTuner-v{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\TB16PTuner.exe
; Close the running app (it holds this mutex) before files are replaced or removed.
AppMutex=Global\TB16PTuner.SingleInstance,Global\Thinkbook16PController.SingleInstance
CloseApplications=yes

; Inno Setup ships no Vietnamese translation; the wizard is English, the app itself is bilingual.
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
DefenderTask=Add the install folder to Microsoft Defender exclusions (Defender blocks the WinRing0 driver the app needs to set power limits)
LaunchApp=Start TB16P Tuner

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; Opt-in: this changes the user's security settings, so it is never done silently.
Name: "defender"; Description: "{cm:DefenderTask}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\TB16PTuner.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\WinRing0x64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\WinRing0x64.sys"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TB16P Tuner"; Filename: "{app}\TB16PTuner.exe"
Name: "{autodesktop}\TB16P Tuner"; Filename: "{app}\TB16PTuner.exe"; Tasks: desktopicon

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-MpPreference -ExclusionPath '{app}'"""; Flags: runhidden waituntilterminated; Tasks: defender
Filename: "{app}\TB16PTuner.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; The app's "Start with Windows" logon task points at the exe being removed.
Filename: "schtasks.exe"; Parameters: "/Delete /F /TN ""TB16P Tuner"""; Flags: runhidden; RunOnceId: "DeleteLogonTask"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-MpPreference -ExclusionPath '{app}'"""; Flags: runhidden; RunOnceId: "RemoveDefenderExclusion"; Tasks: defender
