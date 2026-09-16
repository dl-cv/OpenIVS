"""使用已编译程序执行 native 与独立进程 DPI 虚拟化回归，不更改系统显示设置。"""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import stat
import struct
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zlib


ROOT = Path(__file__).resolve().parents[1]
TEST_EXE = ROOT / "Test" / "SentinelManagerTest" / "bin" / "Release" / "SentinelManagerTest.exe"
APP_CONFIG = ROOT / "SentinelManager" / "App.config"
SCREENSHOTS = ("initial.png", "result.png", "raw-result.png", "compact.png")
TIMEOUT_SECONDS = 60
REQUIRED_CHECKS = (
    "启动不访问服务", "信息栏只读", "初始未执行", "执行中禁用全部操作", "执行中不能关闭窗体",
    "阻止重复请求", "查询无需确认", "完整展示设备和网络信息", "替换初始结果", "结束恢复操作",
    "复制按钮已启用", "宽屏显示完整表格", "初始与结果图不同", "紧凑窗口可滚动", "紧凑窗口操作可见",
    "取消四项修改不请求服务", "四项修改确认后分别执行一次", "失败结果替换旧结果", "失败后恢复操作", "失败后可再次查询",
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def plain_path(path):
    for part in (path, *path.parents):
        try:
            info = part.lstat()
        except FileNotFoundError:
            continue
        require(not stat.S_ISLNK(info.st_mode) and not (
            getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
        ), "路径不能经过目录链接或重解析点。")


def prepare_output(value):
    path = Path(os.path.abspath(value))
    plain_path(path)
    temporary = Path(tempfile.gettempdir()).resolve()
    resolved = path.resolve()
    require(resolved != temporary and temporary in resolved.parents,
            "输出目录必须是系统临时目录内的专用子目录。")
    require(not path.exists() or path.is_dir() and not any(path.iterdir()), "输出目录必须为空。")
    path.mkdir(parents=True, exist_ok=True)
    plain_path(path)
    return path


def awareness_setting(tree):
    settings = tree.findall("./System.Windows.Forms.ApplicationConfigurationSection/add[@key='DpiAwareness']")
    require(len(settings) == 1, "正式配置必须包含唯一的 DpiAwareness 设置。")
    return settings[0]


def unique_fields(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "JSON 包含重复字段。")
        result[key] = value
    return result


def reject_constant(value):
    raise ValueError("JSON 包含非法常量：" + value)


def read_png(path, expected_size):
    data = path.read_bytes()
    require(data[:8] == b"\x89PNG\r\n\x1a\n", "截图不是 PNG：" + path.name)
    offset, compressed, header, ended = 8, bytearray(), None, False
    while offset < len(data):
        require(offset + 12 <= len(data), "PNG 数据不完整。")
        length = int.from_bytes(data[offset:offset + 4], "big")
        end = offset + 12 + length
        require(end <= len(data), "PNG 数据块不完整。")
        kind = data[offset + 4:offset + 8]
        payload = data[offset + 8:end - 4]
        require(zlib.crc32(kind + payload) & 0xffffffff == int.from_bytes(data[end - 4:end], "big"),
                "PNG 校验失败。")
        if header is None:
            require(kind == b"IHDR" and length == 13, "PNG 缺少图像头。")
            header = struct.unpack(">IIBBBBB", payload)
        elif kind == b"IDAT":
            compressed.extend(payload)
        elif kind == b"IEND":
            require(length == 0 and end == len(data), "PNG 结束位置错误。")
            ended = True
            break
        offset = end
    require(ended and header is not None and compressed, "PNG 图像数据缺失。")
    width, height, depth, color, compression, filtering, interlace = header
    require(width == expected_size["width"] and height == expected_size["height"], "截图尺寸与窗体报告不符。")
    require(depth == 8 and color in (2, 6) and compression == filtering == interlace == 0,
            "截图不是预期的 8 位 RGB/RGBA 图像。")
    pixels = zlib.decompress(compressed)
    stride = width * (3 if color == 2 else 4) + 1
    require(len(pixels) == stride * height and all(pixels[row * stride] <= 4 for row in range(height)),
            "PNG 像素数据不完整。")
    return {"width": width, "height": height, "sha256": hashlib.sha256(data).hexdigest()}


def validate_report(output, mode, expected_native_dpi):
    report = json.loads((output / "ui-test-results.json").read_text(encoding="utf-8"),
                        object_pairs_hook=unique_fields, parse_constant=reject_constant)
    require(report["passed"] is True, "界面检查未通过。")
    checks = report["checks"]
    require(isinstance(checks, list) and all(item["passed"] is True for item in checks), "存在失败检查。")
    names = [item["name"] for item in checks]
    require(len(names) == len(set(names)) and set(REQUIRED_CHECKS).issubset(names), "原有 20 项检查不完整。")
    require(report["screenshots"] == list(SCREENSHOTS), "四张截图清单不完整。")
    require(report["data_source"] == "固定测试数据，未连接真实服务", "测试数据来源不符。")
    metrics = report["metrics"]
    dpi = metrics["device_dpi"]
    require(type(dpi) is int and dpi > 0, "实际 DPI 无效。")
    scale = metrics["scale_factor"]
    require(math.isclose(scale, dpi / 96, abs_tol=1e-8), "缩放系数与实际 DPI 不符。")
    require(metrics["auto_scale_mode"] == "Dpi" and metrics["font_unit"] == "Point"
            and math.isclose(metrics["font_size_points"], 12, abs_tol=0.01), "窗体 DPI 或基础字体不符。")
    window, area = metrics["initial_window_bounds"], metrics["screen_working_area"]
    require(window["width"] > 0 and window["height"] > 0 and all(
        area[axis] <= window[axis] and window[axis] + window[size] <= area[axis] + area[size]
        for axis, size in (("x", "width"), ("y", "height"))
    ), "初始窗口超出工作区。")
    if mode == "native":
        require(metrics["thread_per_monitor_v2"] is True and metrics["window_per_monitor_v2"] is True,
                "native 进程未使用真实 PerMonitorV2 DPI 上下文。")
        if expected_native_dpi is not None:
            require(dpi == expected_native_dpi, "native 实际 DPI 与指定值不符。")
    else:
        require(dpi == 96 and metrics["thread_dpi_unaware"] is True and metrics["window_dpi_unaware"] is True,
                "100% 独立进程未进入 96 DPI 虚拟化环境。")
    images = {}
    for name in SCREENSHOTS:
        layout = report["layouts"][Path(name).stem]
        require(layout["text_errors"] == [], "存在文字裁切或操作显示不完整。")
        require(math.isclose(layout["logical_width"], layout["client_size"]["width"] / scale, abs_tol=1e-8)
                and layout["compact"] == (layout["logical_width"] < 960), "逻辑宽度判定不符。")
        if name == "compact.png":
            require(layout["compact"] is True, "紧凑尺寸未进入紧凑布局。")
        elif name in ("result.png", "raw-result.png"):
            require(layout["compact"] is False, "宽屏尺寸未进入宽屏布局。")
        images[name] = read_png(output / name, layout["window_size"])
    for button in [metrics["repair_button_size"], *(item["repair_button_size"] for item in report["layouts"].values())]:
        require(abs(button["width"] - 140 * scale) <= 2 and abs(button["height"] - 48 * scale) <= 2,
                "修复按钮尺寸不符合 DPI 比例。")
    require(images["initial.png"]["sha256"] != images["result.png"]["sha256"], "初始截图与结果截图相同。")
    return report, images


def run_process(executable, output, mode, expected_native_dpi):
    output.mkdir()
    entry = {"mode": mode, "passed": False, "output_subdirectory": output.name,
             "dpi_environment": "实际设备 DPI，PerMonitorV2" if mode == "native" else
             "独立进程 DPI 虚拟化，Windows 返回 96 DPI；不代表真实 100% 显示器"}
    try:
        with (output / "stdout.txt").open("wb") as stdout, (output / "stderr.txt").open("wb") as stderr:
            # 超时会终止并等待该测试进程，不启动其他辅助程序。
            completed = subprocess.run([str(executable), "ui-test", "--output-dir", str(output)],
                                       cwd=output, stdout=stdout, stderr=stderr, timeout=TIMEOUT_SECONDS,
                                       check=False)
        entry["exit_code"] = completed.returncode
        require(completed.returncode == 0, "测试进程返回非零退出码。")
        report, images = validate_report(output, mode, expected_native_dpi)
        entry.update(passed=True, metrics=report["metrics"], check_count=len(report["checks"]), images=images)
    except subprocess.TimeoutExpired:
        entry.update(error="测试进程超时，已终止。", timed_out=True)
    except (OSError, ValueError, KeyError, TypeError, zlib.error) as exc:
        entry["error"] = str(exc)
    return entry


def run(output, expected_native_dpi=None, test_exe=TEST_EXE):
    require(os.name == "nt", "此回归入口仅支持 Windows。")
    plain_path(test_exe)
    source_executables = (test_exe, test_exe.with_name("SentinelManager.exe"))
    for executable in source_executables:
        require(executable.is_file(), "缺少已编译的测试或应用程序：" + executable.name)
    config = ET.parse(APP_CONFIG)
    require(awareness_setting(config).get("value") == "PerMonitorV2", "正式 App.config 必须启用 PerMonitorV2。")
    native_config = ET.parse(Path(str(test_exe) + ".config"))
    require(awareness_setting(native_config).get("value") == "PerMonitorV2", "native 测试配置必须启用 PerMonitorV2。")
    hashes = {item.name: hashlib.sha256(item.read_bytes()).hexdigest() for item in source_executables}
    summary = {"passed": False, "expected_native_dpi": expected_native_dpi,
               "physical_monitor_switch_tested": False, "system_display_settings_changed": False,
               "binary_sha256": hashes, "processes": [], "comparison": {"passed": False}}
    # 二进制副本仅在系统临时目录运行，退出后删除；报告和截图保留在输出目录。
    with tempfile.TemporaryDirectory(prefix="sentinel-dpi-process-") as directory:
        runtime = Path(directory)
        for source in source_executables:
            destination = runtime / source.name
            shutil.copy2(source, destination)
            require(hashlib.sha256(destination.read_bytes()).hexdigest() == hashes[source.name], "二进制副本校验失败。")
        awareness_setting(config).set("value", "false")
        virtual_exe = runtime / test_exe.name
        config.write(str(virtual_exe) + ".config", encoding="utf-8", xml_declaration=True)
        native = run_process(test_exe, output / "native", "native", expected_native_dpi)
        virtual = run_process(virtual_exe, output / "virtualized-100", "virtualized-100", None)
        summary["processes"] = [native, virtual]
    if native["passed"] and virtual["passed"]:
        ratio = native["metrics"]["device_dpi"] / virtual["metrics"]["device_dpi"]
        first, second = native["metrics"]["repair_button_size"], virtual["metrics"]["repair_button_size"]
        errors = {axis: abs(first[axis] - second[axis] * ratio) for axis in ("width", "height")}
        summary["comparison"] = {"passed": all(value <= 2 for value in errors.values()),
                                 "dpi_ratio": ratio, "button_size_error_pixels": errors,
                                 "button_width_ratio": first["width"] / second["width"],
                                 "button_height_ratio": first["height"] / second["height"]}
        summary["passed"] = summary["comparison"]["passed"]
    return summary


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True, help="系统临时目录内的空子目录")
    parser.add_argument("--expected-native-dpi", type=int, help="期望的实际设备 DPI，例如 144")
    args = parser.parse_args(argv)
    output = None
    try:
        require(args.expected_native_dpi is None or args.expected_native_dpi > 0, "期望 DPI 必须为正整数。")
        output = prepare_output(args.output_dir)
        report = run(output, args.expected_native_dpi)
    except (OSError, ValueError, ET.ParseError) as exc:
        report = {"passed": False, "error": str(exc)}
    if output is not None:
        (output / "dpi-regression-results.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print("DPI 回归通过。" if report["passed"] else "DPI 回归失败：" + report.get("error", "请检查各进程结果。"))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
