"""SentinelManager 独立组件的编译、验证、打包与本机安装入口。"""

from __future__ import annotations

import argparse
import datetime
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import zipfile
import zlib
from dataclasses import dataclass
from pathlib import Path


COMPONENT_DIR = Path(__file__).resolve().parent
EXE_NAME = "SentinelManager.exe"
CONFIG_NAME = EXE_NAME + ".config"
REQUIRED_FILES = frozenset({EXE_NAME, "README.md"})
ALLOWED_FILES = REQUIRED_FILES | {CONFIG_NAME}
MAX_FILE_BYTES = 64 * 1024 * 1024
MAX_ARCHIVE_BYTES = 128 * 1024 * 1024
SCREENSHOTS = ("initial.png", "result.png", "raw-result.png", "compact.png")


class PackageError(RuntimeError):
    """流程未通过必要检查。"""


@dataclass(frozen=True)
class Version:
    numeric: str
    informational: str

    @property
    def archive_name(self) -> str:
        return f"SentinelManager-{self.informational}-win-x64.zip"


def read_version(path: Path) -> Version:
    text = path.read_text(encoding="utf-8-sig")
    # 先保留字符串，再去除注释，避免把注释中的旧版本当成有效声明。
    text = re.sub(r'"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\r\n]*',
                  lambda match: match[0] if match[0].startswith('"') else " ",
                  text, flags=re.DOTALL)
    values = {}
    for name in ("AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion"):
        matches = re.findall(
            rf'\[\s*assembly\s*:\s*(?:System\.Reflection\.)?{name}(?:Attribute)?'
            rf'\s*\(\s*"([^"\r\n]*)"\s*\)\s*\]', text)
        declarations = re.findall(
            rf'\[\s*assembly\s*:\s*(?:System\.Reflection\.)?{name}(?:Attribute)?\s*\(', text)
        if len(matches) != 1 or len(declarations) != 1:
            raise PackageError(f"{name} 必须且只能有一个字符串版本声明。")
        values[name] = matches[0]
    numeric = values["AssemblyFileVersion"]
    if not re.fullmatch(r"[1-9][0-9]{3}\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)", numeric):
        raise PackageError("数值版本必须使用 YYYY.M.D.P 四段整数。")
    year, month, day, revision = map(int, numeric.split("."))
    try:
        datetime.date(year, month, day)
    except ValueError as exc:
        raise PackageError("数值版本日期无效。") from exc
    if revision > 65534 or values["AssemblyVersion"] != numeric:
        raise PackageError("程序集版本与文件版本必须相同，末段不得超过 65534。")
    informational = values["AssemblyInformationalVersion"]
    if not re.fullmatch(re.escape(numeric) + r"(?:a(?:0|[1-9][0-9]*))?", informational):
        raise PackageError("信息版本必须为数值版本或追加 aN。")
    return Version(numeric, informational)


def check_plain_path(path: Path) -> None:
    """拒绝符号链接、目录连接及其他重解析点。"""
    path = Path(os.path.abspath(path))
    for candidate in (path, *path.parents):
        try:
            info = candidate.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise PackageError(f"路径包含链接或重解析点：{candidate.name}")


def require_file(path: Path) -> None:
    check_plain_path(path)
    if not path.is_file():
        raise PackageError(f"缺少文件：{path.name}")
    info = path.stat()
    if not stat.S_ISREG(info.st_mode) or info.st_size == 0 or info.st_size > MAX_FILE_BYTES:
        raise PackageError(f"文件类型或大小不符合要求：{path.name}")


