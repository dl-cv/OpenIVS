"""Generate synthetic fixtures and verify real C#/C++ executables (no SDK/model required)."""
import argparse
import json
import re
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

import cv2
import numpy as np
import zxingcpp


def barcode_fixture(name, scale=5):
    svg_path = Path(__file__).resolve().parent / "data" / "barcode" / (name + ".svg")
    root = ET.fromstring(svg_path.read_text(encoding="utf-8"))
    namespace = {"svg": "http://www.w3.org/2000/svg"}
    width = int(root.attrib["width"])
    height = int(root.attrib["height"])
    image = np.full((height * scale, width * scale), 255, np.uint8)
    path = root.find("svg:g/svg:path", namespace).attrib["d"]
    for match in re.finditer(r"M(\d+) (\d+)h(\d+)v(\d+)h-\3Z", path):
        x, y, rect_width, rect_height = map(int, match.groups())
        image[y * scale:(y + rect_height) * scale, x * scale:(x + rect_width) * scale] = 0
    return image


def low_contrast_ean13_image():
    modules = "10101111010011101011100101101110001011000100101010100001011101001110100111010011011001011100101"
    image = np.full((300, len(modules) * 4 + 80), 180, np.uint8)
    for index, value in enumerate(modules):
        if value == "1":
            image[:, 40 + index * 4:40 + (index + 1) * 4] = 80
    height, width = image.shape
    gradient = np.linspace(-50, 50, width, dtype=np.float32)[None, :]
    image = np.clip(image.astype(np.float32) + gradient, 0, 255).astype(np.uint8)
    matrix = cv2.getRotationMatrix2D((width / 2, height / 2), -1, 1)
    image = cv2.warpAffine(image, matrix, (width, height), borderValue=180)
    yy, xx = np.mgrid[:height, :width]
    shadow = (30 * np.clip((xx - width * .55) / (width * .45), 0, 1)).astype(np.float32)
    return np.clip(image.astype(np.float32) - shadow, 0, 255).astype(np.uint8)


def square_data_matrix(rows, scale, quiet=0):
    n = len(rows)
    bits = (np.asarray(rows, np.uint16)[:, None] >> np.arange(n - 1, -1, -1)) & 1
    bitmap = np.pad(np.uint8(1 - bits) * 255, quiet, constant_values=255)
    return bitmap.repeat(scale, 0).repeat(scale, 1)


def uneven_data_matrix(padded=False):
    # ECC 200 square, encoded text T1616. The contrast varies across the crop.
    rows = (0xAAAA, 0xC859, 0xA4E8, 0xAD5B, 0x86AC, 0xCEFB, 0x9FC2, 0x8B95,
            0xB39A, 0xF70F, 0x9578, 0xE45D, 0xA7A4, 0x9C3D, 0xEAB2, 0xFFFF)
    bitmap = square_data_matrix(rows, 5 if padded else 6)
    y, x = np.indices(bitmap.shape, dtype=np.float32) / bitmap.shape[0]
    if padded:
        image = (130 + 40 * np.sin(x * 5 + 1) * np.cos(y * 8) + np.where(bitmap > 0, 15, -15)).astype(np.uint8)
        return cv2.copyMakeBorder(image, 7, 7, 7, 7, cv2.BORDER_CONSTANT, value=181)
    return (125 + 25 * np.sin(x * 5) * np.cos(y * 8) + np.where(bitmap > 0, 12, -12)).astype(np.uint8)


def read_reference(image, enhance):
    codes = zxingcpp.read_barcodes(image, try_downscale=True, text_mode=zxingcpp.TextMode.HRI)
    if codes or not enhance:
        return codes
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY) if image.ndim == 3 else image
    return zxingcpp.read_barcodes(cv2.equalizeHist(gray), try_downscale=True, text_mode=zxingcpp.TextMode.HRI)


