#define MyAppName "纸伐局域网远控"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Zhifa LAN Remote"
#define MyAppExeName "纸伐局域网远控.exe"
#define PublishDir "..\tests\ZhifaRemote.SmokeTest\bin\Debug\net10.0-windows"

[Setup]
AppId={{B3E7A6F1-8C2D-4E5A-9F30-1A2B3C4D5E6F}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=纸伐局域网远控-完整版-安装程序-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\ZhifaRemote\Assets\App.ico
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,ZhifaRemote.SmokeTest.*"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动纸伐局域网远控"; Flags: nowait postinstall skipifsilent

[Messages]
SetupAppTitle=纸伐局域网远控 安装程序
SetupWindowTitle=安装 - 纸伐局域网远控
WelcomeLabel1=欢迎安装纸伐局域网远控
WelcomeLabel2=本安装程序会将纸伐局域网远控完整版安装到您的计算机。\n\n安装前会检查 .NET 10 桌面运行时，如未安装请先下载安装。
SelectDirLabel3=请选择安装目录
SelectDirBrowseLabel=点击“下一步”继续，或点击“浏览”选择其他目录。
ReadyLabel1=安装程序已准备好开始安装。
ReadyLabel2a=点击“安装”继续。
InstallingLabel=正在安装，请稍候…
FinishedHeadingLabel=安装完成
FinishedLabel=纸伐局域网远控 已成功安装。
ButtonNext=下一步(&N)
ButtonInstall=安装(&I)
ButtonBack=上一步(&B)
ButtonCancel=取消

[Code]
var
  RuntimeInstalled: Boolean;
  DependencyPage: TWizardPage;
  RuntimeStatusLabel: TNewStaticText;
  DownloadButton: TNewButton;
  ModePage: TWizardPage;
  QuickRadio: TRadioButton;
  CustomRadio: TRadioButton;

procedure DownloadClick(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

function IsDotNet10DesktopRuntimeInstalled(): Boolean;
var
  Root: string;
  FindRec: TFindRec;
begin
  Root := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  Result := False;
  if not DirExists(Root) then Exit;
  if FindFirst(Root + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and $10 <> 0) and (Copy(FindRec.Name, 1, 3) = '10.') then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure InitializeWizard;
begin
  RuntimeInstalled := IsDotNet10DesktopRuntimeInstalled();

  DependencyPage := CreateCustomPage(wpWelcome, '环境检查', '检查 .NET 10 桌面运行时');
  RuntimeStatusLabel := TNewStaticText.Create(DependencyPage);
  RuntimeStatusLabel.Parent := DependencyPage.Surface;
  RuntimeStatusLabel.Left := 16;
  RuntimeStatusLabel.Top := 20;
  RuntimeStatusLabel.Width := DependencyPage.SurfaceWidth - 32;
  RuntimeStatusLabel.Height := 90;
  RuntimeStatusLabel.AutoSize := False;
  RuntimeStatusLabel.WordWrap := True;
  if RuntimeInstalled then
    RuntimeStatusLabel.Caption := '已检测到 .NET 10 桌面运行时，可以继续安装。'
  else
    RuntimeStatusLabel.Caption := '未检测到 .NET 10 桌面运行时。' + #13#10 +
      '完整版需要 .NET 10 Desktop Runtime 才能运行，请先安装。' + #13#10 +
      '安装完成后返回本向导继续安装。';

  DownloadButton := TNewButton.Create(DependencyPage);
  DownloadButton.Parent := DependencyPage.Surface;
  DownloadButton.Left := 16;
  DownloadButton.Top := 120;
  DownloadButton.Width := 180;
  DownloadButton.Height := 28;
  DownloadButton.Caption := '打开 .NET 10 下载页';
  DownloadButton.OnClick := @DownloadClick;
  DownloadButton.Enabled := not RuntimeInstalled;

  ModePage := CreateCustomPage(wpWelcome, '选择安装方式', '选择快速安装或自定义安装路径');

  QuickRadio := TRadioButton.Create(ModePage);
  QuickRadio.Parent := ModePage.Surface;
  QuickRadio.Left := 16;
  QuickRadio.Top := 24;
  QuickRadio.Width := ModePage.SurfaceWidth - 32;
  QuickRadio.Height := 34;
  QuickRadio.Caption := '快速安装（推荐）' + #13#10 + '安装到 C:\Program Files\纸伐局域网远控，自动创建桌面快捷方式';
  QuickRadio.Checked := True;

  CustomRadio := TRadioButton.Create(ModePage);
  CustomRadio.Parent := ModePage.Surface;
  CustomRadio.Left := 16;
  CustomRadio.Top := 84;
  CustomRadio.Width := ModePage.SurfaceWidth - 32;
  CustomRadio.Height := 34;
  CustomRadio.Caption := '自定义安装' + #13#10 + '手动选择安装目录';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = DependencyPage.ID then
  begin
    if not IsDotNet10DesktopRuntimeInstalled() then
    begin
      MsgBox('尚未检测到 .NET 10 桌面运行时，请先安装后再继续。', mbInformation, MB_OK);
      Result := False;
    end;
  end;
end;

function ShouldSkipPage(Page: Integer): Boolean;
begin
  Result := False;
  if (Page = wpSelectDir) and QuickRadio.Checked then
    Result := True;
end;
