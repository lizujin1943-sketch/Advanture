#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
dotnet build samples/TacticalGame.Network/TacticalGame.Network.csproj -c Release --nologo
dotnet build framework/src/FrameSync.Diagnostics/FrameSync.Diagnostics.csproj -c Release --nologo
core_dir="adapters/Unity/Runtime/Plugins"
game_dir="samples/TacticalGame.Unity/Assets/TacticalGame/Plugins/Generated"
built_dir="samples/TacticalGame.Network/bin/Release/netstandard2.1"
mkdir -p "$core_dir" "$game_dir" artifacts/tactical-network
for name in FrameSync.Abstractions FrameSync.Runtime FrameSync.Transport FrameSync.Protocol.Protobuf FrameSync.Sessions ReliableUdp.Core FrameSync.Transport.Udp; do
  cp "$built_dir/$name.dll" "$core_dir/$name.dll"
done
# 只分发运行库；Grpc.Tools/protoc 留在构建机。Unity 的 netstandard2.1 已提供 Memory/Buffers。
cp "$built_dir/Google.Protobuf.dll" "$core_dir/Google.Protobuf.dll"
cp "$built_dir/System.Runtime.CompilerServices.Unsafe.dll" "$core_dir/System.Runtime.CompilerServices.Unsafe.dll"
cp framework/src/FrameSync.Diagnostics/bin/Release/netstandard2.1/FrameSync.Diagnostics.dll "$core_dir/FrameSync.Diagnostics.dll"
for name in Advanture.Tactical.Simulation Advanture.Tactical.Network; do
  cp "$built_dir/$name.dll" "$game_dir/$name.dll"
done
shasum -a 256 "$core_dir"/*.dll "$game_dir"/*.dll > artifacts/tactical-network/unity-libraries.sha256
