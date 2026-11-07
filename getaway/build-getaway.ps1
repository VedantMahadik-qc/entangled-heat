# Builds the Quantum Getaway release zip: release\QuantumGetaway-<version>.zip
# Usage (from this folder):  powershell -ExecutionPolicy Bypass -File build-getaway.ps1 [-Version 0.1.0]
# Refresh the seed cache first if needed:  python tools/getaway_seed.py <hop-sample.json> <leap-sample.json>
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe" }

& $dotnet build (Join-Path $root "src\QuantumGetaway.sln") -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "build failed" }
& $dotnet test (Join-Path $root "src\QuantumGetaway.Tests") -c Release --no-build --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

$stage = Join-Path $root "release\QuantumGetaway-$Version"
$scripts = Join-Path $stage "scripts"
$cache = Join-Path $scripts "QuantumGetaway\cache"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $cache | Out-Null

# The script, its Core library and Newtonsoft.Json. SHVDN itself comes from the player's install.
$bin = Join-Path $root "src\QuantumGetaway\bin\Release\net48"
foreach ($dll in "QuantumGetaway.dll", "QuantumGetaway.Core.dll", "Newtonsoft.Json.dll") {
    Copy-Item (Join-Path $bin $dll) $scripts
}
# Example config with an empty key: players add their own.
Copy-Item (Join-Path $root "config\QuantumGetaway.example.ini") (Join-Path $scripts "QuantumGetaway.ini")
# Real Atlas jobs, so the very first tunnels have quantum data even before the API answers.
foreach ($name in @("leap.json", "orbs.json")) {
    $seed = Join-Path $root "seed-cache\$name"
    if (-not (Test-Path $seed)) { throw "seed-cache\$name missing; run: python tools/getaway_seed.py" }
    Copy-Item $seed $cache
}
Copy-Item (Join-Path $root "README.md") $stage
@"
Quantum Getaway $Version uses Newtonsoft.Json (MIT licence, https://www.newtonsoft.com/json).
It needs ScriptHookV (Alexander Blade) and ScriptHookVDotNet 3, which are not included.
"@ | Set-Content (Join-Path $stage "THIRD_PARTY.txt") -Encoding utf8

# Never ship the real API key.
$ini = Join-Path $root "config\QuantumGetaway.ini"
if (Test-Path $ini) {
    $key = (Select-String -Path $ini -Pattern '^\s*ApiKey\s*=\s*(\S+)').Matches | ForEach-Object { $_.Groups[1].Value } | Select-Object -First 1
    if ($key) {
        $leak = Get-ChildItem $stage -Recurse -File | Select-String -SimpleMatch $key -List
        if ($leak) { throw "API key found in release files: $($leak.Path -join ', ')" }
    }
}

$zip = Join-Path $root "release\QuantumGetaway-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Built $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { "  " + $_.FullName.Substring($stage.Length + 1) }
