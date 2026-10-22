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
parser.add_argument("--app", type=Path)
parser.add_argument("--players", type=int, choices=range(1, 5), default=2)
parser.add_argument("--port", type=int, default=0)
parser.add_argument("--smoke", action="store_true")
parser.add_argument("--shop", action="store_true", help="Fish for coins, visit the shop, upgrade both stats, then catch again.")
parser.add_argument("--prank", action="store_true", help="Fish, pair up, take turns hooking teammates, then verify synchronized release.")
parser.add_argument("--platform", action="store_true", help="Collect personal wood/bones, build both tile types, and walk onto the shared expansion.")
parser.add_argument("--rods", action="store_true", help="Earn personal coins, buy and equip rods, then catch again on four synchronized clients.")
parser.add_argument("--inventory", action="store_true", help="Verify personal wallets, grid inventory, transfer/build and character equipment on four clients.")
parser.add_argument("--swimming", action="store_true", help="Earn/equip a swim ring, swim/cast with movement locked, rescue a friend on four clients.")
parser.add_argument("--enemies", action="store_true", help="Interrupt the monster, take a waterball hit, fall and receive piranha damage.")
parser.add_argument("--revival", action="store_true", help="Earn coins, tow and help a downed teammate, then pay for a personal respawn on four clients.")
parser.add_argument("--restart", action="store_true", help="Lose, restart the whole party in the same room, then fish again.")
parser.add_argument("--cooperative", action="store_true", help="Verify shared fish, reverse stealing and shared piranha through ordinary input in a development fixture.")
parser.add_argument("--room-rules", action="store_true", help="Verify host policy, blocked land casts, mid-pull disable and water rescue.")
parser.add_argument("--treasure", action="store_true", help="Retrieve small/rare chests and haul/open a large chest through ordinary input.")
parser.add_argument("--commissions", action="store_true", help="Complete three shared fishing commissions through ordinary input, preserving personal rewards.")
parser.add_argument("--ocean", action="store_true", help="Unlock ocean bands and catch a recovery visit through ordinary input.")
parser.add_argument("--finale", action="store_true", help="Earn bait, complete three giant-fish shores, win and restart the same room.")
parser.add_argument("--merchants", action="store_true", help="Earn materials, build all supply routes, buy a better rod and return to fishing.")
parser.add_argument("--recovery", action="store_true", help="Earn and buy, interrupt the final player's UDP connection, reconnect through the normal action, then fish again.")
parser.add_argument("--late-join", action="store_true", help="Start alone and join reserved seats sequentially before normal earning and purchases.")
args = parser.parse_args()
if (args.recovery or args.late_join) and (args.recovery and args.late_join or any((args.merchants,args.finale,args.ocean,args.commissions,args.treasure,args.cooperative,args.room_rules,args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods))):
    parser.error("--recovery and --late-join are separate acceptance scenarios.")
