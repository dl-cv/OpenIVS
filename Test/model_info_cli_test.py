"""验证模型信息命令行接口的普通模型与流程模型行为。"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any, Callable, Iterable


JsonValue = Any
JsonValidator = Callable[[JsonValue], bool]


def _configure_output_streams() -> None:
    """统一终端输出编码，并在终端编码不兼容时替换异常字符。"""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except AttributeError:
            continue


def _is_int(value: JsonValue) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _shape_from_value(value: JsonValue) -> tuple[int, int, int, int] | None:
    if isinstance(value, list) and len(value) == 4 and all(
        _is_int(item) and item > 0 for item in value
    ):
        return tuple(value)  # type: ignore[return-value]
    if not isinstance(value, dict):
        return None
    for key in ("max_shape", "input", "input_shapes"):
        if key in value:
            shape = _shape_from_value(value[key])
            if shape is not None:
                return shape
    return None


def _shape_source(info: dict[str, JsonValue]) -> JsonValue | None:
    inner = info.get("model_info")
    candidates = [info.get("input_shapes")]
    if isinstance(inner, dict):
        candidates.append(inner.get("input_shapes"))
        nested = inner.get("model_info")
        if isinstance(nested, dict):
            candidates.append(nested.get("input_shapes"))
    for candidate in candidates:
        if candidate is not None and _shape_from_value(candidate) is not None:
            return candidate
    return None


def _shape_from_info(info: dict[str, JsonValue]) -> tuple[int, int, int, int]:
    source = _shape_source(info)
    shape = _shape_from_value(source)
    if shape is None:
        raise AssertionError("模型信息缺少有效的四维 input_shapes")
    return shape


def _validate_core(info: JsonValue) -> tuple[dict[str, JsonValue], tuple[int, int, int, int]]:
    if not isinstance(info, dict):
        raise AssertionError("兼容模型信息不是对象")
    inner = _inner_model_info(info)
    channels = inner.get("in_channels")
    task_type = inner.get("task_type")
    classes = inner.get("classes")
    num_classes = inner.get("num_classes")
    if not _is_int(channels) or channels <= 0:
        raise AssertionError("in_channels 必须是正整数")
    if not isinstance(task_type, str) or not task_type:
        raise AssertionError("task_type 必须是非空字符串")
    if not isinstance(classes, list):
        raise AssertionError("classes 必须是数组")
    if not _is_int(num_classes) or num_classes < 0 or num_classes != len(classes):
        raise AssertionError("num_classes 必须是与 classes 数量一致的非负整数")
    return inner, _shape_from_info(info)


def _json_candidates(output: str) -> Iterable[JsonValue]:
    """从含日志的文本中提取所有可独立解析的 JSON 值。"""
    decoder = json.JSONDecoder()
    for index, character in enumerate(output):
        if character not in "[{":
            continue
        try:
            value, _ = decoder.raw_decode(output[index:])
        except json.JSONDecodeError:
            continue
        yield value


def _find_json(output: str, validator: JsonValidator) -> JsonValue:
    """从输出中选择最后一个符合指定结构的 JSON 值。"""
    candidates = list(_json_candidates(output))
    for value in reversed(candidates):
        if validator(value):
            return value
    raise ValueError("输出中未找到符合结构的 JSON")


def _inner_model_info(info: JsonValue) -> dict[str, JsonValue]:
    if not isinstance(info, dict):
        raise ValueError("模型信息不是对象")
    inner = info.get("model_info")
    if not isinstance(inner, dict):
        raise ValueError("模型信息缺少对象类型的 model_info")
    return inner


def _validate_compatible(info: JsonValue) -> bool:
    """检查普通模型兼容信息的字段结构和内容类型。"""
    try:
        _validate_core(info)
    except (AssertionError, ValueError):
        return False
    return True


def _run_command(runner: Path, command: str, model: Path) -> tuple[int, str]:
    args = [str(runner), command, str(model)]
    print(f"执行命令: {subprocess.list2cmdline(args)}")
    completed = subprocess.run(
        args,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    output = completed.stdout
    if completed.stdout:
        print(completed.stdout.rstrip())
    if completed.stderr:
        print(completed.stderr.rstrip(), file=sys.stderr)
    return completed.returncode, output


def _run_successful_info(runner: Path, command: str, model: Path) -> dict[str, JsonValue]:
    code, output = _run_command(runner, command, model)
    if code != 0:
        raise AssertionError(f"{command} 执行失败，返回码为 {code}")
    value = _find_json(output, _validate_compatible)
    return value


def _run_rejected_info(runner: Path, command: str, model: Path) -> None:
    code, _ = _run_command(runner, command, model)
    if code == 0:
        raise AssertionError(f"{command} 对普通模型应当失败")


def _successful_meta(detail: dict[str, JsonValue]) -> list[dict[str, JsonValue]]:
    loaded = detail.get("loaded_model_meta")
    if not isinstance(loaded, list) or not loaded:
        raise AssertionError("流程详细信息缺少非空的 loaded_model_meta")
    result = []
    for item in loaded:
        if not isinstance(item, dict) or item.get("status_code") != 0:
            continue
        if not _is_int(item.get("node_id")) or not _is_int(item.get("order")):
            raise AssertionError("成功加载的模型节点缺少有效的 node_id 或 order")
        if not isinstance(item.get("model_name"), str) or not item["model_name"]:
            raise AssertionError("成功加载的模型节点缺少有效的 model_name")
        if not isinstance(item.get("model_path_original"), str) or not item["model_path_original"]:
            raise AssertionError("成功加载的模型节点缺少有效的 model_path_original")
        if item.get("model_path") != item["model_path_original"]:
            raise AssertionError("模型节点的 model_path 与原始路径不一致")
        if not isinstance(item.get("model_info"), dict):
            raise AssertionError("成功加载的模型节点缺少对象类型的 model_info")
        _validate_core(item["model_info"])
        result.append(item)
    if not result:
        raise AssertionError("流程详细信息没有成功加载的模型节点")
    result.sort(key=lambda item: (item.get("order", sys.maxsize), item.get("node_id", sys.maxsize)))
    return result


def _validate_detail(value: JsonValue) -> bool:
    if not isinstance(value, dict):
        return False
    if not isinstance(value.get("nodes"), list) or not value["nodes"]:
        return False
    if not isinstance(value.get("loaded_model_meta"), list) or not value["loaded_model_meta"]:
        return False
    if not isinstance(value.get("model_info"), dict) or not value["model_info"]:
        return False
    if not _is_int(value.get("input_model_node_id")) or not _is_int(value.get("output_model_node_id")):
        return False
    node_ids = []
    for node in value["nodes"]:
        if not isinstance(node, dict) or not _is_int(node.get("id")):
            return False
        node_ids.append(node["id"])
    return len(node_ids) == len(set(node_ids))


def _check_flow_mapping(compatible: dict[str, JsonValue], detail: dict[str, JsonValue]) -> None:
    first, *rest = _successful_meta(detail)
    node_by_id = {node["id"]: node for node in detail["nodes"]}
    meta_ids = {item["node_id"] for item in [first, *rest]}
    if not meta_ids.issubset(node_by_id):
        raise AssertionError("loaded_model_meta 中存在未出现在 nodes 的节点引用")
    for node_id in meta_ids:
        node_type = node_by_id[node_id].get("type")
        if not isinstance(node_type, str) or not node_type.startswith("model/"):
            raise AssertionError("模型节点引用的 nodes 类型不是模型节点")
    if detail["input_model_node_id"] != first.get("node_id"):
        raise AssertionError("input_model_node_id 未对应首个成功加载的模型节点")
    output_id = detail["output_model_node_id"]
    output = next((item for item in rest + [first] if item.get("node_id") == output_id), None)
    if output is None:
        raise AssertionError("output_model_node_id 未对应成功加载的模型节点")

    compatible_inner, compatible_shape = _validate_core(compatible)
    first_root = first["model_info"]
    output_root = output["model_info"]
    first_inner = first_root.get("model_info", first_root)
    output_inner = output_root.get("model_info", output_root)
    if not isinstance(first_inner, dict) or not isinstance(output_inner, dict):
        raise AssertionError("首尾模型的普通模型信息结构不完整")

    first_shape = _shape_from_info(first_root)
    if compatible_shape != first_shape:
        raise AssertionError("兼容信息未使用首模型的 input_shapes")
    if compatible_inner.get("in_channels") != first_inner.get("in_channels"):
        raise AssertionError("兼容信息未使用首模型的 in_channels")
    for field in ("task_type", "classes", "num_classes"):
        if field not in output_inner or compatible_inner.get(field) != output_inner[field]:
            raise AssertionError(f"兼容信息未使用输出模型的 {field}")

    model_map = detail["model_info"]
    expected_keys = []
    used_keys = set()
    for item in [first, *rest]:
        name = item.get("model_name") or item.get("model_path_original")
        if not isinstance(name, str) or not name:
            raise AssertionError("模型节点缺少可用于映射的模型名称")
        base = name.replace("\\", "/").rsplit("/", 1)[-1]
        key = base
        if key in used_keys:
            key = f"{base}#{item['node_id']}"
            suffix = 2
            while key in used_keys:
                key = f"{base}#{suffix}"
                suffix += 1
        used_keys.add(key)
        expected_keys.append(key)
        if key not in model_map or model_map[key] != item["model_info"]:
            raise AssertionError(f"model_info 缺少与模型节点对应的映射: {key}")
    if set(model_map) != set(expected_keys):
        raise AssertionError("model_info 映射与 loaded_model_meta 数量或名称不一致")


def run(args: argparse.Namespace) -> int:
    runner = Path(args.runner)
    dvt = Path(args.dvt)
    dvst = Path(args.dvst)
    try:
        ordinary = _run_successful_info(runner, "get-model-info", dvt)
        print("验证结论: 普通模型 GetModelInfo 成功，兼容结构正确")
        _run_rejected_info(runner, "get-dvs-model-info", dvt)
        print("验证结论: 普通模型 GetDvsModelInfo 按预期失败")
        flow_compatible = _run_successful_info(runner, "get-model-info", dvst)
        ordinary_inner, ordinary_shape = _validate_core(ordinary)
        flow_inner, flow_shape = _validate_core(flow_compatible)
        for field in ("in_channels", "task_type", "classes", "num_classes"):
            if type(ordinary_inner[field]) is not type(flow_inner[field]):
                raise AssertionError(f"普通模型与流程模型的 {field} 字段类型不一致")
        if type(_shape_source(ordinary)) is not type(_shape_source(flow_compatible)):
            raise AssertionError("普通模型与流程模型的 input_shapes 类型不一致")
        if len(ordinary_shape) != 4 or len(flow_shape) != 4:
            raise AssertionError("普通模型或流程模型的 input_shapes 不是四维形状")
        for field in ("model_info", "input_shapes"):
            if field not in ordinary or field not in flow_compatible:
                raise AssertionError(f"普通模型与流程模型缺少核心外层字段 {field}")
        print("验证结论: 流程模型 GetModelInfo 成功，兼容结构与普通模型一致")

        code, output = _run_command(runner, "get-dvs-model-info", dvst)
        if code != 0:
            raise AssertionError(f"流程模型 GetDvsModelInfo 执行失败，返回码为 {code}")
        detail = _find_json(output, _validate_detail)
        _check_flow_mapping(flow_compatible, detail)
        print("验证结论: 流程模型详细信息字段及兼容字段来源校验通过")
        return 0
    except (AssertionError, ValueError, OSError) as error:
        print(f"验证结论: 失败：{error}", file=sys.stderr)
        return 1


def main() -> int:
    _configure_output_streams()
    parser = argparse.ArgumentParser(description="验证模型信息命令行接口")
    parser.add_argument("--runner", required=True, help="模型信息命令行程序路径")
    parser.add_argument("--dvt", required=True, help="普通模型路径")
    parser.add_argument("--dvst", required=True, help="流程模型路径")
    return run(parser.parse_args())


if __name__ == "__main__":
    raise SystemExit(main())
