"""从真实完整窗口截图检查首行两个蓝色跨列按钮的文字中心。"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


def check_button_centers(image_path):
    pixels = np.asarray(Image.open(image_path).convert("RGB"))
    blue = np.all(pixels == (25, 118, 210), axis=2)
    for top, row in enumerate(blue):
        edges = np.flatnonzero(np.diff(np.r_[False, row, False].astype(int)))
        spans = [(int(left), int(right)) for left, right in zip(edges[::2], edges[1::2]) if right - left >= 50]
        if len(spans) == 2:
            break
    else:
        raise AssertionError("未找到首行的两个蓝色按钮。")

    results = []
    for name, (left, right) in zip(("load_model", "open_image"), spans):
        bottom = top
        while bottom < blue.shape[0] and blue[bottom, left + 2]:
            bottom += 1
        inner = pixels[top + 3:bottom - 3, left + 3:right - 3]
        text_y, text_x = np.where(np.all(inner > 210, axis=2))
        if not text_x.size:
            raise AssertionError(name + " 未找到白色按钮文字。")
        text_center_x = (int(text_x.min()) + int(text_x.max()) + 1) / 2 + left + 3
        text_center_y = (int(text_y.min()) + int(text_y.max()) + 1) / 2 + top + 3
        offset_x = text_center_x - (left + right) / 2
        offset_y = text_center_y - (top + bottom) / 2
        results.append({"button": name, "visible_bounds": [left, top, right, bottom],
                        "text_center_offset_px": [offset_x, offset_y],
                        "passed": abs(offset_x) <= 2 and abs(offset_y) <= 2})
    return {"passed": all(item["passed"] for item in results), "buttons": results}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = check_button_centers(args.image)
    text = json.dumps(result, ensure_ascii=False, indent=2)
    print(text)
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
