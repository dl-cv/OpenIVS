"""收集已编译的独立 Demo、公开头文件和指定 DLL，不重新编译 OpenIVS。"""
import argparse
from pathlib import Path
import shutil
import zipfile


ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True, type=Path, help="与当前公开头文件配套的 dlcv_infer_cpp.dll")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Release")
    args = parser.parse_args()
    project = ROOT / "dlcv_infer_binary_demo"
    files = {
        "main.cpp": project / "main.cpp",
        "dlcv_infer_binary_demo.vcxproj": project / "dlcv_infer_binary_demo.vcxproj",
        "dlcv_infer_c_api.h": ROOT / "dlcv_infer_cpp" / "dlcv_infer_c_api.h",
        "dlcv_infer_cpp.dll": args.dll.resolve(),
        "dlcv_infer_binary_demo.exe": project / "bin" / args.configuration / "dlcv_infer_binary_demo.exe",
        "README.md": ROOT / "docs" / "二进制最小Demo.md",
    }
    for source in files.values():
        if not source.is_file():
            parser.error(f"文件不存在: {source}")
    output = ROOT / "dist" / "dlcv_infer_binary_demo"
    output.mkdir(parents=True, exist_ok=True)
    for name, source in files.items():
        shutil.copy2(source, output / name)
    archive = output.with_suffix(".zip")
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
        for name in files:
            package.write(output / name, f"{output.name}/{name}")
    print(archive)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
