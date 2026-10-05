#!/usr/bin/env python3
"""Static inventory helper for Vortex Cloud documentation.

This intentionally produces indexes only. It does not attempt to infer runtime semantics.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from pathlib import Path
from xml.etree import ElementTree as ET

SKIP_DIRS = {"bin", "obj", ".git", "node_modules", ".svelte-kit", "dist", "coverage"}


def files(root: Path, pattern: str):
    for p in root.rglob(pattern):
        if not any(part in SKIP_DIRS for part in p.parts):
            yield p


def rel(root: Path, p: Path) -> str:
    return p.relative_to(root).as_posix()


def text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def parse_csproj(root: Path, p: Path):
    result = {"path": rel(root, p), "project_references": [], "package_references": [], "target_frameworks": []}
    try:
        tree = ET.parse(p)
        project = tree.getroot()
        for e in project.iter():
            tag = e.tag.rsplit("}", 1)[-1]
            if tag == "ProjectReference" and e.attrib.get("Include"):
                result["project_references"].append(e.attrib["Include"].replace("\\", "/"))
            elif tag == "PackageReference" and e.attrib.get("Include"):
                result["package_references"].append({"name": e.attrib["Include"], "version": e.attrib.get("Version")})
            elif tag in {"TargetFramework", "TargetFrameworks"} and e.text:
                result["target_frameworks"].extend(x.strip() for x in e.text.split(";") if x.strip())
    except ET.ParseError:
        result["parse_error"] = True
    return result


def scan_symbols(root: Path):
    grain_interfaces = []
    grain_impls = []
    persistent_states = []
    handlers = []
    endpoints = []
    dbcontexts = []
    entities = []
    migrations = []
    revision_artifacts = []

    class_re = re.compile(r"\b(?:public|internal)?\s*(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)")
    interface_re = re.compile(r"\binterface\s+(I\w+Grain\w*)\b")

    for p in files(root, "*.cs"):
        rp = rel(root, p)
        s = text(p)

        for m in interface_re.finditer(s):
            grain_interfaces.append({"symbol": m.group(1), "path": rp})

        if re.search(r"\b(?:Grain|Grain<[^>]+>)\b", s) or "IGrain" in s:
            for m in class_re.finditer(s):
                name = m.group(1)
                if "Grain" in name:
                    grain_impls.append({"symbol": name, "path": rp})

        for m in re.finditer(r"\[PersistentState(?:\(([^\]]*)\))?\]", s):
            persistent_states.append({"path": rp, "attribute": m.group(0)})

        if "Vortex.PacketHandlers/" in rp or "MessageHandler" in s or "IMessageHandler" in s:
            for m in class_re.finditer(s):
                if "Handler" in m.group(1):
                    handlers.append({"symbol": m.group(1), "path": rp})

        if any(token in s for token in ("MapGet(", "MapPost(", "MapPut(", "MapDelete(", "[HttpGet", "[HttpPost", "[HttpPut", "[HttpDelete", "MapGroup(")):
            endpoints.append({"path": rp})

        if re.search(r"\bclass\s+\w+DbContext\b|:\s*DbContext\b", s):
            for m in class_re.finditer(s):
                if "Context" in m.group(1):
                    dbcontexts.append({"symbol": m.group(1), "path": rp})

        if "/Entities/" in f"/{rp}" or "/Models/Entities/" in f"/{rp}":
            for m in class_re.finditer(s):
                entities.append({"symbol": m.group(1), "path": rp})

        if "/Migrations/" in f"/{rp}" and "Migration" in s:
            migrations.append({"path": rp})

        if "Vortex.Revisions/" in rp and any(x in rp for x in ("Parsers/", "Serializers/", "Headers.cs", "Revision")):
            revision_artifacts.append({"path": rp})

    return {
        "grain_interfaces": grain_interfaces,
        "grain_implementations": grain_impls,
        "persistent_state_usages": persistent_states,
        "packet_handlers": handlers,
        "endpoint_files": endpoints,
        "dbcontexts": dbcontexts,
        "entities": entities,
        "migrations": migrations,
        "revision_artifacts": revision_artifacts,
    }


def git(root: Path, *args: str) -> str | None:
    try:
        return subprocess.check_output(["git", "-C", str(root), *args], text=True, stderr=subprocess.DEVNULL).strip()
    except Exception:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=".")
    ap.add_argument("--output", required=True)
    ns = ap.parse_args()

    root = Path(ns.root).resolve()
    projects = [parse_csproj(root, p) for p in files(root, "*.csproj")]
    tests = [p for p in projects if ".Tests/" in p["path"] or p["path"].endswith(".Tests.csproj")]
    benchmarks = [p for p in projects if "Benchmark" in p["path"] or "LoadGen" in p["path"]]

    config_keys = set()
    config_re = re.compile(r"(?:GetValue|GetSection|GetRequiredSection)\s*(?:<[^>]+>)?\s*\(\s*[\"']([^\"']+)")
    indexer_re = re.compile(r"(?:configuration|_configuration|config)\s*\[\s*[\"']([^\"']+)[\"']\s*\]", re.I)
    for p in files(root, "*.cs"):
        s = text(p)
        config_keys.update(config_re.findall(s))
        config_keys.update(indexer_re.findall(s))

    result = {
        "generator": "document-vortex-v1",
        "root": str(root),
        "git": {
            "head": git(root, "rev-parse", "HEAD"),
            "branch": git(root, "rev-parse", "--abbrev-ref", "HEAD"),
        },
        "solution_files": sorted(rel(root, p) for p in files(root, "*.sln")),
        "projects": projects,
        "test_projects": tests,
        "performance_projects": benchmarks,
        "configuration_keys": sorted(config_keys),
        "symbols": scan_symbols(root),
        "repo_contracts": [x for x in ("AGENTS.md", "CONTEXT.md", "CLAUDE.md") if (root / x).exists()],
    }

    out = Path(ns.output)
    if not out.is_absolute():
        out = root / out
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Wrote {out}")
    print(f"Projects: {len(projects)} | grains: {len(result['symbols']['grain_implementations'])} | handlers: {len(result['symbols']['packet_handlers'])}")


if __name__ == "__main__":
    main()