def mixed_scale_qr_image():
    small = barcode_fixture("qr", scale=5)
    large = barcode_fixture("qr", scale=20)
    noise = np.random.default_rng(20261024).random(large.shape) < 0.1
    large[noise] = 255 - large[noise]
    image = np.full((large.shape[0] + 80, large.shape[1] + small.shape[1] + 120), 255, np.uint8)
    image[40:40 + large.shape[0], 40:40 + large.shape[1]] = large
    image[40:40 + small.shape[0], 80 + large.shape[1]:80 + large.shape[1] + small.shape[1]] = small
    return image


def normalized(detections, local=False):
    result = []
    for d in detections:
        item = {k: d[k] for k in ("category_name", "bbox", "extra_info")}
        if local:
            x, y, w, h = item["bbox"]
            item["bbox"] = [x, y, x+w, y+h]
        result.append(item)
    return sorted(result, key=lambda d: (d["category_name"], d["bbox"]))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--cs-exe", type=Path, required=True)
    parser.add_argument("--cpp-exe", type=Path, required=True)
    args = parser.parse_args()
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    cases = {False: [], True: []}
    expected = {}
    images = {}
    def add(name, im, texts, *, enhance=False):
        path = output / (name + ".png")
        ok, encoded = cv2.imencode(".png", im)
        assert ok
        path.write_bytes(encoded.tobytes())
        item = {"name": name, "path": str(path)}
        if enhance:
            item["enhance"] = True
        cases[enhance].append(item)
        expected[name] = sorted(texts)
        images[name] = im
    for fmt, text, name in [(zxingcpp.BarcodeFormat.QRCode, "深度视觉-QR", "qr"),
                             (zxingcpp.BarcodeFormat.DataMatrix, "DLCV-DM-123", "dm"),
                             (zxingcpp.BarcodeFormat.Code128, "DLCV-128", "code128")]:
        im = barcode_fixture(name, scale=5)
        add(name, im, [text])
        add(name + "-rotated", np.rot90(im), [text])
        add(name + "-bgr", cv2.cvtColor(im, cv2.COLOR_GRAY2BGR), [text])
        add(name + "-bgra", cv2.cvtColor(im, cv2.COLOR_GRAY2BGRA), [text])
        add(name + "-inverted", 255-im, [] if name == "code128" else [text])
    low_contrast = low_contrast_ean13_image()
    assert zxingcpp.read_barcodes(low_contrast, text_mode=zxingcpp.TextMode.HRI) == []
    add("ean13-low-contrast-default", low_contrast, [])
    add("ean13-low-contrast-enhanced", low_contrast, ["5345898399924"], enhance=True)
    uneven = uneven_data_matrix()
    assert not read_reference(uneven, False) and not read_reference(uneven, True)
    add("dm-uneven-default", uneven, [])
    add("dm-uneven-recovered", uneven, ["T1616"], enhance=True)
    uneven_padded = uneven_data_matrix(padded=True)
    assert not read_reference(uneven_padded, False) and not read_reference(uneven_padded, True)
    add("dm-padded-default", uneven_padded, [])
    add("dm-padded-recovered", uneven_padded, ["T1616"], enhance=True)
    small_dm = square_data_matrix((0x2AA, 0x367, 0x218, 0x24D, 0x23A, 0x3A3, 0x2DA, 0x22F, 0x28A, 0x3FF), 2, 3)
    add("dm-small-default", small_dm, ["17"])
    add("dm-small-enhanced", small_dm, ["17"], enhance=True)
    add("qr-small-default", barcode_fixture("qr", scale=2), ["深度视觉-QR"])
    add("qr-small-enhanced", barcode_fixture("qr", scale=2), ["深度视觉-QR"], enhance=True)
    add("blank-enhanced", np.full((60, 60), 128, np.uint8), [], enhance=True)
    add("blank", np.full((200, 200), 255, np.uint8), [])
    a, b = images["qr"], images["dm"]
    im = np.full((max(a.shape[0], b.shape[0])+40, a.shape[1]+b.shape[1]+80), 255, np.uint8)
    im[20:20+a.shape[0], 20:20+a.shape[1]] = a
    im[20:20+b.shape[0], 60+a.shape[1]:60+a.shape[1]+b.shape[1]] = b
    add("multi", im, expected["qr"]+expected["dm"])
    mixed_scale = mixed_scale_qr_image()
    # 两个真实码只有一个能在原分辨率识别，缩放扫描必须补回另一个。
    assert len(zxingcpp.read_barcodes(mixed_scale, try_downscale=False)) == 1
    assert len(zxingcpp.read_barcodes(mixed_scale, try_downscale=True)) == 2
    add("multi-scale-qr", mixed_scale, ["深度视觉-QR", "深度视觉-QR"])
    default_flow = Path(__file__).resolve().parent / "data" / "barcode_flow.json"
    enhanced_flow = output / "barcode-flow-enhanced.json"
    graph = json.loads(default_flow.read_text(encoding="utf-8"))
    reader = next(node for node in graph["nodes"] if node["type"] == "features/barcode_reader")
    reader["properties"]["enhance"] = True
    enhanced_flow.write_text(json.dumps(graph, ensure_ascii=False), encoding="utf-8")
    paired_reports = []
    for enhance, batch in cases.items():
        suffix = "enhanced" if enhance else "default"
        manifest = output / ("manifest-" + suffix + ".json")
        manifest.write_text(json.dumps(batch, ensure_ascii=False), encoding="utf-8")
        flow = enhanced_flow if enhance else default_flow
        reports = []
        for runtime, exe in [("csharp", args.cs_exe), ("cpp", args.cpp_exe)]:
            report = output / (runtime + "-" + suffix + ".json")
            subprocess.run([str(exe.resolve()), "barcode-selftest", str(manifest), str(flow), str(report)], check=True)
            reports.append(json.loads(report.read_text(encoding="utf-8-sig")))
        assert all(len(report) == len(batch) for report in reports)
        paired_reports.extend((enhance, left, right) for left, right in zip(*reports))
    for enhance, left, right in paired_reports:
        name = left["name"]
        assert name == right["name"]
        for runtime in (left, right):
            for route in ("local", "flow"):
                assert sorted(d["category_name"] for d in runtime[route]) == expected[name], (name, route, runtime[route])
            assert normalized(runtime["local"], local=True) == normalized(runtime["flow"]), (name, "local vs flow", runtime)
        if name in ("dm-uneven-recovered", "dm-padded-recovered"):
            start, width = (0, 96) if name == "dm-uneven-recovered" else (7, 80)
            end = start + width
            reference_quad = np.float32([[start, start], [end, start], [end, end], [start, end]])
            for runtime in (left, right):
                info = runtime["local"][0]["extra_info"]
                assert info["barcode_format"] == "Data Matrix" and info["polyline"][0] == info["polyline"][-1]
                quad = cv2.convexHull(np.float32(info["polyline"][:-1]))
                area, _ = cv2.intersectConvexConvex(reference_quad, quad)
                assert area / (width * width + cv2.contourArea(quad) - area) >= .85, (name, info)
            continue
        assert normalized(left["local"]) == normalized(right["local"]), (name, "C# vs C++")
        image = images[name]
        if image.ndim == 3 and image.shape[2] == 4:
            image = cv2.cvtColor(image, cv2.COLOR_BGRA2BGR)
        python = read_reference(image, enhance)
        assert sorted(c.text for c in python) == expected[name]
        python_geometry = []
        for c in python:
            points = [[p.x, p.y] for p in (c.position.top_left, c.position.top_right, c.position.bottom_right, c.position.bottom_left)]
            python_geometry.append({"barcode_format": str(c.format), "polyline": points + [points[0]]})
        geometry_key = lambda value: json.dumps(value, sort_keys=True)
        assert sorted((d["extra_info"] for d in left["local"]), key=geometry_key) == sorted(python_geometry, key=geometry_key), (name, "Python geometry")
    actual = {left["name"]: (left["local"], right["local"]) for _, left, right in paired_reports}
    for name in ("qr-small", "dm-small"):
        assert actual[name + "-default"] == actual[name + "-enhanced"], name
    print(f"PASS: {len(expected)} fixtures: C#/C++ module and flow results, Data Matrix recovery and coordinate checks")


if __name__ == "__main__":
    main()
