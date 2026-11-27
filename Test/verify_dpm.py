"""比较两个 dpm-selftest 报告；图像、模式、模块结果相等才通过，不忽略漏码。"""
import argparse
import json
from pathlib import Path

import cv2
import numpy as np


def without_timings(value):
    if isinstance(value, dict):
        return {k: without_timings(v) for k, v in value.items() if not k.endswith('_ms')}
    if isinstance(value, list):
        return [without_timings(v) for v in value]
    return value


def read_image(path):
    return cv2.imdecode(np.fromfile(path, dtype=np.uint8), cv2.IMREAD_GRAYSCALE)


def compare(cs_path, cpp_path, crops_only=False):
    cs = json.loads(cs_path.read_text(encoding='utf-8-sig'))
    cpp = json.loads(cpp_path.read_text(encoding='utf-8-sig'))
    assert cs['crops'] and len(cs['crops']) == len(cpp['crops']), '裁图数量不一致或为空'
    for a, b in zip(cs['crops'], cpp['crops']):
        assert a['name'] == b['name'], '裁图顺序不一致'
        name = a['name']
        ia, ib = (read_image(p.parent/(name+'.png')) for p in (cs_path, cpp_path))
        assert ia is not None and ib is not None, name+' 缺少增强图'
        np.testing.assert_array_equal(ia, ib, err_msg=name)
        assert a['mode'] == b['mode'], name+' 模式不一致'
        assert without_timings(a['local']) == without_timings(b['local']), name+' 模块结果不一致'
    flows = []
    if not crops_only:
        assert cs['flow'] and len(cs['flow']) == len(cpp['flow']), '完整流程报告缺失或数量不一致'
        for a, b in zip(cs['flow'], cpp['flow']):
            assert a['name'] == b['name'], '原图顺序不一致'
            ca, cb = a['result'], b['result']['result_list']
            texts_a = sorted(r['category_name'] for r in ca)
            texts_b = sorted(r['category_name'] for r in cb)
            flows.append(dict(name=a['name'], cs_count=len(ca), cpp_count=len(cb),
                              texts_equal=texts_a == texts_b,
                              results_equal=without_timings(ca) == without_timings(cb),
                              cs_ms=a['ms'], cpp_ms=b['ms']))
    return dict(crops_equal=True, crop_count=len(cs['crops']), flow=flows,
                full_flow_checked=not crops_only,
                full_flow_equal=all(r['results_equal'] for r in flows) if flows else None)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cs', type=Path, required=True)
    parser.add_argument('--cpp', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--crops-only', action='store_true')
    args = parser.parse_args()
    report = compare(args.cs, args.cpp, args.crops_only)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False))
    return 1 if report['full_flow_equal'] is False else 0


if __name__ == '__main__':
    raise SystemExit(main())
