#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/drowned-bell-town
dotnet build tools/HookIsland.Town.Tests -c Release --disable-build-servers -m:1 -v:quiet
# 独立进程验证冷启动，防止前面用例生成的静态缓存掩盖首帧建图回归。
dotnet tools/HookIsland.Town.Tests/bin/Release/net8.0/HookIsland.Town.Tests.dll --navigation-startup-only | tee artifacts/drowned-bell-town/town-navigation-startup.log
dotnet tools/HookIsland.Town.Tests/bin/Release/net8.0/HookIsland.Town.Tests.dll "$@" | tee artifacts/drowned-bell-town/town-test.log
dotnet build tools/HookIsland.Town.Adapter.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Town.Adapter.Tests/bin/Release/net8.0/HookIsland.Town.Adapter.Tests.dll | tee artifacts/drowned-bell-town/town-adapter.log
