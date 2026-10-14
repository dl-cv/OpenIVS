"""Generate synthetic fixtures and verify real C#/C++ executables (no SDK/model required)."""
import argparse
import json
from pathlib import Path
import subprocess

import cv2
import numpy as np
import zxingcpp


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
        assert cv2.imwrite(str(path), im)
        cases.append({"name": name, "path": str(path)})
        expected[name] = sorted(texts)
        images[name] = im
    for fmt, text, name in [(zxingcpp.BarcodeFormat.QRCode, "深度视觉-QR", "qr"),
                             (zxingcpp.BarcodeFormat.DataMatrix, "DLCV-DM-123", "dm"),
                             (zxingcpp.BarcodeFormat.Code128, "DLCV-128", "code128")]:
        im = np.asarray(zxingcpp.write_barcode_to_image(zxingcpp.create_barcode(text, fmt), scale=5))
        add(name, im, [text])
        add(name + "-rotated", np.rot90(im), [text])
        add(name + "-bgr", cv2.cvtColor(im, cv2.COLOR_GRAY2BGR), [text])
        add(name + "-bgra", cv2.cvtColor(im, cv2.COLOR_GRAY2BGRA), [text])
        add(name + "-inverted", 255-im, [] if name == "code128" else [text])
    add("blank", np.full((200, 200), 255, np.uint8), [])
    a, b = images["qr"], images["dm"]
    im = np.full((max(a.shape[0], b.shape[0])+40, a.shape[1]+b.shape[1]+80), 255, np.uint8)
    im[20:20+a.shape[0], 20:20+a.shape[1]] = a
    im[20:20+b.shape[0], 60+a.shape[1]:60+a.shape[1]+b.shape[1]] = b
    add("multi", im, expected["qr"]+expected["dm"])
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
        python = zxingcpp.read_barcodes(image, text_mode=zxingcpp.TextMode.HRI)
        assert sorted(c.text for c in python) == expected[name]
        for c in python:
            d = next(d for d in left["local"] if d["category_name"] == c.text)
            points = [[p.x, p.y] for p in (c.position.top_left, c.position.top_right, c.position.bottom_right, c.position.bottom_left)]
            assert d["extra_info"] == {"barcode_format": str(c.format), "polyline": points + [points[0]]}
    print(f"PASS: {len(cases)} fixtures, C#/C++ modules + exported flow API + Python geometry parity")


if __name__ == "__main__":
    main()
