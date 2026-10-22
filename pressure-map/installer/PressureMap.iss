; 学期压力地图 安装包脚本（Inno Setup 7）
; 编译：dev.ps1 -Mode Package，或 ISCC.exe /DMyAppVersion=2.0.0 installer\PressureMap.iss
; 产物：output\installer\PressureMap-<版本>-setup.exe
;
; 设计要点：
; 1. 默认“仅为当前用户”安装到 %LOCALAPPDATA%\Programs\PressureMap，不需要管理员权限；
;    用户在向导中可切换为“为所有用户安装”（此时安装到 Program Files 并触发 UAC）。
; 2. 许可协议页与安装位置页均由 Inno Setup 原生提供，不需要在 C# 代码里做任何事。
; 3. 卸载不会删除 %APPDATA%\学期压力地图 中的个人数据，卸载完成后会再提示一次位置。
; 4. 安装包只做“解包 + 快捷方式 + 卸载项”，程序本身的运行逻辑完全不变。

#define MyAppName "学期压力地图"
#define MyAppFolder "PressureMap"
#define MyAppExeName "PressureMap.exe"
#define MyAppPublisher "PressureMap"
#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\PressureMap"
#endif

[Setup]
; AppId 一经发布不可修改，否则新版本会被当成另一个软件安装。
AppId={{3F1B7C42-9E4A-4B7D-8F61-2C5E9A0D4B13}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoCompany={#MyAppPublisher}

; 默认按当前用户安装，无需管理员；向导内可切换为所有用户。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\{#MyAppFolder}
UsePreviousAppDir=yes
UsePreviousPrivileges=yes
DisableDirPage=no
DirExistsWarning=auto
DisableProgramGroupPage=yes
AllowNoIcons=yes
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}
Uninstallable=yes

; 许可协议页：向导第一步显示 EULA，必须选择“我接受”才能继续。
LicenseFile=EULA.txt

; 外观与图标
SetupIconFile=..\assets\app.ico
WizardStyle=modern
ShowLanguageDialog=no

; 体积与压缩：217MB 的发布目录压到 60MB 左右。
Compression=lzma2/max
SolidCompression=yes

; 系统要求
MinVersion=10.0.14393
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 升级时自动关闭正在运行的程序（安装包覆盖 PressureMap.exe 需要它退出）
CloseApplications=yes
RestartApplications=no

OutputDir=..\output\installer
OutputBaseFilename={#MyAppFolder}-{#MyAppVersion}-setup
UsePreviousLanguage=no

[Languages]
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 整个自包含发布目录原样安装，包含 .NET 运行时、OnlineCourses 运行时与文档。
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 安装目录内保留一份协议全文，便于日后查看。
Source: "EULA.txt"; DestDir: "{app}"; DestName: "许可协议.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppName}"; Tasks: desktopicon

[Registry]
; 注册 App Paths，方便“运行”对话框和第三方启动器直接找到 PressureMap.exe。
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#MyAppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\使用说明.md"; Description: "查看使用说明"; Flags: shellexec nowait postinstall skipifsilent unchecked

[UninstallDelete]
; 只清理安装目录内可能出现的日志残渣，不触碰用户数据（数据在 %APPDATA%\学期压力地图）。
Type: files; Name: "{app}\native.log"
Type: files; Name: "{app}\startup-error.log"

[Code]
const
  DataFolderName = '学期压力地图';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\') + DataFolderName;
    if DirExists(DataDir) then
      SuppressibleMsgBox('个人数据已保留：' + #13#10 + DataDir + #13#10 + #13#10 +
             '课程、课表、待办与账号缓存都在这里。' + #13#10 +
             '如果确定不再使用，可手动删除该文件夹。',
             mbInformation, MB_OK, IDOK);
  end;
end;