def read_exe_version(path: Path) -> Version:
    require_file(path)
    env = os.environ.copy()
    env["SENTINEL_PACKAGE_EXE"] = str(path.absolute())
    command = (
        "$ErrorActionPreference = 'Stop'; "
        "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); "
        "$v = (Get-Item -LiteralPath $env:SENTINEL_PACKAGE_EXE).VersionInfo; "
        "@{ FileVersion = $v.FileVersion; ProductVersion = $v.ProductVersion } | ConvertTo-Json -Compress"
    )
    result = subprocess.run(
        ["powershell.exe", "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command],
        env=env, check=True, capture_output=True, text=True, encoding="utf-8", errors="strict", timeout=30,
    )
    try:
        data = json.loads(result.stdout.lstrip("\ufeff"))
        file_version, informational = data["FileVersion"], data["ProductVersion"]
        if not isinstance(file_version, str) or not isinstance(informational, str):
            raise ValueError("版本字段不是字符串")
    except (ValueError, KeyError, TypeError) as exc:
        raise PackageError("无法读取 EXE 文件版本与信息版本。") from exc
    return Version(file_version, informational)


def verify_exe_version(path: Path, expected: Version) -> None:
    actual = read_exe_version(path)
    if actual != expected:
        raise PackageError(
            f"EXE 版本不符：文件版本 {actual.numeric}，信息版本 {actual.informational}；"
            f"应为 {expected.numeric} / {expected.informational}。")


def package_files(component: Path) -> dict[str, Path]:
    output = component / "bin" / "Release"
    files = {EXE_NAME: output / EXE_NAME, "README.md": component / "README.md"}
    config = output / CONFIG_NAME
    check_plain_path(config)
    if config.exists():
        files[CONFIG_NAME] = config
    for path in files.values():
        require_file(path)
    return files


def validate_archive(path: Path) -> dict[str, bytes]:
    check_plain_path(path)
    if not path.is_file() or path.stat().st_size > MAX_ARCHIVE_BYTES:
        raise PackageError("安装包不存在或体积过大。")
    try:
        with zipfile.ZipFile(path) as archive:
            members = archive.infolist()
            if len(members) not in (2, 3):
                raise PackageError("安装包文件数量不符合清单。")
            names = set()
            for member in members:
                name = member.filename
                mode = member.external_attr >> 16
                file_type = stat.S_IFMT(mode)
                if (name not in ALLOWED_FILES or member.orig_filename != name or name in names
                        or member.is_dir() or member.flag_bits & 1
                        or file_type not in (0, stat.S_IFREG)
                        or member.external_attr & 0x410
                        or member.file_size <= 0 or member.file_size > MAX_FILE_BYTES
                        or member.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)):
                    raise PackageError(f"安装包包含非法文件或属性：{name!r}")
                names.add(name)
            if not REQUIRED_FILES <= names:
                raise PackageError("安装包缺少应用 EXE 或 README。")
            # 不调用 extract，ZIP 中的名称仅用于严格清单内的平面文件。
            return {member.filename: archive.read(member) for member in members}
    except PackageError:
        raise
    except (zipfile.BadZipFile, RuntimeError, NotImplementedError, EOFError, zlib.error) as exc:
        raise PackageError("安装包读取或完整性检查失败。") from exc


