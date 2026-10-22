[CmdletBinding(SupportsShouldProcess)]
param([switch]$OldReleases)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
function Assert-CleanupPath([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside project: $absolute" }
    if (Test-Path -LiteralPath $absolute) {
        $item = Get-Item -LiteralPath $absolute -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked directory: $absolute" }
    }
    return $absolute
}
$targets = [Collections.Generic.List[string]]::new()
foreach ($name in @('.test-temp','.release-stage','test-results')) { $targets.Add((Assert-CleanupPath (Join-Path $projectRoot $name))) }
foreach ($source in @('src','tests')) {
    Get-ChildItem -LiteralPath (Join-Path $projectRoot $source) -Filter '*.csproj' -File -Recurse | ForEach-Object {
        foreach ($name in @('bin','obj')) { $targets.Add((Assert-CleanupPath (Join-Path $_.DirectoryName $name))) }
    }
}
if ($OldReleases) {
    foreach ($name in @('PressureMap-Updated','PressureMap-ScanSettings','PressureMap-OnlineCourses','PressureMap-OnlineCourses-v2','PressureMap-OnlineCourses-v3','PressureMap-OnlineCourses-v4','PressureMap-OnlineCourses-v5','PressureMap-OnlineCourses-v6')) {
        $target = Assert-CleanupPath (Join-Path $projectRoot "dist\$name")
        if (Test-Path -LiteralPath $target) {
            $privateFiles = Get-ChildItem -LiteralPath $target -File -Recurse -Force | Where-Object { $_.Extension -in @('.db','.sqlite','.sqlite3','.account','.session') -or $_.Name -eq 'settings-v2.json' }
            if ($privateFiles) { throw "User data found; preserving old release: $target" }
        }
        $targets.Add($target)
    }
}
$running = @(Get-Process -Name PressureMap,PressureMapNative -ErrorAction SilentlyContinue)
[long]$bytes = 0
foreach ($target in $targets | Select-Object -Unique) {
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $verified = Assert-CleanupPath $target
    if ($running | Where-Object { $_.Path -and $_.Path.StartsWith($verified + '\', [StringComparison]::OrdinalIgnoreCase) }) { throw "Application is running from cleanup target: $verified" }
    if ($PSCmdlet.ShouldProcess($verified, 'Remove generated files')) {
        $size = (Get-ChildItem -LiteralPath $verified -File -Recurse -Force | Measure-Object Length -Sum).Sum
        Remove-Item -LiteralPath $verified -Recurse -Force
        $bytes += $size
        Write-Output "Removed: $verified"
    }
}
Write-Output ('Cleaned {0:N1} MB. Source, runtime dependencies, dist\PressureMap and user data preserved.' -f ($bytes / 1MB))
