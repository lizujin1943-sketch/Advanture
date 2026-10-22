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
dotnet framework/tests/FrameSync.Persistence.Tests/bin/Release/net8.0/FrameSync.Persistence.Tests.dll
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

# 阶段 B：真实字节链路、命令行交互，以及两个游戏各 10000 帧的四客户端一致性。
dotnet build tools/SyncHarness.Tests -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/SyncHarness.Tests/bin/Release/net8.0/SyncHarness.Tests.dll
sync_harness="tools/SyncHarness/bin/Release/net8.0/SyncHarness.dll"
mkdir -p artifacts/stage-b
dotnet "$sync_harness" | tee artifacts/stage-b/two-clients.log
dotnet "$sync_harness" --game counter --clients 4 --frames 10000 | tee artifacts/stage-b/counter-four-clients.log
dotnet "$sync_harness" --game grid --clients 4 --frames 10000 | tee artifacts/stage-b/grid-four-clients.log

# 阶段 E：独立传输算法的故障网络测试，以及真正独立进程的 UDP 联机。
dotnet framework/tests/ReliableUdp.Tests/bin/Release/net8.0/ReliableUdp.Tests.dll
dotnet build tools/UdpHarness.Tests -c Release --disable-build-servers -m:1 -v:quiet
mkdir -p artifacts/stage-e
dotnet tools/UdpHarness.Tests/bin/Release/net8.0/UdpHarness.Tests.dll | tee artifacts/stage-e/integration.log

# 阶段 C/D 集成：真实可靠 UDP 算法配合可重复故障网络，验证两种玩法的预测与确认。
dotnet build tools/PredictionHarness -c Release --disable-build-servers -m:1 -v:quiet
prediction_out="artifacts/initial-core/prediction"
dotnet tools/PredictionHarness/bin/Release/net8.0/PredictionHarness.dll --output "$prediction_out"
for recording in "$prediction_out"/*.fsrp; do
    expected=$(cat "${recording%.fsrp}.sha256")
    dotnet "$harness" --replay "$recording" --expected-hash "$expected" --report "${recording%.fsrp}-replay.json"
done

# 独立试玩接入：纯 C# 移动、碰撞、保存恢复，以及双客户端真实 UDP。无需安装 Unity。
./scripts/verify-tactical.sh
./scripts/verify-hook-island.sh

# 角色属性、装备装卸、状态恢复及四客户端一致性。
./scripts/verify-hook-character.sh

# 落水、泳圈、出钩移动锁与四客户端救援。
./scripts/verify-hook-swimming.sh

# 食人鱼、水怪、打断和击退，含单人及四客户端完整 checkpoint 一致性。
./scripts/verify-hook-enemies.sh

# 倒地、免费救援、个人付费复活与四客户端一致性。
./scripts/verify-hook-revival.sh

# 全队失败后保留连接重开、旧局隔离与单人／四人连续换局。
./scripts/verify-hook-restart.sh
