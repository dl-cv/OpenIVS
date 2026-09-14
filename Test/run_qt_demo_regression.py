#!/usr/bin/env python3
"""串行执行 C 与 C++ Qt Demo 的真实进程回归检查。"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path
from typing import Any

from run_demo_regression import _sha256

CASES_PATH = Path(__file__).resolve().with_name("qt_demo_regression_cases.json")
WRAPPER_DLL_NAME = "dlcv_infer_cpp.dll"
REQUIRED_CASE_IDS = {"classification_dvt", "classification_dvo", "segmentation_dvt", "flow_dvst"}
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
PNG_END = b"\x00\x00\x00\x00IEND\xaeB`\x82"
SELFTEST_SIZE = (500, 400)


def _reject_constant(value: str) -> None:
    raise ValueError(value)


def _parse_float(value: str) -> float:
    parsed = float(value)
    if not math.isfinite(parsed):
        raise ValueError(value)
    return parsed


def _read_json(path: Path, label: str) -> tuple[Any | None, list[str]]:
    try:
        text = path.read_text(encoding="utf-8", errors="strict")
    except FileNotFoundError:
        return None, [f"{label}未生成"]
    except UnicodeDecodeError:
        return None, [f"{label}不是有效 UTF-8"]
    except OSError:
        return None, [f"{label}读取失败"]
    try:
        return json.loads(text, parse_constant=_reject_constant, parse_float=_parse_float), []
    except (json.JSONDecodeError, ValueError):
        return None, [f"{label}格式无效或包含非有限数值"]


def _number(value: Any) -> bool:
    return not isinstance(value, bool) and isinstance(value, (int, float)) and math.isfinite(value)


def _validate_config(value: Any) -> list[str]:
    if not isinstance(value, dict):
        return ["固定测试清单顶层类型错误"]
    errors: list[str] = []
    if value.get("schema_version") != 1:
        errors.append("固定测试清单 schema_version 错误")
    threshold = value.get("threshold")
    tolerance = value.get("score_tolerance")
    if not _number(threshold) or not 0 <= threshold <= 1:
        errors.append("固定测试清单 threshold 错误")
    if not _number(tolerance) or tolerance <= 0:
        errors.append("固定测试清单 score_tolerance 错误")
    cases = value.get("inference_cases")
    if not isinstance(cases, list):
        return errors + ["固定测试清单 inference_cases 类型错误"]
    seen: set[str] = set()
    for index, case in enumerate(cases):
        prefix = f"固定测试清单 inference_cases[{index}]"
        if not isinstance(case, dict):
            errors.append(f"{prefix} 类型错误")
            continue
        case_id = case.get("id")
        if not isinstance(case_id, str) or not case_id or case_id in seen:
            errors.append(f"{prefix}.id 错误或重复")
        else:
            seen.add(case_id)
        for name in ("model", "image"):
            relative = case.get(name)
            if not isinstance(relative, str) or not relative:
                errors.append(f"{prefix}.{name} 错误")
            elif Path(relative).is_absolute() or ".." in Path(relative).parts:
                errors.append(f"{prefix}.{name} 必须是模型目录内相对路径")
        if not isinstance(case.get("with_mask"), bool):
            errors.append(f"{prefix}.with_mask 类型错误")
        if case.get("compare_between_demos") is not True:
            errors.append(f"{prefix}.compare_between_demos 必须为 true")
        case_tolerance = case.get("score_tolerance", tolerance)
        if not _number(case_tolerance) or case_tolerance <= 0:
            errors.append(f"{prefix}.score_tolerance 错误")
        expected = case.get("expected")
        if not isinstance(expected, dict):
            errors.append(f"{prefix}.expected 类型错误")
            continue
        count, categories, scores = expected.get("count"), expected.get("categories"), expected.get("scores")
        if isinstance(count, bool) or not isinstance(count, int) or count < 1:
            errors.append(f"{prefix}.expected.count 错误")
        if not isinstance(categories, list) or not all(isinstance(x, str) and x for x in categories):
            errors.append(f"{prefix}.expected.categories 错误")
        if not isinstance(scores, list) or not all(_number(x) and 0 <= x <= 1 for x in scores):
            errors.append(f"{prefix}.expected.scores 错误")
        if isinstance(count, int) and not isinstance(count, bool):
            if isinstance(categories, list) and len(categories) != count:
                errors.append(f"{prefix}.expected.categories 数量错误")
            if isinstance(scores, list) and len(scores) != count:
                errors.append(f"{prefix}.expected.scores 数量错误")
    if seen != REQUIRED_CASE_IDS:
        errors.append("固定测试清单缺少或增加了推理用例")
    return errors


def _load_config(path: Path) -> tuple[dict[str, Any] | None, list[str]]:
    value, errors = _read_json(path, "固定测试清单")
    if errors:
        return None, errors
    errors = _validate_config(value)
    return (value, []) if not errors else (None, errors)


def _inside(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def _fixture(root: Path, relative: str) -> Path | None:
    root = root.resolve()
    path = (root / relative).resolve()
    return path if _inside(path, root) else None


def _report_path_error(path: Path) -> str | None:
    root = Path(tempfile.gettempdir()).resolve()
    target = path.expanduser().resolve(strict=False)
    if target == root or not _inside(target, root):
        return "--output 必须位于系统临时目录内"
    if target.exists() and not target.is_file():
        return "--output 必须是文件路径"
    return None

def _normalize(code: int) -> int:
    return code & 0xFFFFFFFF if code < 0 else code


def _stream(data: bytes) -> dict[str, Any]:
    return {"bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def _artifact(path: Path) -> dict[str, Any] | None:
    try:
        return {"bytes": path.stat().st_size, "sha256": _sha256(path)}
    except OSError:
        return None


def _invoke(command: list[str], cwd: Path, timeout: float, work: Path) -> dict[str, Any]:
    stdout_path, stderr_path = work / "stdout.bin", work / "stderr.bin"
    environment = os.environ.copy()
    for name in ("TEMP", "TMP", "TMPDIR"):
        environment[name] = str(work)
    result: dict[str, Any] = {"launched": False, "timed_out": False, "exit_code": None, "errors": []}
    try:
        with stdout_path.open("wb") as stdout, stderr_path.open("wb") as stderr:
            result["launched"] = True
            completed = subprocess.run(
                command, check=False, timeout=timeout, cwd=str(cwd), env=environment,
                stdout=stdout, stderr=stderr,
                creationflags=(subprocess.CREATE_NO_WINDOW if os.name == "nt" and hasattr(subprocess, "CREATE_NO_WINDOW") else 0),
            )
        result["exit_code"] = _normalize(completed.returncode)
    except subprocess.TimeoutExpired:
        result["timed_out"] = True
        result["errors"].append("进程执行超时")
    except OSError as exc:
        result["errors"].append(f"进程启动失败：{exc.strerror or type(exc).__name__}")
    try:
        result["stdout"], result["stderr"] = stdout_path.read_bytes(), stderr_path.read_bytes()
    except OSError:
        result["stdout"], result["stderr"] = b"", b""
        result["errors"].append("进程输出读取失败")
    return result


def _process_result(value: dict[str, Any]) -> dict[str, Any]:
    return {
        "launched": value["launched"], "timed_out": value["timed_out"],
        "exit_code": value["exit_code"], "stdout": _stream(value["stdout"]),
        "stderr": _stream(value["stderr"]),
    }


def _summary(name: str, value: Any, expected: dict[str, Any], tolerance: float) -> list[str]:
    if not isinstance(value, dict):
        return [f"{name} 结果缺失或类型错误"]
    errors: list[str] = []
    count = value.get("count")
    if type(count) is not int or count != expected["count"]:
        errors.append(f"{name}.count 与基准不符")
    if value.get("categories") != expected["categories"]:
        errors.append(f"{name}.categories 与基准不符")
    scores = value.get("scores")
    if not isinstance(scores, list) or len(scores) != len(expected["scores"]):
        errors.append(f"{name}.scores 数量与基准不符")
    else:
        for index, (actual, baseline) in enumerate(zip(scores, expected["scores"])):
            if not _number(actual) or abs(actual - baseline) > tolerance:
                errors.append(f"{name}.scores[{index}] 与基准不符")
    if value.get("below_threshold") != []:
        errors.append(f"{name}.below_threshold 必须为空数组")
    return errors


def _summary_view(value: Any) -> dict[str, Any] | None:
    if not isinstance(value, dict):
        return None
    return {name: value.get(name) for name in ("count", "categories", "scores")}


def _check_payload(demo: str, value: Any, case: dict[str, Any], threshold: float, tolerance: float) -> tuple[list[str], dict[str, Any] | None]:
    if not isinstance(value, dict):
        return ["推理 JSON 顶层类型错误"], None
    errors: list[str] = []
    if "error" in value:
        errors.append("推理 JSON 包含 error 字段")
    if value.get("language") != demo:
        errors.append("language 与 Demo 类型不符")
    if not _number(value.get("threshold")) or abs(value["threshold"] - threshold) > 1e-12:
        errors.append("threshold 与调用参数不符")
    if type(value.get("device")) is not int or value.get("device") != 0:
        errors.append("device 与调用参数不符")
    if value.get("with_mask") is not case["with_mask"]:
        errors.append("with_mask 与调用参数不符")
    if value.get("calc_mean") is not False:
        errors.append("calc_mean 与调用参数不符")
    for name in ("structured", "json"):
        errors.extend(_summary(name, value.get(name), case["expected"], tolerance))
    structured, json_value = _summary_view(value.get("structured")), _summary_view(value.get("json"))
    if structured != json_value:
        errors.append("structured 与 json 的类别、分数或数量不一致")
    for name in ("consistent", "threshold_check_passed", "mean_check_passed", "release_check_passed"):
        if value.get(name) is not True:
            errors.append(f"{name} 不为 true")
    if demo == "c":
        if value.get("inspection_supported") is not False:
            errors.append("C Demo inspection_supported 不为 false")
        if "inspection_consistent" not in value or value.get("inspection_consistent") is not None:
            errors.append("C Demo inspection_consistent 不为 null")
    elif value.get("inspection_consistent") is not True:
        errors.append("C++ Demo inspection_consistent 不为 true")
    return errors, {"structured": structured, "json": json_value}


def _infer_case(demo: str, exe: Path, cwd: Path, root: Path, case: dict[str, Any], threshold: float, default_tolerance: float, timeout: float, work: Path) -> tuple[dict[str, Any], dict[str, Any] | None]:
    work.mkdir(parents=True)
    model, image = _fixture(root, case["model"]), _fixture(root, case["image"])
    errors: list[str] = []
    if model is None or not model.is_file():
        errors.append("模型文件不存在")
    if image is None or not image.is_file():
        errors.append("图片文件不存在")
    base = {"id": case["id"], "demo": demo, "kind": "infer", "model": case["model"], "image": case["image"], "expected_exit_code": 0}
    if errors:
        base.update({"passed": False, "process": {"launched": False, "timed_out": False, "exit_code": None, "stdout": _stream(b""), "stderr": _stream(b"")}, "result_artifact": None, "observed": None, "errors": errors})
        return base, None
    output = work / "infer.json"
    command = [str(exe), "infer", "--model", str(model), "--image", str(image), "--threshold", format(threshold, ".17g"), "--device", "0", "--with-mask", str(case["with_mask"]).lower(), "--calc-mean", "false", "--output", str(output)]
    process = _invoke(command, cwd, timeout, work)
    errors.extend(process["errors"])
    if process["exit_code"] != 0:
        errors.append(f"进程退出码与预期不符：{process['exit_code']} != 0")
    value, json_errors = _read_json(output, "推理 JSON")
    errors.extend(json_errors)
    observed = None
    if not json_errors:
        tolerance = case.get("score_tolerance", default_tolerance)
        checked, observed = _check_payload(demo, value, case, threshold, tolerance)
        errors.extend(checked)
    base.update({"passed": not errors, "process": _process_result(process), "result_artifact": _artifact(output), "observed": observed, "errors": errors})
    return base, observed if not errors else None

def _expected_exit(demo: str, case_id: str, exe: Path, cwd: Path, command: list[str], expected: int, timeout: float, work: Path, output: Path | None = None, protected: Path | None = None, help_check: bool = False) -> dict[str, Any]:
    work.mkdir(parents=True, exist_ok=True)
    before = _sha256(protected) if protected else None
    process = _invoke(command, cwd, timeout, work)
    errors = list(process["errors"])
    if process["exit_code"] != expected:
        errors.append(f"进程退出码与预期不符：{process['exit_code']} != {expected}")
    if help_check:
        try:
            process["stdout"].decode("utf-8", errors="strict")
        except UnicodeDecodeError:
            errors.append("帮助输出不是有效 UTF-8")
        if b"Usage:" not in process["stdout"]:
            errors.append("帮助输出缺少 Usage")
    if output is not None and output.exists():
        errors.append("参数错误后仍生成了输出文件")
    if protected is not None:
        try:
            if _sha256(protected) != before:
                errors.append("输入文件被输出操作修改")
        except OSError:
            errors.append("受保护输入文件无法读取")
    return {"id": case_id, "demo": demo, "kind": "cli", "expected_exit_code": expected, "passed": not errors, "process": _process_result(process), "errors": errors}


def _put(path: Path, data: bytes) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return path


def _cli_cases(demo: str, exe: Path, cwd: Path, timeout: float, root: Path) -> list[dict[str, Any]]:
    results = [_expected_exit(demo, "help", exe, cwd, [str(exe), "--help"], 0, timeout, root / "help", help_check=True)]
    results.append(_expected_exit(demo, "missing_required_arguments", exe, cwd, [str(exe), "infer"], 2, timeout, root / "missing_required_arguments"))

    work = root / "invalid_threshold"
    model, image, output = _put(work / "model.dvt", b"model"), _put(work / "image.jpg", b"image"), work / "result.json"
    results.append(_expected_exit(demo, "invalid_threshold", exe, cwd, [str(exe), "infer", "--model", str(model), "--image", str(image), "--threshold", "1.1", "--output", str(output)], 2, timeout, work, output=output))

    work = root / "missing_model"
    image, output = _put(work / "image.jpg", b"image"), work / "result.json"
    results.append(_expected_exit(demo, "missing_model", exe, cwd, [str(exe), "infer", "--model", str(work / "missing.dvt"), "--image", str(image), "--threshold", "0.5", "--output", str(output)], 2, timeout, work, output=output))

    work = root / "missing_image"
    model, output = _put(work / "model.dvt", b"model"), work / "result.json"
    results.append(_expected_exit(demo, "missing_image", exe, cwd, [str(exe), "infer", "--model", str(model), "--image", str(work / "missing.jpg"), "--threshold", "0.5", "--output", str(output)], 2, timeout, work, output=output))

    work = root / "dvsp_rejected"
    model, image, output = _put(work / "invalid.dvsp", b"dvsp"), _put(work / "image.jpg", b"image"), work / "result.json"
    results.append(_expected_exit(demo, "dvsp_rejected", exe, cwd, [str(exe), "infer", "--model", str(model), "--image", str(image), "--threshold", "0.5", "--output", str(output)], 2, timeout, work, output=output))

    work = root / "output_overwrites_model"
    model, image = _put(work / "model.dvt", b"protected-model"), _put(work / "image.jpg", b"image")
    results.append(_expected_exit(demo, "output_overwrites_model", exe, cwd, [str(exe), "infer", "--model", str(model), "--image", str(image), "--threshold", "0.5", "--output", str(model)], 2, timeout, work, protected=model))

    work = root / "output_overwrites_image"
    model, image = _put(work / "model.dvt", b"model"), _put(work / "image.jpg", b"protected-image")
    results.append(_expected_exit(demo, "output_overwrites_image", exe, cwd, [str(exe), "infer", "--model", str(model), "--image", str(image), "--threshold", "0.5", "--output", str(image)], 2, timeout, work, protected=image))
    return results


def _png_errors(path: Path) -> list[str]:
    try:
        data = path.read_bytes()
    except FileNotFoundError:
        return ["mask 自测 PNG 未生成"]
    except OSError:
        return ["mask 自测 PNG 读取失败"]
    if len(data) < 45 or not data.startswith(PNG_SIGNATURE):
        return ["mask 自测产物不是有效非空 PNG"]
    offset, chunks, image_data = 8, [], bytearray()
    try:
        while offset < len(data):
            length = struct.unpack(">I", data[offset:offset + 4])[0]
            kind = data[offset + 4:offset + 8]
            payload = data[offset + 8:offset + 8 + length]
            checksum = struct.unpack(">I", data[offset + 8 + length:offset + 12 + length])[0]
            if len(kind) != 4 or len(payload) != length or zlib.crc32(kind + payload) & 0xFFFFFFFF != checksum:
                raise ValueError
            chunks.append((kind, payload))
            if kind == b"IDAT":
                image_data.extend(payload)
            offset += 12 + length
            if kind == b"IEND":
                break
        if offset != len(data) or not chunks or chunks[0][0] != b"IHDR" or len(chunks[0][1]) != 13 or chunks[-1][0] != b"IEND" or not image_data:
            raise ValueError
        width, height = struct.unpack(">II", chunks[0][1][:8])
        if (width, height) != SELFTEST_SIZE:
            return [f"mask 自测 PNG 尺寸错误：{width}x{height} != 500x400"]
        if not zlib.decompress(bytes(image_data)):
            raise ValueError
    except (ValueError, struct.error, zlib.error):
        return ["mask 自测产物不是有效非空 PNG"]
    return []

def _mask_case(demo: str, exe: Path, cwd: Path, timeout: float, work: Path) -> dict[str, Any]:
    work.mkdir(parents=True)
    output = work / "mask.png"
    process = _invoke([str(exe), "mask-visualization-selftest", "--output", str(output)], cwd, timeout, work)
    errors = list(process["errors"])
    if process["exit_code"] != 0:
        errors.append(f"进程退出码与预期不符：{process['exit_code']} != 0")
    errors.extend(_png_errors(output))
    return {"id": "mask_visualization_selftest", "demo": demo, "kind": "png-selftest", "expected_exit_code": 0, "passed": not errors, "process": _process_result(process), "artifact": _artifact(output), "expected_size": {"width": 500, "height": 400}, "errors": errors}

def _comparisons(config: dict[str, Any], observed: dict[str, dict[str, dict[str, Any]]]) -> list[dict[str, Any]]:
    checks: list[dict[str, Any]] = []
    for case in config["inference_cases"]:
        errors: list[str] = []
        left, right = observed["c"].get(case["id"]), observed["cpp"].get(case["id"])
        if left is None or right is None:
            errors.append("缺少可比较的 C 或 C++ 推理结果")
        else:
            tolerance = case.get("score_tolerance", config["score_tolerance"])
            for name in ("structured", "json"):
                a, b = left[name], right[name]
                if a["count"] != b["count"]:
                    errors.append(f"{name}.count 在两个 Demo 间不一致")
                if a["categories"] != b["categories"]:
                    errors.append(f"{name}.categories 在两个 Demo 间不一致")
                if not isinstance(a["scores"], list) or not isinstance(b["scores"], list) or len(a["scores"]) != len(b["scores"]):
                    errors.append(f"{name}.scores 在两个 Demo 间数量不一致")
                else:
                    for index, (x, y) in enumerate(zip(a["scores"], b["scores"])):
                        if not _number(x) or not _number(y) or abs(x - y) > tolerance:
                            errors.append(f"{name}.scores[{index}] 在两个 Demo 间不一致")
        checks.append({"id": case["id"], "passed": not errors, "errors": errors})
    return checks


def _hash(path: Path, label: str, errors: list[str]) -> str | None:
    if not path.is_file():
        errors.append(f"{label} 文件不存在")
        return None
    try:
        return _sha256(path)
    except OSError:
        errors.append(f"{label} 文件读取失败")
        return None


def _preflight(args: argparse.Namespace) -> tuple[dict[str, Any], list[str], bool]:
    errors: list[str] = []
    paths = {name: Path(getattr(args, name)).resolve() for name in ("c_exe", "cpp_exe", "dll", "model_root")}
    paths["core_dir"] = Path(args.core_dll_directory).resolve() if args.core_dll_directory is not None else None
    hashes = {name: _hash(paths[name], name.replace("_", "-"), errors) for name in ("c_exe", "cpp_exe", "dll")}
    can_launch = all(hashes.values())
    if paths["c_exe"] == paths["cpp_exe"]:
        errors.append("--c-exe 与 --cpp-exe 必须指向不同文件")
        can_launch = False
    if any(paths[name].suffix.lower() != ".exe" for name in ("c_exe", "cpp_exe")):
        errors.append("两个 Demo 路径都必须是 .exe 文件")
        can_launch = False
    if not paths["model_root"].is_dir():
        errors.append("model-root 目录不存在")
        can_launch = False
    if not _number(args.timeout) or args.timeout <= 0:
        errors.append("timeout 必须是有限正数")
        can_launch = False
    if paths["core_dir"] is not None and not paths["core_dir"].is_dir():
        errors.append("core-dll-directory 目录不存在")
        can_launch = False
    deployed: dict[str, str | None] = {}
    for demo, name in (("c", "c_exe"), ("cpp", "cpp_exe")):
        deployed[demo] = _hash(paths[name].with_name(WRAPPER_DLL_NAME), f"{demo} EXE 目录 wrapper DLL", errors)
        if deployed[demo] is None or hashes["dll"] is None or deployed[demo] != hashes["dll"]:
            if deployed[demo] is not None and hashes["dll"] is not None:
                errors.append(f"{demo} EXE 目录 wrapper DLL 与指定 DLL SHA256 不一致")
            can_launch = False
    return {"paths": paths, "hashes": hashes, "deployed": deployed}, errors, can_launch


def _fixtures(config: dict[str, Any], root: Path) -> dict[str, Any]:
    report: dict[str, Any] = {}
    names = sorted({name for case in config["inference_cases"] for name in (case["model"], case["image"])})
    for name in names:
        path = _fixture(root, name)
        report[name] = {"present": path is not None and path.is_file(), "sha256": _sha256(path) if path is not None and path.is_file() else None}
    return report


def run_regression(args: argparse.Namespace, cases_path: Path = CASES_PATH) -> dict[str, Any]:
    config, config_errors = _load_config(cases_path)
    state, preflight_errors, can_launch = _preflight(args)
    paths = state["paths"]
    cases: list[dict[str, Any]] = []
    observed: dict[str, dict[str, dict[str, Any]]] = {"c": {}, "cpp": {}}
    if config is not None and can_launch:
        with tempfile.TemporaryDirectory(prefix="dlcv_qt_demo_regression_") as temp_name:
            temp_root = Path(temp_name)
            for demo, exe in (("c", paths["c_exe"]), ("cpp", paths["cpp_exe"])):
                cwd, demo_root = paths["core_dir"] or exe.parent, temp_root / demo
                for case in config["inference_cases"]:
                    result, summary = _infer_case(demo, exe, cwd, paths["model_root"], case, config["threshold"], config["score_tolerance"], args.timeout, demo_root / case["id"])
                    cases.append(result)
                    if summary is not None:
                        observed[demo][case["id"]] = summary
                cases.extend(_cli_cases(demo, exe, cwd, args.timeout, demo_root / "cli"))
                cases.append(_mask_case(demo, exe, cwd, args.timeout, demo_root / "mask"))
        comparisons = _comparisons(config, observed)
    else:
        comparisons = []
    errors = config_errors + preflight_errors
    passed_cases = sum(case["passed"] for case in cases)
    passed_comparisons = sum(case["passed"] for case in comparisons)
    passed = not errors and bool(cases) and passed_cases == len(cases) and bool(comparisons) and passed_comparisons == len(comparisons)
    return {
        "passed": passed,
        "executables": {
            "c": {"sha256": state["hashes"]["c_exe"], "wrapper_dll_sha256": state["deployed"]["c"]},
            "cpp": {"sha256": state["hashes"]["cpp_exe"], "wrapper_dll_sha256": state["deployed"]["cpp"]},
        },
        "specified_dll_sha256": state["hashes"]["dll"],
        "fixtures": _fixtures(config, paths["model_root"]) if config is not None else {},
        "summary": {"case_total": len(cases), "case_passed": passed_cases, "case_failed": len(cases) - passed_cases, "comparison_total": len(comparisons), "comparison_passed": passed_comparisons, "comparison_failed": len(comparisons) - passed_comparisons},
        "cases": cases, "cross_demo_comparisons": comparisons, "errors": errors,
    }


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="执行 C 与 C++ Qt Demo 真实进程回归检查")
    for name in ("c-exe", "cpp-exe", "dll", "model-root", "output"):
        parser.add_argument(f"--{name}", required=True, type=Path)
    parser.add_argument("--timeout", type=float, default=120.0)
    parser.add_argument("--core-dll-directory", type=Path)
    return parser


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="strict")
    args = _parser().parse_args(argv)
    output_error = _report_path_error(args.output)
    if output_error:
        print(output_error, file=sys.stderr)
        return 2
    report = run_regression(args)
    output = args.output.expanduser().resolve(strict=False)
    try:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8", errors="strict")
    except (OSError, ValueError) as exc:
        print(f"回归报告写入失败：{type(exc).__name__}", file=sys.stderr)
        return 2
    print(output)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())