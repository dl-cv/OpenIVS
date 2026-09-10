#!/usr/bin/env python3
"""通过独立外部进程执行 OpenIVS Demo 命令行回归测试。"""

from __future__ import annotations

import argparse
import codecs
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable, Mapping, Sequence


SCHEMA_EXAMPLE = r'''{
  "configuration": "Release",
  "programs": {
    "cpp-demo": {
      "kind": "cpp_qt",
      "executable": {"Debug": "../Debug/cpp-demo.exe", "Release": "../Release/cpp-demo.exe"},
      "dlls": [{"name": "dlcv_infer_cpp.dll", "path": "../Release/dlcv_infer_cpp.dll"}]
    },
    "csharp-demo": {"kind": "csharp", "executable": "../Release/csharp-demo.exe", "dlls": []},
    "c-console": {
      "kind": "c_console",
      "executable": "../Release/c-console.exe",
      "dlls": [],
      "stdout_encoding": "utf-8"
    }
  },
  "models": {"sample-model": {"path": "inputs/model.dvst", "sha256": "optional-64-lowercase-hex"}},
  "images": {"sample-image": {"path": "inputs/image.png", "sha256": "optional-64-lowercase-hex"}},
  "cases": [
    {
      "name": "cpp ui test",
      "program": "cpp-demo",
      "command": "ui-test",
      "model": "sample-model",
      "image": "sample-image",
      "timeout_seconds": 120,
      "options": {"threshold": 0.5, "device": 0, "batch_size": 1, "calc_mean": false, "device_timeout_ms": 30000},
      "expect": {
        "json": {
          "success": true,
          "device_initialization_completed": true,
          "model_info_read": true,
          "inference_completed": true,
          "batch_size": 1,
          "render_completed": true,
          "release_completed": true,
          "first_result_count": 1,
          "categories": ["示例类别"]
        },
        "result_baseline": {"count": 1, "categories": ["示例类别"]}
      }
    },
    {
      "name": "c console infer",
      "program": "c-console",
      "command": "infer",
      "model": "sample-model",
      "image": "sample-image",
      "timeout_seconds": 120,
      "options": {"alias": "model", "device": 0, "threshold": 0.5, "with_mask": true},
      "expect": {
        "stdout_contains": ["推理成功", "模型已释放"],
        "result_baseline": {"count": 1, "categories": ["示例类别"], "count_text": "目标数=1"}
      }
    }
  ]
}'''

SUPPORTED_KINDS = {"cpp_qt", "csharp", "c_console"}
JSON_COMMANDS = {("cpp_qt", "infer"), ("cpp_qt", "ui-test"), ("csharp", "infer"), ("csharp", "ui-test")}
SIGNAL_KEYS = {"consistent", "inspection_consistent", "threshold_check_passed", "mean_check_passed", "ok", "success", "status"}
SHA256_PATTERN = re.compile(r"^[0-9a-f]{64}$")
SAFE_NAME_PATTERN = re.compile(r"[^A-Za-z0-9._-]+")


class ConfigError(ValueError):
    """外部 JSON 配置不符合执行要求。"""


@dataclass(frozen=True)
class FileRecord:
    logical_name: str
    path: Path
    file_name: str
    size: int
    sha256: str

    def report(self) -> dict[str, Any]:
        return {"logical_name": self.logical_name, "file_name": self.file_name, "size": self.size, "sha256": self.sha256}


@dataclass(frozen=True)
class ProgramRecord:
    logical_name: str
    kind: str
    executable: FileRecord
    dlls: tuple[FileRecord, ...]
    stdout_encoding: str

    def report(self) -> dict[str, Any]:
        return {
            "logical_name": self.logical_name,
            "kind": self.kind,
            "executable": self.executable.report(),
            "dlls": [item.report() for item in self.dlls],
            "stdout_encoding": self.stdout_encoding,
        }


@dataclass(frozen=True)
class ProcessResult:
    exit_code: int | None
    duration_ms: int
    timed_out: bool


@dataclass(frozen=True)
class CommandSpec:
    arguments: tuple[str, ...]
    json_output: Path | None
    artifacts: Mapping[str, Path]


@dataclass
class LoadedConfig:
    file_name: str
    configuration: str
    programs: dict[str, ProgramRecord]
    models: dict[str, FileRecord]
    images: dict[str, FileRecord]
    cases: list[dict[str, Any]]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _file_record(logical_name: str, path: Path) -> FileRecord:
    stat = path.stat()
    return FileRecord(logical_name, path, path.name, stat.st_size, _sha256(path))


def _read_utf8_json(path: Path) -> Any:
    try:
        raw = path.read_bytes()
    except OSError as exc:
        raise ConfigError(f"无法读取 JSON 文件 {path.name}：{exc.__class__.__name__}") from exc
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        raise ConfigError(f"JSON 文件 {path.name} 不是 UTF-8 编码") from exc
    try:
        return json.loads(text)
    except json.JSONDecodeError as exc:
        raise ConfigError(f"JSON 文件 {path.name} 内容无效，位置为第 {exc.lineno} 行、第 {exc.colno} 列") from exc


