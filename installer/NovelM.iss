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
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "data\*;runtime\*"
Source: "..\src\NovelM.App\Assets\AppIcon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\轻书架"; Filename: "{app}\NovelM.exe"

[Code]
procedure AddMissing(var Missing: string; const Item: string);
begin
  if Missing <> '' then
    Missing := Missing + '、';
  Missing := Missing + Item;
end;

function HasDotNetDesktopRuntime(): Boolean;
var
  RootPath: string;
  FindRec: TFindRec;
begin
  Result := False;
  RootPath := ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
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

function HasVCRuntime(): Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1);
end;

function InitializeSetup(): Boolean;
var
  Missing: string;
begin
  Missing := '';
  if not HasDotNetDesktopRuntime() then
    AddMissing(Missing, 'x64 .NET 10 Desktop Runtime');
  if not HasWindowsAppRuntime() then
    AddMissing(Missing, '当前用户注册且健康的 x64 Microsoft.WindowsAppRuntime.2.4');
  if not HasVCRuntime() then
    AddMissing(Missing, 'x64 Microsoft Visual C++ 2015-2022 运行库');

  if Missing <> '' then
  begin
    MsgBox('安装前检查失败，缺少以下组件：' + #13#10 + Missing + #13#10 + #13#10 +
      '请从以下地址安装：' + #13#10 +
      'x64 .NET 10 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/10.0' + #13#10 +
      'Windows App Runtime: https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads' + #13#10 +
      'Visual C++ 2015-2022: https://aka.ms/vc14/vc_redist.x64.exe',
      mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;
