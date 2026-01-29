; Script di setup per ToolArchiviazioniMilestone - Inno Setup 6
; Target: .NET Framework 4.7.2 (lato sistema), app WinForms

#define MyAppName "Tool Archiviazioni Milestone"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Tua Azienda"
#define MyAppExeName "ToolArchiviazioniMilestone.exe"

[Setup]
; Identificatore univoco dell'applicazione (usa il ProjectGuid del .csproj)
AppId={{5509544E-2037-47AB-9BD2-8F6683B92BA4}}

AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=""
AppSupportURL=""
AppUpdatesURL=""

; Directory di installazione (modificabile dall'utente)
DefaultDirName={pf}\{#MyAppName}
DisableDirPage=no

; Gruppo Programmi nel menu Start
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; File di output del setup
OutputDir=Output
OutputBaseFilename=ToolArchiviazioniMilestone_Setup

; Icona e stile moderno del wizard
SetupIconFile=logo.ico
WizardStyle=modern

Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin

; Lingue (italiano)
[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\\Italian.isl"

; Task opzionali (collegamento desktop)
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; File da installare
; IMPORTANTE: prima di compilare il setup, esegui una build Debug,
; in modo che nella cartella bin\Debug ci sia l'eseguibile aggiornato
; e tutte le dipendenze/copied output.
[Files]
Source: "bin\\Debug\\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

; Collegamenti
[Icons]
; Collegamento nel menu Start
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

; Collegamento sul desktop (opzionale, dipende dal task)
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; Esecuzione dell'app al termine dell'installazione
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