def _require_mapping(value: Any, label: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise ConfigError(f"{label} 必须是对象")
    return value


def _require_nonempty_string(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"{label} 必须是非空字符串")
    return value.strip()


def _resolve_path(base_dir: Path, value: Any, label: str) -> Path:
    path = Path(_require_nonempty_string(value, label)).expanduser()
    return (base_dir / path).resolve() if not path.is_absolute() else path.resolve()


def _select_configuration_path(value: Any, configuration: str, label: str) -> Any:
    if isinstance(value, Mapping):
        if configuration not in value:
            raise ConfigError(f"{label} 未定义 {configuration} 配置")
        return value[configuration]
    return value


def _validate_expected_hash(record: FileRecord, expected: Any, label: str) -> None:
    if expected is None:
        return
    expected_text = _require_nonempty_string(expected, f"{label}.sha256").lower()
    if not SHA256_PATTERN.fullmatch(expected_text):
        raise ConfigError(f"{label}.sha256 必须是 64 位十六进制字符串")
    if record.sha256 != expected_text:
        raise ConfigError(f"{label}.sha256 与 {record.file_name} 不一致")


def _load_named_files(config: Mapping[str, Any], key: str, base_dir: Path) -> dict[str, FileRecord]:
    declarations = _require_mapping(config.get(key), key)
    if not declarations:
        raise ConfigError(f"{key} 不能为空")
    result = {}
    for logical_name, declaration in declarations.items():
        name = _require_nonempty_string(logical_name, f"{key} logical name")
        if isinstance(declaration, str):
            path_value, expected_hash = declaration, None
        else:
            item = _require_mapping(declaration, f"{key}.{name}")
            path_value, expected_hash = item.get("path"), item.get("sha256")
        path = _resolve_path(base_dir, path_value, f"{key}.{name}.path")
        if not path.is_file():
            raise ConfigError(f"{key}.{name} 文件不存在：{path.name}")
        record = _file_record(name, path)
        _validate_expected_hash(record, expected_hash, f"{key}.{name}")
        result[name] = record
    return result


def _iter_dll_declarations(value: Any, program_name: str) -> Iterable[tuple[str, Any, Any]]:
    if value is None:
        return ()
    if isinstance(value, list):
        result = []
        for index, item_value in enumerate(value):
            item = _require_mapping(item_value, f"programs.{program_name}.dlls[{index}]")
            name = _require_nonempty_string(item.get("name"), f"programs.{program_name}.dlls[{index}].name")
            result.append((name, item.get("path"), item.get("sha256")))
        return result
    mapping = _require_mapping(value, f"programs.{program_name}.dlls")
    result = []
    for logical_name, item_value in mapping.items():
        name = _require_nonempty_string(logical_name, f"programs.{program_name}.dll logical name")
        if isinstance(item_value, str):
            result.append((name, item_value, None))
        else:
            item = _require_mapping(item_value, f"programs.{program_name}.dlls.{name}")
            result.append((name, item.get("path"), item.get("sha256")))
    return result


def _normalize_stdout_encoding(value: Any, label: str) -> str:
    name = _require_nonempty_string(value, label).lower()
    try:
        return codecs.lookup(name).name
    except LookupError as exc:
        raise ConfigError(f"{label} 不是 Python 支持的编码") from exc


def _load_programs(config: Mapping[str, Any], base_dir: Path, configuration: str) -> dict[str, ProgramRecord]:
    declarations = _require_mapping(config.get("programs"), "programs")
    if not declarations:
        raise ConfigError("programs 不能为空")
    result = {}
    for logical_name, declaration in declarations.items():
        name = _require_nonempty_string(logical_name, "program logical name")
        item = _require_mapping(declaration, f"programs.{name}")
        kind = _require_nonempty_string(item.get("kind"), f"programs.{name}.kind")
        if kind not in SUPPORTED_KINDS:
            raise ConfigError(f"programs.{name}.kind 不受支持")
        executable_value = _select_configuration_path(item.get("executable", item.get("path")), configuration, f"programs.{name}.executable")
        executable_path = _resolve_path(base_dir, executable_value, f"programs.{name}.executable")
        if not executable_path.is_file():
            raise ConfigError(f"programs.{name} 可执行文件不存在：{executable_path.name}")
        executable = _file_record(name, executable_path)
        _validate_expected_hash(executable, item.get("sha256"), f"programs.{name}")
        dlls = []
        for dll_name, dll_path_value, dll_hash in _iter_dll_declarations(item.get("dlls"), name):
            selected = _select_configuration_path(dll_path_value, configuration, f"programs.{name}.dlls.{dll_name}.path")
            dll_path = _resolve_path(base_dir, selected, f"programs.{name}.dlls.{dll_name}.path")
            if not dll_path.is_file():
                raise ConfigError(f"programs.{name} DLL 不存在：{dll_path.name}")
            dll_record = _file_record(dll_name, dll_path)
            _validate_expected_hash(dll_record, dll_hash, f"programs.{name}.dlls.{dll_name}")
            dlls.append(dll_record)
        encoding_value = item.get("stdout_encoding")
        if kind == "c_console":
            if encoding_value is None:
                raise ConfigError(f"programs.{name}.stdout_encoding 为 C 控制台必填参数")
            stdout_encoding = _normalize_stdout_encoding(
                encoding_value, f"programs.{name}.stdout_encoding")
        else:
            if encoding_value is not None:
                raise ConfigError(f"programs.{name}.stdout_encoding 仅用于 C 控制台")
            stdout_encoding = "utf-8"
        result[name] = ProgramRecord(name, kind, executable, tuple(dlls), stdout_encoding)
    return result


def _normalize_required_text(value: Any, label: str) -> list[str]:
    values = [value] if isinstance(value, str) else value
    if not isinstance(values, list) or not values:
        raise ConfigError(f"{label} 必须是非空字符串 or array")
    return [_require_nonempty_string(item, f"{label}[{index}]") for index, item in enumerate(values)]


def _validate_case_shape(case: Mapping[str, Any], index: int) -> None:
    prefix = f"cases[{index}]"
    allowed_case_keys = {"name", "program", "command", "model", "image", "timeout_seconds", "options", "expect"}
    if set(case) - allowed_case_keys:
        raise ConfigError(f"{prefix} 包含不支持的字段")
    for key in ("name", "program", "command", "model", "image"):
        _require_nonempty_string(case.get(key), f"{prefix}.{key}")
    timeout = case.get("timeout_seconds")
    if isinstance(timeout, bool) or not isinstance(timeout, (int, float)) or timeout <= 0:
        raise ConfigError(f"{prefix}.timeout_seconds 必须为正数")
    expect = _require_mapping(case.get("expect"), f"{prefix}.expect")
    if not expect:
        raise ConfigError(f"{prefix}.expect 不能为空")
    if set(expect) - {"json", "stdout_contains", "artifact", "result_baseline"}:
        raise ConfigError(f"{prefix}.expect 包含不支持的字段")
    baseline = _require_mapping(expect.get("result_baseline"), f"{prefix}.expect.result_baseline")
    if set(baseline) - {"count", "categories", "count_text"}:
        raise ConfigError(f"{prefix}.expect.result_baseline 包含不支持的字段")
    count = _integer(baseline.get("count"), f"{prefix}.expect.result_baseline.count", 1)
    categories = _normalize_required_text(
        baseline.get("categories"), f"{prefix}.expect.result_baseline.categories")
    if not categories:
        raise ConfigError(f"{prefix}.expect.result_baseline.categories 不能为空")
    if "count_text" in baseline:
        count_text = _require_nonempty_string(
            baseline["count_text"], f"{prefix}.expect.result_baseline.count_text")
        if str(count) not in count_text:
            raise ConfigError(f"{prefix}.expect.result_baseline.count_text 必须包含 count 数值")
    if "json" in expect:
        expected_json = expect["json"]
        if not isinstance(expected_json, (Mapping, list)) or not expected_json:
            raise ConfigError(f"{prefix}.expect.json 必须是非空对象或数组")
    if "stdout_contains" in expect:
        _normalize_required_text(expect["stdout_contains"], f"{prefix}.expect.stdout_contains")
    if "artifact" in expect:
        artifact = _require_mapping(expect["artifact"], f"{prefix}.expect.artifact")
        if not artifact or set(artifact) - {"min_size", "sha256"}:
            raise ConfigError(f"{prefix}.expect.artifact 必须包含 min_size 或 sha256")


def load_config(config_path: Path) -> LoadedConfig:
    config_path = config_path.resolve()
    config = _require_mapping(_read_utf8_json(config_path), "configuration root")
    configuration = _require_nonempty_string(config.get("configuration"), "configuration")
    if configuration not in {"Debug", "Release"}:
        raise ConfigError("configuration 必须是 Debug 或 Release")
    raw_cases = config.get("cases")
    if not isinstance(raw_cases, list) or not raw_cases:
        raise ConfigError("cases 必须是非空数组")
    cases = []
    for index, value in enumerate(raw_cases):
        case = dict(_require_mapping(value, f"cases[{index}]"))
        _validate_case_shape(case, index)
        cases.append(case)
    return LoadedConfig(
        file_name=config_path.name,
        configuration=configuration,
        programs=_load_programs(config, config_path.parent, configuration),
        models=_load_named_files(config, "models", config_path.parent),
        images=_load_named_files(config, "images", config_path.parent),
        cases=cases,
    )


def _option(options: Mapping[str, Any], name: str) -> Any:
    return options.get(name, options.get(name.replace("_", "-")))


def _bool_text(value: Any, label: str) -> str:
    if not isinstance(value, bool):
        raise ConfigError(f"{label} 必须是 true 或 false")
    return "true" if value else "false"


def _number_text(value: Any, label: str, minimum: float, maximum: float) -> str:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ConfigError(f"{label} 必须是数字")
    numeric = float(value)
    if not minimum <= numeric <= maximum:
        raise ConfigError(f"{label} 超出支持范围")
    return format(value, ".15g") if isinstance(value, float) else str(value)


def _integer(value: Any, label: str, minimum: int | None = None, maximum: int | None = None) -> int:
    if isinstance(value, bool) or not isinstance(value, int):
        raise ConfigError(f"{label} 必须是整数")
    if minimum is not None and value < minimum:
        raise ConfigError(f"{label} 小于支持范围")
    if maximum is not None and value > maximum:
        raise ConfigError(f"{label} 大于支持范围")
    return value


def _reject_unknown_options(options: Mapping[str, Any], allowed: set[str]) -> None:
    unknown = {str(key).replace("-", "_") for key in options} - allowed
    if unknown:
        raise ConfigError(f"case.options 包含不支持的参数：{', '.join(sorted(unknown))}")


def _append_infer_options(arguments: list[str], options: Mapping[str, Any], allow_calc_mean: bool = True) -> None:
    if _option(options, "device") is not None:
        arguments.extend(["--device", str(_integer(_option(options, "device"), "options.device", -2))])
    if _option(options, "with_mask") is not None:
        arguments.extend(["--with-mask", _bool_text(_option(options, "with_mask"), "options.with_mask")])
    if allow_calc_mean and _option(options, "calc_mean") is not None:
        arguments.extend(["--calc-mean", _bool_text(_option(options, "calc_mean"), "options.calc_mean")])


def build_command(program: ProgramRecord, case: Mapping[str, Any], model: FileRecord, image: FileRecord, case_dir: Path) -> CommandSpec:
    command = _require_nonempty_string(case.get("command"), "case.command").lower()
    options = _require_mapping(case.get("options", {}), "case.options")
    arguments = [str(program.executable.path)]
    json_output = None
    artifacts: dict[str, Path] = {}
    if program.kind == "cpp_qt":
        if command in {"infer", "render"}:
            allowed = {"threshold", "device", "with_mask", "calc_mean"}
            if command == "render":
                allowed.remove("calc_mean")
            _reject_unknown_options(options, allowed)
            threshold = _number_text(_option(options, "threshold"), "options.threshold", 0.0, 1.0)
            arguments.extend([command, "--model", str(model.path), "--image", str(image.path), "--threshold", threshold])
            if command == "infer":
                json_output = case_dir / "result.json"
                arguments.extend(["--output", str(json_output)])
                _append_infer_options(arguments, options)
            else:
                artifacts["artifact_output"] = case_dir / "artifact.png"
                arguments.extend(["--output", str(artifacts["artifact_output"])])
                _append_infer_options(arguments, options, False)
        elif command == "ui-test":
            _reject_unknown_options(options, {"threshold", "device", "batch_size", "calc_mean", "device_timeout_ms"})
            threshold = _number_text(_option(options, "threshold"), "options.threshold", 0.0, 1.0)
            json_output = case_dir / "result.json"
            arguments.extend(["ui-test", "--model", str(model.path), "--image", str(image.path), "--threshold", threshold, "--output", str(json_output)])
            if _option(options, "device") is not None:
                arguments.extend(["--device", str(_integer(_option(options, "device"), "options.device", -2))])
            if _option(options, "batch_size") is not None:
                arguments.extend(["--batch-size", str(_integer(_option(options, "batch_size"), "options.batch_size", 1, 1024))])
            if _option(options, "calc_mean") is not None:
                arguments.extend(["--calc-mean", _bool_text(_option(options, "calc_mean"), "options.calc_mean")])
            if _option(options, "device_timeout_ms") is not None:
                arguments.extend(["--device-timeout-ms", str(_integer(_option(options, "device_timeout_ms"), "options.device_timeout_ms", 1))])
        else:
            raise ConfigError("cpp_qt 命令必须是 infer、render 或 ui-test")
    elif program.kind == "csharp":
        if command == "infer":
            _reject_unknown_options(options, {"threshold", "device", "with_mask", "calc_mean"})
            threshold = _number_text(_option(options, "threshold"), "options.threshold", 0.0, 1.0)
            json_output = case_dir / "result.json"
            arguments.extend(["infer", "--model", str(model.path), "--image", str(image.path), "--threshold", threshold, "--output", str(json_output)])
            _append_infer_options(arguments, options)
        elif command == "ui-test":
            _reject_unknown_options(options, {"threshold", "device", "calc_mean", "interactive_dialogs", "screenshot"})
            json_output = case_dir / "result.json"
            arguments.extend(["ui-test", "--model", str(model.path), "--image", str(image.path), "--output", str(json_output)])
            if _option(options, "threshold") is not None:
                arguments.extend(["--threshold", _number_text(_option(options, "threshold"), "options.threshold", 0.0, 1.0)])
            if _option(options, "device") is not None:
                arguments.extend(["--device", str(_integer(_option(options, "device"), "options.device", -2))])
            if _option(options, "calc_mean") is not None:
                arguments.extend(["--calc-mean", _bool_text(_option(options, "calc_mean"), "options.calc_mean")])
            interactive = False if _option(options, "interactive_dialogs") is None else _option(options, "interactive_dialogs")
            arguments.extend(["--interactive-dialogs", _bool_text(interactive, "options.interactive_dialogs")])
            if _option(options, "screenshot") is not None:
                if _option(options, "screenshot") is not True:
                    raise ConfigError("填写 options.screenshot 时值必须为 true")
                artifacts["artifact_output"] = case_dir / "artifact.png"
                arguments.extend(["--screenshot", str(artifacts["artifact_output"])])
        else:
            raise ConfigError("csharp 命令必须是 infer 或 ui-test")
    else:
        if command != "infer":
            raise ConfigError("c_console 命令必须是 infer")
        _reject_unknown_options(options, {"alias", "device", "threshold", "with_mask", "calc_mean"})
        alias_value = _option(options, "alias")
        alias = "model" if alias_value is None else _require_nonempty_string(alias_value, "options.alias")
        arguments.extend(["load-model", alias, str(model.path)])
        if _option(options, "device") is not None:
            arguments.extend(["--device", str(_integer(_option(options, "device"), "options.device", -2))])
        arguments.extend(["--then", "infer", alias, str(image.path)])
        if _option(options, "threshold") is not None:
            arguments.extend(["--threshold", _number_text(_option(options, "threshold"), "options.threshold", 0.0, 1.0)])
        if _option(options, "with_mask") is not None:
            arguments.extend(["--with-mask", _bool_text(_option(options, "with_mask"), "options.with_mask")])
        if _option(options, "calc_mean") is not None:
            arguments.extend(["--calc-mean", _bool_text(_option(options, "calc_mean"), "options.calc_mean")])
        arguments.extend(["--then", "free-model", alias])

    if (program.kind, command) in JSON_COMMANDS:
        expected_json = _require_mapping(case["expect"], "case.expect").get("json")
        if not isinstance(expected_json, (Mapping, list)) or not expected_json:
            raise ConfigError("生成 JSON 的命令必须填写非空 expect.json")
    if program.kind == "cpp_qt" and command == "ui-test":
        expected_json = _require_mapping(case["expect"], "case.expect")["json"]
        if not isinstance(expected_json, Mapping) or "success" not in expected_json:
            raise ConfigError("cpp_qt ui-test 的 expect.json 必须明确包含 success")
    expect = _require_mapping(case["expect"], "case.expect")
    if artifacts and "artifact" not in expect:
        raise ConfigError("生成文件的命令必须填写 expect.artifact")
    if "artifact" in expect and not artifacts:
        raise ConfigError("当前命令不支持 expect.artifact")
    return CommandSpec(tuple(arguments), json_output, artifacts)


def _execute_process(arguments: Sequence[str], stdout_path: Path, stderr_path: Path, timeout_seconds: float) -> ProcessResult:
    started = time.perf_counter()
    creationflags = (getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0) | getattr(subprocess, "CREATE_NO_WINDOW", 0)) if os.name == "nt" else 0
    with stdout_path.open("wb") as stdout_stream, stderr_path.open("wb") as stderr_stream:
        process = subprocess.Popen(list(arguments), stdin=subprocess.DEVNULL, stdout=stdout_stream, stderr=stderr_stream, creationflags=creationflags)
        try:
            exit_code = process.wait(timeout=timeout_seconds)
            timed_out = False
        except subprocess.TimeoutExpired:
            timed_out = True
            process.kill()
            exit_code = None
            process.wait()
    return ProcessResult(exit_code, max(0, round((time.perf_counter() - started) * 1000)), timed_out)


