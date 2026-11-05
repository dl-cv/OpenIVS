import os
import shutil

from setuptools import setup

version = '2026.11.5.1a0'

package_name = "dlcvpro_infer_cpp"


def main():
    setup(
        name=package_name,
        version=version,
        description="C++ 测试程序",
        author="AI平台",
        author_email="ypw@dlcv.ai",
        url="",
        keywords="AI, Deep Learning, Computer Vision",
        packages=[package_name],
        package_data={package_name: ["*"]},
        options={
            "build": {"build_base": "build_cpp"},
            "bdist_wheel": {
                "bdist_dir": "build_cpp/bdist",
                'plat_name': 'win_amd64',
                'python_tag': 'cp311',
            },
        },
    )


if __name__ == "__main__":
    import warnings

    egg_info_path = f'{package_name}.egg-info'

    path_list = ['build_cpp', 'dist_cpp', 'whl_cpp', egg_info_path]
    for path in path_list:
        if os.path.exists(path):
            warnings.warn(f'remove {path}')
            shutil.rmtree(path)
        else:
            print(f'no such file:{path}')

    main()

    path_list = ['build_cpp', egg_info_path]
    for path in path_list:
        if os.path.exists(path):
            warnings.warn(f'remove {path}')
            shutil.rmtree(path)
        else:
            print(f'no such file:{path}')
