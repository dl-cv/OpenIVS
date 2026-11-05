from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

from build_package import (
    SIGNTOOL_PATH,
    TIMESTAMP_URL,
    find_signing_certificate_thumbprint,
    require_file,
)


REPO_ROOT = Path(__file__).resolve().parent
PACKAGE_NAME = "dlcvpro_infer_cpp"
STAGING_DIR = REPO_ROOT / PACKAGE_NAME
DIST_DIR = REPO_ROOT / "dist_cpp"
DEMO_OUTPUT_DIR = REPO_ROOT / "temp" / "cpp-qt-shared"
DEMO_EXE_NAME = "C++ 测试程序.exe"
SHARED_RUNTIME_DIR = Path(r"C:\dlcv\bin")
SHARED_FILE_NAMES = (
    "ZXing.dll", "opencv_world4100.dll", "Qt6Core.dll", "Qt6Gui.dll",
    "Qt6Widgets.dll", "qwindows.dll", "styles/qmodernwindowsstyle.dll",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="构建、签名并打包 C++ 测试程序")
    parser.add_argument("--install", action="store_true", help="打包后安装本次生成的 wheel")
    return parser.parse_args()


def run_step(name: str, command: list[str], env: dict[str, str] | None = None) -> None:
    print(f"[build_cpp_package] {name}", flush=True)
    subprocess.run(command, cwd=REPO_ROOT, check=True, env=env)


def rebuild_staging_directory() -> None:
    path = STAGING_DIR.absolute()
    if path.parent != REPO_ROOT.resolve() or path.name != PACKAGE_NAME or path.resolve() != path:
        raise RuntimeError(f"拒绝清理仓库外目录或链接: {path}")
    if path.exists():
        shutil.rmtree(path)
    path.mkdir()


def copy_package_files() -> Path:
    sources = {
        DEMO_EXE_NAME: DEMO_OUTPUT_DIR / "dlcv_infer_cpp_qt_demo.exe",
        "dlcv_infer_cpp.dll": DEMO_OUTPUT_DIR / "dlcv_infer_cpp.dll",
        "hasp_26146.ini": REPO_ROOT / "hasp_26146.ini",
    }
    for source in (*sources.values(), *(SHARED_RUNTIME_DIR / name for name in SHARED_FILE_NAMES)):
        require_file(source)
    rebuild_staging_directory()
    for name, source in sources.items():
        shutil.copy2(source, STAGING_DIR / name)
    return STAGING_DIR / DEMO_EXE_NAME


def snapshot_wheels() -> dict[Path, tuple[int, int, int]]:
    return {
        path: (path.stat().st_mtime_ns, path.stat().st_ctime_ns, path.stat().st_size)
        for path in DIST_DIR.glob(f"{PACKAGE_NAME}-*.whl")
    }


def find_generated_wheel(before: dict[Path, tuple[int, int, int]]) -> Path:
    generated = [path for path, state in snapshot_wheels().items() if before.get(path) != state]
    if len(generated) != 1:
        raise RuntimeError(f"本次生成的 C++ wheel 数量应为 1，实际为 {len(generated)}")
    return generated[0]


def main() -> int:
    args = parse_args()
    build_script = REPO_ROOT / ".cursor" / "skills" / "vs-build" / "scripts" / "build.py"
    demo_project = REPO_ROOT / "dlcv_infer_cpp_qt_demo" / "dlcv_infer_cpp_qt_demo.vcxproj"
    try:
        for path in (build_script, demo_project, SIGNTOOL_PATH):
            require_file(path)
        run_step(
            "构建 C++ 测试程序",
            [sys.executable, str(build_script), str(demo_project), "--configuration", "Release",
             "--platform", "x64", "--target", "Build", "--verbosity", "minimal"],
            env={**os.environ, "DlcvUseSharedQt": "true"},
        )
        executable = copy_package_files()
        thumbprint = find_signing_certificate_thumbprint()
        run_step(
            "签名 C++ 测试程序",
            [str(SIGNTOOL_PATH), "sign", "/sha1", thumbprint, "/tr", TIMESTAMP_URL,
             "/td", "sha256", "/fd", "sha256", "/v", str(executable)],
        )
        before = snapshot_wheels()
        run_step(
            "构建 C++ wheel",
            [sys.executable, "-m", "build", "--wheel", "--no-isolation", "--outdir", str(DIST_DIR)],
            env={**os.environ, "DLCV_PACKAGE_VARIANT": "cpp"},
        )
        wheel = find_generated_wheel(before)
        if args.install:
            run_step("安装 C++ wheel", [sys.executable, "-m", "pip", "install", "--force-reinstall", str(wheel)])
        print(f"[build_cpp_package] wheel: {wheel}")
        return 0
    except subprocess.CalledProcessError as exc:
        print(f"[build_cpp_package] 步骤失败，退出码: {exc.returncode}", file=sys.stderr)
        return exc.returncode or 1
    except Exception as exc:
        print(f"[build_cpp_package] 失败: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
