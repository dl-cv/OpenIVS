"""将已构建的内部原生运行时及其 OpenCV 依赖随独立运行时包分发。"""
from pathlib import Path

from setuptools import setup
from setuptools.command.build_py import build_py

from setup_cpp import version

package = "dlcv_datamatrix_runtime"
root = Path(__file__).resolve().parents[1]
native = root / "dlcv_infer_cpp" / "native" / "datamatrix"
dll = f"{package}.dll"
build_dir = root / "temp" / "datamatrix_wheel"


class BuildRuntime(build_py):
    def run(self):
        super().run()
        directory = native / "bin" / "x64" / "Release"
        dependencies = sorted(directory.glob("opencv_world[0-9]*.dll"))
        if len(dependencies) != 1:
            raise RuntimeError(f"读码运行时需要一份 OpenCV Release DLL，实际找到 {len(dependencies)} 份")
        for source in (directory / dll, *dependencies):
            if not source.is_file():
                raise FileNotFoundError(f"缺少读码运行时构建产物: {source}")
            self.copy_file(str(source), str(Path(self.build_lib) / package / source.name))
        notices = directory / "opencv_licenses"
        if notices.is_dir():
            dest = Path(self.build_lib) / package / notices.name
            self.mkpath(str(dest))
            for source in sorted(notices.iterdir()):
                self.copy_file(str(source), str(dest / source.name))

if __name__ == "__main__":
    build_dir.mkdir(parents=True, exist_ok=True)
    setup(
        name=package,
        version=version,
        description="Data Matrix 读码内部运行时",
        license_files=["LICENSE", "third_party/opencv/OpenCV-LICENSE.txt", "third_party/scipy/LICENSE.txt"],
        packages=[package],
        package_dir={package: "dlcv_infer_cpp/native/datamatrix/python"},
        cmdclass={"build_py": BuildRuntime},
        options={"build": {"build_base": str(build_dir)},
                 "egg_info": {"egg_base": str(build_dir)},
                 "bdist_wheel": {"plat_name": "win_amd64", "python_tag": "py3"}},
    )
