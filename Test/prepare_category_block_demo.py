"""Insert category_block into a three-node instance-segmentation DVST for Demo checks."""
import copy
import json
from pathlib import Path
import sys


def main():
    source, destination = map(Path, sys.argv[1:3])
    with source.open('rb') as stream:
        assert stream.readline() == b'DV\n'
        header = json.loads(stream.readline())
        payloads = [stream.read(size) for size in header['file_size']]
        assert not stream.read(1)
    index = header['file_list'].index('pipeline.json')
    pipeline = json.loads(payloads[index])
    assert [node['type'] for node in pipeline['nodes']] == ['input/image', 'model/instance_seg', 'output/return_json']
    pipeline['nodes'][0]['type'] = 'input/frontend_image'
    pipeline['nodes'][0]['properties'] = {}
    model, output = pipeline['nodes'][1:]
    node = {'id': 4, 'order': 2, 'type': 'post_process/category_block', 'title': '类别屏蔽',
            'properties': {'categories': []}, 'inputs': copy.deepcopy(output['inputs']),
            'outputs': [{'name': 'image', 'type': 'image_chan', 'links': [5]},
                        {'name': 'results', 'type': 'result_chan', 'links': [6]}]}
    output['order'] = 3
    output['inputs'][0]['link'] = 5
    output['inputs'][1]['link'] = 6
    pipeline['nodes'].insert(2, node)
    pipeline['links'] = [[2, 1, 0, 2, 0, 'image_chan'], [3, 2, 0, 4, 0, 'image_chan'],
                         [4, 2, 1, 4, 1, 'result_chan'], [5, 4, 0, 3, 0, 'image_chan'],
                         [6, 4, 1, 3, 1, 'result_chan']]
    pipeline['last_node_id'], pipeline['last_link_id'] = 4, 6
    destination.mkdir(parents=True, exist_ok=True)
    for name, categories in [('保留全部类别', []), ('屏蔽杯子', ['杯子']), ('恢复显示', ['不存在的类别'])]:
        node['properties']['categories'] = categories
        payloads[index] = json.dumps(pipeline, ensure_ascii=False).encode('utf-8')
        header['file_size'] = [len(data) for data in payloads]
        target = destination / (name + '.dvst')
        with target.open('xb') as stream:
            stream.write(b'DV\n' + json.dumps(header, ensure_ascii=False).encode('utf-8') + b'\n')
            for data in payloads: stream.write(data)
        print(target)


if __name__ == '__main__':
    main()
