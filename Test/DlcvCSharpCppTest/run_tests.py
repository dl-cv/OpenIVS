"""串行运行实际 EXE，验证已安装 SDK、命令行、界面处理与输出文件。"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--model", type=Path, action="append", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--sdk-directory", type=Path, default=Path("C:/dlcv/Lib/site-packages/dlcvpro_infer"))
    args = parser.parse_args()
    exe = args.exe.resolve(strict=True)
    models = [path.resolve(strict=True) for path in args.model]
    sdk = args.sdk_directory.resolve(strict=True)
    root = args.output_dir.resolve()
    if not root.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        parser.error("结果目录必须位于系统临时目录")
    root.mkdir(parents=True, exist_ok=True)
    if any(root.iterdir()):
        parser.error("结果目录必须为空")
    results = []

    def run(name, arguments, expected=0, native=False, screenshot=False):
        output = root / (name + ".json")
        command = [str(exe), arguments[0], "--output", str(output), *arguments[1:]]
        image = root / (name + ".png")
        if screenshot:
            command += ["--screenshot", str(image)]
        with (root / (name + ".log")).open("wb") as log:
            process = subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, timeout=240)
        report = json.loads(output.read_text(encoding="utf-8"))
        if process.returncode != expected or report["status"] != ("passed" if expected == 0 else "failed"):
            raise AssertionError(f"{name}: exit={process.returncode}, {report.get('error')}")
        if native:
            modules = report.get("native_modules", report.get("ui", {}).get("native_modules", []))
            engines = [module for module in modules if module["name"] in ("dlcv_infer.dll", "dlcv_infer_v.dll")]
            if len(engines) != 1:
                raise AssertionError("实际加载的推理 DLL 数量错误")
            for module in engines:
                actual = Path(module["path"]).resolve(strict=True)
                expected_path = (sdk / module["name"]).resolve(strict=True)
                if actual != expected_path:
                    raise AssertionError("进程未使用指定安装目录的 SDK")
            wrapper = next(module for module in modules if module["name"] == "dlcv_infer_cpp.dll")
            if Path(wrapper["path"]).resolve() != (exe.parent / "dlcv_infer_cpp.dll").resolve():
                raise AssertionError("进程未使用本次 EXE 目录的包装 DLL")
        if screenshot:
            content = image.read_bytes()
            if content[:8] != b"\x89PNG\r\n\x1a\n":
                raise AssertionError("界面图片不是 PNG")
            width, height = struct.unpack(">II", content[16:24])
            if width < 880 or height < 480:
                raise AssertionError("界面图片尺寸错误")
        results.append({"case": name, "exit_code": process.returncode, "status": "passed"})
        print(name + ": passed", flush=True)

    try:
        # 项目配置与实际 EXE 分别检查，编译成功不能替代 IDE 平台声明检查。
        process = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                                  str(Path(__file__).with_name("check_startup_configuration.ps1"))],
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=60)
        (root / "project-configuration.log").write_bytes(process.stderr)
        if process.returncode != 0:
            raise AssertionError("项目启动配置检查失败，详见 project-configuration.log")
        report = json.loads(process.stdout.decode("utf-8-sig"))
        if report["status"] != "passed" or "x64" not in report["platforms"]:
            raise AssertionError("项目启动配置检查结果不正确")
        (root / "project-configuration.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        results.append({"case": "project-configuration", "exit_code": 0, "status": "passed"})
        print("project-configuration: passed", flush=True)
        run("designer", ["designer-test"])
        run("empty-ui", ["ui-test"], screenshot=True)
        run("missing-model", ["model-test"], expected=1)
        run("bad-order", ["model-test", "--model", str(models[0]), "--release-order", "invalid"], expected=1)
        run("bad-device", ["model-test", "--model", str(models[0]), "--device", "-2"], expected=1)
        run("bad-option", ["ui-test", "--unknown", "value"], expected=1)
        for index, model in enumerate(models):
            for order in ("csharp-first", "cpp-first"):
                run(f"model-{index}-{order}", ["model-test", "--model", str(model), "--release-order", order], native=True)
            run(f"ui-{index}", ["ui-test", "--model", str(model)], native=True, screenshot=True)
        protected = root / "protected.json"
        protected.write_bytes(b"keep")
        with (root / "protected.log").open("wb") as log:
            process = subprocess.run([str(exe), "ui-test", "--output", str(protected)], cwd=root,
                                     stdout=log, stderr=subprocess.STDOUT, timeout=60)
        if process.returncode != 1 or protected.read_bytes() != b"keep":
            raise AssertionError("已有结果文件被覆盖")
        results.append({"case": "existing-output", "exit_code": 1, "status": "passed"})
        summary = {"status": "passed", "count": len(results), "cases": results,
                   "exe_sha256": hashlib.sha256(exe.read_bytes()).hexdigest()}
        (root / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"total: {len(results)}/{len(results)} passed")
        return 0
    except Exception as error:
        (root / "summary.json").write_text(json.dumps({"status": "failed", "cases": results,
            "error": str(error)}, ensure_ascii=False, indent=2), encoding="utf-8")
        raise


if __name__ == "__main__":
    raise SystemExit(main())
