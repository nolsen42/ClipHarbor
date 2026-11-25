; ClipHarbor installer for Windows (Inno Setup 6). GitHub Actions builds it with:
;   iscc /DAppVersion=1.2.3 /DSourceExe=publish\ClipHarbor.exe /DOutDir=dist packaging\windows\ClipHarbor.iss
;
; It installs for the current user only (no admin prompt) into %LOCALAPPDATA%\Programs\ClipHarbor.
; That folder stays writable, so ClipHarbor can keep updating itself there. The portable .exe is
; released next to this installer and is the same program.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\..\publish\ClipHarbor.exe"
#endif
#ifndef OutDir
  #define OutDir "..\..\dist"
#endif

[Setup]
; Never change AppId: Windows uses it to recognise ClipHarbor for updates and uninstalling.
AppId={{7309A087-F682-411A-8085-7FF30344039B}
AppName=ClipHarbor
AppVersion={#AppVersion}
AppVerName=ClipHarbor {#AppVersion}
AppPublisher=ClipHarbor
AppPublisherURL=https://github.com/nolsen42/ClipHarbor
AppSupportURL=https://github.com/nolsen42/ClipHarbor/issues
AppUpdatesURL=https://github.com/nolsen42/ClipHarbor/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName=ClipHarbor
VersionInfoDescription=ClipHarbor setup
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\ClipHarbor
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutDir}
OutputBaseFilename=ClipHarbor-windows-x64-setup
SetupIconFile=..\..\assets\ClipHarbor.ico
UninstallDisplayIcon={app}\ClipHarbor.exe
UninstallDisplayName=ClipHarbor
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; A running ClipHarbor is closed for the update and started again afterwards.
CloseApplications=force
RestartApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Add a shortcut to the desktop"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "ClipHarbor.exe"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\ClipHarbor"; Filename: "{app}\ClipHarbor.exe"; Comment: "Download videos and audio"
Name: "{userdesktop}\ClipHarbor"; Filename: "{app}\ClipHarbor.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ClipHarbor.exe"; Description: "Start ClipHarbor"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Left behind by ClipHarbor's own updates.
Type: files; Name: "{app}\ClipHarbor.exe.old"
Type: filesandordirs; Name: "{app}\.clipharbor-update"

[Code]
// ClipHarbor writes these itself while running: browser links (clipharbor://) and start at sign-in.
procedure RemoveAppRegistry();
begin
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\clipharbor');
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipHarbor');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveAppRegistry();
    if not UninstallSilent and (MsgBox('Also remove your download list, settings and the downloaded engine (yt-dlp and ffmpeg)?' + #13#10#13#10 +
        'Your downloaded videos are not touched either way.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    begin
      DelTree(ExpandConstant('{userappdata}\ClipHarbor'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\ClipHarbor'), True, True, True);
    end;
  end;
end;
