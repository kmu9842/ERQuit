param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 x64 C# compiler is required.' }
$dist = Join-Path $root 'dist'
$testOut = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
New-Item -ItemType Directory -Path $testOut -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Where-Object Name -ne 'Setup.cs' | ForEach-Object FullName)
$common = @('/nologo', '/optimize+', '/warnaserror+', '/langversion:5', '/platform:x64',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Management.dll')
$app = Join-Path $dist 'ERQuit.exe'
& $compiler @common '/target:winexe' "/win32manifest:$root\app.manifest" "/out:$app" @sources
if ($LASTEXITCODE -ne 0) { throw 'ERQuit build failed.' }

if (-not $SkipTests) {
    $testExe = Join-Path $testOut 'ERQuit.Tests.exe'
    & $compiler @common '/target:exe' '/main:ERQuit.Tests' "/out:$testExe" @sources (Join-Path $root 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $dist 'Usage.txt') -Force
$setup = Join-Path $dist 'ERQuit-Setup.exe'
& $compiler @common '/target:winexe' "/win32manifest:$root\app.manifest" "/out:$setup" "/resource:$app,ERQuit.exe" "/resource:$root\README.md,Usage.txt" (Join-Path $root 'src\Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Get-FileHash -LiteralPath $app,$setup -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + [IO.Path]::GetFileName($_.Path) } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Build complete: $setup"
