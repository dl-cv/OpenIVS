from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parent
PACKAGE_NAME = "dlcvpro_infer_csharp"
STAGING_DIR = REPO_ROOT / PACKAGE_NAME
DEMO_OUTPUT_DIR = REPO_ROOT / "DlcvDemo" / "bin"
DEMO_EXE_NAME = "C# 测试程序.exe"
SIGNTOOL_PATH = Path(r"C:\sign-tool\signtool.exe")
SIGN_CERT_SUBJECT = "深度视觉（广东）人工智能研究有限公司"
TIMESTAMP_URL = "http://time.certum.pl"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="构建、签名并打包 C# 测试程序")
    parser.add_argument(
        "--install",
        action="store_true",
        help="打包成功后使用当前 Python 环境安装本次生成的 wheel",
    )
    return parser.parse_args()


def require_file(path: Path) -> None:
    if not path.is_file():
        raise FileNotFoundError(f"未找到必需文件: {path}")


def run_step(name: str, command: list[str]) -> None:
    print(f"[build_package] {name}")
    print(f"[build_package] command: {subprocess.list2cmdline(command)}")
    subprocess.run(command, cwd=REPO_ROOT, check=True)


def rebuild_staging_directory() -> None:
    repo_root = REPO_ROOT.resolve()
    lexical_path = STAGING_DIR.absolute()
    resolved_path = STAGING_DIR.resolve(strict=False)

    if lexical_path.parent != repo_root or lexical_path.name != PACKAGE_NAME:
        raise RuntimeError(f"拒绝清理仓库外目录: {lexical_path}")
    if resolved_path != lexical_path:
        raise RuntimeError(f"拒绝清理链接或重解析目录: {lexical_path} -> {resolved_path}")

    if STAGING_DIR.exists():
        if not STAGING_DIR.is_dir():
            raise RuntimeError(f"暂存路径不是目录: {STAGING_DIR}")
        shutil.rmtree(STAGING_DIR)
    STAGING_DIR.mkdir()


def copy_package_files() -> Path:
    if not DEMO_OUTPUT_DIR.is_dir():
        raise FileNotFoundError(f"未找到 C# 构建输出目录: {DEMO_OUTPUT_DIR}")

    rebuild_staging_directory()

    for pattern in ("*.exe", "*.config", "*.dll"):
        sources = sorted(path for path in DEMO_OUTPUT_DIR.glob(pattern) if path.is_file())
        if not sources:
            raise FileNotFoundError(f"构建输出中没有匹配文件: {DEMO_OUTPUT_DIR / pattern}")
        for source in sources:
            shutil.copy2(source, STAGING_DIR / source.name)

    hasp_ini = REPO_ROOT / "hasp_26146.ini"
    require_file(hasp_ini)
    shutil.copy2(hasp_ini, STAGING_DIR / hasp_ini.name)

    demo_exe = STAGING_DIR / DEMO_EXE_NAME
    require_file(demo_exe)
    return demo_exe


def snapshot_wheels() -> dict[Path, tuple[int, int, int]]:
    dist_dir = REPO_ROOT / "dist"
    return {
        path.resolve(): (path.stat().st_mtime_ns, path.stat().st_ctime_ns, path.stat().st_size)
        for path in dist_dir.glob("*.whl")
    }


def find_generated_wheel(before: dict[Path, tuple[int, int, int]]) -> Path:
    dist_dir = REPO_ROOT / "dist"
    generated: list[Path] = []
    for path in dist_dir.glob("*.whl"):
        resolved = path.resolve()
        stat = path.stat()
        current = (stat.st_mtime_ns, stat.st_ctime_ns, stat.st_size)
        if before.get(resolved) != current:
            generated.append(resolved)

    if len(generated) != 1:
        names = ", ".join(str(path) for path in sorted(generated)) or "无"
        raise RuntimeError(f"无法唯一确认本次生成的 wheel: {names}")
    return generated[0]


def main() -> int:
    args = parse_args()
    os.chdir(REPO_ROOT)

    sync_script = REPO_ROOT / "sync_assembly_version.py"
    build_script = REPO_ROOT / ".cursor" / "skills" / "vs-build" / "scripts" / "build.py"
    demo_project = REPO_ROOT / "DlcvDemo" / "DlcvDemo.csproj"

    try:
        require_file(sync_script)
        require_file(build_script)
        require_file(demo_project)
        require_file(SIGNTOOL_PATH)

        run_step("同步程序集版本", [sys.executable, str(sync_script)])
        run_step(
            "构建 C# 测试程序",
            [
                sys.executable,
                str(build_script),
                str(demo_project),
                "--configuration",
                "Release",
                "--platform",
                "x64",
                "--target",
                "Build",
                "--verbosity",
                "minimal",
            ],
        )

        demo_exe = copy_package_files()
        run_step(
            "签名 C# 测试程序",
            [
                str(SIGNTOOL_PATH),
                "sign",
                "/n",
                SIGN_CERT_SUBJECT,
                "/t",
                TIMESTAMP_URL,
                "/fd",
                "sha256",
                "/v",
                str(demo_exe),
            ],
        )
        wheels_before_build = snapshot_wheels()
        run_step(
            "构建 wheel",
            [sys.executable, "-m", "build", "--wheel", "--outdir", str(REPO_ROOT / "dist")],
        )

        wheel_path = find_generated_wheel(wheels_before_build)
        install_status = "未安装（未指定 --install）"
        if args.install:
            run_step(
                "安装本次生成的 wheel",
                [sys.executable, "-m", "pip", "install", "--force-reinstall", str(wheel_path)],
            )
            install_status = "已安装"

        print(f"[build_package] wheel: {wheel_path}")
        print(f"[build_package] install: {install_status}")
        return 0
    except subprocess.CalledProcessError as exc:
        exit_code = exc.returncode if exc.returncode != 0 else 1
        print(f"[build_package] 步骤失败，退出码: {exit_code}", file=sys.stderr)
        return exit_code
    except Exception as exc:
        print(f"[build_package] 失败: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
