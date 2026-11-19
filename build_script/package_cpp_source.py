from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
SOURCE_PROJECT = REPO_ROOT / "dlcv_infer_cpp"
SHARED_FILES = (
    "WindowsSdk.props",
    "DlcvProInfer.props",
    "OpenCvVersion.props",
    "OpenCvDiagnostics.targets",
)
EXCLUDE_DIRS = {"Debug", "Release", "obj", "bin", ".vs", "__pycache__", "x64"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="生成可独立引用的 dlcv_infer_cpp 源码目录")
    parser.add_argument("--output", default=str(REPO_ROOT / "dist_cpp_source"),
                        help="输出目录，默认 dist_cpp_source")
    parser.add_argument("--force", action="store_true", help="输出目录已存在时清空")
    return parser.parse_args()


def ignore(directory: str, names: list[str]) -> list[str]:
    return [name for name in names if name in EXCLUDE_DIRS or name.endswith(".user") or name == ".git"]


def main() -> int:
    args = parse_args()
    output = Path(args.output).expanduser().resolve()
    target = output / "dlcv_infer_cpp"
    if target.exists():
        if not args.force:
            print(f"输出目录已存在：{target}，使用 --force 覆盖", file=sys.stderr)
            return 1
        shutil.rmtree(target)
    output.mkdir(parents=True, exist_ok=True)
    shutil.copytree(SOURCE_PROJECT, target, ignore=ignore)
    build_dir = target / "build"
    build_dir.mkdir()
    for name in SHARED_FILES:
        shutil.copy2(REPO_ROOT / "build_script" / name, build_dir / name)
    print(f"已生成：{target}")
    print("把 dlcv_infer_cpp 目录复制到目标解决方案根即可，不再需要同级的 build_script。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
