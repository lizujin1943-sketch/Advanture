#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/hook-island-playable
dotnet build tools/HookIsland.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Tests/bin/Release/net8.0/HookIsland.Tests.dll | tee artifacts/hook-island-playable/logic-network-tests.log

# 格子背包、个人钱包、库存事务及四客户端真实 UDP 回归。
mkdir -p artifacts/backpack
dotnet build tools/HookIsland.Inventory.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HookIsland.Inventory.Tests/bin/Release/net8.0/HookIsland.Inventory.Tests.dll | tee artifacts/backpack/logic-network-tests.log
