#!/usr/bin/env python3
"""Generic Visual Studio build helper."""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
MSBUILD_CANDIDATES = [
    Path(r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe"),
    Path(r"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build a Visual Studio solution or C# project")
    parser.add_argument("path", nargs="?", help="Target .sln or .csproj path")
    parser.add_argument("--configuration", default="Debug", choices=["Debug", "Release"])
    parser.add_argument("--platform", default="x64", choices=["x86", "x64", "Any CPU"])
    parser.add_argument("--target", default="Build", help="MSBuild target, for example Build/Clean/Rebuild")
    parser.add_argument(
        "--verbosity",
        default="minimal",
        choices=["quiet", "minimal", "normal", "detailed", "diagnostic"],
    )
    parser.add_argument("--msbuild", help="Explicit MSBuild.exe path")
    return parser.parse_args()


def default_target() -> Path:
    root_solutions = sorted(REPO_ROOT.glob("*.sln"))
    if len(root_solutions) == 1:
        return root_solutions[0]
    if not root_solutions:
        raise FileNotFoundError(f"No root solution found in: {REPO_ROOT}")
    names = ", ".join(path.name for path in root_solutions)
    raise ValueError(f"Multiple root solutions found, pass one explicitly: {names}")


def resolve_target(raw_path: str | None) -> Path:
    if not raw_path:
        target = default_target()
    else:
        target = Path(raw_path).expanduser()
        if not target.is_absolute():
            target = (REPO_ROOT / target).resolve()

    if not target.exists():
        raise FileNotFoundError(f"Target not found: {target}")
    if target.suffix.lower() not in {".sln", ".csproj"}:
        raise ValueError(f"Only .sln or .csproj is supported: {target}")
    return target


def find_msbuild(user_value: str | None) -> Path:
    if user_value:
        candidate = Path(user_value)
        if candidate.exists() and candidate.name.lower() == "msbuild.exe":
            return candidate
        raise FileNotFoundError(f"MSBuild path is invalid: {candidate}")

    for candidate in MSBUILD_CANDIDATES:
        if candidate.exists():
            return candidate

    detected = shutil.which("msbuild")
    if detected:
        return Path(detected)

    raise FileNotFoundError("MSBuild.exe was not found")


def build_command(msbuild: Path, target: Path, args: argparse.Namespace) -> list[str]:
    return [
        str(msbuild),
        str(target),
        f"/p:Configuration={args.configuration}",
        f"/p:Platform={args.platform}",
        f"/t:{args.target}",
        f"/v:{args.verbosity}",
    ]


def main() -> int:
    args = parse_args()

    try:
        target = resolve_target(args.path)
        msbuild = find_msbuild(args.msbuild)
    except (FileNotFoundError, ValueError) as exc:
        print(f"Error: {exc}")
        return 2

    cmd = build_command(msbuild, target, args)
    print("VS Build")
    print(f"repo: {REPO_ROOT}")
    print(f"file: {target}")
    print(f"configuration: {args.configuration}")
    print(f"platform: {args.platform}")
    print(f"target: {args.target}")
    print(f"verbosity: {args.verbosity}")
    print(f"msbuild: {msbuild}")
    print("-" * 80)

    result = subprocess.run(
        cmd,
        cwd=target.parent,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
    )

    if result.stdout:
        print(result.stdout.rstrip())

    print("-" * 80)
    print("status: success" if result.returncode == 0 else "status: failed")
    print(f"exit_code: {result.returncode}")
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
