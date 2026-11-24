"""运行测试程序2无界面推理，比较完整结果并保存性能报告。"""

import argparse
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import time


def bounded_integer(value, minimum, maximum):
    if not value.isascii() or not value.isdecimal() or not minimum <= int(value) <= maximum:
        raise argparse.ArgumentTypeError(f"须为 {minimum} 到 {maximum} 的整数")
    return int(value)


def read_json(text):
    def reject(value):
        raise ValueError("JSON 包含非有限数值: " + value)

    def finite_float(value):
        number = float(value)
        if not math.isfinite(number):
            reject(value)
        return number

    return json.loads(text, parse_constant=reject, parse_float=finite_float)


def parse_result(path):
    text = path.read_text(encoding="utf-8", errors="strict")
    header, separator, payload = text.partition("\n完整结果JSON:\n")
    if not separator:
        raise ValueError("结果文本缺少完整结果 JSON")
    results = read_json(payload)
    if not isinstance(results, list) or not all(isinstance(item, dict) for item in results):
        raise ValueError("完整结果须为对象数组")
    fields = dict(line.split(": ", 1) for line in header.splitlines() if ": " in line)
    counts = {
        name: int(fields[name])
        for name in ("滑窗数量", "元件提取模型合并后目标数", "元件检测模型结果数", "IC检测模型结果数", "最终结果数")
    }
    if any(value < 0 for value in counts.values()) or counts["最终结果数"] != len(results):
        raise ValueError("结果数量与完整结果 JSON 不符")
    timings = {}
    for name, value in fields.items():
        if name.endswith("耗时"):
            if not value.endswith(" ms"):
                raise ValueError("耗时单位错误: " + name)
            milliseconds = float(value[:-3])
            if not math.isfinite(milliseconds) or milliseconds < 0:
                raise ValueError("耗时无效: " + name)
            timings[name] = milliseconds
    for name in ("推理耗时", "提取阶段耗时", "提取结果合并耗时", "局部阶段耗时", "最终结果整理耗时"):
        if name not in timings:
            raise ValueError("缺少耗时: " + name)
    return results, {
        "counts": counts,
        "timings_ms": timings,
        "memory": header.splitlines()[0],
        "legacy_hash": int(fields["结果哈希"]),
    }


def loaded_dll_paths(stdout, stderr, expected):
    paths = []
    for line in (stdout + b"\n" + stderr).splitlines():
        if b"[dlcv_infer] loaded:" in line:
            path = Path(line.split(b"loaded:", 1)[1].strip().decode("gbk", errors="strict"))
            if not path.is_absolute() or path.resolve(strict=True) != expected:
                raise ValueError("实际加载的 SDK DLL 路径不符: " + str(path))
            paths.append(str(path.resolve()))
    if not paths:
        raise ValueError("日志缺少 SDK DLL 实际加载路径")
    return paths


