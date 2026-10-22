param([switch]$SkipAudit)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$dotnet = if (Test-Path 'E:/AIGC/.tools/dotnet/dotnet.exe') { 'E:/AIGC/.tools/dotnet/dotnet.exe' } else { 'dotnet' }
$buildArgs = @('build', 'PressureMap.Server.slnx', '-m:1', '--nologo')
if ($SkipAudit) { $buildArgs += '-p:NuGetAudit=false' }
& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet run --project tests/PressureMap.Server.Checks --no-build
exit $LASTEXITCODE
