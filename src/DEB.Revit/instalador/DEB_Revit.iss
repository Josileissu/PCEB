; Instalador do Detalhador Estrutural Brasileiro para Revit (Inno Setup 6): Revit 2025 e 2026.
; Instala por usuário (sem administrador) em %AppData%\Autodesk\Revit\Addins\<ano>\ (DEB.Revit.addin + pasta DEB.Revit).
; Procura os Revit instalados e já deixa marcados os que encontrou; desmarcar tira o plugin daquela versão.
; Gerar: instalador\gerar.bat (compila 2025 e 2026, assina se houver o certificado "CN=PCEB DEB" e roda o ISCC)
;        → instalador\saida\DEB_Revit_Setup_<versão>.exe
#define Raiz ".."
#define Bin Raiz + "\bin\Release"
#define Versao GetStringFileInfo(Bin + "\R2026\DEB.Revit.dll", "ProductVersion")
#define Addins "{userappdata}\Autodesk\Revit\Addins"

[Setup]
AppId={{9B6E1F3A-4C2D-4F8B-A7E5-3D1C9F0B2E47}
AppName=Detalhador Estrutural Brasileiro para Revit
AppVersion={#Versao}
AppPublisher=Projeto PCEB
DefaultDirName={userappdata}\PCEB\DEB.Revit\instalacao
DisableProgramGroupPage=yes
DisableDirPage=yes
UsePreviousSetupType=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=saida
OutputBaseFilename=DEB_Revit_Setup_{#Versao}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Detalhador Estrutural Brasileiro para Revit

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.SelectComponentsDesc=Para quais versões do Revit instalar o Detalhador Estrutural Brasileiro?
ptbr.SelectComponentsLabel2=As versões encontradas neste computador já estão marcadas. Desmarque as que não quiser; uma versão desmarcada deixa de carregar o plugin.

[Types]
Name: "escolha"; Description: "Escolher as versões"; Flags: iscustom

[Components]
; a ordem importa: InitializeWizard usa os índices 0 e 1
Name: "r2025"; Description: "Revit 2025"
Name: "r2026"; Description: "Revit 2026"

[InstallDelete]
; nome antigo do plugin (StructuralDetailer): mesmo ClientId, não pode carregar duas vezes
Type: files; Name: "{#Addins}\2025\StructuralDetailer.addin"
Type: filesandordirs; Name: "{#Addins}\2025\StructuralDetailer"
Type: files; Name: "{#Addins}\2026\StructuralDetailer.addin"
Type: filesandordirs; Name: "{#Addins}\2026\StructuralDetailer"
; versão desmarcada: tira o plugin dela
Type: files; Name: "{#Addins}\2025\DEB.Revit.addin"; Check: not WizardIsComponentSelected('r2025')
Type: filesandordirs; Name: "{#Addins}\2025\DEB.Revit"; Check: not WizardIsComponentSelected('r2025')
Type: files; Name: "{#Addins}\2026\DEB.Revit.addin"; Check: not WizardIsComponentSelected('r2026')
Type: filesandordirs; Name: "{#Addins}\2026\DEB.Revit"; Check: not WizardIsComponentSelected('r2026')

[Files]
Source: "{#Bin}\R2025\*.dll"; DestDir: "{#Addins}\2025\DEB.Revit"; Components: r2025; Flags: ignoreversion
Source: "{#Bin}\R2025\DEB.Revit.deps.json"; DestDir: "{#Addins}\2025\DEB.Revit"; Components: r2025; Flags: ignoreversion
Source: "{#Raiz}\DEB.Revit.addin"; DestDir: "{#Addins}\2025"; Components: r2025; Flags: ignoreversion
Source: "{#Bin}\R2026\*.dll"; DestDir: "{#Addins}\2026\DEB.Revit"; Components: r2026; Flags: ignoreversion
Source: "{#Bin}\R2026\DEB.Revit.deps.json"; DestDir: "{#Addins}\2026\DEB.Revit"; Components: r2026; Flags: ignoreversion
Source: "{#Raiz}\DEB.Revit.addin"; DestDir: "{#Addins}\2026"; Components: r2026; Flags: ignoreversion
; padrão de prancha de fábrica: só se o usuário ainda não tiver o dele (nunca sobrescreve nem apaga)
Source: "{#Raiz}\Padroes\*.json"; DestDir: "{userappdata}\PCEB\DEB.Revit\Padroes"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#Raiz}\DetalhamentoConfig.exemplo.json"; DestDir: "{userappdata}\PCEB\DEB.Revit"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{#Addins}\2025\DEB.Revit"
Type: filesandordirs; Name: "{#Addins}\2026\DEB.Revit"

[Code]
function RevitInstalado(Ano: String): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf64}\Autodesk\Revit ' + Ano + '\Revit.exe'));
end;

procedure InitializeWizard();
var
  Achou: Boolean;
begin
  { /COMPONENTS= na linha de comando (instalação silenciosa): vale a escolha dela, sem marcar o que foi encontrado por cima }
  if ExpandConstant('{param:COMPONENTS|}') <> '' then Exit;
  Achou := False;
  if RevitInstalado('2025') then
  begin
    WizardForm.ComponentsList.Checked[0] := True;
    WizardForm.ComponentsList.ItemCaption[0] := 'Revit 2025 (encontrado)';
    Achou := True;
  end
  else WizardForm.ComponentsList.Checked[0] := False;
  if RevitInstalado('2026') then
  begin
    WizardForm.ComponentsList.Checked[1] := True;
    WizardForm.ComponentsList.ItemCaption[1] := 'Revit 2026 (encontrado)';
    Achou := True;
  end
  else WizardForm.ComponentsList.Checked[1] := False;
  { nenhum encontrado (instalado em outra pasta): deixa os dois marcados para o usuário escolher }
  if not Achou then
  begin
    WizardForm.ComponentsList.Checked[0] := True;
    WizardForm.ComponentsList.Checked[1] := True;
  end;
end;

function GetCurrentProcessId(): Cardinal;
  external 'GetCurrentProcessId@kernel32.dll stdcall';
function ProcessIdToSessionId(ProcessId: Cardinal; var SessionId: Cardinal): Boolean;
  external 'ProcessIdToSessionId@kernel32.dll stdcall';

{ Só conta o Revit aberto na sessão do Windows de quem instala (a instalação é por usuário). }
function RevitAberto(): Boolean;
var
  Wmi, Servico, Lista: Variant;
  Sessao: Cardinal;
  Filtro: String;
begin
  Result := False;
  Filtro := '';
  if ProcessIdToSessionId(GetCurrentProcessId(), Sessao) then
    Filtro := ' AND SessionId = ' + IntToStr(Sessao);
  try
    Wmi := CreateOleObject('WbemScripting.SWbemLocator');
    Servico := Wmi.ConnectServer('.', 'root\cimv2');
    Lista := Servico.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''Revit.exe''' + Filtro);
    Result := Lista.Count > 0;
  except
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  { com o Revit aberto a DLL fica presa e a cópia falha }
  while RevitAberto() do
    if MsgBox('O Revit está aberto. Feche-o e clique em OK para continuar (ou Cancelar para sair).', mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := 'Instalação cancelada: feche o Revit e rode o instalador de novo.';
      Exit;
    end;
end;
