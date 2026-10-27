#!/usr/bin/env python3
"""Check a hand-written Mermaid diagram against the code it claims to describe.

Structure diagrams rot silently: nobody notices that a box was deleted or an
arrow now points the wrong way. This script does the mechanical part of the
review — it confirms that every file and type mentioned by a diagram node still
exists, and lists project references in the code that the diagram omits.

It deliberately does NOT try to parse Mermaid in full. It reads node labels and
quoted names, which is enough to catch the common staleness bugs without
pretending to understand the diagram's semantics.

Usage:
    python3 verify_diagram.py docs/architecture-overview.md --root .
    python3 verify_diagram.py diagram.mmd --root . --json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from scan_structure import collect_projects, rel, walk_files  # noqa: E402

MERMAID_BLOCK = re.compile(r"```mermaid\n(.*?)```", re.S)
NODE_LABEL = re.compile(r'[\[\(\{]{1,2}"?([^"\]\)\}\n|]+)"?[\]\)\}]{1,2}')
CLASS_DECL = re.compile(r"^\s*class\s+(\w+)(?:\[[^\]]*\])?", re.M)
NODE_DEF = re.compile(r"(?<![\w\]>])(\w+)\s*\[\"?(?P<label>[^\"\]\n]+)\"?\]")
# A lookahead lets arrows that share a node on one line all be found. Mermaid
# arrows mix dots and dashes (`-.->`, `==>`, `--o`), so the connector is matched
# as a run of those characters rather than an enumeration of every variant.
EDGE = re.compile(
    r"(?=(?<![\w])(\w+)\s*(?:-{1,3}[.=-]{0,3}>|[-=.]+x|[-=.]+o|\|\s*>)\s*(\w+))")
EDGE_LABEL = re.compile(r"\|[^|\n]*\|")


def load_diagrams(path: Path) -> list[dict]:
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    if path.suffix in (".mmd", ".mermaid"):
        return [{"source": path.name, "line": 1, "body": text}]
    diagrams = []
    for match in MERMAID_BLOCK.finditer(text):
        line = text[: match.start()].count("\n") + 2
        diagrams.append({"source": path.name, "line": line, "body": match.group(1)})
    return diagrams


def collect_repo_symbols(root: Path) -> dict:
    """Index the names a diagram is allowed to mention."""
    files = {rel(p, root) for p in root.rglob("*")
             if p.is_file() and not set(p.relative_to(root).parts) & {
                 ".git", ".build", "artifacts", "bin", "obj", "Library", "node_modules"}}
    types = set()
    for cs in walk_files(root, (".cs",)):
        try:
            text = cs.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        for match in re.finditer(r"\b(?:class|interface|struct|record|enum)\s+([A-Za-z_]\w*)", text):
            types.add(match.group(1))
    projects = collect_projects(root)
    return {
        "files": files,
        "types": types,
        "project_names": {p["name"] for p in projects},
        "project_paths": {p["path"] for p in projects},
        "projects": projects,
    }


CJK = re.compile(r"[\u3400-\u9fff\u3000-\u303f]")


def looks_like_path(token: str) -> bool:
    """Only ASCII tokens can be repository paths; CJK labels are prose."""
    if CJK.search(token):
        return False
    return "/" in token or re.search(r"\.\w{1,4}$", token) is not None


def check_diagram(body: str, symbols: dict, root: Path) -> dict:
    labels = {m.group(1).strip() for m in NODE_LABEL.finditer(body)}
    labels = {label for label in labels if label and not label.startswith("#")}
    classes = {m.group(1) for m in CLASS_DECL.finditer(body)}

    unknown_paths, unknown_types, unknown_projects = [], [], []
    for label in sorted(labels):
        base = label.split("<br/>")[0].strip()
        if looks_like_path(base):
            if base not in symbols["files"] and not (root / base).exists():
                unknown_paths.append(base)
        elif re.fullmatch(r"[A-Za-z_][\w.]*", base):
            short = base.split(".")[-1]
            if short in symbols["types"] or base in symbols["project_names"]:
                continue
            if short in {"graph", "classDiagram", "sequenceDiagram", "stateDiagram_v2"}:
                continue
            # Labels are frequently prose; only flag identifier-shaped misses
            # when they look like a type or project name.
            if re.fullmatch(r"[A-Z]\w*", short) and len(short) > 3:
                unknown_types.append(base)
    for name in sorted(classes):
        if name not in symbols["types"] and re.fullmatch(r"[A-Z]\w*", name):
            unknown_types.append(name)

    return {
        "labels_checked": len(labels),
        "missing_files": sorted(set(unknown_paths)),
        "unknown_type_or_project_names": sorted(set(unknown_types)),
    }


def diagram_graph(body: str) -> tuple[dict[str, str], set[tuple[str, str]]]:
    """Resolve node ids to their visible labels, then read edges as label pairs.

    Edges are written with ids (`n_a --> n_b`) while the code comparison works
    with names, so the id-to-label mapping has to come first. Matching on raw
    text instead would report every styled diagram as missing edges.
    """
    labels = {m.group(1): m.group("label").split("<br/>")[0].strip()
              for m in NODE_DEF.finditer(body)}
    # Nodes are often declared inline (`a[Alpha] --> b[Beta]`), which puts the
    # definition between the id and its arrow. Strip definitions first so the
    # edge pattern only ever sees bare ids.
    bare = EDGE_LABEL.sub(" ", NODE_DEF.sub(lambda m: m.group(1), body))
    # `graph RL` only mirrors the layout; `A --> B` still means A points at B.
    # Direction is therefore read literally, and every diagram in this skill
    # writes "dependent --> dependency" so the comparison stays meaningful.
    edges = set()
    for match in EDGE.finditer(bare):
        src, dst = labels.get(match.group(1)), labels.get(match.group(2))
        if src is None or dst is None:
            continue
        edges.add((src, dst))
    return labels, edges


def omitted_references(projects: list[dict], body: str) -> list[str]:
    """Project references that exist in code but have no matching diagram edge."""
    labels, edges = diagram_graph(body)
    visible = set(labels.values())
    present = {p["name"] for p in projects if p["name"] in visible}
    if len(present) < 2:
        return []  # Not a project graph; coverage is meaningless here.
    missing = []
    by_path = {p["path"]: p for p in projects}
    for item in projects:
        if item["name"] not in present:
            continue
        for ref in item["refs"]:
            target = by_path.get(ref)
            name = target["name"] if target else Path(ref).stem
            if name in present and (item["name"], name) not in edges:
                missing.append(f"{item['name']} -> {name}")
    return sorted(set(missing))


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("target", help="Markdown file or .mmd file containing diagrams")
    parser.add_argument("--root", default=".", help="repository root (default: current directory)")
    parser.add_argument("--json", action="store_true", help="machine-readable output")
    args = parser.parse_args(argv)

    root = Path(args.root).resolve()
    target = Path(args.target)
    if not target.exists():
        raise SystemExit(f"error: {target} does not exist")
    diagrams = load_diagrams(target)
    if not diagrams:
        raise SystemExit(f"error: no mermaid blocks found in {target}")

    symbols = collect_repo_symbols(root)
    report = []
    for diagram in diagrams:
        result = check_diagram(diagram["body"], symbols, root)
        result["omitted_project_references"] = omitted_references(symbols["projects"], diagram["body"])
        result.update({"source": diagram["source"], "line": diagram["line"]})
        report.append(result)

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        problems = 0
        for item in report:
            header = f"{item['source']}:{item['line']}  ({item['labels_checked']} labels)"
            issues = []
            if item["missing_files"]:
                issues.append(("missing files", item["missing_files"]))
            if item["unknown_type_or_project_names"]:
                issues.append(("unknown names", item["unknown_type_or_project_names"]))
            if item["omitted_project_references"]:
                issues.append(("edges missing from diagram", item["omitted_project_references"]))
            if not issues:
                print(f"OK  {header}")
                continue
            problems += len(issues)
            print(f"CHECK  {header}")
            for title, entries in issues:
                print(f"  {title}:")
                for entry in entries:
                    print(f"    - {entry}")
        if problems:
            print("\nReview the items above against the source before publishing. "
                  "A label may be intentional prose; a missing edge usually means the "
                  "diagram is stale.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
