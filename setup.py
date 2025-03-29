import os
import re
import shutil

from setuptools import setup

version = '2025.3.29.0'

package_name = "dlcvpro_infer_csharp"  # 包名
packages: list = [package_name]  # 需要打包的包
package_data: dict[str:list] = {package_name: ["*"]}  # 哪个包需要打包哪些资源文件


def main():
    setup(
        name=package_name,
        version=version,
        description="深度视觉部署SDK C# 测试程序",
        author="DLCV",
        author_email="ypw@dlcv.ai",
        url="",
        keywords="DLCV, Machine Learning",
        packages=packages,
        package_data=package_data,
        options={
            "bdist_wheel": {
                'plat_name': 'win_amd64',
                'python_tag': 'cp311',
            },
        },
    )


if __name__ == "__main__":
    import warnings

    egg_info_path = f'{package_name}.egg-info'

    path_list = ['build', 'dist', 'whl', egg_info_path]
    for path in path_list:
        if os.path.exists(path):
            warnings.warn(f'remove {path}')
            shutil.rmtree(path)
        else:
            print(f'no such file:{path}')

    main()

    path_list = ['build', egg_info_path]
    for path in path_list:
        if os.path.exists(path):
            warnings.warn(f'remove {path}')
            shutil.rmtree(path)
        else:
            print(f'no such file:{path}')