def create_archive(component: Path, version: Version) -> Path:
    files = package_files(component)
    verify_exe_version(files[EXE_NAME], version)
    dist = component / "dist"
    check_plain_path(dist)
    target = dist / version.archive_name
    check_plain_path(target)
    with tempfile.TemporaryDirectory(prefix="sentinel-package-") as directory:
        staged = Path(directory) / version.archive_name
        with zipfile.ZipFile(staged, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, source in sorted(files.items()):
                archive.write(source, name)
        payload = validate_archive(staged)
        if any(payload[name] != source.read_bytes() for name, source in files.items()):
            raise PackageError("打包文件比对失败。")
        dist.mkdir(parents=True, exist_ok=True)
        # 同版本包只有在测试和内容检查完成后才会被替换。
        if target.exists() and target.stat().st_nlink > 1:
            raise PackageError("安装包目标是硬链接，未写入。")
        shutil.copyfile(staged, target)
        if target.read_bytes() != staged.read_bytes():
            raise PackageError("输出安装包比对失败。")
    return target


def run_step(title: str, command: list[str], cwd: Path, timeout: int) -> None:
    print(title, flush=True)
    subprocess.run(command, cwd=cwd, check=True, timeout=timeout)


def validate_ui_results(output: Path) -> None:
    report_path = output / "ui-test-results.json"
    require_file(report_path)

    def unique_fields(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise PackageError("界面结果 JSON 包含重复字段。")
            result[key] = value
        return result

    def reject_constant(value):
        raise PackageError(f"界面结果 JSON 包含非法常量：{value}")

    try:
        report = json.loads(report_path.read_text(encoding="utf-8"),
                            object_pairs_hook=unique_fields, parse_constant=reject_constant)
    except (ValueError, UnicodeError) as exc:
        raise PackageError("界面结果必须是有效的 UTF-8 JSON。") from exc
    if not isinstance(report, dict) or report.get("passed") is not True:
        raise PackageError("界面结果未通过。")
    checks = report.get("checks")
    if not isinstance(checks, list) or not checks:
        raise PackageError("界面结果缺少检查记录。")
    names = set()
    for item in checks:
        if (not isinstance(item, dict) or item.get("passed") is not True
                or not isinstance(item.get("name"), str) or not item["name"].strip()
                or item["name"] in names):
            raise PackageError("界面检查失败或记录格式无效。")
        names.add(item["name"])
    screenshots = report.get("screenshots")
    if (not isinstance(screenshots, list) or len(screenshots) != len(SCREENSHOTS)
            or any(not isinstance(name, str) for name in screenshots)
            or set(screenshots) != set(SCREENSHOTS)):
        raise PackageError("界面截图清单不完整。")
    if report.get("data_source") != "固定测试数据，未连接真实服务":
        raise PackageError("界面结果未声明隔离测试数据来源。")
    try:
        # .NET 的 o 格式包含七位小数；Python 3.10 只接收微秒精度。
        timestamp_text = re.sub(r"(\.\d{6})\d+(?=[+-]\d{2}:\d{2}$)", r"\1", report["timestamp"])
        timestamp = datetime.datetime.fromisoformat(timestamp_text)
        if timestamp.tzinfo is None:
            raise ValueError("缺少时区")
    except (KeyError, TypeError, ValueError) as exc:
        raise PackageError("界面结果缺少有效时间。") from exc
    for name in SCREENSHOTS:
        image = output / name
        require_file(image)
        header = image.read_bytes()[:24]
        if (len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n"
                or header[8:16] != b"\x00\x00\x00\rIHDR"
                or int.from_bytes(header[16:20], "big") == 0
                or int.from_bytes(header[20:24], "big") == 0):
            raise PackageError(f"界面截图不是有效 PNG：{name}")
    if (output / "initial.png").read_bytes() == (output / "result.png").read_bytes():
        raise PackageError("初始截图与执行结果截图相同。")
    print(f"界面结果通过：{len(checks)} 项检查，{len(SCREENSHOTS)} 张截图。")


def build_package(component: Path = COMPONENT_DIR) -> Path:
    root = component.parent
    version_file = component / "Properties" / "AssemblyInfo.cs"
    version = read_version(version_file)
    builder = root / ".cursor" / "skills" / "vs-build" / "scripts" / "build.py"
    project = root / "Test" / "SentinelManagerTest" / "SentinelManagerTest.csproj"
    require_file(builder)
    require_file(project)
    run_step("编译 SentinelManager 测试工程及其应用引用。", [
        sys.executable, "-B", str(builder), str(project), "--configuration", "Release",
        "--platform", "x64", "--target", "Build", "--verbosity", "minimal",
    ], root, 900)
    test_exe = project.parent / "bin" / "Release" / "SentinelManagerTest.exe"
    require_file(test_exe)
    verify_exe_version(component / "bin" / "Release" / EXE_NAME, version)
    with tempfile.TemporaryDirectory(prefix="sentinel-validation-") as directory:
        temporary = Path(directory)
        run_step("执行后端隔离测试。", [str(test_exe)], temporary, 180)
        output = temporary / "ui"
        output.mkdir()
        run_step("执行非交互界面回归。", [
            str(test_exe), "ui-test", "--output-dir", str(output),
        ], temporary, 180)
        validate_ui_results(output)
    if read_version(version_file) != version:
        raise PackageError("验证期间源码版本发生变化，未生成安装包。")
    return create_archive(component, version)


def default_install_dir() -> Path:
    value = os.environ.get("LOCALAPPDATA")
    if not value or not Path(value).is_absolute():
        raise PackageError("LOCALAPPDATA 未设置为有效绝对路径。")
    return Path(value) / "Programs" / "OpenIVS" / "SentinelManager"


def install_package(component: Path = COMPONENT_DIR) -> Path:
    version = read_version(component / "Properties" / "AssemblyInfo.cs")
    archive = component / "dist" / version.archive_name
    payload = validate_archive(archive)
    target = default_install_dir()
    check_plain_path(target)
    if target.exists() and not target.is_dir():
        raise PackageError("安装位置不是目录。")
    for name in ALLOWED_FILES:
        destination = target / name
        check_plain_path(destination)
        if destination.exists() and (not destination.is_file() or destination.stat().st_nlink > 1):
            raise PackageError(f"安装目标不是普通独立文件：{name}")
    if CONFIG_NAME not in payload and (target / CONFIG_NAME).exists():
        raise PackageError("安装包不含配置，但安装位置已有配置；未删除或修改现有配置。")
    with tempfile.TemporaryDirectory(prefix="sentinel-install-") as directory:
        temporary = Path(directory)
        staged = temporary / "package"
        backup = temporary / "backup"
        staged.mkdir()
        backup.mkdir()
        for name, content in payload.items():
            (staged / name).write_bytes(content)
        verify_exe_version(staged / EXE_NAME, version)
        existing = set()
        for name in payload:
            destination = target / name
            if destination.exists():
                shutil.copyfile(destination, backup / name)
                existing.add(name)
        target.mkdir(parents=True, exist_ok=True)
        changed = []
        try:
            for name in sorted(payload):
                check_plain_path(target / name)
                changed.append(name)
                shutil.copyfile(staged / name, target / name)
            for name, content in payload.items():
                if (target / name).read_bytes() != content:
                    raise PackageError(f"安装文件比对失败：{name}")
            verify_exe_version(target / EXE_NAME, version)
        except Exception as exc:
            failures = []
            for name in reversed(changed):
                try:
                    check_plain_path(target / name)
                    if name in existing:
                        shutil.copyfile(backup / name, target / name)
                    else:
                        (target / name).unlink(missing_ok=True)
                except (OSError, PackageError) as restore_error:
                    failures.append(f"{name}: {restore_error}")
            if failures:
                raise PackageError("安装失败，部分文件恢复失败：" + "; ".join(failures)) from exc
            raise
    print(f"安装成功：{target}")
    print(f"文件比对通过：{', '.join(sorted(payload))}")
    print(f"FileVersion：{version.numeric}")
    print(f"信息版本（ProductVersion）：{version.informational}")
    return target


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="SentinelManager 独立编译打包与本机安装")
    parser.add_argument("action", choices=("build", "install"))
    args = parser.parse_args(argv)
    try:
        if args.action == "install":
            install_package()
        else:
            archive = build_package()
            print(f"打包成功：{archive}")
        return 0
    except (OSError, ValueError, PackageError, subprocess.SubprocessError) as exc:
        print(f"操作失败：{exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
