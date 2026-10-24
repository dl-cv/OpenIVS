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


def read_reference(image):
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY) if image.ndim == 3 else image
    for source in (image, cv2.equalizeHist(gray)):
        codes = zxingcpp.read_barcodes(source, try_downscale=True, text_mode=zxingcpp.TextMode.HRI)
        if codes:
            return codes
    return []


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
    cases = []
    expected = {}
    images = {}
    def add(name, im, texts):
        path = output / (name + ".png")
        ok, encoded = cv2.imencode(".png", im)
        assert ok
        path.write_bytes(encoded.tobytes())
        cases.append({"name": name, "path": str(path)})
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
    add("ean13-low-contrast", low_contrast, ["5345898399924"])
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
    manifest = output / "manifest.json"
    manifest.write_text(json.dumps(cases, ensure_ascii=False), encoding="utf-8")
    flow = Path(__file__).resolve().parent / "data" / "barcode_flow.json"
    reports = []
    for runtime, exe in [("csharp", args.cs_exe), ("cpp", args.cpp_exe)]:
        report = output / (runtime + ".json")
        subprocess.run([str(exe.resolve()), "barcode-selftest", str(manifest), str(flow), str(report)], check=True)
        reports.append(json.loads(report.read_text(encoding="utf-8-sig")))
    for left, right in zip(*reports):
        name = left["name"]
        assert name == right["name"]
        for runtime in (left, right):
            for route in ("local", "flow"):
                assert sorted(d["category_name"] for d in runtime[route]) == expected[name], (name, route, runtime[route])
            assert normalized(runtime["local"], local=True) == normalized(runtime["flow"]), (name, "local vs flow", runtime)
        assert normalized(left["local"]) == normalized(right["local"]), (name, "C# vs C++")
        image = images[name]
        if image.ndim == 3 and image.shape[2] == 4:
            image = cv2.cvtColor(image, cv2.COLOR_BGRA2BGR)
        python = read_reference(image)
        assert sorted(c.text for c in python) == expected[name]
        python_geometry = []
        for c in python:
            points = [[p.x, p.y] for p in (c.position.top_left, c.position.top_right, c.position.bottom_right, c.position.bottom_left)]
            python_geometry.append({"barcode_format": str(c.format), "polyline": points + [points[0]]})
        geometry_key = lambda value: json.dumps(value, sort_keys=True)
        assert sorted((d["extra_info"] for d in left["local"]), key=geometry_key) == sorted(python_geometry, key=geometry_key), (name, "Python geometry")
    print(f"PASS: {len(cases)} fixtures, C#/C++ modules + exported flow API + Python geometry parity")


if __name__ == "__main__":
    main()