def _load_fresh_json(path: Path) -> Any:
    if not path.is_file():
        raise ConfigError("声明的 JSON 输出文件未生成")
    if path.stat().st_size <= 0:
        raise ConfigError("声明的 JSON 输出文件为空")
    try:
        text = path.read_bytes().decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        raise ConfigError("声明的 JSON 输出文件不是 UTF-8 编码") from exc
    try:
        return json.loads(text)
    except json.JSONDecodeError as exc:
        raise ConfigError("声明的 JSON 输出文件不是有效 JSON") from exc


def _compare_json_subset(expected: Any, actual: Any, location: str = "$") -> list[str]:
    if isinstance(expected, Mapping):
        if not isinstance(actual, Mapping):
            return [f"{location} 的 JSON 类型不一致"]
        errors = []
        for key, expected_value in expected.items():
            next_location = f"{location}.{key}"
            if key not in actual:
                errors.append(f"{next_location} 缺失")
            else:
                errors.extend(_compare_json_subset(expected_value, actual[key], next_location))
        return errors
    if isinstance(expected, list):
        if not isinstance(actual, list):
            return [f"{location} 的 JSON 类型不一致"]
        if len(expected) > len(actual):
            return [f"{location} 的数组数量少于预期"]
        errors = []
        for index, expected_value in enumerate(expected):
            errors.extend(_compare_json_subset(expected_value, actual[index], f"{location}[{index}]"))
        return errors
    return [] if type(expected) is type(actual) and expected == actual else [f"{location} 与预期不一致"]


