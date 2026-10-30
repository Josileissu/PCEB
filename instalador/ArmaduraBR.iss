; Instalador do Armadura BR para GstarCAD 2021 (Inno Setup 6).
; Gerar: instalador\gerar.bat (compila o plugin e roda o ISCC).

; Demonstração (1 hora de uso): instalador\gerar_demo.bat (ISCC /DDemo) → saida\demo_<versão>.exe
#define Raiz ".."
#ifdef Demo
  #define PastaPlugin Raiz + "\bin\demo\GstarCAD2021"
#else
  #define PastaPlugin Raiz + "\bin\GstarCAD2021"
#endif
#define DllPlugin PastaPlugin + "\ArmaduraBR.GstarCAD.dll"
#define Versao GetStringFileInfo(DllPlugin, "ProductVersion")

[Setup]
AppId={{35C90DD1-3F44-449F-A265-05CBC2275B73}
#ifdef Demo
AppName=Armadura BR para GstarCAD 2021 (demonstração)
#else
AppName=Armadura BR para GstarCAD 2021
#endif
AppVersion={#Versao}
AppPublisher=Marquezin
DefaultDirName={autopf}\ArmaduraBR
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=saida
#ifdef Demo
OutputBaseFilename=demo_{#Versao}
#else
OutputBaseFilename=ArmaduraBR_Setup_{#Versao}
#endif
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#ifdef Demo
UninstallDisplayName=Armadura BR para GstarCAD 2021 (demonstração)
#else
UninstallDisplayName=Armadura BR para GstarCAD 2021
#endif

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#Raiz}\bin\carregador\ArmaduraBR.Carregador.dll"; DestDir: "{app}\carregador"; Flags: ignoreversion
Source: "{#PastaPlugin}\*.dll"; DestDir: "{app}\GstarCAD2021"; Flags: ignoreversion
Source: "{#Raiz}\INSTALACAO.txt"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
; cópias de execução feitas pelo carregador
Type: filesandordirs; Name: "{localappdata}\ArmaduraBR\run"

[Code]
const
  ChaveR21 = 'SOFTWARE\Gstarsoft\GstarCAD\R21';

function GstarAberto(): Boolean;
var
  Wmi, Servico, Lista: Variant;
begin
  Result := False;
  try
    Wmi := CreateOleObject('WbemScripting.SWbemLocator');
    Servico := Wmi.ConnectServer('.', 'root\cimv2');
    Lista := Servico.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''gcad.exe''');
    Result := Lista.Count > 0;
  except
  end;
end;

function AguardarGstarFechado(): Boolean;
begin
  Result := True;
  while GstarAberto() do
    if MsgBox('O GstarCAD está aberto. Feche-o e clique em OK para continuar (ou Cancelar para sair).',
              mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

function InitializeSetup(): Boolean;
begin
  Result := AguardarGstarFechado();
end;

function InitializeUninstall(): Boolean;
begin
  Result := AguardarGstarFechado();
end;

{ Idiomas do GstarCAD 2021 já usados por este usuário (en-WW, pt-BR...). }
function Idiomas(): TArrayOfString;
begin
  if not RegGetSubkeyNames(HKCU, ChaveR21, Result) then
    SetArrayLength(Result, 0);
end;

procedure RegistrarCarregamento();
var
  Lista: TArrayOfString;
  I: Integer;
  Chave: String;
begin
  Lista := Idiomas();
  if GetArrayLength(Lista) = 0 then
  begin
    { GstarCAD nunca aberto neste usuário: usa o idioma padrão }
    SetArrayLength(Lista, 1);
    Lista[0] := 'en-WW';
  end;
  for I := 0 to GetArrayLength(Lista) - 1 do
  begin
    Chave := ChaveR21 + '\' + Lista[I] + '\Applications\ArmaduraBR';
    RegWriteStringValue(HKCU, Chave, 'DESCRIPTION', 'Armadura BR');
    RegWriteDWordValue(HKCU, Chave, 'LOADCTRLS', 2);
    RegWriteDWordValue(HKCU, Chave, 'MANAGED', 1);
    RegWriteStringValue(HKCU, Chave, 'LOADER', ExpandConstant('{app}\carregador\ArmaduraBR.Carregador.dll'));
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegistrarCarregamento();
end;

procedure RestaurarAutocompletar(Idioma: String);
var
  Pasta: String;
begin
  Pasta := ExpandConstant('{userappdata}\Gstarsoft\GstarCAD\R21\') + Idioma + '\Support\';
  if FileExists(Pasta + 'alias.pgc.antes_armadura') then
    if FileCopy(Pasta + 'alias.pgc.antes_armadura', Pasta + 'alias.pgc', False) then
      DeleteFile(Pasta + 'alias.pgc.antes_armadura');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Lista: TArrayOfString;
  I: Integer;
  Restaurar: Boolean;
begin
  if CurUninstallStep <> usUninstall then Exit;
  Restaurar := (not UninstallSilent()) and
    (MsgBox('Restaurar a lista de autocompletar original do GstarCAD (alias.pgc)?' + #13#10 +
            'Escolha Não se você editou essa lista depois de instalar o Armadura BR.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES);
  Lista := Idiomas();
  for I := 0 to GetArrayLength(Lista) - 1 do
  begin
    RegDeleteKeyIncludingSubkeys(HKCU, ChaveR21 + '\' + Lista[I] + '\Applications\ArmaduraBR');
    if Restaurar then
      RestaurarAutocompletar(Lista[I]);
  end;
end;

