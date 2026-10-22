param([ValidateSet('Run','Build','Check','UiCheck','Package')][string]$Mode = 'Run')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$toolRoot = Join-Path (Split-Path $projectRoot -Parent) '.tools'
$dotnetPath = Join-Path $toolRoot 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) { throw '.NET 10 SDK is required for source builds: https://dotnet.microsoft.com/download/dotnet/10.0' }
    $dotnetPath = $command.Source
}
$installedSdks = & $dotnetPath --list-sdks
if (-not ($installedSdks -match '^10\.')) { throw '.NET 10 SDK is required. The portable application does not require an SDK.' }
$env:DOTNET_ROOT = Split-Path $dotnetPath -Parent
$env:DOTNET_CLI_HOME = Join-Path $toolRoot 'dotnet-home'
$env:NUGET_PACKAGES = Join-Path $toolRoot 'nuget'
$env:DOTNET_NOLOGO = '1'
function Invoke-Dotnet([string[]]$Arguments) {
    & $dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
function Assert-ProjectPath([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside project: $absolute" }
    return $absolute
}
function Get-ProjectVersion {
    $props = Join-Path $projectRoot 'Directory.Build.props'
    $match = [regex]::Match((Get-Content -LiteralPath $props -Raw), '<Version>([^<]+)</Version>')
    if (-not $match.Success) { throw 'No <Version> found in Directory.Build.props' }
    return $match.Groups[1].Value.Trim()
}
function Resolve-Iscc {
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add((Join-Path $toolRoot 'innosetup\ISCC.exe'))
    foreach ($base in @($env:ProgramW6432, ${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ([string]::IsNullOrEmpty($base)) { continue }
        foreach ($edition in @('Inno Setup 7', 'Inno Setup 6')) { $candidates.Add((Join-Path $base ($edition + '\ISCC.exe'))) }
    }
    foreach ($candidate in $candidates) { if (Test-Path -LiteralPath $candidate) { return $candidate } }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw 'Inno Setup compiler (ISCC.exe) not found. Install it portably: .tools\innosetup-7.1.0-x64.exe /PORTABLE=1 /VERYSILENT /DIR=<workspace>\.tools\innosetup'
}
# 生成 dist\PressureMap 便携目录：publish 到暂存区、自检、带上文档，再整体替换旧版本。
function Invoke-ReleasePublish {
    $application = 'src\PressureMap.App\PressureMap.App.csproj'
    $staging = Assert-ProjectPath (Join-Path $projectRoot ('.release-stage\' + [guid]::NewGuid().ToString('N')))
    $release = Join-Path $staging 'PressureMap'
    Invoke-Dotnet -Arguments @('publish',$application,'-c','Release','-r','win-x64','--self-contained','true','-p:DebugType=None','-p:DebugSymbols=false','-o',$release)
    $check = Start-Process -FilePath (Join-Path $release 'PressureMap.exe') -ArgumentList '--check' -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw 'Portable application self-check failed; the previous release is preserved.' }
    Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $release '使用说明.md')
    foreach ($name in @('CLOUD_API.md','DAILY_PRESSURE.md','DESIGN_REFERENCE.md','THIRD_PARTY_NOTICES.md','licenses')) { Copy-Item -LiteralPath $name -Destination $release -Recurse }
    $destination = Assert-ProjectPath (Join-Path $projectRoot 'dist')
    $running = Get-Process -Name PressureMap,PressureMapNative -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "Close the running application. The new build is ready at: $release" }
    $backup = Assert-ProjectPath (Join-Path $staging 'previous-release')
    if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $backup }
    try {
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Move-Item -LiteralPath $release -Destination (Join-Path $destination 'PressureMap')
    } catch {
        if (Test-Path -LiteralPath $backup) {
            if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath (Assert-ProjectPath $destination) -Recurse -Force }
            Move-Item -LiteralPath $backup -Destination $destination
        }
        throw
    }
    Remove-Item -LiteralPath (Assert-ProjectPath $staging) -Recurse -Force
    Write-Output 'Ready: dist\PressureMap\PressureMap.exe'
}
Push-Location $projectRoot
try {
    $applicationProject = 'src\PressureMap.App\PressureMap.App.csproj'
    if ($Mode -in @('Check','Build')) {
        $env:PRESSURE_MAP_TEST_ROOT = Join-Path $projectRoot '.test-temp'
        New-Item -ItemType Directory -Force -Path $env:PRESSURE_MAP_TEST_ROOT | Out-Null
        Invoke-Dotnet -Arguments @('run','--project','tests\PressureMap.Checks\PressureMap.Checks.csproj','-c','Release')
        $nodePath = Join-Path $projectRoot 'src\PressureMap.App\OnlineCourses\.runtime\node.exe'
        $onlineTests = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests\OnlineCourses') -Filter '*.cjs' -File | ForEach-Object FullName)
        & $nodePath --test @onlineTests
        if ($LASTEXITCODE -ne 0) { throw 'Online course regression checks failed.' }
    }
    if ($Mode -in @('Build','Package')) { Invoke-ReleasePublish }
    if ($Mode -eq 'Package') {
        # Package 不重复跑回归检查（那是 Check/Build 的事），只把已经发布好的目录打成安装包与便携 zip。
        $version = Get-ProjectVersion
        $iscc = Resolve-Iscc
        $setupScript = Assert-ProjectPath (Join-Path $projectRoot 'installer\PressureMap.iss')
        $outputDirectory = Assert-ProjectPath (Join-Path $projectRoot 'output\installer')
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
        & $iscc ('/Qp') ('/DMyAppVersion=' + $version) $setupScript
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
        $setup = Join-Path $outputDirectory ('PressureMap-' + $version + '-setup.exe')
        if (-not (Test-Path -LiteralPath $setup)) { throw "Installer was not produced: $setup" }
        $zip = Join-Path $outputDirectory ('PressureMap-' + $version + '-portable.zip')
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
        Compress-Archive -Path (Join-Path $projectRoot 'dist\PressureMap') -DestinationPath $zip -CompressionLevel Optimal
        foreach ($file in @($setup, $zip)) {
            $item = Get-Item -LiteralPath $file
            Write-Output ('Built: {0} ({1:N1} MB)' -f $item.FullName, ($item.Length / 1MB))
        }
    } elseif ($Mode -eq 'UiCheck') {
        Invoke-Dotnet -Arguments @('build',$applicationProject,'-c','Release')
        $identity = [guid]::NewGuid().ToString('N')
        $data = Assert-ProjectPath (Join-Path $projectRoot ('.test-temp\ui-' + $identity))
        $output = Assert-ProjectPath (Join-Path $projectRoot ('test-results\ui-' + $identity))
        Invoke-Dotnet -Arguments @('src\PressureMap.App\bin\Release\net10.0\PressureMap.dll','--ui-smoke','--data',$data,'--out',$output)
        Write-Output "UI verification: $output"
    } elseif ($Mode -eq 'Run') {
        Invoke-Dotnet -Arguments @('build',$applicationProject,'-c','Release')
        # Do not hide the window: -WindowStyle Hidden makes the Avalonia main window invisible.
        Start-Process -FilePath (Join-Path $projectRoot 'src\PressureMap.App\bin\Release\net10.0\PressureMap.exe') -WorkingDirectory (Join-Path $projectRoot 'src\PressureMap.App\bin\Release\net10.0')
    } elseif ($Mode -eq 'Check') {
        Invoke-Dotnet -Arguments @('build',$applicationProject,'-c','Release')
    }
} finally { Pop-Location }