class ProcessGpuMemory:
    def __init__(self, pid):
        self.pid = pid
        self.started = time.monotonic()
        self.query = None
        self.counters = {}
        self.instances = []
        self.next_refresh = 0.0
        self.report = {
            "status": "unavailable", "sample_interval_ms": 200, "sample_count": 0,
            "counter_refresh_interval_ms": 1000,
            "valid_sample_count": 0,
            "peak_sampled_dedicated_bytes": None, "peak_sampled_shared_bytes": None,
            "samples": [], "errors": [],
        }
        try:
            import win32pdh
            self.pdh = win32pdh
            self.query = self.pdh.OpenQuery()
        except Exception as error:
            self.record_error(error)

    def record_error(self, error):
        message = f"{type(error).__name__}: {error}"
        if message not in self.report["errors"]:
            self.report["errors"].append(message)

    def sample(self):
        sample = {"elapsed_ms": round((time.monotonic() - self.started) * 1000, 2), "available": False}
        self.report["samples"].append(sample)
        self.report["sample_count"] += 1
        if self.query is None:
            return
        try:
            names = ("Dedicated Usage", "Shared Usage")
            if time.monotonic() >= self.next_refresh:
                self.next_refresh = time.monotonic() + 1.0
                counter_names, instances = self.pdh.EnumObjectItems(
                    None, None, "GPU Process Memory", self.pdh.PERF_DETAIL_WIZARD, 0)
                if not all(name in counter_names for name in names):
                    raise RuntimeError("GPU Process Memory 缺少 Dedicated Usage 或 Shared Usage")
                self.instances = sorted(set(instance for instance in instances
                                           if instance.lower().startswith(f"pid_{self.pid}_")))
            if not self.instances:
                return
            for instance in self.instances:
                for name in names:
                    key = (instance, name)
                    if key not in self.counters:
                        path = self.pdh.MakeCounterPath((None, "GPU Process Memory", instance, None, -1, name))
                        self.counters[key] = self.pdh.AddCounter(self.query, path)
            self.pdh.CollectQueryData(self.query)
            totals = {name: 0 for name in names}
            for instance in self.instances:
                for name in names:
                    counter_type, value = self.pdh.GetFormattedCounterValue(
                        self.counters[(instance, name)], self.pdh.PDH_FMT_LARGE)
                    if value < 0:
                        raise ValueError("GPU Process Memory 返回负数")
                    totals[name] += int(value)
            sample.update(available=True, dedicated_bytes=totals[names[0]], shared_bytes=totals[names[1]])
            self.report["status"] = "available"
            self.report["valid_sample_count"] += 1
            for label, name in (("dedicated", names[0]), ("shared", names[1])):
                key = f"peak_sampled_{label}_bytes"
                previous = self.report[key]
                self.report[key] = totals[name] if previous is None else max(previous, totals[name])
        except Exception as error:
            self.record_error(error)

    def close(self):
        if self.query is not None:
            try:
                self.pdh.CloseQuery(self.query)
            except Exception as error:
                self.record_error(error)
            self.query = None
        if not self.report["valid_sample_count"] and not self.report["errors"]:
            self.report["unavailable_reason"] = "采样期间未找到此进程的 GPU 内存实例"


