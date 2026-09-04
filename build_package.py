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
SIGNTOOL_PATH = Path(r"C:\sign-tool\signtool.exe")
SIGN_CERT_SUBJECT = "深度视觉（广东）人工智能研究有限公司"
TIMESTAMP_URL = "http://time.certum.pl"
CODE_SIGNING_EKU = "1.3.6.1.5.5.7.3.3"


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

        signed_executables = copy_package_files()
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
