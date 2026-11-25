"""Shared cases: hand specified direction/rounding expectations, checked against Python."""
import copy
import json
import math
from pathlib import Path
import sys


def main():
    sys.path.insert(0, sys.argv[1])
    import numpy as np
    from test_backend.multi_tasks.NewModules.image_angle import ImageAngle
    from test_backend.multi_tasks.NewModules.base_module import ModuleImage, TransformationState
    cases = []
    directions = [(0, ['0', '0.0', '0.00']), (90, ['90', '90.0', '90.00']),
                  (180, ['180', '180.0', '180.00']), (270, ['270', '270.0', '270.00']),
                  (-45, ['315', '315.0', '315.00']), (30.5, ['31', '30.5', '30.50']),
                  (30.25, ['30', '30.3', '30.25']), (30.125, ['30', '30.1', '30.13']),
                  (359.995, ['0', '0.0', '0.00']), (359.94, ['0', '359.9', '359.94']),
                  (0.005, ['0', '0.0', '0.01'])]
    for angle, labels in directions:
        r = math.radians(angle)
        matrix = [2 * math.cos(r), 2 * math.sin(r), 123, -2 * math.sin(r), 2 * math.cos(r), -72]
        for places, label in enumerate(labels):
            entry = {'type': 'local', 'index': 1, 'origin_index': 7, 'unique_id': 'target-B',
                     'category_name': 'old', 'sample_results': [
                         {'category_name': 'old', 'bbox': [1, 2, 3, 4], 'score': 0.9, 'extra_info': {'keep': True}}, {}, None]}
            expected = copy.deepcopy(entry)
            expected['category_name'] = label
            for detection in expected['sample_results']:
                if isinstance(detection, dict):
                    detection['category_name'] = label
            cases.append({'name': f'angle-{angle}-places-{places}', 'properties': {'decimal_places': places},
                          'matrices': [[1, 0, 0, 0, 1, 0], matrix], 'input': [entry], 'expected': [expected]})
    base = {'type': 'local', 'index': 0, 'sample_results': [{'category_name': 'old'}]}
    cases.append({'name': 'default-identity', 'properties': {}, 'matrices': [None], 'input': [base],
                  'expected': [dict(base, sample_results=[{'category_name': '0'}])]})
    reordered = [dict(base, index=1, unique_id='B'), dict(base, index=0, unique_id='A'),
                 dict(base, index=1, unique_id='B-repeat')]
    cases.append({'name': 'reordered-results', 'properties': {'decimal_places': 2},
                  'matrices': [[0, 1, 0, -1, 0, 0], [0, -1, 0, 1, 0, 0]],
                  'input': reordered, 'expected': [dict(entry, sample_results=[{'category_name': label}])
                                                 for entry, label in zip(reordered, ['270.00', '90.00', '270.00'])]})
    passthrough = [None, {'type': 'global', 'sample_results': [{}]}, {'type': 'local', 'sample_results': []}]
    cases.append({'name': 'empty-and-global', 'properties': {}, 'matrices': [], 'input': passthrough, 'expected': passthrough})
    for value in [-1, 3, 1.0, '1', True, None]:
        cases.append({'name': f'invalid-places-{value}', 'properties': {'decimal_places': value},
                      'matrices': [], 'input': [], 'error': True})
    for value in [-1, 1, 0.0, '0', True, None]:
        cases.append({'name': f'invalid-index-{value}', 'properties': {}, 'matrices': [None],
                      'input': [dict(base, index=value)], 'error': True})
    for name, matrix in [('mirror', [-1, 0, 0, 0, 1, 0]), ('shear', [1, 1, 0, 0, 1, 0]),
                         ('unequal-scale', [2, 0, 0, 0, 1, 0]), ('singular', [0, 0, 0, 0, 0, 0])]:
        cases.append({'name': name, 'properties': {}, 'matrices': [matrix], 'input': [base], 'error': True})
    for case in cases:
        images = []
        for matrix in case['matrices']:
            image = np.zeros((4, 5, 3), np.uint8)
            state = TransformationState(original_width=5, original_height=4)
            if matrix is not None:
                state.affine_matrix = np.array(matrix, dtype=float).reshape(2, 3)
            images.append(ModuleImage(image, image, transformation_state=state))
        original = copy.deepcopy(case['input'])
        try:
            _, result = ImageAngle(1, properties=case['properties']).process(images, case['input'])
        except ValueError:
            assert case.get('error'), case['name']
        else:
            assert not case.get('error') and result == case['expected'], case['name']
        assert original == case['input']
    path = Path(__file__).with_name('image_angle_cases.json')
    path.write_text('[\n' + ',\n'.join(json.dumps(case, ensure_ascii=True, separators=(',', ':'))
                                      for case in cases) + '\n]\n', encoding='utf-8')
    print(f'{len(cases)} cases agree with Python')


if __name__ == '__main__':
    main()