def run_process(command, runtime, environment, stdout_path, stderr_path, run):
    with stdout_path.open("wb") as stdout, stderr_path.open("wb") as stderr:
        process = subprocess.Popen(command, cwd=runtime, env=environment, stdin=subprocess.DEVNULL,
                                   stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        run["pid"] = process.pid
        monitor = None
        started = time.monotonic()
        try:
            monitor = ProcessGpuMemory(process.pid)
            run["gpu_memory"] = monitor.report
            while process.poll() is None:
                monitor.sample()
                remaining = 600 - (time.monotonic() - started)
                if remaining <= 0:
                    raise subprocess.TimeoutExpired(command, 600)
                try:
                    process.wait(timeout=min(0.2, remaining))
                except subprocess.TimeoutExpired:
                    pass
        finally:
            try:
                if process.poll() is None:
                    process.kill()
            finally:
                run["returncode"] = process.wait()
                if monitor is not None:
                    monitor.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("exe", "runtime-dir", "image", "extract-model", "component-model", "ic-model", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--no-cad", type=Path)
    parser.add_argument("--threads", type=lambda value: bounded_integer(value, 1, 32), required=True)
    parser.add_argument("--flow-threads", type=lambda value: bounded_integer(value, 1, 32), default=1)
    parser.add_argument("--in-process-repeat", type=lambda value: bounded_integer(value, 1, 32), default=1)
    parser.add_argument("--repeat", type=int, default=2)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    if args.repeat < 1:
        parser.error("--repeat 须为正整数")
    if not args.output.is_absolute():
        parser.error("--output 须为绝对路径")
    output = args.output.resolve()
    if output.exists() or not output.parent.is_dir():
        parser.error("报告已存在或报告父目录不存在")
    report = {"success": False, "threads": args.threads, "flow_threads": args.flow_threads,
              "repeat": args.repeat, "in_process_repeat": args.in_process_repeat,
              "processes": [], "runs": [], "results": None}
    exit_code = 1
    try:
        runtime = args.runtime_dir.resolve(strict=True)
        expected_dll = (runtime / "dlcv_infer.dll").resolve(strict=True)
        if not expected_dll.is_file():
            raise ValueError("运行目录缺少 dlcv_infer.dll 文件")
        for name in ("exe", "image", "extract_model", "component_model", "ic_model", "no_cad"):
            path = getattr(args, name)
            if path is not None:
                path = path.resolve(strict=True)
                if not path.is_file():
                    raise ValueError("输入须为文件: " + str(path))
                setattr(args, name, path)
        report["inputs"] = {
            name: str(getattr(args, name)) if getattr(args, name) is not None else None
            for name in ("exe", "image", "extract_model", "component_model", "ic_model", "no_cad", "reference")
        }
        report["runtime_dir"] = str(runtime)
        reference = None
        if args.reference:
            reference_report = read_json(args.reference.read_text(encoding="utf-8"))
            if reference_report.get("success") is not True or not isinstance(reference_report.get("results"), list):
                raise ValueError("参考报告缺少成功状态或完整结果")
            reference = reference_report["results"]
        environment = os.environ.copy()
        environment.update(TEMP=str(output.parent), TMP=str(output.parent), TMPDIR=str(output.parent))
        for index in range(1, args.repeat + 1):
            stem = output.parent / f"{output.stem}-{index:03d}"
            result_path = Path(str(stem) + ".txt")
            stdout_path = Path(str(stem) + ".stdout.log")
            stderr_path = Path(str(stem) + ".stderr.log")
            process_run = {"process": index, "stdout_log": str(stdout_path), "stderr_log": str(stderr_path)}
            report["processes"].append(process_run)
            result_paths = [result_path] if args.in_process_repeat == 1 else [
                Path(str(result_path) + f".{number}.txt") for number in range(1, args.in_process_repeat + 1)]
            for path in (*result_paths, stdout_path, stderr_path):
                with path.open("xb"):
                    pass
            command = [str(args.exe), "--infer", str(args.image), str(args.extract_model),
                       str(args.component_model), str(args.ic_model), "--local-threads", str(args.threads),
                       "--flow-threads", str(args.flow_threads), "--repeat", str(args.in_process_repeat),
                       "--output", str(result_path)]
            if args.no_cad:
                command += ["--no-cad", str(args.no_cad)]
            run_process(command, runtime, environment, stdout_path, stderr_path, process_run)
            if process_run["returncode"] != 0:
                raise RuntimeError(f"第 {index} 个进程退出码为 {process_run['returncode']}")
            process_run["loaded_dll_paths"] = loaded_dll_paths(stdout_path.read_bytes(), stderr_path.read_bytes(), expected_dll)
            for number, path in enumerate(result_paths, 1):
                run = {"process": index, "round": number, "result_file": str(path)}
                report["runs"].append(run)
                results, summary = parse_result(path)
                run.update(summary)
                if report["results"] is None:
                    report["results"] = results
                if results != report["results"]:
                    raise ValueError(f"第 {index} 个进程第 {number} 轮完整结果与首轮不同")
                if reference is not None and results != reference:
                    raise ValueError(f"第 {index} 个进程第 {number} 轮完整结果与参考报告不同")
                run["results_match"] = True
                counts = summary["counts"]
                print(f"进程 {index} 第 {number} 轮: 滑窗 {counts['滑窗数量']}，目标 {counts['元件提取模型合并后目标数']}，"
                      f"结果 {counts['最终结果数']}，耗时 {summary['timings_ms']['推理耗时']:.2f} ms", flush=True)
        report["success"] = True
        exit_code = 0
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
        print(report["error"], file=sys.stderr, flush=True)
    with output.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, allow_nan=False, indent=2)
        stream.write("\n")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
