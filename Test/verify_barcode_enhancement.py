"""执行真实读码模块，比较两语言的增强像素、非耗时结果与可选 Python 报告。"""
import argparse
import json
from pathlib import Path
import subprocess

import cv2
import numpy as np

from verify_dpm import without_timings


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('manifest', 'flow', 'cs-exe', 'cpp-exe', 'output'):
        parser.add_argument('--'+key, type=Path, required=True)
    parser.add_argument('--python-report', type=Path)
    args = parser.parse_args()
    reports = []
    for language, exe in (('csharp', args.cs_exe), ('cpp', args.cpp_exe)):
        folder = args.output.resolve()/language
        folder.mkdir(parents=True, exist_ok=True)
        report = folder/'report.json'
        subprocess.run([str(exe.resolve()), 'barcode-selftest', str(args.manifest.resolve()),
                        str(args.flow.resolve()), str(report)], check=True)
        reports.append(json.loads(report.read_text(encoding='utf-8-sig')))
    manifest = json.loads(args.manifest.read_text(encoding='utf-8-sig'))
    python = json.loads(args.python_report.read_text(encoding='utf-8-sig')) if args.python_report else None
    assert len(manifest) == len(reports[0]) == len(reports[1])
    for index, (left, right) in enumerate(zip(*reports)):
        name = left['name']
        assert name == right['name'] == manifest[index]['name']
        assert without_timings(left['local']) == without_timings(right['local']), name+' local'
        assert without_timings(left['flow']) == without_timings(right['flow']), name+' flow'
        for value in (left, right):
            for route in ('local', 'flow'):
                assert sorted(d['category_name'] for d in value[route]) == manifest[index]['expected'], (name, route)
        for attempt in range(5):
            a, b = (cv2.imdecode(np.fromfile(args.output/language/f'{name}-{attempt}.png', np.uint8), 0)
                    for language in ('csharp', 'cpp'))
            assert a is not None and b is not None
            np.testing.assert_array_equal(a, b, err_msg=f'{name} attempt={attempt}')
        if python:
            assert python[index]['name'] == name
            assert len(left['local']) == len(python[index]['local']), name+' python count'
            for cs, py in zip(left['local'], python[index]['local']):
                assert cs['category_name'] == py['category_name']
                extra = dict(cs['extra_info'])
                assert extra.pop('polyline') == py['polyline']
                assert without_timings(extra) == without_timings(py['extra_info']), name+' python metadata'
                # Python 返回 XYXY，原生模块返回 XYWH。
                x, y, width, height = cs['bbox']
                assert [x, y, x + width, y + height] == py['bbox'], name+' crop box'
    result = dict(cases=len(manifest), enhanced_images=len(manifest)*5, pixel_equal=True,
                  module_equal=True, flow_equal=True, python_checked=python is not None)
    (args.output/'comparison.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
