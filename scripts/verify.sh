#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_CLI_HOME="$PWD/.build/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false

# The core solution intentionally does not contain sample or engine projects.
dotnet build framework/FrameSync.sln -c Release --disable-build-servers -m:1 -v:quiet
dotnet framework/tests/FrameSync.Tests/bin/Release/net8.0/FrameSync.Tests.dll
dotnet build tools/HeadlessHarness.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HeadlessHarness.Tests/bin/Release/net8.0/HeadlessHarness.Tests.dll

harness="tools/HeadlessHarness/bin/Release/net8.0/HeadlessHarness.dll"
out="artifacts/stage-a"
dotnet "$harness" --frames 100000 --output "$out" --report "$out/report.json"
for model in counter grid; do
    # These are fresh OS processes, each loading only its recording from disk.
    expected=$(cat "$out/$model.sha256")
    dotnet "$harness" --replay "$out/$model.fsrp" --expected-hash "$expected" --report "$out/$model-replay.json"
done