if args.merchants and any((args.finale,args.ocean,args.commissions,args.treasure,args.cooperative,args.room_rules,args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--merchants cannot be combined with other scenarios.")
if args.finale and any((args.ocean,args.commissions,args.treasure,args.cooperative,args.room_rules,args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--finale cannot be combined with other scenarios.")
if args.ocean and any((args.commissions,args.treasure,args.cooperative,args.room_rules,args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--ocean cannot be combined with other scenarios.")
if args.commissions and any((args.treasure,args.cooperative,args.room_rules,args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--commissions cannot be combined with other scenarios.")
if args.treasure and any((args.cooperative,args.room_rules,args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--treasure cannot be combined with other scenarios.")
if args.room_rules and any((args.cooperative,args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--room-rules cannot be combined with other scenarios.")
if args.cooperative and any((args.restart,args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--cooperative cannot be combined with other scenarios.")
if args.restart and any((args.enemies,args.revival,args.swimming,args.inventory,args.platform,args.shop,args.prank,args.rods)):
    parser.error("--restart cannot be combined with other scenarios.")
if args.revival and (args.players != 4 or args.enemies or args.swimming or args.inventory or args.platform or args.shop or args.prank or args.rods):
    parser.error("--revival requires --players 4 and no other scenario.")
if args.enemies and (args.swimming or args.inventory or args.platform or args.shop or args.prank or args.rods):
    parser.error("--enemies cannot be combined with other scenarios.")
if args.swimming and (args.inventory or args.platform or args.shop or args.prank or args.rods or args.players != 4):
    parser.error("--swimming requires --players 4 and no other scenario.")
if args.inventory and (args.platform or args.shop or args.prank or args.rods or args.players != 4):
    parser.error("--inventory requires --players 4 and no other scenario.")
if args.rods and (args.platform or args.shop or args.prank):
    parser.error("--rods is a separate platform acceptance scenario.")
if args.platform and (args.shop or args.prank):
    parser.error("--platform uses the platform scene and cannot be combined with legacy shop/prank scenarios.")
if args.app is None:
    args.app = ROOT / "artifacts/hook-island-playable" / ("HookPlatform.app" if args.platform or args.rods or args.inventory or args.swimming or args.enemies or args.revival or args.restart or args.cooperative or args.room_rules or args.treasure or args.commissions or args.ocean or args.finale or args.merchants or args.recovery or args.late_join else "HookIsland.app")
if args.prank and (args.players not in (2, 4) or args.shop):
    parser.error("--prank requires 2 or 4 players and cannot be combined with --shop.")
if args.shop or args.prank or args.platform or args.rods or args.inventory or args.swimming or args.enemies or args.revival or args.restart or args.cooperative or args.room_rules or args.treasure or args.commissions or args.ocean or args.finale or args.merchants or args.recovery or args.late_join:
    args.smoke = True
with (args.app / "Contents/Info.plist").open("rb") as file:
    executable = args.app / "Contents/MacOS" / plistlib.load(file)["CFBundleExecutable"]
port = args.port
if not port:
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.bind(("127.0.0.1", 0))
        port = probe.getsockname()[1]
output = ROOT / "artifacts/hook-island-playable" / (
    ("recovery-smoke-" if args.recovery else "late-join-smoke-" if args.late_join else "merchant-smoke-" if args.merchants else "finale-smoke-" if args.finale else "ocean-smoke-" if args.ocean else "commission-smoke-" if args.commissions else "treasure-smoke-" if args.treasure else "room-rules-smoke-" if args.room_rules else "cooperative-smoke-" if args.cooperative else "restart-smoke-" if args.restart else "revival-smoke-" if args.revival else "enemy-smoke-" if args.enemies else "swimming-smoke-" if args.swimming else "inventory-smoke-" if args.inventory else "rod-smoke-" if args.rods else "platform-smoke-" if args.platform else "prank-smoke-" if args.prank else "shop-smoke-" if args.shop else "smoke-" if args.smoke else "party-") + time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:6])
output.mkdir(parents=True)
processes = []
try:
    for i in range(args.players):
        if args.late_join and i:
            deadline = time.monotonic() + 40
            stage = output / f"client0.json.joined-{i}.png"
            while not stage.exists() and time.monotonic() < deadline and processes[0].poll() is None:
                time.sleep(.2)
            if not stage.exists():
                raise RuntimeError(f"Owner did not render exactly {i} entered players before next join.")
            time.sleep(2)
        command = [str(executable), "-island-role", "host" if i == 0 else "join",
                   "-island-address", "127.0.0.1", "-island-port", str(port), "-island-players", str(args.players),
                   "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "800",
                   "-logFile", str(output / f"client{i}.log")]
        if args.smoke:
            command += ["-island-report", str(output / f"client{i}.json")]
        if args.recovery or args.late_join:
            command += ["-island-recovery-smoke", "1"]
        if args.late_join:
            command += ["-island-late-join", "1"]
        if args.merchants:
            command += ["-island-merchant-smoke", "1"]
        if args.finale:
            command += ["-island-finale-smoke", "1"]
        if args.ocean:
            command += ["-island-ocean-smoke", "1"]
        if args.commissions:
            command += ["-island-commission-smoke", "1"]
        if args.treasure:
            command += ["-island-treasure-smoke", "1"]
        if args.room_rules:
            command += ["-island-room-rules-smoke", "1"]
        if args.cooperative:
            command += ["-island-cooperative-smoke", "1"]
        if args.swimming:
            command += ["-island-swimming-smoke", "1"]
        if args.restart:
            command += ["-island-restart-smoke", "1"]
        if args.revival:
            command += ["-island-revival-smoke", "1"]
        if args.enemies:
            command += ["-island-enemy-smoke", "1"]
        if args.inventory:
            command += ["-island-inventory-smoke", "1"]
        if args.rods:
            command += ["-island-rod-smoke", "1"]
        if args.shop:
            command += ["-island-shop-smoke", "1"]
        if args.prank:
            command += ["-island-prank-smoke", "1"]
        with (output / f"client{i}.process.log").open("wb") as process_log:
            processes.append(subprocess.Popen(command, cwd=ROOT, start_new_session=True,
                                              stdin=subprocess.DEVNULL, stdout=process_log, stderr=subprocess.STDOUT))
    print(f"{args.players} 个客户端已启动，UDP {port}，输出：{output}", flush=True)
    if args.smoke:
        deadline = time.monotonic() + (240 if args.recovery or args.late_join else 450 if args.merchants else 210 if args.rods or args.inventory or args.swimming or args.revival or args.restart or args.commissions or args.ocean or args.finale else 145 if args.shop or args.prank or args.platform or args.cooperative or args.treasure else 95)
        for index, process in enumerate(processes):
            code = process.wait(timeout=max(1, deadline - time.monotonic()))
            if code:
                raise RuntimeError(f"Client {index} exited {code}; inspect client{index}.log and client{index}.process.log in {output}.")
        paths = [output / "client0.json.server.json"] + [output / f"client{i}.json" for i in range(args.players)]
        reports = [json.loads(p.read_text()) for p in paths]
        if any(r.get("error") for r in reports):
            raise RuntimeError("Fishing verification failed: " + repr(reports))
        if len({(r["tick"], r["hash"]) for r in reports}) != 1 or int(reports[0]["tick"]) < (45 if args.restart or args.finale else 600):
            raise RuntimeError("Server/client final states do not agree.")
        for r in reports[1:]:
            if args.recovery or args.late_join:
                if not r["complete"] or r["renderedPlayers"] != args.players or r["activeSmokeDrivers"] != 1:
                    raise RuntimeError("Recovery or late join presentation incomplete.")
                if any(p["request"] == 0 or p["receipt"] != "Success" or p["earned"] <= p["coins"] for p in r["players"]):
                    raise RuntimeError("Recovery did not preserve normal earning and purchase receipts.")
                if args.recovery and not all(r[k] for k in ("offlineSeen", "resumed", "preserved")):
                    raise RuntimeError("Recovery did not preserve the offline player's state.")
                if args.recovery and r["slot"] == args.players - 1 and (not r["syncImage"] or r["sessionRetained"]):
                    raise RuntimeError("Target did not create a new connection through reconnect.")
                if (args.late_join or r["slot"] != args.players - 1) and not r["sessionRetained"]:
                    raise RuntimeError("Another player's arrival reset an existing session.")
                continue
            if args.merchants:
                if not r["complete"] or r["visited"] != 15 or r["returned"] != (1 << args.players) - 1 or r["purchaseTier"] < 2:
                    raise RuntimeError("Merchant routes, upgraded rod or return catch incomplete.")
                if r["panelImages"] != 15 or not r["overview"] or not r["purchaseImage"] or r["renderedShips"] != 4 or r["renderedPlayers"] != args.players or r["activeSmokeDrivers"] != 1:
                    raise RuntimeError("Merchant panels, landmarks or driver isolation incomplete.")
                if r["tiles"] != 33 or sum(p["bonesEarned"] - p["bones"] for p in r["players"]) != 96:
                    raise RuntimeError("Supply routes bypassed ordinary material costs.")
                if sum(p["receipt"] == "Success" for p in r["players"]) != 1 or sum(p["receipt"] == "SoldOut" for p in r["players"]) != args.players - 1:
                    raise RuntimeError("Shared stock purchase duplicated or lost receipts.")
                continue
            if args.finale:
                if not all(r[k] for k in ("complete","won","fresh","stable","giantSeen","dived","markerSeen","connectionRetained","identityRetained")) or r["epoch"] != "2" or r["changes"] != 1:
                    raise RuntimeError("Finale victory or same-room restart incomplete.")
                if r["shoresSeen"] != 7 or r["shoreImages"] != 7 or r["maxHooks"] != args.players or r["hookedPlayers"] != (1 << args.players) - 1:
                    raise RuntimeError("Finale shore changes or full party participation incomplete.")
                if r["activeSmokeDrivers"] != 1 or r["renderedPlayers"] != args.players or r["maxRenderedLines"] < args.players:
                    raise RuntimeError("Finale presentation or driver isolation incomplete.")
                continue
            if args.ocean:
                if not all(r[k] for k in ("complete","recoverySeen","visitCaught","recoveryImage","visitImage")) or r["stage"] != 2 or r["stagesSeen"] != 7:
                    raise RuntimeError("Ocean stages or recovery capture incomplete.")
                if r["activeSmokeDrivers"] != 1 or r["renderedPlayers"] != args.players:
                    raise RuntimeError("Ocean presentation or driver isolation incomplete.")
                if args.players > 1 and (not r["sharedSeen"] or r["maxRenderedLines"] < 2):
                    raise RuntimeError("Cooperative ocean fishing was not observed.")
                if any(p["catches"] <= 0 or p["coins"] != p["earned"] for p in r["players"]):
                    raise RuntimeError("Ocean captures lost personal rewards.")
                continue
            if args.commissions:
                if not all(r[k] for k in ("complete","progressSeen")) or r["completed"] != 3 or r["bait"] != 3 or r["completionsSeen"] != 15:
                    raise RuntimeError("Shared commission progress or bait incomplete.")
                if r["activeSmokeDrivers"] != 1 or r["renderedPlayers"] != args.players:
                    raise RuntimeError("Commission presentation or driver isolation incomplete.")
                if args.players > 1 and (not r["sharedSeen"] or r["maxRenderedLines"] < 2):
                    raise RuntimeError("Cooperative commission contribution not observed.")
                if any(p["catches"] <= 0 or p["coins"] != p["earned"] for p in r["players"]) or r["coins"] != sum(p["coins"] for p in r["players"]):
                    raise RuntimeError("Commission contribution or personal income differs.")
                if not 0 < r["fish"] <= sum(p["catches"] for p in r["players"]):
                    raise RuntimeError("Cooperation duplicated commission progress.")
                continue
            if args.treasure:
                if not r["complete"] or r["activeSmokeDrivers"] != 1 or r["renderedPlayers"] != args.players or r["maxRenderedChests"] < 2:
                    raise RuntimeError("Treasure retrieval or presentation incomplete.")
                if args.players > 1 and (not all(r[k] for k in ("shared","landed","opened")) or r["maxParticipants"] != args.players or r["maxRenderedLines"] < args.players):
                    raise RuntimeError("Large chest did not involve the full party.")
                if r["coins"] != sum(p["coins"] for p in r["players"]):
                    raise RuntimeError("Treasure wallet totals differ.")
                continue
            if args.room_rules:
                if not all(r[k] for k in ("complete","disabledSeen","enabledSeen","menuCaptured")) or r["friendlyPranksEnabled"] or r["lastRequest"] != 3 or r["activeSmokeDrivers"] != 1 or r["renderedPlayers"] != args.players:
                    raise RuntimeError("Room rules, request confirmation or menu incomplete.")
                if args.players > 1 and not all(r[k] for k in ("blockedLandCast","landPullSeen","releasedLandPull","rescued")):
                    raise RuntimeError("Land protection or water rescue incomplete.")
                if r["slot"] > 0 and not r["guestRejected"]:
                    raise RuntimeError("Guest could queue a host policy request.")
                continue
            if args.cooperative:
                if not r["complete"] or r["activeSmokeDrivers"] != 1 or r["renderedPlayers"] != args.players or r["piranhaCaptures"] != 1:
                    raise RuntimeError("Cooperative flow or presentation incomplete.")
                if args.players > 1 and (not all(r[k] for k in ("sharedFish","stolenFish","sharedPiranha")) or r["maxRenderedLines"] < 2):
                    raise RuntimeError("Shared lines, reverse stealing or piranha cooperation not observed.")
                if r["coins"] != sum(p["coins"] for p in r["players"]) or any(p["coins"] != p["earned"] for p in r["players"]):
                    raise RuntimeError("Cooperative wallet totals differ.")
                continue
            if args.restart:
                if not all(r.get(k) for k in ("complete","sawFailure","fresh","connectionRetained","identityRetained")) or r["epoch"] != "2" or r["changes"] != 1:
                    raise RuntimeError("Same-room restart lifecycle incomplete.")
                if r["renderedPlayers"] != args.players or r["renderedTiles"] != 9 or r["swimmerWakes"] != args.players or r["activeSmokeDrivers"] != 1:
                    raise RuntimeError("Restart left stale presentation or multiple acceptance drivers.")
                continue
            if args.revival:
                if not all(r.get(k) for k in ("complete", "freeRescue", "paidRespawn", "downed", "helping", "dead")) or r["renderedPlayers"] != 4 or r["activeSmokeDrivers"] != 1:
                    raise RuntimeError("Revival flow or presentation incomplete.")
                p = r["players"]
                if p[1]["spent"] != 0 or p[2]["spent"] <= 0 or p[2]["earned"] - p[2]["coins"] != p[2]["spent"]:
                    raise RuntimeError("Free rescue or personal payment ledger incorrect.")
                continue
            if args.enemies:
                if r.get("activeSmokeDrivers") != 1 or not all(r.get(k) for k in ("complete", "windup", "waterball", "knockback", "impact", "patrol", "submerged", "relocated", "hooked")):
                    raise RuntimeError("Enemy attack/interrupt/hit presentation incomplete.")
                if r["renderedPlayers"] != args.players or not 0 <= r["renderedPiranhas"] <= 3 or r["renderedMonsters"] not in (0, 1):
                    raise RuntimeError("Enemy/player presentation count differs.")
                if min(r[k] for k in ("bites", "shots", "interruptions", "waterHits", "piranhaCaptures")) < 1:
                    raise RuntimeError("Enemy combat cycle incomplete.")
                continue
            if args.swimming:
                if not r["complete"] or not r["lockObserved"] or r["renderedPlayers"] != 4:
                    raise RuntimeError("Swimming, rescue or movement lock presentation incomplete.")
                p = r["players"]
                if p[0]["ring"] != 1 or p[0]["health"] != 100 or p[0]["earned"] - p[0]["coins"] != 150:
                    raise RuntimeError("Swim ring protection or personal payment failed.")
                if not p[0]["water"] or p[2]["water"] or p[2]["rescued"] < 1 or p[2]["health"] >= 100 or not p[3]["water"]:
                    raise RuntimeError("Water and rescued states are incorrect.")
                continue
            if args.inventory:
                if not r["complete"] or not r["panelsChecked"] or not r["characterChecked"] or r["renderedPlayers"] != args.players:
                    raise RuntimeError("Backpack, character panel or presentation incomplete.")
                if [p["spent"] for p in r["players"]] != [200, 0, 0, 0] or any(p["earned"] - p["spent"] != p["coins"] for p in r["players"]):
                    raise RuntimeError("Personal purchase ledger differs.")
                if r["tiles"] != 9 + args.players or r["ground"] != 0:
                    raise RuntimeError("Dropped materials were not picked up and used to build.")
                continue
            if args.rods:
                if not r["complete"] or not r["spentStamina"] or not r["recovered"] or not r["panelsChecked"] or r["renderedPlayers"] != args.players:
                    raise RuntimeError("Rod equipment, stamina, recovery or presentation incomplete.")
                if any(p["equipped"] <= args.players or p["earned"] <= p["coins"] or p["catches"] < 2 for p in r["players"]):
                    raise RuntimeError("Personal fishing income was not used to buy and equip rods.")
                if r["coins"] != sum(p["coins"] for p in r["players"]):
                    raise RuntimeError("Personal wallet totals differ.")
                continue
            if args.platform:
                expected_tiles = 9 + args.players * 2
                if not r["complete"] or r["tileCount"] != expected_tiles or r["renderedTiles"] != expected_tiles or r["renderedPlayers"] != args.players:
                    raise RuntimeError("Platform geometry, shared state, or traversal is incomplete.")
                if r["boneTiles"] != args.players or r["woodTiles"] != 9 + args.players:
                    raise RuntimeError("Both materials were not built by all players.")
                if sum(p["woodEarned"] - p["wood"] for p in r["players"]) != 4 * args.players or sum(p["bonesEarned"] - p["bones"] for p in r["players"]) != 4 * args.players:
                    raise RuntimeError("Personal inventories did not pay the exact tile costs.")
                if any(p["catches"] < 1 or p["woodEarned"] < 4 or p["bonesEarned"] < 4 for p in r["players"]):
                    raise RuntimeError("A player bypassed the material collection loop.")
                continue
            if not r.get("observedSwimming") or not r.get("observedForwardLeap"):
                raise RuntimeError("A client did not observe underwater swimming and forward leaps.")
            if r["renderedPlayers"] != args.players or r["presentedCatches"] <= 0 or r["maxPositionError"] > .25:
                raise RuntimeError("Rendered actors/rewards do not match simulation.")
            if any(p["catches"] <= 0 or p["casts"] <= 0 for p in r["players"]):
                raise RuntimeError("A player did not complete the real fishing loop.")
            if args.shop:
                if r["postUpgradeCatches"] <= 0 or any(p["rangeLevel"] != 1 or p["speedLevel"] != 1 for p in r["players"]):
                    raise RuntimeError("A player did not buy both upgrades and catch again.")
                if any(p["coins"] <= p["catches"] * 10 for p in r["players"]):
                    raise RuntimeError("A player did not catch a higher-value school after upgrading range.")
                if r["coins"] != sum(p["coins"] - p["spent"] for p in r["players"]):
                    raise RuntimeError("Shared wallet did not subtract the exact upgrade costs.")
            if args.prank:
                if not r["observedPull"] or not r["observedProtection"] or r["presentedPlayerHooks"] < args.players:
                    raise RuntimeError("A client did not present synchronized player hooks and protection.")
                if any(p["playerHooks"] < 1 or p["timesHooked"] < 1 or p["pulledBy"] >= 0 or p["hookedPlayer"] >= 0 for p in r["players"]):
                    raise RuntimeError("A player did not finish both sides of a teammate hook.")
        if args.recovery or args.late_join:
            checkpoints = [p.with_name(p.name + ".checkpoint").read_bytes() for p in paths]
            if any(data != checkpoints[0] for data in checkpoints[1:]):
                raise RuntimeError("Server and clients differ in complete checkpoint bytes.")
            if args.late_join and reports[1]["joinedMask"] != (1 << args.players) - 1:
                raise RuntimeError("Owner did not observe every reserved-seat arrival.")
        result = dict(passed=True, players=args.players, tick=reports[0]["tick"], hash=reports[0]["hash"],
                      coins=reports[0]["coins"], shop=args.shop, prank=args.prank, platform=args.platform, rods=args.rods, inventory=args.inventory, swimming=args.swimming, enemies=args.enemies, revival=args.revival, restart=args.restart, cooperative=args.cooperative, room_rules=args.room_rules, treasure=args.treasure, commissions=args.commissions, ocean=args.ocean, finale=args.finale, merchants=args.merchants, recovery=args.recovery, late_join=args.late_join, output=str(output))
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
