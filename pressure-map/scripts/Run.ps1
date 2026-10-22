param([switch]$NoBuild, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$toolRoot = Join-Path (Split-Path $projectRoot -Parent) '.tools'
$dotnetPath = Join-Path $toolRoot 'dotnet\dotnet.exe'
$appProject = Join-Path $projectRoot 'src\PressureMap.App\PressureMap.App.csproj'
$binDir = Join-Path $projectRoot 'src\PressureMap.App\bin\Release\net10.0'
$appExe = Join-Path $binDir 'PressureMap.exe'
$appDll = Join-Path $binDir 'PressureMap.dll'
# 构建戳记：只改 Core 时 App.dll 不会更新，用它判断会误判成“有更新”而每次重编译。
$stamp = Join-Path $binDir '.build-stamp'
$distExe = Join-Path $projectRoot 'dist\PressureMap\PressureMap.exe'
$sourceExtensions = @('.cs', '.axaml', '.cjs', '.csproj', '.props', '.sql', '.png', '.svg', '.ico')

# 1) 结束旧实例：应用是单实例（App.axaml.cs 的 Mutex），
#    旧进程还在时再次启动只会 RestoreMain 已有窗口，看不到新代码。
$processNames = @('PressureMap', 'PressureMapNative')
function Get-AppProcesses {
    @($processNames | ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue } | Where-Object {
        -not $_.Path -or $_.Path.StartsWith($projectRoot, [StringComparison]::OrdinalIgnoreCase)
    })
}
$running = Get-AppProcesses
if ($running.Count -gt 0) {
    Write-Host ("[1/4] 关闭旧实例（PID {0}）..." -f (($running | ForEach-Object Id) -join ', '))
    foreach ($process in $running) { try { $process.Kill() } catch { } }
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $left = Get-AppProcesses
        if ($left.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($left.Count -gt 0) { throw "无法关闭旧实例（PID $((($left | ForEach-Object Id) -join ', '))），请先在托盘退出应用后重试。" }
} else {
    Write-Host '[1/4] 没有正在运行的实例。'
}

# 2) 找最新源码时间。跳过 bin/obj/.runtime，只扫约 160 个真实文件，
#    避免把 1.7G 的构建产物也遍历一遍（这正是旧启动器慢的原因）。
function Get-NewestSourceTime([string]$start) {
    $newest = [DateTime]::MinValue
    $stack = [Collections.Generic.Stack[string]]::new()
    $stack.Push($start)
    while ($stack.Count -gt 0) {
        $directory = $stack.Pop()
        try { $info = [IO.DirectoryInfo]::new($directory) } catch { continue }
        foreach ($file in $info.EnumerateFiles()) {
            if ($sourceExtensions -notcontains $file.Extension) { continue }
            if ($file.LastWriteTimeUtc -gt $newest) { $newest = $file.LastWriteTimeUtc }
        }
        foreach ($child in $info.EnumerateDirectories()) {
            if ($child.Name -in @('bin', 'obj', '.runtime', 'node_modules')) { continue }
            $stack.Push($child.FullName)
        }
    }
    return $newest
}

$newestSource = @('src', 'tests') | ForEach-Object {
    $path = Join-Path $projectRoot $_
    if (Test-Path -LiteralPath $path) { Get-NewestSourceTime $path }
} | Measure-Object -Maximum | Select-Object -ExpandProperty Maximum
Write-Host ("[2/4] 最新源码 {0:HH:mm:ss}。" -f $newestSource.ToLocalTime())

# 3) 只有源码比现有产物新时才编译（增量；实测 2~8 秒，远快于整包发布）。
$stampTime = if (Test-Path -LiteralPath $stamp) { (Get-Item -LiteralPath $stamp).LastWriteTimeUtc } else { $null }
$stale = -not (Test-Path -LiteralPath $appExe) -or $null -eq $stampTime -or $newestSource -gt $stampTime
if ($stale -and -not $NoBuild) {
    if (-not (Test-Path -LiteralPath $dotnetPath)) {
        Write-Host '[3/4] 未找到便携版 SDK，改为启动现有便携包。'
        if (-not (Test-Path -LiteralPath $distExe)) { throw "既没有 SDK 也没有便携包：$distExe" }
        if (-not $DryRun) { Start-Process -FilePath $distExe -WorkingDirectory (Split-Path $distExe) }
        return
    }
    Write-Host '[3/4] 源码有更新，增量编译...'
    if ($DryRun) { Write-Host '    DryRun：跳过编译。'; return }
    $env:DOTNET_ROOT = Split-Path $dotnetPath -Parent
    $env:DOTNET_CLI_HOME = Join-Path $toolRoot 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $toolRoot 'nuget'
    $env:DOTNET_NOLOGO = '1'
    # 已有还原结果时跳过还原检查，单文件改动可省 3~4 秒。
    $arguments = @('build', $appProject, '-c', 'Release', '--nologo', '-v', 'q')
    if (Test-Path -LiteralPath (Join-Path $projectRoot 'src\PressureMap.App\obj\project.assets.json')) { $arguments += '--no-restore' }
    $buildLog = @()
    # Core 没改动时不再连带编译它，界面改动可再省 2~3 秒。
    $coreDll = Join-Path $projectRoot 'src\PressureMap.Core\bin\Release\net10.0\PressureMap.Core.dll'
    $coreNewest = Get-NewestSourceTime (Join-Path $projectRoot 'src\PressureMap.Core')
    if ((Test-Path -LiteralPath $coreDll) -and $coreNewest -le (Get-Item -LiteralPath $coreDll).LastWriteTimeUtc) { $arguments += '-p:BuildProjectReferences=false' }
    & $dotnetPath @arguments 2>&1 | ForEach-Object { $buildLog += $_ }
    if ($LASTEXITCODE -ne 0) {
        $buildLog | Write-Host
        throw "编译失败（退出码 $LASTEXITCODE），请查看上面的错误。"
    }
    New-Item -ItemType File -Path $stamp -Force | Out-Null
    (Get-Item -LiteralPath $stamp).LastWriteTimeUtc = [DateTime]::UtcNow
} else {
    Write-Host '[3/4] 产物已是最新，跳过编译。'
}

if ($DryRun) { Write-Host "    DryRun：将启动 $appExe"; return }
if (-not (Test-Path -LiteralPath $appExe)) { throw "找不到可执行文件：$appExe" }

# 4) 启动。bin 产物是框架依赖版，需要指向工作区自带的 .NET 10 运行时。
if (Test-Path -LiteralPath $dotnetPath) { $env:DOTNET_ROOT = Split-Path $dotnetPath -Parent }
Write-Host "[4/4] 启动 $appExe"
Start-Process -FilePath $appExe -WorkingDirectory $binDir
