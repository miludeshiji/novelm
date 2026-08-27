# Windows GitHub Release Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GitHub Actions workflow that builds and validates the Windows x64 app on every `main` push and publishes a framework-dependent ZIP plus an Inno Setup installer for strict `vX.Y.Z` tags.

**Architecture:** A single workflow owns build and release orchestration. Its read-only `build` job produces and smoke-tests both assets; its tag-only `release` job receives `contents: write` and creates the GitHub Release. A standalone Inno Setup script owns per-user installation and prerequisite detection.

**Tech Stack:** GitHub Actions, PowerShell 7, .NET 10, WinUI 3, Inno Setup 6+, GitHub CLI

---

### Task 1: Add the Inno Setup installer

**Files:**
- Create: `installer/NovelM.iss`

- [ ] **Step 1: Confirm the existing publish output and local compiler state**

Run:

```powershell
Test-Path artifacts\NovelM-win-x64-framework-dependent\NovelM.exe
Get-Command ISCC.exe -ErrorAction SilentlyContinue
```

Expected: the first command prints `True`; the second currently returns no command on this workstation.

- [ ] **Step 2: Create the complete installer script**

Create `installer/NovelM.iss` with:

```iss
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-local"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\NovelM-win-x64-framework-dependent"
#endif

#define MyAppName "轻书架"
#define MyAppExeName "NovelM.exe"

[Setup]
AppId={{7F45FC28-AE14-4D8A-B594-3C01C86BF53D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={localappdata}\Programs\NovelM
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=NovelM-setup
SetupIconFile=..\src\NovelM.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\AppIcon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Code]
const
  DotNetDownloadUrl = 'https://dotnet.microsoft.com/download/dotnet/10.0';
  WindowsAppRuntimeDownloadUrl = 'https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads';
  VcRuntimeDownloadUrl = 'https://aka.ms/vc14/vc_redist.x64.exe';

procedure AddMissingPrerequisite(
  var Missing: String;
  const DisplayName: String;
  const DownloadUrl: String);
begin
  if Missing <> '' then
    Missing := Missing + #13#10 + #13#10;

  Missing := Missing + '• ' + DisplayName + #13#10 + DownloadUrl;
end;

function HasDotNetDesktopRuntime10: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(
    ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'),
    FindRec);

  if Result then
    FindClose(FindRec);
end;

function HasWindowsAppRuntime24: Boolean;
var
  ExitCode: Integer;
  PowerShellPath: String;
  Parameters: String;
begin
  PowerShellPath := ExpandConstant(
    '{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters :=
    '-NoProfile -NonInteractive -Command "' +
    '$package = Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.2.4'' ' +
    '| Where-Object { $_.Architecture -eq ''X64'' -and $_.Status -eq ''Ok'' }; ' +
    'if ($null -ne $package) { exit 0 } else { exit 1 }"';

  Result := Exec(
    PowerShellPath,
    Parameters,
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ExitCode);

  if Result then
    Result := ExitCode = 0;
end;

function HasVcRuntimeX64: Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(
    HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Installed',
    Installed) and (Installed = 1);
end;

function InitializeSetup: Boolean;
var
  Missing: String;
begin
  Missing := '';

  if not HasDotNetDesktopRuntime10 then
    AddMissingPrerequisite(
      Missing,
      '.NET 10 Desktop Runtime (x64)',
      DotNetDownloadUrl);

  if not HasWindowsAppRuntime24 then
    AddMissingPrerequisite(
      Missing,
      'Windows App Runtime 2.4 (x64)',
      WindowsAppRuntimeDownloadUrl);

  if not HasVcRuntimeX64 then
    AddMissingPrerequisite(
      Missing,
      'Microsoft Visual C++ 2015–2022 Redistributable (x64)',
      VcRuntimeDownloadUrl);

  if Missing <> '' then
  begin
    MsgBox(
      '无法继续安装。请先安装以下运行库，然后重新运行安装程序：' +
      #13#10 + #13#10 + Missing,
      mbError,
      MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;
```

- [ ] **Step 3: Publish a local framework-dependent input directory**

Run:

