#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-local"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\NovelM-win-x64-framework-dependent"
#endif

[Setup]
AppId={{7F45FC28-AE14-4D8A-B594-3C01C86BF53D}
AppName=轻书架
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\NovelM
PrivilegesRequired=lowest
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\NovelM.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\AppIcon.ico
OutputBaseFilename=NovelM-{#MyAppVersion}-setup

[Languages]
Name: "chinesesimp"; MessagesFile: "Languages\ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "\data\*,\runtime\*"
Source: "..\src\NovelM.App\Assets\AppIcon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\轻书架"; Filename: "{app}\NovelM.exe"

[Code]
procedure AddMissing(var Missing: string; const Item, Url: string);
begin
  if Missing <> '' then
    Missing := Missing + #13#10;
  Missing := Missing + Item + ': ' + Url;
end;

function HasDotNetDesktopRuntime(): Boolean;
var
  InstallLocation: string;
  RootPath: string;
  FindRec: TFindRec;
begin
  Result := False;
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', InstallLocation) then
    Exit;
  if InstallLocation = '' then
    Exit;
  RootPath := InstallLocation;
  if RootPath[Length(RootPath)] <> '\' then
    RootPath := RootPath + '\';
  RootPath := RootPath + 'shared\Microsoft.WindowsDesktop.App';
  if FindFirst(RootPath + '\10.*', FindRec) then
  begin
    try
      repeat
        if DirExists(RootPath + '\' + FindRec.Name) then
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

function HasWindowsAppRuntime(): Boolean;
var
  PowerShellPath: string;
  Parameters: string;
  ResultCode: Integer;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "if (Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.2.4'' | Where-Object { $_.Architecture -eq ''X64'' -and $_.Status -eq ''Ok'' }) { exit 0 } else { exit 1 }"';
  Result := Exec(PowerShellPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function HasVCRuntimeAt(RootKey: Integer): Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(RootKey, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1);
end;

function HasVCRuntime(): Boolean;
begin
  Result := HasVCRuntimeAt(HKLM32) or HasVCRuntimeAt(HKLM64);
end;

function InitializeSetup(): Boolean;
var
  Missing: string;
begin
  Missing := '';
  if not HasDotNetDesktopRuntime() then
    AddMissing(Missing, 'x64 .NET 10 Desktop Runtime', 'https://dotnet.microsoft.com/download/dotnet/10.0');
  if not HasWindowsAppRuntime() then
    AddMissing(Missing, '当前用户注册且健康的 x64 Microsoft.WindowsAppRuntime.2.4', 'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads');
  if not HasVCRuntime() then
    AddMissing(Missing, 'x64 Microsoft Visual C++ 2015-2022 运行库', 'https://aka.ms/vc14/vc_redist.x64.exe');

  if Missing <> '' then
  begin
    SuppressibleMsgBox('安装前检查失败，缺少以下组件及对应下载地址：' + #13#10 + Missing,
      mbError, MB_OK, IDOK);
    Result := False;
  end
  else
    Result := True;
end;