def _walk_signal_values(value: Any) -> Iterable[tuple[str, Any]]:
    if isinstance(value, Mapping):
        for key, child in value.items():
            normalized = str(key).lower()
            if normalized in SIGNAL_KEYS:
                yield normalized, child
            yield from _walk_signal_values(child)
    elif isinstance(value, list):
        for child in value:
            yield from _walk_signal_values(child)


def _signal_state(value: Any) -> bool | None:
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        normalized = value.strip().lower()
        if normalized in {"ok", "success", "successful", "pass", "passed", "true"}:
            return True
        if normalized in {"fail", "failed", "failure", "error", "false", "unsuccessful"}:
            return False
    return None


def _signal_conflicts(expected: Any, actual: Any) -> list[str]:
    expected_states: dict[str, set[bool]] = {}
    positive_intent = False
    for key, value in _walk_signal_values(expected):
        state = _signal_state(value)
        if state is not None:
            expected_states.setdefault(key, set()).add(state)
            if key in {"ok", "success", "status"} and state:
                positive_intent = True
    errors = []
    for key, value in _walk_signal_values(actual):
        state = _signal_state(value)
        if state is None:
            continue
        if key in expected_states and state not in expected_states[key]:
            errors.append(f"实际 JSON 状态字段 {key} 与 expect.json 冲突")
        elif state is False:
            errors.append(f"实际 JSON 状态字段 {key} 表示失败")
    return errors