```powershell
dotnet publish src\NovelM.App\NovelM.App.csproj -p:Platform=x64 -c Release --no-restore --self-contained false -p:WindowsAppSDKSelfContained=false -p:Version=0.0.0-local -o artifacts\workflow-validation\publish --disable-build-servers -m:1 -nr:false --verbosity minimal
```

Expected: exit code `0` and `artifacts\workflow-validation\publish\NovelM.exe` exists.

- [ ] **Step 4: Install the local compiler only after approval, then compile the script**

The workstation currently has no `ISCC.exe`. After obtaining approval for the machine-level tool installation, run:

```powershell
winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements
$iscc = Get-ChildItem 'C:\Program Files*\Inno Setup *\ISCC.exe' -File -ErrorAction Stop | Select-Object -First 1 -ExpandProperty FullName
& $iscc '--define=MyAppVersion=0.0.0-local' "--define=SourceDir=$((Resolve-Path 'artifacts\workflow-validation\publish').Path)" "--output-dir=$((Resolve-Path 'artifacts\workflow-validation').Path)" '--output-filename=NovelM-local-setup' 'installer\NovelM.iss'
```

Expected: compiler exit code `0` and `artifacts\workflow-validation\NovelM-local-setup.exe` exists.

- [ ] **Step 5: Commit the installer script**

```powershell
git add installer\NovelM.iss
git commit -m "build: 添加 Windows 安装程序"
```

### Task 2: Add the build and release workflow

**Files:**
- Create: `.github/workflows/windows-release.yml`

- [ ] **Step 1: Create the workflow**

Create `.github/workflows/windows-release.yml` with:

```yaml
name: Windows Build and Release

on:
  push:
    branches:
      - main
    tags:
      - "v*"

permissions:
  contents: read

env:
  DOTNET_CLI_TELEMETRY_OPTOUT: "1"
  DOTNET_NOLOGO: "1"

jobs:
  build:
    name: Build Windows x64 assets
    runs-on: windows-2025
    outputs:
      artifact_name: ${{ steps.metadata.outputs.artifact_name }}
      zip_name: ${{ steps.metadata.outputs.zip_name }}
      installer_name: ${{ steps.metadata.outputs.installer_name }}

    steps:
      - name: Check out repository
        uses: actions/checkout@v6

      - name: Set up .NET SDK
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - name: Resolve build metadata
        id: metadata
        shell: pwsh
        run: |
          if ($env:GITHUB_REF_TYPE -eq 'tag') {
            if ($env:GITHUB_REF_NAME -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') {
              throw "标签必须严格符合 vX.Y.Z：$env:GITHUB_REF_NAME"
            }

            $appVersion = $env:GITHUB_REF_NAME.Substring(1)
            $assetLabel = $env:GITHUB_REF_NAME
          }
          else {
            $appVersion = "0.0.0-ci.$env:GITHUB_RUN_NUMBER"
            $assetLabel = "ci-$env:GITHUB_RUN_NUMBER"
          }

          $zipName = "NovelM-$assetLabel-win-x64.zip"
          $installerName = "NovelM-$assetLabel-win-x64-setup.exe"
          $artifactName = "NovelM-$assetLabel-win-x64"

          "APP_VERSION=$appVersion" >> $env:GITHUB_ENV
          "ASSET_LABEL=$assetLabel" >> $env:GITHUB_ENV
          "ZIP_NAME=$zipName" >> $env:GITHUB_ENV
          "INSTALLER_NAME=$installerName" >> $env:GITHUB_ENV
          "artifact_name=$artifactName" >> $env:GITHUB_OUTPUT
          "zip_name=$zipName" >> $env:GITHUB_OUTPUT
          "installer_name=$installerName" >> $env:GITHUB_OUTPUT

      - name: Restore dependencies
        shell: pwsh
        run: dotnet restore NovelM.sln -p:Platform=x64

      - name: Run tests
        shell: pwsh
        run: dotnet test NovelM.sln -c Release -p:Platform=x64 --no-restore --disable-build-servers -m:1 -nr:false --verbosity minimal

      - name: Publish framework-dependent app
        shell: pwsh
        run: |
          $publishDir = Join-Path $env:RUNNER_TEMP 'NovelM-publish'
          dotnet publish src\NovelM.App\NovelM.App.csproj `
            -p:Platform=x64 `
            -c Release `
            --no-restore `
            --self-contained false `
            -p:WindowsAppSDKSelfContained=false `
            -p:Version=$env:APP_VERSION `
            -o $publishDir `
            --disable-build-servers `
            -m:1 `
            -nr:false `
            --verbosity minimal

          if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
          }

      - name: Validate publish directory
        shell: pwsh
        run: |
          $publishDir = Join-Path $env:RUNNER_TEMP 'NovelM-publish'
          if (-not (Test-Path (Join-Path $publishDir 'NovelM.exe'))) {
            throw 'Published NovelM.exe is missing.'
          }

          foreach ($forbiddenName in @('data', 'runtime')) {
            if (Test-Path (Join-Path $publishDir $forbiddenName)) {
              throw "Publish output unexpectedly contains $forbiddenName."
            }
          }

      - name: Create and validate ZIP
        shell: pwsh
        run: |
          $publishDir = Join-Path $env:RUNNER_TEMP 'NovelM-publish'
          $assetsDir = Join-Path $env:RUNNER_TEMP 'release-assets'
          $zipPath = Join-Path $assetsDir $env:ZIP_NAME
          $checkDir = Join-Path $env:RUNNER_TEMP 'NovelM-zip-check'
          New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
          Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
          Expand-Archive -LiteralPath $zipPath -DestinationPath $checkDir

          if (-not (Test-Path (Join-Path $checkDir 'NovelM.exe'))) {
            throw 'ZIP does not contain NovelM.exe at its root.'
          }

      - name: Compile installer
        shell: pwsh
        run: |
          $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
          if (-not $iscc) {
            $candidates = @(
              (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
              (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
            )
            $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
          }

          if (-not $iscc) {
            throw 'ISCC.exe was not found on the GitHub-hosted runner.'
          }

          $publishDir = Join-Path $env:RUNNER_TEMP 'NovelM-publish'
          $assetsDir = Join-Path $env:RUNNER_TEMP 'release-assets'
          $installerBaseName = [IO.Path]::GetFileNameWithoutExtension($env:INSTALLER_NAME)
          & $iscc `
            "--define=MyAppVersion=$env:APP_VERSION" `
            "--define=SourceDir=$publishDir" `
            "--output-dir=$assetsDir" `
            "--output-filename=$installerBaseName" `
            'installer\NovelM.iss'

          if ($LASTEXITCODE -ne 0) {
            throw "ISCC failed with exit code $LASTEXITCODE"
          }

      - name: Ensure Windows App Runtime for smoke test
        shell: pwsh
        run: |
          $runtime = Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2.4' |
            Where-Object { $_.Architecture -eq 'X64' -and $_.Status -eq 'Ok' } |
            Select-Object -First 1

          if (-not $runtime) {
            $runtimeInstaller = Join-Path $env:RUNNER_TEMP 'WindowsAppRuntimeInstall-x64.exe'
            Invoke-WebRequest `
              -Uri 'https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe' `
              -OutFile $runtimeInstaller
            & $runtimeInstaller --quiet
            if ($LASTEXITCODE -ne 0) {
              throw "Windows App Runtime installation failed with exit code $LASTEXITCODE"
            }
          }

      - name: Smoke-test installer and uninstaller
        shell: pwsh
        run: |
          $assetsDir = Join-Path $env:RUNNER_TEMP 'release-assets'
          $installerPath = Join-Path $assetsDir $env:INSTALLER_NAME
          $installDir = Join-Path $env:RUNNER_TEMP 'NovelM-installed'
          $install = Start-Process `
            -FilePath $installerPath `
            -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$installDir" `
            -Wait `
            -PassThru

          if ($install.ExitCode -ne 0 -or -not (Test-Path (Join-Path $installDir 'NovelM.exe'))) {
            throw "Installer smoke test failed with exit code $($install.ExitCode)."
          }

          New-Item -ItemType Directory -Path (Join-Path $installDir 'data') -Force | Out-Null
          New-Item -ItemType Directory -Path (Join-Path $installDir 'runtime') -Force | Out-Null
          Set-Content -LiteralPath (Join-Path $installDir 'data\preserve.txt') -Value 'preserve'
          Set-Content -LiteralPath (Join-Path $installDir 'runtime\preserve.txt') -Value 'preserve'

          $uninstallerPath = Join-Path $installDir 'unins000.exe'
          $uninstall = Start-Process `
            -FilePath $uninstallerPath `
            -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' `
            -Wait `
            -PassThru

          if ($uninstall.ExitCode -ne 0) {
            throw "Uninstaller smoke test failed with exit code $($uninstall.ExitCode)."
          }

          if (Test-Path (Join-Path $installDir 'NovelM.exe')) {
            throw 'Uninstaller left NovelM.exe behind.'
          }

          foreach ($preservedPath in @('data\preserve.txt', 'runtime\preserve.txt')) {
            if (-not (Test-Path (Join-Path $installDir $preservedPath))) {
              throw "Uninstaller removed user data: $preservedPath"
            }
          }

      - name: Upload Windows assets
        uses: actions/upload-artifact@v6
        with:
          name: ${{ steps.metadata.outputs.artifact_name }}
          path: |
            ${{ runner.temp }}\release-assets\${{ steps.metadata.outputs.zip_name }}
            ${{ runner.temp }}\release-assets\${{ steps.metadata.outputs.installer_name }}
          if-no-files-found: error
          compression-level: 0

  release:
    name: Create GitHub Release
    if: startsWith(github.ref, 'refs/tags/v')
    needs: build
    runs-on: windows-2025
    permissions:
      contents: write

    steps:
      - name: Download Windows assets
        uses: actions/download-artifact@v6
        with:
          name: ${{ needs.build.outputs.artifact_name }}
          path: release-assets

      - name: Create release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
          ZIP_NAME: ${{ needs.build.outputs.zip_name }}
          INSTALLER_NAME: ${{ needs.build.outputs.installer_name }}
        run: |
          $generatedNotes = gh api `
            "repos/$env:GITHUB_REPOSITORY/releases/generate-notes" `
            -f "tag_name=$env:GITHUB_REF_NAME" `
            --jq '.body'

          if ($LASTEXITCODE -ne 0) {
            throw "Release notes generation failed with exit code $LASTEXITCODE"
          }

          $prerequisites = @'
          ## Windows 运行要求

          这是 x64 框架依赖版本。运行 ZIP 或安装程序前，请先安装：

          - [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)
          - [Windows App Runtime 2.4 (x64)](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
          - [Microsoft Visual C++ 2015–2022 Redistributable (x64)](https://aka.ms/vc14/vc_redist.x64.exe)

          安装程序当前未进行代码签名，Windows SmartScreen 可能显示“未知发布者”。
          '@

          $body = $prerequisites.Trim() + [Environment]::NewLine +
            [Environment]::NewLine + ($generatedNotes -join [Environment]::NewLine)
          Set-Content -LiteralPath 'release-notes.md' -Value $body -Encoding utf8NoBOM

          gh release create $env:GITHUB_REF_NAME `
            "release-assets/$env:ZIP_NAME" `
            "release-assets/$env:INSTALLER_NAME" `
            --verify-tag `
            --title $env:GITHUB_REF_NAME `
            --notes-file 'release-notes.md'

          if ($LASTEXITCODE -ne 0) {
            throw "Release creation failed with exit code $LASTEXITCODE"
          }
```

- [ ] **Step 2: Run a workflow syntax and semantic check**

Run the pinned validator without adding it to the repository:

```powershell
go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.10 .github\workflows\windows-release.yml
```

Expected: exit code `0` and no findings.

- [ ] **Step 3: Review only the requested files**

Run:

```powershell
git diff --check
git status --short
git diff -- installer\NovelM.iss .github\workflows\windows-release.yml
```

Expected: no whitespace errors; only the installer and workflow are uncommitted implementation files.

### Task 3: Verify the complete release path locally

**Files:**
- Verify: `installer/NovelM.iss`
- Verify: `.github/workflows/windows-release.yml`

- [ ] **Step 1: Run the full test suite**

```powershell
dotnet test NovelM.sln -c Release -p:Platform=x64 --no-restore --disable-build-servers -m:1 -nr:false --verbosity minimal
```

Expected: `413` passed, `0` failed, `0` skipped.

- [ ] **Step 2: Build and inspect the framework-dependent publish directory**

```powershell
dotnet publish src\NovelM.App\NovelM.App.csproj -p:Platform=x64 -c Release --no-restore --self-contained false -p:WindowsAppSDKSelfContained=false -p:Version=0.0.0-local -o artifacts\workflow-validation\publish --disable-build-servers -m:1 -nr:false --verbosity minimal
Test-Path artifacts\workflow-validation\publish\NovelM.exe
Test-Path artifacts\workflow-validation\publish\data
Test-Path artifacts\workflow-validation\publish\runtime
```

Expected: publish succeeds and the three checks print `True`, `False`, `False`.

- [ ] **Step 3: Create and re-open the ZIP**

```powershell
Compress-Archive -Path artifacts\workflow-validation\publish\* -DestinationPath artifacts\workflow-validation\NovelM-v0.0.0-local-win-x64.zip -CompressionLevel Optimal -Force
Expand-Archive -LiteralPath artifacts\workflow-validation\NovelM-v0.0.0-local-win-x64.zip -DestinationPath artifacts\workflow-validation\zip-check -Force
Test-Path artifacts\workflow-validation\zip-check\NovelM.exe
```

Expected: the final check prints `True`.

- [ ] **Step 4: Compile and smoke-test the installer**

```powershell
$iscc = Get-ChildItem 'C:\Program Files*\Inno Setup *\ISCC.exe' -File -ErrorAction Stop | Select-Object -First 1 -ExpandProperty FullName
& $iscc '--define=MyAppVersion=0.0.0-local' "--define=SourceDir=$((Resolve-Path 'artifacts\workflow-validation\publish').Path)" "--output-dir=$((Resolve-Path 'artifacts\workflow-validation').Path)" '--output-filename=NovelM-v0.0.0-local-win-x64-setup' 'installer\NovelM.iss'
$installer = Resolve-Path artifacts\workflow-validation\NovelM-v0.0.0-local-win-x64-setup.exe
$installDir = Join-Path (Resolve-Path artifacts\workflow-validation).Path 'installed'
$process = Start-Process $installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$installDir" -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode)" }
Test-Path (Join-Path $installDir 'NovelM.exe')
```

Expected: compile and install exit with code `0`; the final check prints `True`. This local installation writes a temporary HKCU uninstall entry and therefore requires explicit approval before execution.

- [ ] **Step 5: Verify user data survives uninstall**

```powershell
New-Item -ItemType Directory -Path (Join-Path $installDir 'data'),(Join-Path $installDir 'runtime') -Force | Out-Null
Set-Content (Join-Path $installDir 'data\preserve.txt') 'preserve'
Set-Content (Join-Path $installDir 'runtime\preserve.txt') 'preserve'
$uninstaller = Join-Path $installDir 'unins000.exe'
$process = Start-Process $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Uninstaller failed: $($process.ExitCode)" }
Test-Path (Join-Path $installDir 'NovelM.exe')
Test-Path (Join-Path $installDir 'data\preserve.txt')
Test-Path (Join-Path $installDir 'runtime\preserve.txt')
```

Expected: the checks print `False`, `True`, `True`. Remove only the temporary `artifacts\workflow-validation\installed` directory after resolving and verifying that exact path remains inside the repository's ignored `artifacts` directory.

- [ ] **Step 6: Commit the workflow after all verification passes**

```powershell
git add .github\workflows\windows-release.yml
git commit -m "ci: 添加 Windows 自动发布工作流"
```

- [ ] **Step 7: Final repository verification**

```powershell
git status -sb
git log -3 --oneline
```

Expected: `main` has no uncommitted changes. Do not push or create a `vX.Y.Z` tag without separate user authorization.
