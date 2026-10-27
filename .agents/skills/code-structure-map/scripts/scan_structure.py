#!/usr/bin/env python3
"""Inventory a .NET / Unity repository and emit structure diagrams.

Only the Python standard library is used, so the script runs wherever the
repository can be checked out. It answers four narrow questions and nothing
else: which projects reference which, which types live where, how namespaces
group those types, and how much code each source directory holds. Treat the
output as an index of declarations, not as proof of runtime behaviour.

Examples:
    python3 scan_structure.py projects --root . --focus framework
    python3 scan_structure.py types --path framework/src/FrameSync.Runtime
    python3 scan_structure.py namespaces --path samples/HookIsland
    python3 scan_structure.py tree --path framework/src --depth 2
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

SKIP_DIRS = {
    ".git", ".build", ".vs", ".idea", ".vscode", "artifacts",
    "bin", "obj", "Library", "Temp", "Logs", "UserSettings",
    "node_modules", "MemoryCaptures", "Recordings",
}

CSPROJ_REF = re.compile(r"<ProjectReference\s+Include\s*=\s*[\"']([^\"']+)[\"']", re.IGNORECASE)
TYPE_DECL = re.compile(
    r"^\s*(?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|ref|unsafe|new|file)\s+)*"
    r"(?P<kind>class|interface|struct|record|enum)\s+(?P<name>[A-Za-z_]\w*)"
)
RECORD_CLASS = re.compile(r"^\s*(?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|ref|unsafe|new|file)\s+)*"
                          r"record\s+(?:class|struct)\s+(?P<name>[A-Za-z_]\w*)")
BASE_LIST = re.compile(
    r"^\s*(?:(?:public|internal|private|protected|static|abstract|sealed|partial|readonly|ref|unsafe|new|file)\s+)*"
    r"(?:class|interface|struct|record)\s+[A-Za-z_]\w*\s*(?:<[^{]*?>)?\s*"
    r"(?:\([^)]*\))?\s*:\s*(?P<bases>[^{;]+)")
NAMESPACE_BLOCK = re.compile(r"^\s*namespace\s+(?P<name>[\w.]+)\s*\{")
NAMESPACE_FILE = re.compile(r"^\s*namespace\s+(?P<name>[\w.]+)\s*;")


def warn(message: str) -> None:
    print(f"warning: {message}", file=sys.stderr)


def rel(path: Path, root: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def walk_files(root: Path, suffixes: tuple[str, ...]):
    root = root.resolve()
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix not in suffixes:
            continue
        if set(path.relative_to(root).parts) & SKIP_DIRS:
            continue
        yield path


def strip_noise(text: str) -> str:
    """Blank out comments and string literals while preserving line structure.

    Brace counting and namespace tracking are only trustworthy when
    commented-out code and braces inside string literals cannot influence the
    parse. Characters are replaced with spaces so columns and lines stay put.
    """
    out: list[str] = []
    i, n = 0, len(text)
    state = None  # None | 'line' | 'block' | 'str' | 'verbatim' | 'char'
    while i < n:
        ch = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if state is None:
            if ch == "/" and nxt == "/":
                state = "line"
                out.append("  ")
                i += 2
            elif ch == "/" and nxt == "*":
                state = "block"
                out.append("  ")
                i += 2
            elif ch == "@" and nxt == '"':
                state = "verbatim"
                out.append("  ")
                i += 2
            elif ch == '"':
                state = "str"
                out.append(" ")
                i += 1
            elif ch == "'":
                state = "char"
                out.append(" ")
                i += 1
            else:
                out.append(ch)
                i += 1
            continue
        if state == "line":
            state = None if ch == "\n" else state
            out.append("\n" if ch == "\n" else " ")
            i += 1
        elif state == "block":
            if ch == "*" and nxt == "/":
                state = None
                out.append("  ")
                i += 2
            else:
                out.append("\n" if ch == "\n" else " ")
                i += 1
        elif state == "verbatim":
            if ch == '"' and nxt == '"':
                out.append("  ")
                i += 2
            elif ch == '"':
                state = None
                out.append(" ")
                i += 1
            else:
                out.append("\n" if ch == "\n" else " ")
                i += 1
        else:  # 'str' or 'char'
            if ch == "\\":
                out.append("  ")
                i += 2
            elif (state == "str" and ch == '"') or (state == "char" and ch == "'"):
                state = None
                out.append(" ")
                i += 1
            else:
                out.append("\n" if ch == "\n" else " ")
                i += 1
    return "".join(out)


def join_continuations(lines: list[str]) -> list[str]:
    """Fold base lists and constraints that start on the line after the header."""
    merged: list[str] = []
    for line in lines:
        stripped = line.strip()
        if merged and (stripped.startswith(":") or stripped.startswith("where ")):
            merged[-1] = merged[-1].rstrip() + " " + stripped
        else:
            merged.append(line)
    return merged


ATTRIBUTE_ONLY = re.compile(r"^\s*\[[^\]]*\]\s*$")
SKIP_HEADER = re.compile(r"^\s*(?:\#|using\b|extern\b|global\b)")


def header_declaration(header: str) -> dict | None:
    """Interpret the text between two braces or semicolons as a declaration.

    Attributes, preprocessor lines and using directives can share the buffer
    with the declaration, so walk back to the last meaningful line. This keeps
    Allman-style code (brace on its own line) and same-line bodies equal.
    """
    lines = [line for line in header.splitlines() if line.strip()]
    for index in range(len(lines) - 1, -1, -1):
        line = lines[index]
        if ATTRIBUTE_ONLY.match(line) or SKIP_HEADER.match(line):
            continue
        candidate = line
        # A multi-line header: glue the preceding line when the candidate opens
        # parentheses or the previous line does not end a statement.
        while index > 0 and candidate.count("(") > candidate.count(")") and index > 0:
            index -= 1
            candidate = lines[index].strip() + " " + candidate.strip()
        ns = NAMESPACE_BLOCK.match(candidate)
        if ns:
            return {"role": "namespace", "name": ns.group("name")}
        if re.match(r"^\s*namespace\s+[\w.]+\s*$", candidate):
            return {"role": "namespace", "name": candidate.strip().split(None, 1)[1]}
        record_style = RECORD_CLASS.match(candidate)
        if record_style:
            return {"role": "type", "name": record_style.group("name"), "kind": "record", "bases": []}
        declared = TYPE_DECL.match(candidate)
        if declared and "=" not in candidate.split("(")[0]:
            bases: list[str] = []
            base_match = BASE_LIST.match(candidate)
            if base_match:
                for part in base_match.group("bases").split(","):
                    head = re.match(r"\s*(?:where\s+)?([\w.]+)", part)
                    if head and head.group(1) not in ("where", "new"):
                        bases.append(head.group(1).split(".")[-1])
            return {"role": "type", "name": declared.group("name"),
                    "kind": declared.group("kind"), "bases": bases}
        return None
    return None


def parse_csharp(path: Path, root: Path) -> list[dict]:
    """Return type declarations with namespace, nesting and base list."""
    try:
        raw = path.read_text(encoding="utf-8-sig", errors="replace")
    except OSError as error:
        warn(f"cannot read {path}: {error}")
        return []

    lines = join_continuations(strip_noise(raw).splitlines())
    declarations: list[dict] = []
    scopes: list[dict] = []          # open namespace/type scopes, innermost last
    file_namespace = ""
    depth = 0
    buffer: list[str] = []

    def record(decl: dict, scope_depth: int) -> None:
        enclosing = next((s for s in reversed(scopes) if s["kind"] == "type"), None)
        namespace = next((s["name"] for s in reversed(scopes) if s["kind"] == "namespace"),
                         file_namespace)
        declarations.append({
            "name": decl["name"],
            "kind": decl["kind"],
            "bases": decl.get("bases", []),
            "namespace": namespace,
            "parent": enclosing["name"] if enclosing else None,
            "file": rel(path, root),
        })
        if scope_depth is not None:
            scopes.append({"kind": "type", "name": decl["name"], "depth": scope_depth})

    for line in lines:
        i = 0
        while i < len(line):
            ch = line[i]
            if ch not in "{};":
                buffer.append(ch)
                i += 1
                continue
            header = "".join(buffer)
            buffer = []
            if ch == "{":
                depth += 1
                decl = header_declaration(header)
                if decl is None:
                    pass
                elif decl["role"] == "namespace":
                    scopes.append({"kind": "namespace", "name": decl["name"], "depth": depth})
                else:
                    record(decl, depth)
            elif ch == ";":
                decl = header_declaration(header)
                if decl is None:
                    pass
                elif decl["role"] == "namespace":
                    file_namespace = decl["name"]
                elif decl["role"] == "type" and decl.get("kind") == "record":
                    # A positional record without a body still declares a type.
                    record(decl, None)
            else:
                depth -= 1
                while scopes and scopes[-1]["depth"] > depth:
                    scopes.pop()
            i += 1
        buffer.append("\n")

    for item in declarations:
        item["qualified"] = ".".join(
            part for part in (item["namespace"], item["parent"], item["name"]) if part)
    return declarations


def merge_partials(declarations: list[dict]) -> list[dict]:
    """Collapse partial declarations that span files into one entry."""
    merged: dict[tuple[str, str], dict] = {}
    for item in declarations:
        key = (item["qualified"], item["kind"])
        current = merged.get(key)
        if current is None:
            merged[key] = {**item, "files": [item["file"]], "partial": False}
        else:
            current["partial"] = True
            if item["file"] not in current["files"]:
                current["files"].append(item["file"])
            for base in item["bases"]:
                if base not in current["bases"]:
                    current["bases"].append(base)
    return list(merged.values())


def project_kind(path: Path) -> str:
    return "unity" if "Assets" in path.parts or path.suffix == ".asmdef" else "dotnet"


def collect_projects(root: Path) -> list[dict]:
    projects = []
    for path in walk_files(root, (".csproj", ".asmdef")):
        if path.suffix == ".csproj":
            text = path.read_text(encoding="utf-8-sig", errors="replace")
            refs = [rel((path.parent / raw.replace("\\", "/")).resolve(), root)
                    for raw in CSPROJ_REF.findall(text)]
            name = path.stem
            generated = 'ProjectCapability Include="Unity"' in text
        else:
            try:
                data = json.loads(path.read_text(encoding="utf-8-sig", errors="replace"))
            except json.JSONDecodeError:
                warn(f"invalid asmdef json: {rel(path, root)}")
                continue
            refs = [str(ref) for ref in data.get("references", [])]
            name = str(data.get("name", path.stem))
            generated = False
        projects.append({"name": name, "path": rel(path, root),
                         "kind": project_kind(path), "refs": refs,
                         "unity_generated": path.suffix == ".csproj" and generated})
    projects.sort(key=lambda item: item["path"])
    return projects


MERMAID_ESCAPE = re.compile(r'["\\]')


def mermaid_id(raw: str) -> str:
    return "n_" + re.sub(r"[^0-9A-Za-z_]", "_", raw)


def mermaid_label(raw: str) -> str:
    return MERMAID_ESCAPE.sub("_", raw)


def resolve_ref(item: dict, ref: str, by_path: dict, by_name: dict) -> dict | None:
    if ref in by_path:
        return by_path[ref]
    stem = Path(ref).stem
    for candidate in (by_name.get(ref, []), by_name.get(stem, [])):
        for target in candidate:
            if target["path"] == item["path"]:
                continue
            if not ref.endswith(stem) and ref not in by_name:
                # An asmdef-style bare name: any project with that name is a
                # plausible target, but only claim it when the name is unique.
                if len(candidate) > 1:
                    return None
            return target
    return None


def collapse_unity_generated(projects: list[dict]) -> list[dict]:
    """Drop Unity-generated .csproj files, keeping their .asmdef counterpart.

    Unity writes one .csproj per .asmdef, so an unmodified scan shows every
    assembly twice and doubles the edge count. The .asmdef is the authored
    source of truth, so keep it and redirect references that pointed at the
    generated project.
    """
    by_name = defaultdict(list)
    for item in projects:
        by_name[item["name"]].append(item)
    survivors = [item for item in projects if not item["unity_generated"]]
    for path in {item["path"] for item in projects if item["unity_generated"]}:
        owner = next(item for item in projects if item["path"] == path)
        replacement = next((c for c in by_name[owner["name"]]
                            if c["kind"] == "unity"), None)
        if replacement is None:
            survivors.append(owner)  # no asmdef: dropping it would lose the node
            continue
        for item in projects:
            item["refs"] = [replacement["path"] if ref == path else ref for ref in item["refs"]]
    return survivors


def emit_projects_mermaid(projects: list[dict], focus: str | None, context_depth: int) -> str:
    by_path = {item["path"]: item for item in projects}
    by_name = defaultdict(list)
    for item in projects:
        by_name[item["name"]].append(item)

    edges: list[tuple[str, str]] = []
    unresolved: list[tuple[str, str]] = []
    all_edges: list[tuple[str, str]] = []
    for item in projects:
        for ref in item["refs"]:
            target = resolve_ref(item, ref, by_path, by_name)
            if target is None:
                unresolved.append((item["path"], ref))
            else:
                edges.append((item["path"], target["path"]))
                all_edges.append((item["path"], target["path"]))

    keep = set(by_path)
    used_by: set[str] = set()      # focus -> these (focus depends on them)
    uses_focus: set[str] = set()   # these -> focus (they depend on focus)
    if focus:
        focus = focus.strip("/")
        keep = {p for p in by_path if p == focus or p.startswith(focus + "/")}
        if not keep:
            raise SystemExit(f"error: --focus {focus!r} matches no project")
        # Strict focus by default: a one-hop boundary explodes whenever the
        # focus subtree is a shared dependency, which is the common case.
        if context_depth > 0:
            for _ in range(context_depth):
                for src, dst in list(edges):
                    for endpoint in (src, dst):
                        if endpoint in keep:
                            continue
                        other = dst if endpoint == src else src
                        if other in keep:
                            keep.add(endpoint)
        used_by = {d for s, d in edges if s in keep and d not in keep}
        uses_focus = {s for s, d in edges if d in keep and s not in keep}
        edges = [e for e in edges if e[0] in keep and e[1] in keep]
        unresolved = [u for u in unresolved if u[0] in keep]

    groups: dict[str, list[str]] = defaultdict(list)
    for path in sorted(keep):
        parts = Path(path).parts
        groups["/".join(parts[:2]) if len(parts) > 2 else parts[0]].append(path)

    lines = ["graph LR"]
    for group, paths in sorted(groups.items()):
        lines.append(f'  subgraph g_{mermaid_id(group)}["{mermaid_label(group)}"]')
        for path in paths:
            item = by_path[path]
            label = mermaid_label(item["name"])
            shape = f"([{label}])" if item["kind"] == "unity" else f"[{label}]"
            lines.append(f"    {mermaid_id(path)}{shape}")
        lines.append("  end")

    for src, dst in sorted(set(edges)):
        lines.append(f"  {mermaid_id(src)} --> {mermaid_id(dst)}")

    # Aggregate the boundary in both directions instead of pulling in the whole
    # dependency ring around a shared subtree. Direction is preserved: an
    # inbound arrow means "outside code depends on this", which is what breaks
    # when the focus changes.
    if used_by:
        lines.append(f'  ext_deps["范围外依赖<br/>{len(used_by)} 项"]')
        for src in sorted(keep):
            if any(s == src and d in used_by for s, d in all_edges):
                lines.append(f"  {mermaid_id(src)} -.-> ext_deps")
    if uses_focus:
        lines.append(f'  ext_users["依赖本范围<br/>{len(uses_focus)} 项"]')
        for dst in sorted(keep):
            if any(d == dst and s in uses_focus for s, d in all_edges):
                lines.append(f"  ext_users -.-> {mermaid_id(dst)}")

    for src, ref in sorted(set(unresolved)):
        missing = f"missing::{src}::{ref}"
        lines.append(f'  {mermaid_id(missing)}["{mermaid_label(ref)}<br/>unresolved"]')
        lines.append(f"  {mermaid_id(src)} -.-> {mermaid_id(missing)}")
    return "\n".join(lines)


def emit_projects_text(projects: list[dict]) -> str:
    by_path = {item["path"]: item for item in projects}
    by_name = defaultdict(list)
    for item in projects:
        by_name[item["name"]].append(item)
    out = []
    for item in projects:
        out.append(f"{item['path']}  [{item['kind']}]  {item['name']}")
        if not item["refs"]:
            out.append("  (no project references)")
        for ref in item["refs"]:
            target = resolve_ref(item, ref, by_path, by_name)
            out.append(f"  -> {target['path'] if target else ref + '  (unresolved)'}")
    return "\n".join(out)


def relation_arrow(target_kind: str | None, base_name: str) -> str:
    """Pick inheritance vs realization from the base type, not the derived one.

    A class that implements an interface is a realization; a class that extends
    a class is inheritance. When the base is outside the scanned set we cannot
    know, so fall back to the C# naming convention and let the reader verify.
    """
    if target_kind == "interface":
        return "..|>"
    if target_kind is not None:
        return "<|--"
    return "..|>" if re.fullmatch(r"I[A-Z]\w*", base_name) else "<|--"


def emit_types_mermaid(declarations: list[dict], max_nodes: int, include_external: bool) -> str:
    total = len(declarations)
    if total > max_nodes:
        warn(f"{total} types found; truncating to {max_nodes}. "
             "Narrow --path or raise --max-nodes for a complete picture.")
        declarations = declarations[:max_nodes]

    by_name: dict[str, list[dict]] = defaultdict(list)
    for item in declarations:
        by_name[item["name"]].append(item)

    lines = ["classDiagram"]
    for ns in sorted({item["namespace"] or "(global)" for item in declarations}):
        # A dotted namespace would be split into nested namespaces by Mermaid,
        # so keep an alias id and show the real name as a quoted label.
        lines.append(f'  namespace {mermaid_id(ns)}["{mermaid_label(ns)}"] {{')
        for item in declarations:
            if (item["namespace"] or "(global)") != ns:
                continue
            alias = mermaid_id(item["qualified"])
            lines.append(f'    class {alias}["{mermaid_label(item["name"])}"] {{')
            lines.append(f"      <<{item['kind']}>>")
            lines.append("    }")
        lines.append("  }")

    relations: list[str] = []
    externals: set[str] = set()
    for item in declarations:
        alias = mermaid_id(item["qualified"])
        if item["parent"]:
            parent = next((d for d in declarations
                           if d["name"] == item["parent"] and d["namespace"] == item["namespace"]), None)
            if parent:
                relations.append(f"  {mermaid_id(parent['qualified'])} *-- {alias} : nested")
        for base in item["bases"]:
            same_ns = [c for c in by_name.get(base, []) if c["namespace"] == item["namespace"]]
            target = (same_ns or by_name.get(base) or [None])[0]
            if target is not None:
                arrow = relation_arrow(target["kind"], base)
                relations.append(f"  {mermaid_id(target['qualified'])} {arrow} {alias}")
            elif include_external:
                externals.add(base)
                arrow = relation_arrow(None, base)
                lines.append(f'  class ext_{mermaid_id(base)}["{mermaid_label(base)}"] {{')
                lines.append("    <<external>>")
                lines.append("  }")
                relations.append(f"  ext_{mermaid_id(base)} {arrow} {alias}")
    lines.extend(sorted(set(relations)))
    return "\n".join(lines)


def emit_types_text(declarations: list[dict]) -> str:
    grouped: dict[str, list[dict]] = defaultdict(list)
    for item in declarations:
        grouped[item["namespace"] or "(global)"].append(item)
    out = []
    for ns in sorted(grouped):
        out.append(f"namespace {ns}")
        for item in sorted(grouped[ns], key=lambda d: d["qualified"]):
            suffix = f" : {', '.join(item['bases'])}" if item["bases"] else ""
            where = ", ".join(item.get("files", [])) if item.get("partial") else ""
            out.append(f"  {item['kind']} {item['qualified']}{suffix}"
                       + (f"  [partial: {where}]" if where else ""))
    return "\n".join(out)


def emit_namespaces_mermaid(declarations: list[dict], max_nodes: int) -> str:
    counts = Counter(item["namespace"] or "(global)" for item in declarations)
    if len(counts) > max_nodes:
        warn(f"{len(counts)} namespaces found; showing the {max_nodes} largest.")
    top = [ns for ns, _ in counts.most_common(max_nodes)]
    lines = ["graph LR"]
    for ns in sorted(top):
        lines.append(f'  {mermaid_id(ns)}["{mermaid_label(ns)}<br/>{counts[ns]} types"]')
    return "\n".join(lines)


def emit_tree(base: Path, depth: int) -> str:
    rows = []
    for cs in walk_files(base, (".cs",)):
        parts = cs.parent.relative_to(base).parts[:depth]
        rows.append("/".join(parts) or ".")
    counts = Counter(rows)
    width = max((len(name) for name in counts), default=5)
    out = [f"{name:<{width}}  {count:>4} .cs" for name, count in sorted(counts.items())]
    out.append(f"{'total':<{width}}  {sum(counts.values()):>4} .cs")
    return "\n".join(out)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["projects", "types", "namespaces", "tree"])
    parser.add_argument("--root", default=".", help="repository root (default: current directory)")
    parser.add_argument("--path", help="subtree for types/namespaces/tree")
    parser.add_argument("--focus", help="limit the project graph to one subtree, e.g. framework")
    parser.add_argument("--context", type=int, default=0,
                        help="extra dependency hops to include around --focus (default: 0)")
    parser.add_argument("--collapse-unity", action="store_true",
                        help="drop Unity-generated .csproj nodes; keep the authored .asmdef")
    parser.add_argument("--format", choices=["mermaid", "text", "json"], default="mermaid")
    parser.add_argument("--max-nodes", type=int, default=80, help="node cap (default: 80)")
    parser.add_argument("--no-external", action="store_true",
                        help="omit base types defined outside the scanned set")
    parser.add_argument("--depth", type=int, default=2, help="directory depth for tree")
    parser.add_argument("--out", help="write to this file instead of stdout")
    parser.add_argument("--title", default="", help="comment line placed above the output")
    args = parser.parse_args(argv)

    root = Path(args.root).resolve()
    if not root.is_dir():
        raise SystemExit(f"error: --root {root} is not a directory")

    if args.command == "projects":
        projects = collect_projects(root)
        if not projects:
            raise SystemExit("error: no .csproj or .asmdef files found")
        if args.collapse_unity:
            projects = collapse_unity_generated(projects)
        if args.format == "json":
            payload = json.dumps(projects, indent=2)
        elif args.format == "text":
            payload = emit_projects_text(projects)
        else:
            payload = emit_projects_mermaid(projects, args.focus, args.context)
    else:
        target = (root / args.path).resolve() if args.path else root
        if not target.is_dir():
            raise SystemExit(f"error: --path {args.path} is not a directory")
        if args.command == "tree":
            payload = emit_tree(target, args.depth)
        else:
            declarations = merge_partials(
                [d for cs in walk_files(target, (".cs",)) for d in parse_csharp(cs, root)])
            if not declarations:
                raise SystemExit(f"error: no C# type declarations under {rel(target, root)}")
            if args.format == "json":
                payload = json.dumps({"types": declarations}, indent=2)
            elif args.command == "types":
                payload = (emit_types_text(declarations) if args.format == "text"
                           else emit_types_mermaid(declarations, args.max_nodes, not args.no_external))
            else:
                payload = emit_namespaces_mermaid(declarations, args.max_nodes)

    header = []
    if args.title:
        header.append(f"%% {args.title}")
    if args.format == "mermaid":
        header.append("%% generated by scan_structure.py; verify against source before publishing")
    body = "\n".join(header + [payload]) + "\n"
    if args.out:
        out_path = Path(args.out)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(body, encoding="utf-8")
        print(f"wrote {out_path}", file=sys.stderr)
    else:
        sys.stdout.write(body)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