def _decode_stdout_strict(raw: bytes, encoding: str) -> tuple[str | None, str | None]:
    try:
        return raw.decode(encoding, errors="strict"), None
    except UnicodeDecodeError as exc:
        message = (
            f"标准输出无法按 {encoding} 严格解码："
            f"字节偏移 {exc.start}-{exc.end}")
        return None, message


def _json_result_summary(actual: Any) -> tuple[int, list[str]] | None:
    if not isinstance(actual, Mapping):
        return None
    candidates = []
    structured = actual.get("structured")
    if isinstance(structured, Mapping):
        candidates.append((structured.get("count"), structured.get("categories")))
    candidates.extend([
        (actual.get("first_result_count"), actual.get("categories")),
        (actual.get("count"), actual.get("categories")),
    ])
    for count, categories in candidates:
        if isinstance(count, int) and not isinstance(count, bool) and isinstance(categories, list):
            if all(isinstance(item, str) for item in categories):
                return count, list(categories)
    return None


def _validate_result_baseline(
    declaration: Any,
    actual_json: Any | None,
    stdout_text: str | None,
) -> list[str]:
    item = _require_mapping(declaration, "expect.result_baseline")
    expected_count = _integer(item.get("count"), "expect.result_baseline.count", 1)
    expected_categories = _normalize_required_text(
        item.get("categories"), "expect.result_baseline.categories")
    summary = _json_result_summary(actual_json)
    if summary is not None:
        actual_count, actual_categories = summary
        errors = []
        if actual_count != expected_count:
            errors.append("结果数量与基准不一致")
        if actual_categories != expected_categories:
            errors.append("结果类别与基准不一致")
        return errors

    result_text = actual_json.get("result_text") if isinstance(actual_json, Mapping) else None
    count_text = item.get("count_text")
    if not isinstance(count_text, str) or not count_text:
        return ["当前输出没有结构化数量和类别，result_baseline.count_text 必须明确填写"]
    if isinstance(result_text, str):
        errors = []
        if count_text not in result_text:
            errors.append("结果文本不包含数量基准")
        for category in expected_categories:
            if category not in result_text:
                errors.append(f"结果文本不包含类别基准：{category}")
        return errors

    if stdout_text is None:
        return ["标准输出编码校验失败，无法核对数量和类别基准"]
    errors = []
    if count_text not in stdout_text:
        errors.append("标准输出不包含数量基准")
    for category in expected_categories:
        if category not in stdout_text:
            errors.append(f"标准输出不包含类别基准：{category}")
    return errors


