#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
mkdir -p artifacts/combat-spacing
dotnet build tools/HookIsland.CombatSpacing.Tests -c Release --nologo --disable-build-servers -m:1 -v:quiet
if ! dotnet tools/HookIsland.CombatSpacing.Tests/bin/Release/net8.0/HookIsland.CombatSpacing.Tests.dll "$@" > artifacts/combat-spacing/tests.log 2>&1; then
  cat artifacts/combat-spacing/tests.log
  exit 1
fi
cat artifacts/combat-spacing/tests.log
