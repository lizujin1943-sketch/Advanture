#!/usr/bin/env python3
"""把纯 C# 工程加入 Unity 生成的解决方案，供 Cursor / VS Code 同时索引。"""

import os
from pathlib import Path
import re
import subprocess
import sys


def main():
    root = Path(__file__).resolve().parent.parent
    solution = root / "samples/TacticalGame.Unity/TacticalGame.Unity.sln"
    if not solution.is_file():
        sys.exit(
            "请先在 Unity Preferences > External Tools 选择 Visual Studio Code，"
            "并执行 Regenerate project files，再运行本脚本。"
        )

    content = (root / "Advanture.sln").read_text(encoding="utf-8-sig")
    paths = re.findall(r'^Project\([^\n]+ = "[^"]+", "([^"]+\.csproj)"', content, re.M)
    if not paths:
        sys.exit("Advanture.sln 中未找到 C# 工程，未修改 Unity 解决方案。")
    projects = [root / path.replace("\\", "/") for path in paths]
    for project in projects:
        if not project.is_file():
            sys.exit(f"工程不存在，未修改 Unity 解决方案：{project}")

    env = dict(os.environ, DOTNET_CLI_HOME=str(root / ".build/dotnet-home"))
    subprocess.run(
        ["dotnet", "sln", str(solution), "add", *map(str, projects)],
        cwd=root, env=env, check=True,
    )
    print(f"Unity 解决方案已包含 {len(projects)} 个纯 C# 工程。")


if __name__ == "__main__":
    main()
