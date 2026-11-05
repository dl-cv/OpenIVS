from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parent
PACKAGE_NAME = "dlcvpro_infer_csharp"
STAGING_DIR = REPO_ROOT / PACKAGE_NAME
DEMO_OUTPUT_DIR = REPO_ROOT / "DlcvDemo" / "bin"
DEMO_EXE_NAME = "C# 测试程序.exe"
RPC_EXE_NAME = "AIModelRPC.exe"
RPC_EXE_SOURCE = Path(sys.prefix) / "Lib" / "site-packages" / PACKAGE_NAME / RPC_EXE_NAME
PACKAGE_OUTPUT_FILE_NAMES = (
    DEMO_EXE_NAME,
    f"{DEMO_EXE_NAME}.config",
    "DlcvCsharpApi.dll",
    "ZXingCpp.dll",
    "ZXing.dll",
    "DlcvCsharpApi.dll.config",
    "ImageViewer.dll",
    "ImageViewer.dll.config",
    "Newtonsoft.Json.dll",
    "OpenCvSharp.dll",
    "OpenCvSharp.Extensions.dll",
    "PressureTestRunner.dll",
    "System.Buffers.dll",
    "System.Drawing.Common.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
)
SIGNED_EXE_NAMES = (DEMO_EXE_NAME, RPC_EXE_NAME)
CPP_PACKAGE_NAME = "dlcvpro_infer_cpp"
CPP_STAGING_DIR = REPO_ROOT / CPP_PACKAGE_NAME
CPP_DEMO_OUTPUT_DIR = REPO_ROOT / "temp" / "cpp-qt-shared"
CPP_DEMO_EXE_NAME = "C++ 测试程序.exe"
CPP_SHARED_RUNTIME_DIR = Path(r"C:\dlcv\bin")
CPP_SHARED_FILE_NAMES = (
    "opencv_world4100.dll", "Qt6Core.dll", "Qt6Gui.dll", "Qt6Widgets.dll",
    "qwindows.dll", "styles/qmodernwindowsstyle.dll",
)
CPP_OUTPUT_FILE_NAMES = (
    "dlcv_infer_cpp.dll",
    "ZXing.dll",
)
SIGNTOOL_PATH = Path(r"C:\sign-tool\signtool.exe")
SIGN_CERT_SUBJECT = "深度视觉（广东）人工智能研究有限公司"
TIMESTAMP_URL = "http://time.certum.pl"
CODE_SIGNING_EKU = "1.3.6.1.5.5.7.3.3"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="构建、签名并打包推理测试程序")
    parser.add_argument("--package", choices=("csharp", "cpp"), default="csharp")
    parser.add_argument(
        "--install",
        action="store_true",
        help="打包成功后使用当前 Python 环境安装本次生成的 wheel",
    )
    return parser.parse_args()


def require_file(path: Path) -> None:
    if not path.is_file():
        raise FileNotFoundError(f"未找到必需文件: {path}")


def run_step(name: str, command: list[str], env: dict[str, str] | None = None) -> None:
    print(f"[build_package] {name}")
    print(f"[build_package] command: {subprocess.list2cmdline(command)}")
    subprocess.run(command, cwd=REPO_ROOT, check=True, env=env)


def find_signing_certificate_thumbprint() -> str:
    powershell = shutil.which("powershell.exe")
    if powershell is None:
        raise FileNotFoundError("未找到 powershell.exe，无法读取代码签名证书")

    subject_code_units = ", ".join(str(ord(character)) for character in SIGN_CERT_SUBJECT)
    script = f"""
$subjectName = -join @({subject_code_units} | ForEach-Object {{ [char]$_ }})
$codeSigningEku = '{CODE_SIGNING_EKU}'
$now = Get-Date
$certificates = @(Get-ChildItem -LiteralPath 'Cert:\\CurrentUser\\My' | Where-Object {{
    $certificate = $_
    $ekuExtension = $certificate.Extensions |
        Where-Object {{ $_.Oid.Value -eq '2.5.29.37' }} |
        Select-Object -First 1
    $hasCodeSigningEku = $null -ne $ekuExtension -and
        @($ekuExtension.EnhancedKeyUsages | Where-Object {{ $_.Value -eq $codeSigningEku }}).Count -gt 0
    $certificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false
    ) -ceq $subjectName -and
        $certificate.HasPrivateKey -and
        $certificate.NotBefore -le $now -and
        $certificate.NotAfter -ge $now -and
        $hasCodeSigningEku
}})
if ($certificates.Count -ne 1) {{
    [Console]::Error.Write($certificates.Count)
    exit 1
}}
[Console]::Out.Write($certificates[0].Thumbprint.Replace(' ', ''))
"""
    powershell_env = os.environ.copy()
    powershell_env["PSModulePath"] = os.pathsep.join(
        os.path.expandvars(path)
        for path in (
            r"%USERPROFILE%\Documents\WindowsPowerShell\Modules",
            r"%ProgramFiles%\WindowsPowerShell\Modules",
            r"%SystemRoot%\System32\WindowsPowerShell\v1.0\Modules",
        )
    )
    result = subprocess.run(
        [powershell, "-NoProfile", "-NonInteractive", "-Command", script],
        cwd=REPO_ROOT,
        check=False,
        capture_output=True,
        text=True,
        encoding="ascii",
        env=powershell_env,
    )
    if result.returncode != 0:
        certificate_count = result.stderr.strip() or "未知"
        raise RuntimeError(f"有效代码签名证书数量不是 1：{certificate_count}")
    thumbprint = result.stdout.strip()
    if re.fullmatch(r"[0-9A-Fa-f]{40}", thumbprint) is None:
        raise RuntimeError("代码签名证书指纹格式无效")
    return thumbprint


