"""Generate shared cases using the reviewed Python module (pass its checkout)."""
import copy
import importlib
import json
from pathlib import Path
import sys


def main():
    sys.path.insert(0, sys.argv[1])
    module = importlib.import_module('test_backend.multi_tasks.NewModules.category_block')
    detections = [
        {'category_name': name, 'score': 0.9, 'bbox': [1, 2, 3, 4],
         'mask_rle': {'size': [2, 2], 'counts': [0, 4]}, 'extra_info': {'x': 1}}
        for name in [' Cup ', 'cupboard', '缺陷', 'ÄBC', 'ΑΒΣ', 'İ', '𐐀',
                     '\u001c\u2003Cup\u0085', 'AΣ\u0301', 'AΣ\u0301A', 'AΣ\u02b0',
                     1, 1.0, True, None, '', 'NONE']
    ] + [{}, None, 17, 'cup']
    entries = [
        {'type': 'local', 'index': 5, 'origin_index': 0, 'transform': {'affine_2x3': [1, 0, 3, 0, 1, 4]},
         'metadata': {'keep': True}, 'sample_results': detections},
        {'type': 'local', 'index': 8, 'origin_index': 1, 'sample_results': [{'category_name': 'Cup'}]},
        {'type': 'local', 'index': 9, 'origin_index': 1, 'sample_results': []},
        {'type': 'global', 'sample_results': [{'category_name': 'cup'}]},
        {'type': 'LOCAL', 'sample_results': [{'category_name': 'cup'}]},
        {'type': 'local', 'sample_results': None}, {'type': 'local'}, None, 1,
    ]
    cases = []
    for name, props in [
        ('missing', {}), ('empty', {'categories': []}), ('null', {'categories': None}),
        ('trim-case-exact', {'categories': [' cUP ', 'cup', '', ' ', 'unknown']}),
        ('unicode', {'categories': ['缺陷', 'äbc', 'αβς', 'i\u0307', '𐐨', 'aς\u0301', 'aσ\u0301a', 'aς\u02b0']}),
        ('scalar-labels', {'categories': [1, 1.0, True, 'none', None, {}, []]}),
        ('all-strings', {'categories': ['cup', 'cupboard', '缺陷', 'äbc', 'αβς', 'i\u0307', '𐐨', '1', '1.0', 'true', 'none']}),
    ]:
        original = copy.deepcopy(entries)
        obj = module.CategoryBlock(1, properties=props)
        _, expected = obj.process([], entries)
        assert entries == original
        cases.append({'name': name, 'properties': props, 'input': entries, 'expected': expected})
    cases.append({'name': 'empty-input', 'properties': {'categories': ['cup']}, 'input': [], 'expected': []})
    for value in [1e-7, 1e-4, 1e15, 1e16, -0.0, 1.2345678901234567]:
        for label, category in [(value, str(value)), (str(value), value)]:
            entries = [{'type': 'local', 'sample_results': [{'category_name': label}]}]
            props = {'categories': [category]}
            _, expected = module.CategoryBlock(1, properties=props).process([], entries)
            assert expected[0]['sample_results'] == []
            case = {'name': 'float-' + str(value), 'properties': props, 'input': entries, 'expected': expected}
            if str(value) == '-0.0':
                case['csharp_json_limitation'] = 'Newtonsoft JSON/.NET Framework parses numeric -0.0 as +0.0 before module execution; use a string label.'
            cases.append(case)
    path = Path(__file__).with_name('category_block_cases.json')
    path.write_text(json.dumps(cases, ensure_ascii=True, separators=(',', ':')) + '\n', encoding='utf-8')
    print(f'{len(cases)} Python reference cases: {path}')


if __name__ == '__main__':
    main()