def _validate_artifact(path: Path, declaration: Any) -> list[str]:
    item = _require_mapping(declaration, "expect.artifact")
    if not path.is_file():
        return ["预期输出文件未生成"]
    errors = []
    if "min_size" in item:
        minimum = _integer(item["min_size"], "expect.artifact.min_size", 0)
        if path.stat().st_size < minimum:
            errors.append("输出文件小于预期大小")
    if "sha256" in item:
        expected_hash = _require_nonempty_string(item["sha256"], "expect.artifact.sha256").lower()
        if not SHA256_PATTERN.fullmatch(expected_hash):
            raise ConfigError("expect.artifact.sha256 必须是 64 位十六进制字符串")
        if _sha256(path) != expected_hash:
            errors.append("输出文件 sha256 与预期不一致")
    return errors


def _safe_case_directory_name(index: int, name: str) -> str:
    safe = SAFE_NAME_PATTERN.sub("-", name).strip("-._") or "case"
    return f"{index + 1:03d}-{safe[:64]}"


def _output_record(logical_name: str, path: Path) -> dict[str, Any]:
    return _file_record(logical_name, path).report()


def _normalize_exit_code(exit_code: int | None) -> int | None:
    return None if exit_code is None else exit_code & 0xFFFFFFFF


def _exit_code_hex(exit_code: int | None) -> str | None:
    normalized = _normalize_exit_code(exit_code)
    return None if normalized is None else f"0x{normalized:08X}"