def rebuild_staging_directory(staging_dir: Path | None = None, package_name: str = PACKAGE_NAME) -> None:
    staging_dir = STAGING_DIR if staging_dir is None else staging_dir
    repo_root = REPO_ROOT.resolve()
    lexical_path = staging_dir.absolute()
    resolved_path = staging_dir.resolve(strict=False)

    if lexical_path.parent != repo_root or lexical_path.name != package_name:
        raise RuntimeError(f"拒绝清理仓库外目录: {lexical_path}")
    if resolved_path != lexical_path:
        raise RuntimeError(f"拒绝清理链接或重解析目录: {lexical_path} -> {resolved_path}")

    if staging_dir.exists():
        if not staging_dir.is_dir():
            raise RuntimeError(f"暂存路径不是目录: {staging_dir}")
        shutil.rmtree(staging_dir)
    staging_dir.mkdir()


def copy_package_files() -> list[Path]:
    if not DEMO_OUTPUT_DIR.is_dir():
        raise FileNotFoundError(f"未找到 C# 构建输出目录: {DEMO_OUTPUT_DIR}")

    rebuild_staging_directory()

    for file_name in PACKAGE_OUTPUT_FILE_NAMES:
        source = DEMO_OUTPUT_DIR / file_name
        require_file(source)
        shutil.copy2(source, STAGING_DIR / source.name)

    require_file(RPC_EXE_SOURCE)
    shutil.copy2(RPC_EXE_SOURCE, STAGING_DIR / RPC_EXE_NAME)

    hasp_ini = REPO_ROOT / "hasp_26146.ini"
    require_file(hasp_ini)
    shutil.copy2(hasp_ini, STAGING_DIR / hasp_ini.name)

    signed_executables = [STAGING_DIR / file_name for file_name in SIGNED_EXE_NAMES]
    for executable in signed_executables:
        require_file(executable)
    return signed_executables


def copy_cpp_package_files() -> list[Path]:
    sources = {
        CPP_DEMO_EXE_NAME: CPP_DEMO_OUTPUT_DIR / "dlcv_infer_cpp_qt_demo.exe",
        **{name: CPP_DEMO_OUTPUT_DIR / name for name in CPP_OUTPUT_FILE_NAMES},
        "hasp_26146.ini": REPO_ROOT / "hasp_26146.ini",
    }
    for source in (*sources.values(), *(CPP_SHARED_RUNTIME_DIR / name for name in CPP_SHARED_FILE_NAMES)):
        require_file(source)
    rebuild_staging_directory(CPP_STAGING_DIR, CPP_PACKAGE_NAME)
    for name, source in sources.items():
        destination = CPP_STAGING_DIR / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    return [CPP_STAGING_DIR / CPP_DEMO_EXE_NAME]


def snapshot_wheels(dist_dir: Path | None = None) -> dict[Path, tuple[int, int, int]]:
    dist_dir = REPO_ROOT / "dist" if dist_dir is None else dist_dir
    return {
        path.resolve(): (path.stat().st_mtime_ns, path.stat().st_ctime_ns, path.stat().st_size)
        for path in dist_dir.glob("*.whl")
    }


def find_generated_wheel(before: dict[Path, tuple[int, int, int]], dist_dir: Path | None = None) -> Path:
    dist_dir = REPO_ROOT / "dist" if dist_dir is None else dist_dir
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
    is_cpp = args.package == "cpp"
    demo_project = (REPO_ROOT / "dlcv_infer_cpp_qt_demo" / "dlcv_infer_cpp_qt_demo.vcxproj"
                    if is_cpp else REPO_ROOT / "DlcvDemo" / "DlcvDemo.csproj")
    dist_dir = REPO_ROOT / ("dist_cpp" if is_cpp else "dist")

    try:
        if not is_cpp:
            require_file(sync_script)
        require_file(build_script)
        require_file(demo_project)
        require_file(SIGNTOOL_PATH)

        if not is_cpp:
            run_step("同步程序集版本", [sys.executable, str(sync_script)])
        run_step(
            "构建 C++ 测试程序" if is_cpp else "构建 C# 测试程序",
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
            env={**os.environ, "DlcvUseSharedQt": "true"} if is_cpp else None,
        )

        signed_executables = copy_cpp_package_files() if is_cpp else copy_package_files()
        certificate_thumbprint = find_signing_certificate_thumbprint()
        for executable in signed_executables:
            run_step(
                f"签名 {executable.name}",
                [
                    str(SIGNTOOL_PATH),
                    "sign",
                    "/sha1",
                    certificate_thumbprint,
                    "/tr",
                    TIMESTAMP_URL,
                    "/td",
                    "sha256",
                    "/fd",
                    "sha256",
                    "/v",
                    str(executable),
                ],
            )
        wheels_before_build = snapshot_wheels(dist_dir)
        wheel_env = os.environ.copy()
        wheel_env["DLCV_PACKAGE_VARIANT"] = args.package
        run_step(
            "构建 wheel",
            [sys.executable, "-m", "build", "--wheel", "--no-isolation", "--outdir", str(dist_dir)],
            env=wheel_env,
        )

        wheel_path = find_generated_wheel(wheels_before_build, dist_dir)
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
