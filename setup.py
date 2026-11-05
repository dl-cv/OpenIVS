import os
import re
import shutil
from pathlib import Path

from setuptools import setup

version = '2026.11.5.0a0'
cpp_version = '2026.11.5.0a0'

variant = os.environ.get("DLCV_PACKAGE_VARIANT", "csharp")
if variant not in ("csharp", "cpp"):
    raise ValueError(f"未知包类型: {variant}")
package_name = f"dlcvpro_infer_{variant}"
build_dir = "build_cpp" if variant == "cpp" else "build"
dist_dir = "dist_cpp" if variant == "cpp" else "dist"
packages: list = [package_name]  # 需要打包的包
package_data: dict[str:list] = {package_name: ["*"]}  # 哪个包需要打包哪些资源文件
if variant == "cpp":
    package_data[package_name] = [
        path.relative_to(package_name).as_posix()
        for path in Path(package_name).rglob("*") if path.is_file()
    ]


def main():
    setup(
        name=package_name,
        version=cpp_version if variant == "cpp" else version,
        description="C++ 测试程序" if variant == "cpp" else "C# 测试程序",
        author="AI平台",
        author_email="ypw@dlcv.ai",
        url="",
        keywords="AI, Deep Learning, Computer Vision",
        packages=packages,
        package_data=package_data,
        options={
            "build": {"build_base": build_dir},
            "bdist_wheel": {
                "bdist_dir": f"{build_dir}/bdist",
                'plat_name': 'win_amd64',
                'python_tag': 'cp311',
            },
        },
    )


if __name__ == "__main__":
    import warnings

    egg_info_path = f'{package_name}.egg-info'

    path_list = [build_dir, dist_dir, 'whl_cpp' if variant == 'cpp' else 'whl', egg_info_path]
    for path in path_list:
        if os.path.exists(path):
            warnings.warn(f'remove {path}')
            shutil.rmtree(path)
        else:
            print(f'no such file:{path}')

    main()

    path_list = [build_dir, egg_info_path]
    for path in path_list:
        if os.path.exists(path):
            warnings.warn(f'remove {path}')
            shutil.rmtree(path)
        else:
            print(f'no such file:{path}')