def _run_case(
    loaded: LoadedConfig,
    case: Mapping[str, Any],
    index: int,
    run_dir: Path,
    process_runner: Callable[[Sequence[str], Path, Path, float], ProcessResult] = _execute_process,
) -> dict[str, Any]:
    name = _require_nonempty_string(case.get("name"), f"cases[{index}].name")
    program_name = _require_nonempty_string(case.get("program"), f"cases[{index}].program")
    model_name = _require_nonempty_string(case.get("model"), f"cases[{index}].model")
    image_name = _require_nonempty_string(case.get("image"), f"cases[{index}].image")
    program = loaded.programs.get(program_name)
    model = loaded.models.get(model_name)
    image = loaded.images.get(image_name)
    if program is None or model is None or image is None:
        raise ConfigError(f"用例 {name} 引用了未知逻辑名称")

    case_dir = run_dir / _safe_case_directory_name(index, name)
    case_dir.mkdir(parents=False, exist_ok=False)
    stdout_path, stderr_path = case_dir / "stdout.txt", case_dir / "stderr.txt"
    spec = build_command(program, case, model, image, case_dir)
    for path in ([spec.json_output] if spec.json_output is not None else []) + list(spec.artifacts.values()):
        try:
            path.unlink()
        except FileNotFoundError:
            pass

    result = process_runner(spec.arguments, stdout_path, stderr_path, float(case["timeout_seconds"]))
    normalized_exit_code = _normalize_exit_code(result.exit_code)
    errors: list[str] = []
    if result.timed_out:
        errors.append("进程执行超时")
    elif normalized_exit_code != 0:
        errors.append(f"进程退出码为 {_exit_code_hex(result.exit_code)}")

    expect = _require_mapping(case["expect"], "case.expect")
    actual_json = None
    if spec.json_output is not None:
        try:
            actual_json = _load_fresh_json(spec.json_output)
        except ConfigError as exc:
            errors.append(str(exc))
        else:
            errors.extend(_compare_json_subset(expect["json"], actual_json))
            errors.extend(_signal_conflicts(expect["json"], actual_json))

    stdout_bytes = stdout_path.read_bytes() if stdout_path.is_file() else b""
    check_stdout_encoding = program.kind == "c_console" or "stdout_contains" in expect
    stdout_text = None
    stdout_encoding_error = None
    if check_stdout_encoding:
        stdout_text, stdout_encoding_error = _decode_stdout_strict(
            stdout_bytes, program.stdout_encoding)
        if stdout_encoding_error is not None:
            errors.append(stdout_encoding_error)

    if "stdout_contains" in expect and stdout_text is not None:
        for required_text in _normalize_required_text(expect["stdout_contains"], "expect.stdout_contains"):
            if required_text not in stdout_text:
                errors.append(f"标准输出不包含必需文本：{required_text}")

    errors.extend(_validate_result_baseline(
        expect["result_baseline"], actual_json, stdout_text))

    if "artifact" in expect:
        artifact_path = spec.artifacts.get("artifact_output")
        if artifact_path is None:
            errors.append("当前命令不生成输出文件")
        else:
            errors.extend(_validate_artifact(artifact_path, expect["artifact"]))

    outputs = []
    for logical_name, path in (("stdout", stdout_path), ("stderr", stderr_path)):
        if path.is_file():
            outputs.append(_output_record(logical_name, path))
    if spec.json_output is not None and spec.json_output.is_file():
        outputs.append(_output_record("json_output", spec.json_output))
    for logical_name, path in spec.artifacts.items():
        if path.is_file():
            outputs.append(_output_record(logical_name, path))

    return {
        "name": name,
        "program": program_name,
        "program_kind": program.kind,
        "program_binary": program.report(),
        "command": case["command"],
        "configuration": loaded.configuration,
        "model": model.report(),
        "image": image.report(),
        "duration_ms": result.duration_ms,
        "exit_code": normalized_exit_code,
        "exit_code_hex": _exit_code_hex(result.exit_code),
        "timed_out": result.timed_out,
        "stdout_encoding": {
            "encoding": program.stdout_encoding,
            "checked": check_stdout_encoding,
            "valid": None if not check_stdout_encoding else stdout_encoding_error is None,
        },
        "passed": not errors,
        "errors": errors,
        "outputs": outputs,
    }


