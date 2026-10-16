#!/usr/bin/env python3
"""启动两个真正独立的 macOS Unity 客户端。--smoke 会自动输入、核对状态并退出。"""
import argparse
import json
import plistlib
import socket
import subprocess
import time
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app', type=Path, default=ROOT / 'artifacts/tactical-network/Yard07Network.app')
parser.add_argument('--smoke', action='store_true')
parser.add_argument('--port', type=int, default=0, help='0 表示选择当前空闲的本机 UDP 端口')
args = parser.parse_args()
with (args.app / 'Contents/Info.plist').open('rb') as file:
    executable = args.app / 'Contents/MacOS' / plistlib.load(file)['CFBundleExecutable']
port = args.port
if port == 0:
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.bind(('127.0.0.1', 0))
        port = probe.getsockname()[1]
# 每次运行建立独立目录，避免下一次试玩覆盖上一次证据。
run_name = ('smoke-' if args.smoke else 'manual-') + time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8]
output = ROOT / 'artifacts/tactical-network' / run_name
output.mkdir(parents=True, exist_ok=False)
processes = []
try:
    for role in ('host', 'join'):
        command = [str(executable), '-yard-role', role, '-yard-address', '127.0.0.1', '-yard-port', str(port),
                   '-yard-diagnostics', str(output / 'diagnostics'),
                   '-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '640', '-logFile', str(output / (role + '.log'))]
        if args.smoke:
            command += ['-yard-smoke', '-yard-report', str(output / (role + '.json'))]
        processes.append(subprocess.Popen(command, cwd=ROOT, start_new_session=True, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
    print(f'两个客户端已启动，端口 {port}，日志目录：{output}', flush=True)
    if not args.smoke:
        # 普通试玩留在桌面，脚本退出不会关闭客户端。
        raise SystemExit(0)
    deadline = time.monotonic() + 60
    for process in processes:
        if process.wait(timeout=max(1, deadline - time.monotonic())) != 0:
            raise RuntimeError('Unity 客户端异常退出，请查看日志')
    reports = [json.loads((output / name).read_text()) for name in ('host.json.server.json', 'host.json', 'join.json')]
    if any(item.get('error') for item in reports):
        raise RuntimeError('联机报告包含失败：' + repr(reports))
    if len({(item['tick'], item['hash']) for item in reports}) != 1 or int(reports[0]['tick']) < 180:
        raise RuntimeError('服务器和两端最终确认状态不一致')
    for item in reports[1:]:
        for key in ('sawRemoteMovement', 'sawRemoteTurning', 'sawRenderedMovement', 'remoteInView'):
            if not item.get(key):
                raise RuntimeError(f"玩家 {item['slot']} 未验证 {key}")
    # 普通运行和自动验收走同一条诊断流程，不能只验证 smoke 的专用报告。
    diagnostic_paths = sorted((output / 'diagnostics').glob('*/*/summary.json'))
    if len(diagnostic_paths) != 3:
        raise RuntimeError('缺少服务器或客户端的自动诊断摘要')
    for path in diagnostic_paths:
        item = json.loads(path.read_text())
        if not item['finished'] or item['desyncDetected'] or item['endState'] != 'SessionClosed':
            raise RuntimeError('诊断记录未正常结束：' + str(path))
        if (str(item['confirmedTick']), item['confirmedHash']) != (reports[0]['tick'], reports[0]['hash']):
            raise RuntimeError('诊断快照与正式验收结果不一致：' + str(path))
        if not (path.parent / 'final.checkpoint').is_file():
            raise RuntimeError('没有保存最终快照：' + str(path))
    result = {'diagnosticSummaries': [str(p.relative_to(output)) for p in diagnostic_paths], 'passed': True, 'port': port, 'tick': reports[0]['tick'], 'hash': reports[0]['hash'], 'output': str(output)}
    (output / 'result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)
finally:
    if args.smoke:
        # 只清理由本脚本创建的进程，不影响已经打开的编辑器或其他试玩。
        for process in processes:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
