#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Reuse the same core assemblies as the existing network sample.
./scripts/sync-unity-libraries.sh
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
dotnet build samples/HookIsland.Network/HookIsland.Network.csproj -c Release --nologo --disable-build-servers
destination="samples/TacticalGame.Unity/Assets/HookIsland/Plugins/Generated"
mkdir -p "$destination"
for name in Advanture.HookIsland.Simulation Advanture.HookIsland.Network FrameSync.Persistence FrameSync.Storage.FileSystem; do
  cp "samples/HookIsland.Network/bin/Release/netstandard2.1/$name.dll" "$destination/$name.dll"
done
python3 scripts/prepare-hook-island-map.py
