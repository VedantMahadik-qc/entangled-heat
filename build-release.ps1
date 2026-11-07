# Builds the Entangled Heat release zip: release\EntangledHeat-<version>.zip
# Usage (from the project root):  powershell -ExecutionPolicy Bypass -File build-release.ps1 [-Version 0.1.0]
# Refresh the seed cache first if needed:  python src/tools/atlas_probe.py seed
param([string]$Version = "0.2.0")
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe" }

& $dotnet build (Join-Path $root "src\EntangledHeat.sln") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "build failed" }
& $dotnet test (Join-Path $root "src\EntangledHeat.Tests") -c Release --no-build --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "tests failed" }
# The getaway half (portals + entangled orbs) lives in getaway\ and ships in the same zip.
$getaway = Join-Path $root "getaway"
& $dotnet build (Join-Path $getaway "src\QuantumGetaway.sln") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "getaway build failed" }
& $dotnet test (Join-Path $getaway "src\QuantumGetaway.Tests") -c Release --no-build --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "getaway tests failed" }

$stage = Join-Path $root "release\EntangledHeat-$Version"
$scripts = Join-Path $stage "scripts"
$cache = Join-Path $scripts "EntangledHeat\cache"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $cache | Out-Null

# The script, its Core library and Newtonsoft.Json. SHVDN itself comes from the player's install.
$bin = Join-Path $root "src\EntangledHeat\bin\Release\net48"
foreach ($dll in "EntangledHeat.dll", "EntangledHeat.Core.dll", "Newtonsoft.Json.dll") {
    Copy-Item (Join-Path $bin $dll) $scripts
}
# Example config with an empty key: players add their own.
Copy-Item (Join-Path $root "config\EntangledHeat.example.ini") (Join-Path $scripts "EntangledHeat.ini")
# Real Atlas jobs, so the very first run has quantum data even before the API answers.
$seed = Get-ChildItem (Join-Path $root "seed-cache") -Filter *.json
if ($seed.Count -lt 6) { throw "seed-cache is incomplete; run: python src/tools/atlas_probe.py seed" }
$seed | Copy-Item -Destination $cache

# Portals and entangled orbs: their script, Core library, config (empty key: it uses
# EntangledHeat.ini's) and real Atlas jobs (blur-v1 and graph-v1) for the first run.
$gbin = Join-Path $getaway "src\QuantumGetaway\bin\Release\net48"
foreach ($dll in "QuantumGetaway.dll", "QuantumGetaway.Core.dll") {
    Copy-Item (Join-Path $gbin $dll) $scripts
}
Copy-Item (Join-Path $getaway "config\QuantumGetaway.example.ini") (Join-Path $scripts "QuantumGetaway.ini")
$gcache = Join-Path $scripts "QuantumGetaway\cache"
New-Item -ItemType Directory -Force $gcache | Out-Null
foreach ($name in "leap.json", "orbs.json") {
    Copy-Item (Join-Path $getaway "seed-cache\$name") $gcache
}
Copy-Item (Join-Path $root "docs\INSTALL.md") $stage
@"
Entangled Heat $Version uses Newtonsoft.Json (MIT licence, https://www.newtonsoft.com/json).
It needs ScriptHookV (Alexander Blade) and ScriptHookVDotNet 3, which are not included.
"@ | Set-Content (Join-Path $stage "THIRD_PARTY.txt") -Encoding utf8

# Never ship the real API key.
$ini = Join-Path $root "config\EntangledHeat.ini"
if (Test-Path $ini) {
    $key = (Select-String -Path $ini -Pattern '^\s*ApiKey\s*=\s*(\S+)').Matches | ForEach-Object { $_.Groups[1].Value } | Select-Object -First 1
    if ($key) {
        $leak = Get-ChildItem $stage -Recurse -File | Select-String -SimpleMatch $key -List
        if ($leak) { throw "API key found in release files: $($leak.Path -join ', ')" }
    }
}

$zip = Join-Path $root "release\EntangledHeat-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Built $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { "  " + $_.FullName.Substring($stage.Length + 1) }
