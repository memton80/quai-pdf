; Installateur de Quai PDF (Inno Setup 6), repris de celui de Quai : un seul exécutable, installation par
; utilisateur (sans droits administrateur), désinstallation depuis « Applications installées » de Windows.
;
; Compilé par le CI (.github/workflows/build.yml) :
;   ISCC.exe /DAppVersion=0.1.0 /DAppNumericVersion=0.1.0 /DArch=x64 /DSourceDir=..\publish\x64 /DOutputDir=..\dist installer\QuaiPdf.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppNumericVersion
  #define AppNumericVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\" + Arch
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Quai PDF"
#define AppExe "QuaiPdf.exe"
; Dossier des données (%LOCALAPPDATA%\QuaiPdf : réglages, journaux), voir AppPaths.cs.
#define DataDir "QuaiPdf"
#define ProgId "QuaiPdf.Document"
#define AppUrl "https://github.com/memton80/quai-pdf"

[Setup]
; Identifiant stable : ne jamais le changer, il relie les mises à jour et la désinstallation.
AppId={{881A2EB2-5817-4B9E-AA96-BEBBED0A8E2E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppNumericVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Installation de {#AppName}

; Par utilisateur : %LOCALAPPDATA%\Programs\Quai PDF, aucune élévation.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=no
UsePreviousAppDir=yes
MinVersion=10.0.17763

#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

OutputDir={#OutputDir}
OutputBaseFilename=QuaiPdf-Setup-{#Arch}
SetupIconFile=..\src\QuaiPdf.App\Assets\QuaiPdf.ico
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Mise à jour par-dessus une version ouverte : l'installateur propose de fermer Quai PDF.
CloseApplications=yes
RestartApplications=no

; « Ouvrir avec » pour les PDF (sans prendre la place du lecteur par défaut, que Windows laisse choisir à l'utilisateur).
ChangesAssociations=yes

UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
french.PdfDocument=Document PDF
french.RemoveUserData=Supprimer aussi vos réglages et journaux de Quai PDF ?%n%nVos documents PDF ne sont jamais touchés.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Type de document propre à Quai PDF, proposé dans « Ouvrir avec » pour les fichiers .pdf.
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "{cm:PdfDocument}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Mise à jour lancée par Quai PDF lui-même (/SILENT /RELAUNCH=1) : la nouvelle version redémarre une fois installée.
Filename: "{app}\{#AppExe}"; Parameters: "--after-update"; WorkingDir: "{app}"; Flags: nowait; Check: RelaunchRequested

[UninstallDelete]
; Journaux : jamais conservés, ce ne sont pas des données de l'utilisateur.
Type: filesandordirs; Name: "{localappdata}\{#DataDir}\logs"
Type: filesandordirs; Name: "{localappdata}\{#DataDir}\updates"
Type: dirifempty; Name: "{localappdata}\{#DataDir}"

[Code]
var
  RemoveUserData: Boolean;

// Installation demandée par le bouton « Mettre à jour » de Quai PDF : relancer l'application à la fin.
function RelaunchRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

// Désinstallation : on demande s'il faut aussi effacer les réglages (non par défaut).
// En mode silencieux, le paramètre /PURGE=1 répond « oui ».
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if UninstallSilent() then
    RemoveUserData := ExpandConstant('{param:PURGE|0}') = '1'
  else
    RemoveUserData := MsgBox(CustomMessage('RemoveUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveUserData then
    DelTree(ExpandConstant('{localappdata}\{#DataDir}'), True, True, True);
end;
