#!/usr/bin/env python3
"""执行 C++ Qt 主 Demo infer 回归检查。"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import subprocess
import sys
import tempfile
from pathlib import Path
from typing import Any
THRESHOLD = "0.5"
SIGNALS = ("consistent", "inspection_consistent", "threshold_check_passed", "mean_check_passed")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()

def _check_summary(name: str, value: Any, count: int, category: str, score: float) -> list[str]:
    if not isinstance(value, dict):
        return [f"{name} 结果缺失或类型错误"]
    errors = []
    actual_count = value.get("count")
    if isinstance(actual_count, bool) or actual_count != count:
        errors.append(f"{name}.count 与基准不符")
    if value.get("categories") != [category] * count:
        errors.append(f"{name}.categories 与基准不符")
    scores = value.get("scores")
    if not isinstance(scores, list) or len(scores) != count:
        errors.append(f"{name}.scores 数量与基准不符")
    elif any(isinstance(item, bool) or not isinstance(item, (int, float))
             or not math.isfinite(item) or abs(item - score) > 1e-6 for item in scores):
        errors.append(f"{name}.scores 与基准不符")
    return errors


def _check_result(value: Any, count: int, category: str, score: float) -> list[str]:
    if not isinstance(value, dict):
        return ["推理 JSON 顶层类型错误"]
    errors = []
    for name in ("structured", "json"):
        errors.extend(_check_summary(name, value.get(name), count, category, score))
    errors.extend(f"{name} 不为 true" for name in SIGNALS if value.get(name) is not True)
    return errors


def run_regression(args: argparse.Namespace) -> dict[str, Any]:
    paths = {name: Path(getattr(args, name)).resolve() for name in ("exe", "dll", "model", "image")}
    hashes: dict[str, str | None] = {name: None for name in paths}
    errors: list[str] = []
    for name, path in paths.items():
        if not path.is_file():
            errors.append(f"{name} 文件不存在")
        else:
            try:
                hashes[name] = _sha256(path)
            except OSError:
                errors.append(f"{name} 文件读取失败")

    deployed_dll = paths["exe"].with_name("dlcv_infer_cpp.dll")
    try:
        deployed_hash = _sha256(deployed_dll) if deployed_dll.is_file() else None
    except OSError:
        deployed_hash = None
        errors.append("EXE 目录 DLL 读取失败")
    if not deployed_dll.is_file():
        errors.append("EXE 目录缺少 dlcv_infer_cpp.dll")
    elif deployed_hash is not None and hashes["dll"] is not None and deployed_hash != hashes["dll"]:
        errors.append("EXE 目录 DLL 与本次构建 DLL 不配套")
    if args.count < 1:
        errors.append("count 必须大于零")
    if not args.category:
        errors.append("category 不能为空")
    if not math.isfinite(args.score) or not 0.0 <= args.score <= 1.0:
        errors.append("score 必须是 [0, 1] 内有限数值")
    if not math.isfinite(args.timeout) or args.timeout <= 0:
        errors.append("timeout 必须大于零")

    exit_code = None
    if not errors:
        with tempfile.TemporaryDirectory(prefix="dlcv_demo_regression_") as temp_dir:
            result_path = Path(temp_dir) / "infer.json"
            command = [str(paths["exe"]), "infer", "--model", str(paths["model"]), "--image",
                       str(paths["image"]), "--threshold", THRESHOLD, "--output", str(result_path)]
            try:
                completed = subprocess.run(
                    command, check=False, timeout=args.timeout, stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
                exit_code = completed.returncode & 0xFFFFFFFF if completed.returncode < 0 else completed.returncode
            except subprocess.TimeoutExpired:
                errors.append("进程执行超时")
            except OSError as exc:
                errors.append(f"进程启动失败：{exc.strerror or type(exc).__name__}")
            if exit_code not in (None, 0):
                errors.append(f"进程退出码非零：{exit_code}")
            if exit_code is not None:
                try:
                    text = result_path.read_text(encoding="utf-8", errors="strict")
                except FileNotFoundError:
                    errors.append("未生成推理 JSON")
                except UnicodeDecodeError:
                    errors.append("推理 JSON 不是有效 UTF-8")
                except OSError:
                    errors.append("推理 JSON 读取失败")
                else:
                    try:
                        value = json.loads(text)
                    except json.JSONDecodeError:
                        errors.append("推理 JSON 格式无效")
                    else:
                        errors.extend(_check_result(value, args.count, args.category, args.score))
    return {"passed": not errors, "exit_code": exit_code, "exe_sha256": hashes["exe"],
            "dll_sha256": hashes["dll"],
            "input_sha256": {"model": hashes["model"], "image": hashes["image"]}, "errors": errors}


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="执行 C++ Qt 主 Demo infer 回归检查")
    for name in ("exe", "dll", "model", "image"):
        parser.add_argument(f"--{name}", required=True, type=Path)
    parser.add_argument("--category", required=True)
    parser.add_argument("--score", required=True, type=float)
    parser.add_argument("--count", type=int, default=1)
    parser.add_argument("--timeout", type=float, default=90)
    parser.add_argument("--output", type=Path, help="报告文件路径，默认写入系统临时目录")
    return parser


def main(argv=None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"): stream.reconfigure(encoding="utf-8", errors="strict")
    args = _parser().parse_args(argv)
    report = run_regression(args)
    report_path = args.output or Path(tempfile.mkdtemp(prefix="dlcv_demo_report_")) / "report.json"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(report_path.resolve())
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
