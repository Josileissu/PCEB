; Instalador único do Armadura BR (Inno Setup 6): GstarCAD 2021 a 2027, AutoCAD 2021 a 2027 e ZWCAD 2024 a 2026.
; Procura os programas no registro e já deixa marcados os que encontrou; o usuário pode desmarcar.
; Gerar: instalador\gerar.bat (compila tudo e roda o ISCC) → saida\ArmaduraBR_Setup_<versão>.exe
; Demonstração (1 hora de uso): instalador\gerar_demo.bat (ISCC /DDemo) → saida\demo_<versão>.exe
#define Raiz ".."
#ifdef Demo
  #define Bin Raiz + "\bin\demo"
#else
  #define Bin Raiz + "\bin"
#endif
#define Versao GetStringFileInfo(Bin + "\GstarCAD2021\ArmaduraBR.GstarCAD.dll", "ProductVersion")
#define Bundle "{userappdata}\Autodesk\ApplicationPlugins\ArmaduraBR.bundle"

[Setup]
; mesmo AppId do instalador antigo do GstarCAD: a versão nova instala por cima dele
AppId={{35C90DD1-3F44-449F-A265-05CBC2275B73}
#ifdef Demo
AppName=Armadura BR (demonstração)
#else
AppName=Armadura BR
#endif
AppVersion={#Versao}
AppPublisher=Marquezin
DefaultDirName={autopf}\ArmaduraBR
DisableProgramGroupPage=yes
DisableDirPage=yes
; a lista sempre parte do que foi encontrado neste computador, não da instalação anterior (tipo e componentes)
UsePreviousSetupType=no
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
UninstallDisplayName=Armadura BR (demonstração)
#else
UninstallDisplayName=Armadura BR
#endif

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.SelectComponentsDesc=Para quais programas instalar o Armadura BR?
ptbr.SelectComponentsLabel2=Os programas encontrados neste computador já estão marcados. Desmarque os que não quiser; um programa desmarcado deixa de carregar o Armadura BR.

[Types]
Name: "escolha"; Description: "Escolher os programas"; Flags: iscustom

[Components]
; a ordem importa: InitializeWizard usa os índices 0, 1 e 2
Name: "gstarcad"; Description: "GstarCAD 2021 a 2027"
Name: "autocad"; Description: "AutoCAD 2021 a 2027"
Name: "zwcad"; Description: "ZWCAD 2024 a 2026"

[Files]
; GstarCAD 2021: o carregador (registro Applications) faz NETLOAD de uma cópia do plugin
Source: "{#Raiz}\bin\carregador\ArmaduraBR.Carregador.dll"; DestDir: "{app}\carregador"; Components: gstarcad; Flags: ignoreversion
Source: "{#Bin}\GstarCAD2021\*.dll"; DestDir: "{app}\GstarCAD2021"; Components: gstarcad; Flags: ignoreversion
; GstarCAD 2022 a 2027: o registro Applications aponta direto para a DLL da versão
Source: "{#Bin}\GstarCAD2022\*.dll"; DestDir: "{app}\GstarCAD2022"; Components: gstarcad; Flags: ignoreversion
Source: "{#Bin}\GstarCAD2023\*.dll"; DestDir: "{app}\GstarCAD2023"; Components: gstarcad; Flags: ignoreversion
Source: "{#Bin}\GstarCAD2024\*.dll"; DestDir: "{app}\GstarCAD2024"; Components: gstarcad; Flags: ignoreversion
Source: "{#Bin}\GstarCAD2025\*.dll"; DestDir: "{app}\GstarCAD2025"; Components: gstarcad; Flags: ignoreversion
Source: "{#Bin}\GstarCAD2026\*.dll"; DestDir: "{app}\GstarCAD2026"; Components: gstarcad; Flags: ignoreversion
Source: "{#Bin}\GstarCAD2027\*.dll"; DestDir: "{app}\GstarCAD2027"; Components: gstarcad; Flags: ignoreversion
Source: "{#Raiz}\INSTALACAO.txt"; DestDir: "{app}"; Flags: ignoreversion

; AutoCAD: bundle do Autoloader, uma DLL compilada para cada versão (PackageContents.xml escolhe pela série)
Source: "{#Raiz}\instalador\AutoCAD.bundle\PackageContents.xml"; DestDir: "{#Bundle}"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2021\ArmaduraBR.AutoCAD.dll"; DestDir: "{#Bundle}\Contents\Windows\2021"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2021\Armadura.Core.dll"; DestDir: "{#Bundle}\Contents\Windows\2021"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2022\ArmaduraBR.AutoCAD.dll"; DestDir: "{#Bundle}\Contents\Windows\2022"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2022\Armadura.Core.dll"; DestDir: "{#Bundle}\Contents\Windows\2022"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2023\ArmaduraBR.AutoCAD.dll"; DestDir: "{#Bundle}\Contents\Windows\2023"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2023\Armadura.Core.dll"; DestDir: "{#Bundle}\Contents\Windows\2023"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2024\ArmaduraBR.AutoCAD.dll"; DestDir: "{#Bundle}\Contents\Windows\2024"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2024\Armadura.Core.dll"; DestDir: "{#Bundle}\Contents\Windows\2024"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2025\ArmaduraBR.AutoCAD.dll"; DestDir: "{#Bundle}\Contents\Windows\2025"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2025\Armadura.Core.dll"; DestDir: "{#Bundle}\Contents\Windows\2025"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2026\ArmaduraBR.AutoCAD.dll"; DestDir: "{#Bundle}\Contents\Windows\2026"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2026\Armadura.Core.dll"; DestDir: "{#Bundle}\Contents\Windows\2026"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2027\ArmaduraBR.AutoCAD.dll"; DestDir: "{#Bundle}\Contents\Windows\2027"; Components: autocad; Flags: ignoreversion
Source: "{#Bin}\AutoCAD2027\Armadura.Core.dll"; DestDir: "{#Bundle}\Contents\Windows\2027"; Components: autocad; Flags: ignoreversion
Source: "{#Raiz}\INSTALACAO_AUTOCAD.txt"; DestDir: "{#Bundle}\Contents\Help"; Components: autocad; Flags: ignoreversion

; ZWCAD: o registro Applications aponta direto para a DLL da versão
Source: "{#Bin}\ZWCAD2024\*.dll"; DestDir: "{app}\ZWCAD2024"; Components: zwcad; Flags: ignoreversion
Source: "{#Bin}\ZWCAD2025\*.dll"; DestDir: "{app}\ZWCAD2025"; Components: zwcad; Flags: ignoreversion
Source: "{#Bin}\ZWCAD2026\*.dll"; DestDir: "{app}\ZWCAD2026"; Components: zwcad; Flags: ignoreversion

[InstallDelete]
; bundle do instalador antigo só do AutoCAD 2026: DLLs direto em Contents\Windows
Type: files; Name: "{#Bundle}\Contents\Windows\*.dll"

[UninstallDelete]
; cópias de execução feitas pelo carregador do GstarCAD
Type: filesandordirs; Name: "{localappdata}\ArmaduraBR\run"
; o bundle inteiro (o Autoloader do AutoCAD deixa de carregar na próxima abertura)
Type: filesandordirs; Name: "{#Bundle}"

[Code]
const
  ChaveGstar = 'SOFTWARE\Gstarsoft\GstarCAD\';
  ChaveZw = 'SOFTWARE\ZWSOFT\ZWCAD\';
  ChaveAcad = 'SOFTWARE\Autodesk\AutoCAD\';
  { instalador antigo só do AutoCAD (AppId próprio): é desinstalado antes, para não apagar o bundle depois }
  ChaveAcadAntigo = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6B7A0FEC-1FB7-48F2-A698-A0CEAA1EBF03}_is1';
  NomeApp = 'ArmaduraBR';

var
  GstarAchados, AcadAchados, ZwAchados: String;

{ ---------- versões de cada programa ---------- }

function AnosGstar(): TArrayOfString;
begin
  Result := ['2021', '2022', '2023', '2024', '2025', '2026', '2027'];
end;

{ chave do GstarCAD de um ano: 2021 → SOFTWARE\Gstarsoft\GstarCAD\R21 }
function ChaveGstarAno(Ano: String): String;
begin
  Result := ChaveGstar + 'R' + Copy(Ano, 3, 2);
end;

function AnosZw(): TArrayOfString;
begin
  Result := ['2024', '2025', '2026'];
end;

{ ---------- detecção ---------- }

function ChaveExiste(Chave: String): Boolean;
begin
  Result := RegKeyExists(HKLM, Chave) or RegKeyExists(HKCU, Chave);
end;

procedure Acrescentar(var Lista: String; Ano: String);
begin
  if Lista <> '' then Lista := Lista + ', ';
  Lista := Lista + Ano;
end;

{ Versões do GstarCAD 2021-2027 encontradas, ex.: '2021, 2025' (vazio = nenhuma). }
function VersoesGstar(): String;
var
  Anos: TArrayOfString;
  I: Integer;
begin
  Result := '';
  Anos := AnosGstar();
  for I := 0 to GetArrayLength(Anos) - 1 do
    if ChaveExiste(ChaveGstarAno(Anos[I])) then Acrescentar(Result, Anos[I]);
end;

{ Versões do AutoCAD 2021-2027 encontradas. }
function VersoesAcad(): String;
var
  Series, Anos: TArrayOfString;
  I: Integer;
begin
  Result := '';
  Series := ['R24.0', 'R24.1', 'R24.2', 'R24.3', 'R25.0', 'R25.1', 'R26.0'];
  Anos := ['2021', '2022', '2023', '2024', '2025', '2026', '2027'];
  for I := 0 to GetArrayLength(Series) - 1 do
    if ChaveExiste(ChaveAcad + Series[I]) then Acrescentar(Result, Anos[I]);
end;

{ Versões do ZWCAD 2024-2026 encontradas. }
function VersoesZw(): String;
var
  Anos: TArrayOfString;
  I: Integer;
begin
  Result := '';
  Anos := AnosZw();
  for I := 0 to GetArrayLength(Anos) - 1 do
    if ChaveExiste(ChaveZw + Anos[I]) then Acrescentar(Result, Anos[I]);
end;

function Legenda(Nome, Achados: String): String;
begin
  if Achados <> '' then
    Result := Nome + '  (encontrado: ' + Achados + ')'
  else
    Result := Nome + '  (não encontrado)';
end;

procedure InitializeWizard();
var
  Lista: TNewCheckListBox;
  Marcar: String;
begin
  GstarAchados := VersoesGstar();
  AcadAchados := VersoesAcad();
  ZwAchados := VersoesZw();
  Lista := WizardForm.ComponentsList;
  Lista.ItemCaption[0] := Legenda('GstarCAD 2021 a 2027', GstarAchados);
  Lista.ItemCaption[1] := Legenda('AutoCAD 2021 a 2027', AcadAchados);
  Lista.ItemCaption[2] := Legenda('ZWCAD 2024 a 2026', ZwAchados);

  if GstarAchados <> '' then Marcar := 'gstarcad' else Marcar := '!gstarcad';
  if AcadAchados <> '' then Marcar := Marcar + ',autocad' else Marcar := Marcar + ',!autocad';
  if ZwAchados <> '' then Marcar := Marcar + ',zwcad' else Marcar := Marcar + ',!zwcad';
  WizardSelectComponents(Marcar);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and (WizardSelectedComponents(False) = '') then
  begin
    MsgBox('Marque pelo menos um programa.', mbError, MB_OK);
    Result := False;
  end;
end;

{ ---------- programas abertos ---------- }

function ProcessoAberto(Exe: String): Boolean;
var
  Wmi, Servico, Lista: Variant;
begin
  Result := False;
  try
    Wmi := CreateOleObject('WbemScripting.SWbemLocator');
    Servico := Wmi.ConnectServer('.', 'root\cimv2');
    Lista := Servico.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''' + Exe + '''');
    Result := Lista.Count > 0;
  except
  end;
end;

{ Espera o programa ser fechado; False = o usuário cancelou. }
function AguardarFechado(Exe, Nome: String): Boolean;
begin
  Result := True;
  while ProcessoAberto(Exe) do
    if MsgBox('O ' + Nome + ' está aberto. Feche-o e clique em OK para continuar (ou Cancelar para sair).',
              mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

function TodosFechados(): Boolean;
begin
  Result := AguardarFechado('gcad.exe', 'GstarCAD') and
            AguardarFechado('acad.exe', 'AutoCAD') and
            AguardarFechado('zwcad.exe', 'ZWCAD');
end;

procedure RemoverInstaladorAcadAntigo();
var
  Desinstalar: String;
  Codigo: Integer;
begin
  if RegQueryStringValue(HKCU, ChaveAcadAntigo, 'UninstallString', Desinstalar) then
    Exec(RemoveQuotes(Desinstalar), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE,
         ewWaitUntilTerminated, Codigo);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  { desmarcar também mexe no programa (tira o carregamento), então ele também precisa estar fechado }
  if not TodosFechados() then
  begin
    Result := 'Instalação cancelada: feche os programas e rode o instalador de novo.';
    Exit;
  end;
  RemoverInstaladorAcadAntigo();
end;

{ ---------- registro do carregamento (GstarCAD e ZWCAD: chave Applications por idioma) ---------- }

{ Idiomas (en-WW, pt-BR...) de uma chave do CAD; só nomes no formato xx-YY. }
function Idiomas(Raiz: Integer; Chave: String): TArrayOfString;
var
  Todas: TArrayOfString;
  I, N: Integer;
begin
  SetArrayLength(Result, 0);
  if not RegGetSubkeyNames(Raiz, Chave, Todas) then Exit;
  N := 0;
  for I := 0 to GetArrayLength(Todas) - 1 do
    if (Length(Todas[I]) = 5) and (Todas[I][3] = '-') then
    begin
      SetArrayLength(Result, N + 1);
      Result[N] := Todas[I];
      N := N + 1;
    end;
end;

{ Idiomas para gravar: os já usados por este usuário; senão os instalados; senão o padrão. }
function IdiomasParaGravar(Chave, Padrao: String): TArrayOfString;
begin
  Result := Idiomas(HKCU, Chave);
  if GetArrayLength(Result) = 0 then Result := Idiomas(HKLM, Chave);
  if GetArrayLength(Result) = 0 then Result := [Padrao];
end;

{ Grava a aplicação em todos os idiomas daquela versão do CAD. }
procedure GravarAplicacao(Chave, Padrao, Loader: String);
var
  Lista: TArrayOfString;
  I: Integer;
  App: String;
begin
  Lista := IdiomasParaGravar(Chave, Padrao);
  for I := 0 to GetArrayLength(Lista) - 1 do
  begin
    App := Chave + '\' + Lista[I] + '\Applications\' + NomeApp;
    RegWriteStringValue(HKCU, App, 'DESCRIPTION', 'Armadura BR');
    RegWriteDWordValue(HKCU, App, 'LOADCTRLS', 2);
    RegWriteDWordValue(HKCU, App, 'MANAGED', 1);
    RegWriteStringValue(HKCU, App, 'LOADER', Loader);
  end;
end;

procedure RemoverAplicacao(Chave: String);
var
  Lista: TArrayOfString;
  I: Integer;
begin
  Lista := Idiomas(HKCU, Chave);
  for I := 0 to GetArrayLength(Lista) - 1 do
    RegDeleteKeyIncludingSubkeys(HKCU, Chave + '\' + Lista[I] + '\Applications\' + NomeApp);
end;

procedure RegistrarGstar();
var
  Anos: TArrayOfString;
  I: Integer;
  Loader: String;
begin
  Anos := AnosGstar();
  for I := 0 to GetArrayLength(Anos) - 1 do
    { marcado sem GstarCAD encontrado: grava para todas as versões, para quando ele for instalado }
    if (GstarAchados = '') or ChaveExiste(ChaveGstarAno(Anos[I])) then
    begin
      { 2021: carregador (como nas versões anteriores do instalador); demais: a DLL da versão }
      if Anos[I] = '2021' then
        Loader := ExpandConstant('{app}\carregador\ArmaduraBR.Carregador.dll')
      else
        Loader := ExpandConstant('{app}\GstarCAD' + Anos[I] + '\ArmaduraBR.GstarCAD.dll');
      GravarAplicacao(ChaveGstarAno(Anos[I]), 'en-WW', Loader);
    end;
end;

procedure RemoverGstar();
var
  Anos: TArrayOfString;
  I: Integer;
begin
  Anos := AnosGstar();
  for I := 0 to GetArrayLength(Anos) - 1 do
    RemoverAplicacao(ChaveGstarAno(Anos[I]));
end;

procedure RegistrarZw();
var
  Anos: TArrayOfString;
  I: Integer;
begin
  Anos := AnosZw();
  for I := 0 to GetArrayLength(Anos) - 1 do
    if (ZwAchados = '') or ChaveExiste(ChaveZw + Anos[I]) then
      GravarAplicacao(ChaveZw + Anos[I], 'en-US', ExpandConstant('{app}\ZWCAD' + Anos[I] + '\ArmaduraBR.ZWCAD.dll'));
end;

procedure RemoverZw();
var
  Anos: TArrayOfString;
  I: Integer;
begin
  Anos := AnosZw();
  for I := 0 to GetArrayLength(Anos) - 1 do
    RemoverAplicacao(ChaveZw + Anos[I]);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Anos: TArrayOfString;
  I: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  { desmarcado = deixa de carregar naquele programa (tira o que uma instalação anterior deixou) }
  if WizardIsComponentSelected('gstarcad') then
    RegistrarGstar()
  else
  begin
    RemoverGstar();
    Anos := AnosGstar();
    for I := 0 to GetArrayLength(Anos) - 1 do
      DelTree(ExpandConstant('{app}\GstarCAD' + Anos[I]), True, True, True);
    DelTree(ExpandConstant('{app}\carregador'), True, True, True);
  end;
  if not WizardIsComponentSelected('autocad') then
    DelTree(ExpandConstant('{#Bundle}'), True, True, True);
  if WizardIsComponentSelected('zwcad') then
    RegistrarZw()
  else
  begin
    RemoverZw();
    Anos := AnosZw();
    for I := 0 to GetArrayLength(Anos) - 1 do
      DelTree(ExpandConstant('{app}\ZWCAD' + Anos[I]), True, True, True);
  end;
end;

{ ---------- desinstalação ---------- }

function InitializeUninstall(): Boolean;
begin
  Result := TodosFechados();
end;

{ Listas de autocompletar do GstarCAD com cópia de antes da instalação (todas as versões e idiomas). }
function CopiasAutocompletar(): TArrayOfString;
var
  Anos, Lista: TArrayOfString;
  I, J, N: Integer;
  Arq: String;
begin
  SetArrayLength(Result, 0);
  N := 0;
  Anos := AnosGstar();
  for I := 0 to GetArrayLength(Anos) - 1 do
  begin
    Lista := Idiomas(HKCU, ChaveGstarAno(Anos[I]));
    for J := 0 to GetArrayLength(Lista) - 1 do
    begin
      Arq := ExpandConstant('{userappdata}\Gstarsoft\GstarCAD\R') + Copy(Anos[I], 3, 2) + '\' + Lista[J] + '\Support\alias.pgc';
      if FileExists(Arq + '.antes_armadura') then
      begin
        SetArrayLength(Result, N + 1);
        Result[N] := Arq;
        N := N + 1;
      end;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Copias: TArrayOfString;
  I: Integer;
begin
  if CurUninstallStep <> usUninstall then Exit;
  Copias := CopiasAutocompletar();
  if (GetArrayLength(Copias) > 0) and (not UninstallSilent()) and
     (MsgBox('Restaurar a lista de autocompletar original do GstarCAD (alias.pgc)?' + #13#10 +
             'Escolha Não se você editou essa lista depois de instalar o Armadura BR.',
             mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    for I := 0 to GetArrayLength(Copias) - 1 do
      if FileCopy(Copias[I] + '.antes_armadura', Copias[I], False) then
        DeleteFile(Copias[I] + '.antes_armadura');
  RemoverGstar();
  RemoverZw();
end;
