; Instalador do Armadura BR para AutoCAD 2026 (Inno Setup 6).
; Gera um bundle do Autoloader em %AppData%\Autodesk\ApplicationPlugins\ArmaduraBR.bundle.
; Gerar: instalador\gerar_autocad.bat (compila o plugin e roda o ISCC).

#define Raiz ".."
#define DllPlugin Raiz + "\bin\AutoCAD2026\ArmaduraBR.AutoCAD.dll"
#define Versao GetStringFileInfo(DllPlugin, "ProductVersion")

[Setup]
AppId={{6B7A0FEC-1FB7-48F2-A698-A0CEAA1EBF03}
AppName=Armadura BR para AutoCAD 2026
AppVersion={#Versao}
AppPublisher=Marquezin
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\ArmaduraBR.bundle
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=saida
OutputBaseFilename=ArmaduraBR_AutoCAD_Setup_{#Versao}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Armadura BR para AutoCAD 2026

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#Raiz}\instalador\AutoCAD2026.bundle\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Raiz}\bin\AutoCAD2026\ArmaduraBR.AutoCAD.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#Raiz}\bin\AutoCAD2026\Armadura.Core.dll"; DestDir: "{app}\Contents\Windows"; Flags: ignoreversion
Source: "{#Raiz}\INSTALACAO_AUTOCAD.txt"; DestDir: "{app}\Contents\Help"; Flags: ignoreversion

[UninstallDelete]
; remove o bundle inteiro (o Autoloader procura a pasta na próxima abertura)
Type: filesandordirs; Name: "{app}"

[Code]
function AcabAberto(): Boolean;
var
  Wmi, Servico, Lista: Variant;
begin
  Result := False;
  try
    Wmi := CreateOleObject('WbemScripting.SWbemLocator');
    Servico := Wmi.ConnectServer('.', 'root\cimv2');
    Lista := Servico.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''acad.exe''');
    Result := Lista.Count > 0;
  except
  end;
end;

function AguardarAcabFechado(): Boolean;
begin
  Result := True;
  while AcabAberto() do
    if MsgBox('O AutoCAD está aberto. Feche-o e clique em OK para continuar (ou Cancelar para sair).',
              mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

function InitializeSetup(): Boolean;
begin
  Result := AguardarAcabFechado();
end;

function InitializeUninstall(): Boolean;
begin
  Result := AguardarAcabFechado();
end;
