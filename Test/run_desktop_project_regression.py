"""串行验证三个实际桌面程序；输出仅写入系统临时目录。"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import tempfile

CASES = {
    "classification": ("猫狗-分类_120_50_s.dvt", "猫狗-狗.jpg", "狗", 0.9951171875),
    "segmentation": ("气球-实例分割_120_50_s.dvt", "气球.jpg", "气球", 0.9892578125),
}


def read_json(path):
    def reject(value):
        raise ValueError("JSON 包含非有限数值: " + value)
    return json.loads(path.read_text(encoding="utf-8", errors="strict"), parse_constant=reject)


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def check_result(value, expected):
    errors = []
    if type(value.get("count")) is not int or value["count"] != 1:
        errors.append("目标数量不是 1")
    if value.get("categories") != [expected[2]]:
        errors.append("类别与固定结果不符")
    scores = value.get("scores")
    if not isinstance(scores, list) or len(scores) != 1 or type(scores[0]) not in (int, float) or not math.isfinite(scores[0]) or abs(scores[0] - expected[3]) > 1e-6:
        errors.append("分数与固定结果不符")
    return errors


def error_stream(work):
    path = work / "stderr.bin"
    return base64.b64encode(path.read_bytes()).decode("ascii") if path.is_file() else ""


def launch(command, directory, work, timeout):
    work.mkdir(parents=True, exist_ok=True)
    environment = os.environ.copy()
    environment.update(TEMP=str(work), TMP=str(work))
    with (work / "stdout.bin").open("wb") as stdout, (work / "stderr.bin").open("wb") as stderr:
        process = subprocess.run(command, cwd=directory, env=environment, stdout=stdout, stderr=stderr, timeout=timeout, check=False)
    return process.returncode


def run_demo(exe, directory, model_root, root, timeout):
    results = []
    for name, expected in CASES.items():
        for mode in ("infer", "ui-test"):
            work = root / (name + "-" + mode)
            output, image = work / "result.json", work / "render.png"
            command = [str(exe), mode, "--model", str(model_root / expected[0]), "--image", str(model_root / expected[1]), "--threshold", "0.5", "--output", str(output)]
            if mode == "ui-test":
                command += ["--interactive-dialogs", "false", "--screenshot", str(image)]
            errors, value, code = [], None, None
            try:
                code = launch(command, directory, work, timeout)
                if code != 0:
                    errors.append("退出码不是 0: " + str(code))
                value = read_json(output)
                if mode == "infer":
                    for path in ("structured", "json"):
                        errors.extend(check_result(value.get(path, {}), expected))
                    if value.get("consistent") is not True or value.get("threshold_check_passed") is not True:
                        errors.append("推理一致性或阈值检查失败")
                else:
                    if value.get("ok") is not True or value.get("status") != "passed" or value.get("interactive_dialogs") is not False or value.get("error") is not None:
                        errors.append("非交互界面检查失败")
                    text = value.get("result_text", "")
                    if "推理结果: 1个" not in text or expected[2] not in text:
                        errors.append("界面结果数量或类别错误")
                    png = image.read_bytes()
                    if len(png) < 24 or png[:8] != b"\x89PNG\r\n\x1a\n" or min(struct.unpack(">II", png[16:24])) <= 0:
                        errors.append("界面绘制 PNG 无效")
            except (OSError, ValueError, TypeError, AttributeError, subprocess.TimeoutExpired) as exc:
                errors.append(type(exc).__name__ + ": " + str(exc))
            results.append({"project": "DlcvDemo", "name": name, "mode": mode, "passed": not errors, "exit_code": code, "result": value, "stderr_base64": error_stream(work) if errors else "", "errors": errors})
    return results


def run_wpf(project, exe, directory, model_root, root, timeout):
    work = root / project
    output = work / "result.json"
    errors, value, code = [], None, None
    try:
        code = launch([str(exe), "selftest", "--model-root", str(model_root), "--output", str(output)], directory, work, timeout)
        if code != 0:
            errors.append("退出码不是 0: " + str(code))
        value = read_json(output)
        if value.get("passed") is not True:
            errors.append("程序自测未通过")
        cases = value.get("cases")
        if not isinstance(cases, list) or len(cases) != 2 or {case.get("name") for case in cases} != set(CASES):
            errors.append("程序自测缺少固定用例")
        else:
            for case in cases:
                errors.extend(case["name"] + ": " + message for message in check_result(case, CASES[case["name"]]))
                png = (work / (case["name"] + ".png")).read_bytes()
                if len(png) < 24 or png[:8] != b"\x89PNG\r\n\x1a\n" or min(struct.unpack(">II", png[16:24])) <= 0:
                    errors.append(case["name"] + ": 实际控件 PNG 无效")
                if any(case.get(flag) is not True for flag in ("passed", "release_check_passed", "render_check_passed")):
                    errors.append(case["name"] + ": 推理、释放或绘制检查失败")
    except (OSError, ValueError, TypeError, AttributeError, KeyError, subprocess.TimeoutExpired) as exc:
        errors.append(type(exc).__name__ + ": " + str(exc))
    return {"project": project, "mode": "selftest", "passed": not errors, "exit_code": code, "result": value, "stderr_base64": error_stream(work) if errors else "", "errors": errors}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("demo-exe", "dlcvtest-exe", "wpf-exe", "api-dll", "model-root", "core-dll-directory", "output"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--timeout", type=float, default=120)
    args = parser.parse_args(argv)
    output, temporary = args.output.resolve(), Path(tempfile.gettempdir()).resolve()
    if output == temporary or temporary not in output.parents:
        parser.error("--output 必须位于系统临时目录")
    if not math.isfinite(args.timeout) or args.timeout <= 0:
        parser.error("--timeout 必须是有限正数")
    executables = {"DlcvDemo": args.demo_exe.resolve(), "DlcvTest": args.dlcvtest_exe.resolve(), "OpenIVSWPF": args.wpf_exe.resolve()}
    inputs = [args.api_dll, args.core_dll_directory / "dlcv_infer.dll"] + list(executables.values())
    inputs += [args.model_root / name for case in CASES.values() for name in case[:2]]
    for path in inputs:
        if not path.is_file():
            parser.error("缺少输入文件: " + str(path))
    expected_hash = sha256(args.api_dll)
    for project, exe in executables.items():
        if sha256(exe.with_name("DlcvCsharpApi.dll")) != expected_hash:
            parser.error(project + " 目录 API DLL 与本次构建不一致")
    with tempfile.TemporaryDirectory(prefix="dlcv_desktop_regression_") as temp:
        root = Path(temp)
        checks = run_demo(executables["DlcvDemo"], args.core_dll_directory, args.model_root, root / "DlcvDemo", args.timeout)
        for name in ("DlcvTest", "OpenIVSWPF"):
            checks.append(run_wpf(name, executables[name], args.core_dll_directory, args.model_root, root, args.timeout))
    report = {"passed": all(check["passed"] for check in checks), "checks": checks, "api_sha256": expected_hash, "executables": {name: sha256(path) for name, path in executables.items()}}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, allow_nan=False, indent=2) + "\n", encoding="utf-8")
    print(output)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
