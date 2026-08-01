#define MyAppName "纸伐局域网远控"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Zhifa LAN Remote"
#define MyAppExeName "纸伐局域网远控.exe"
#define PublishDir "..\dist\publish"

[Setup]
AppId={{B3E7A6F1-8C2D-4E5A-9F30-1A2B3C4D5E6F}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=纸伐局域网远控-安装程序-x64
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
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动纸伐局域网远控"; Flags: nowait postinstall skipifsilent

[Messages]
SetupAppTitle=纸伐局域网远控 安装程序
SetupWindowTitle=安装 - 纸伐局域网远控
WelcomeLabel1=欢迎安装纸伐局域网远控
WelcomeLabel2=本安装程序会将纸伐局域网远控安装到您的计算机。\n\n软件已自携带 .NET 10 运行时，安装过程无需联网下载。
SelectDirLabel3=请选择安装目录
SelectDirBrowseLabel=点击“下一步”继续，或点击“浏览”选择其他目录。
SelectTasksLabel2=请选择安装时要执行的附加任务。
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
  ModePage: TWizardPage;
  QuickRadio: TRadioButton;
  CustomRadio: TRadioButton;

procedure InitializeWizard;
begin
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

function ShouldSkipPage(Page: Integer): Boolean;
begin
  Result := False;
  if (Page = wpSelectDir) and QuickRadio.Checked then
    Result := True;
end;
