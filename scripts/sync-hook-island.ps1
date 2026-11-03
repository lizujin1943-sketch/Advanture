[CmdletBinding()]
param([string]$Python = "python")

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$previousDotnetCliHome = $env:DOTNET_CLI_HOME
Push-Location $repoRoot
try {
    Get-Command dotnet -CommandType Application -ErrorAction Stop | Out-Null
    Get-Command $Python -CommandType Application -ErrorAction Stop | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $repoRoot ".build/dotnet-home"

    # Build every dependency before replacing any DLL that Unity is using.
    foreach ($project in @(
        "samples/TacticalGame.Network/TacticalGame.Network.csproj",
        "framework/src/FrameSync.Diagnostics/FrameSync.Diagnostics.csproj",
        "samples/HookIsland.Network/HookIsland.Network.csproj"
    )) {
        & dotnet build $project -c Release --nologo --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }

    $coreDir = "adapters/Unity/Runtime/Plugins"
    $tacticalDir = "samples/TacticalGame.Unity/Assets/TacticalGame/Plugins/Generated"
    $hookDir = "samples/TacticalGame.Unity/Assets/HookIsland/Plugins/Generated"
    $tacticalBuild = "samples/TacticalGame.Network/bin/Release/netstandard2.1"
    $hookBuild = "samples/HookIsland.Network/bin/Release/netstandard2.1"
    $copies = @(
        foreach ($name in @(
            "FrameSync.Abstractions", "FrameSync.Runtime", "FrameSync.Transport",
            "FrameSync.Protocol.Protobuf", "FrameSync.Sessions", "ReliableUdp.Core",
            "FrameSync.Transport.Udp", "Google.Protobuf", "System.Runtime.CompilerServices.Unsafe"
        )) {
            @{ Source = "$tacticalBuild/$name.dll"; Destination = "$coreDir/$name.dll" }
        }
        @{ Source = "framework/src/FrameSync.Diagnostics/bin/Release/netstandard2.1/FrameSync.Diagnostics.dll";
           Destination = "$coreDir/FrameSync.Diagnostics.dll" }
        foreach ($name in @("Advanture.Tactical.Simulation", "Advanture.Tactical.Network")) {
            @{ Source = "$tacticalBuild/$name.dll"; Destination = "$tacticalDir/$name.dll" }
        }
        foreach ($name in @(
            "Advanture.HookIsland.Simulation", "Advanture.HookIsland.Network",
            "FrameSync.Persistence", "FrameSync.Storage.FileSystem"
        )) {
            @{ Source = "$hookBuild/$name.dll"; Destination = "$hookDir/$name.dll" }
        }
    )
    foreach ($copy in $copies) {
        if (-not (Test-Path -LiteralPath $copy.Source -PathType Leaf)) {
            throw "Missing build output: $($copy.Source)"
        }
    }
    foreach ($directory in @($coreDir, $tacticalDir, $hookDir, "artifacts/tactical-network")) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    foreach ($copy in $copies) {
        Copy-Item -LiteralPath $copy.Source -Destination $copy.Destination -Force
    }
    $copies | Where-Object { $_.Destination -notlike "$hookDir/*" } | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Destination)"
    } | Set-Content -LiteralPath "artifacts/tactical-network/unity-libraries.sha256" -Encoding ASCII

    & $Python scripts/prepare-hook-island-map.py
    if ($LASTEXITCODE -ne 0) { throw "Island map generation failed." }
    Write-Host "Unity libraries synchronized. In Unity, select Assets > Refresh to recompile."
}
finally {
    $env:DOTNET_CLI_HOME = $previousDotnetCliHome
    Pop-Location
}