def run_config(
    config_path: Path,
    *,
    temp_base: Path | None = None,
    process_runner: Callable[[Sequence[str], Path, Path, float], ProcessResult] = _execute_process,
) -> tuple[Path, dict[str, Any]]:
    loaded = load_config(config_path)
    if temp_base is not None:
        requested_base = temp_base.resolve()
        system_temp = Path(tempfile.gettempdir()).resolve()
        try:
            inside_system_temp = os.path.commonpath([str(requested_base), str(system_temp)]) == str(system_temp)
        except ValueError:
            inside_system_temp = False
        if not inside_system_temp:
            raise ConfigError("回归测试输出必须位于系统临时目录")
    run_dir = Path(tempfile.mkdtemp(prefix="openivs_demo_regression_", dir=str(temp_base.resolve()) if temp_base else None))
    case_reports = []
    for index, case in enumerate(loaded.cases):
        try:
            case_reports.append(_run_case(loaded, case, index, run_dir, process_runner))
        except ConfigError as exc:
            case_reports.append({
                "name": str(case.get("name", f"case-{index + 1}")),
                "program": str(case.get("program", "")),
                "command": str(case.get("command", "")),
                "configuration": loaded.configuration,
                "duration_ms": 0,
                "exit_code": None,
                "exit_code_hex": None,
                "timed_out": False,
                "passed": False,
                "errors": [str(exc)],
                "outputs": [],
            })
    report = {
        "schema_version": 1,
        "config_file": loaded.file_name,
        "configuration": loaded.configuration,
        "programs": [item.report() for item in loaded.programs.values()],
        "models": [item.report() for item in loaded.models.values()],
        "images": [item.report() for item in loaded.images.values()],
        "passed": all(item["passed"] for item in case_reports),
        "cases": case_reports,
    }
    report_path = run_dir / "report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report_path, report


def _configure_cli_encoding() -> None:
    """将执行器自身的帮助和错误输出固定为严格 UTF-8。"""
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if callable(reconfigure):
            reconfigure(encoding="utf-8", errors="strict")


def _build_parser() -> argparse.ArgumentParser:
    notes = (
        "配置要求：\n"
        "  1. 每个用例必须填写 expect.result_baseline，count 必须大于零，"
        "categories 必须为非空类别数组。\n"
        "  2. 结构化 JSON 会直接核对 count/categories；仅有文本结果时还必须填写 count_text。\n"
        "  3. C 控制台程序必须填写 stdout_encoding。完整标准输出按该编码严格解码，"
        "出现混合编码或非法字节时记录编码失败，不使用替换字符。\n\n"
        "JSON 配置示例：\n" + SCHEMA_EXAMPLE
    )
    parser = argparse.ArgumentParser(
        description="依据外部 UTF-8 JSON 配置，以独立进程执行 Demo 回归测试。",
        epilog=notes,
        formatter_class=argparse.RawDescriptionHelpFormatter,
        add_help=False,
    )
    parser.add_argument("-h", "--help", action="help", help="显示帮助后退出")
    parser.add_argument("config", nargs="?", type=Path, help="UTF-8 JSON 配置文件")
    parser.add_argument("--schema-example", action="store_true", help="输出内嵌 JSON 配置示例后退出")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    _configure_cli_encoding()
    parser = _build_parser()
    args = parser.parse_args(argv)
    if args.schema_example:
        print(SCHEMA_EXAMPLE)
        return 0
    if args.config is None:
        parser.error("除 --schema-example 外必须提供配置文件")
    try:
        report_path, report = run_config(args.config)
    except ConfigError as exc:
        parser.exit(2, f"配置错误：{exc}\n")
    print(str(report_path))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
