#!/usr/bin/env python3
"""Launch 1–4 independent macOS fishing clients; --smoke checks all final states."""
import argparse
import json
import plistlib
from pathlib import Path
import socket
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--app", type=Path, default=ROOT / "artifacts/hook-island-playable/HookIsland.app")
parser.add_argument("--players", type=int, choices=range(1, 5), default=2)
parser.add_argument("--port", type=int, default=0)
parser.add_argument("--smoke", action="store_true")
args = parser.parse_args()
with (args.app / "Contents/Info.plist").open("rb") as file:
    executable = args.app / "Contents/MacOS" / plistlib.load(file)["CFBundleExecutable"]
port = args.port
if not port:
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
output = ROOT / "artifacts/hook-island-playable" / (
    ("smoke-" if args.smoke else "party-") + time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6])
output.mkdir(parents=True)
processes = []
try:
    for i in range(args.players):
        command = [str(executable), "-island-role", "host" if i == 0 else "join",
                   "-island-address", "127.0.0.1", "-island-port", str(port), "-island-players", str(args.players),
                   "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "800",
                   "-logFile", str(output / f"client{i}.log")]
        if args.smoke:
            command += ["-island-report", str(output / f"client{i}.json")]
        processes.append(subprocess.Popen(command, cwd=ROOT, start_new_session=True,
                                          stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
    print(f"{args.players} 个客户端已启动，UDP {port}，输出：{output}", flush=True)
    if args.smoke:
        deadline = time.monotonic() + 95
        for process in processes:
            if process.wait(timeout=max(1, deadline - time.monotonic())):
                raise RuntimeError("Client failed; inspect log files.")
        paths = [output / "client0.json.server.json"] + [output / f"client{i}.json" for i in range(args.players)]
        reports = [json.loads(p.read_text()) for p in paths]
        if any(r.get("error") for r in reports):
            raise RuntimeError("Fishing verification failed: " + repr(reports))
        if len({(r["tick"], r["hash"]) for r in reports}) != 1 or int(reports[0]["tick"]) < 600:
            raise RuntimeError("Server/client final states do not agree.")
        for r in reports[1:]:
            if r["renderedPlayers"] != args.players or r["presentedCatches"] <= 0 or r["maxPositionError"] > .25:
                raise RuntimeError("Rendered actors/rewards do not match simulation.")
            if any(p["catches"] <= 0 or p["casts"] <= 0 for p in r["players"]):
                raise RuntimeError("A player did not complete the real fishing loop.")
        result = dict(passed=True, players=args.players, tick=reports[0]["tick"], hash=reports[0]["hash"],
                      coins=reports[0]["coins"], output=str(output))
        (output / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
        print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
finally:
    if args.smoke:
        for process in processes:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
