"""C# 读图回归：用 Pillow/NumPy 独立生成像素参考，不需要模型或 GPU。

在仓库目录执行（使用已有 Python 环境中的 Pillow、NumPy）：
1. python build_script/build.py Test/DemoImageLoadingTest/DemoImageLoadingTest.csproj
2. python Test/DemoImageLoadingTest/generate_cases.py <输出目录>
3. Test/DemoImageLoadingTest/bin/x64/Debug/net472/DemoImageLoadingTest.exe <输出目录>/cases.tsv
追加 --baseline 对照旧 Unchanged 逻辑。C# 只比较原文件、反复覆盖的改名副本。

覆盖常见格式、方向 1–8、灰度、透明、16 位、GIF 首帧及异常元数据。
无损图逐像素相等；JPEG 解码差异容差 2；TIFF 预乘颜色舍入容差 1。
测试材料生成到指定目录，不提交客户图片；不验证 Windows 右键菜单写文件过程。
"""
import sys
import struct
import zlib
from pathlib import Path
import numpy as np
from PIL import Image

output = Path(sys.argv[1]).resolve()
output.mkdir(parents=True, exist_ok=True)
y, x = np.indices((24, 40))
rgb = np.stack((x * 5, y * 9, (x + y) * 3), axis=-1).astype("uint8")
transforms = {2: Image.Transpose.FLIP_LEFT_RIGHT, 3: Image.Transpose.ROTATE_180,
              4: Image.Transpose.FLIP_TOP_BOTTOM, 5: Image.Transpose.TRANSPOSE,
              6: Image.Transpose.ROTATE_270, 7: Image.Transpose.TRANSVERSE,
              8: Image.Transpose.ROTATE_90}
cases = []

def add_case(name, image, extension, orientation=None, **options):
    path = output / (name + extension)
    exif = Image.Exif()
    if orientation is not None:
        exif[274] = orientation
        options["exif"] = exif
    image.save(path, **options)
    # JPEG and palette formats need a decoded reference for their encoding loss.
    if extension in (".jpg", ".jpeg", ".gif"):
        with Image.open(path) as decoded:
            expected = decoded.convert("RGBA" if extension == ".gif" else image.mode)
    else:
        expected = image.copy()
    # OpenCV/libtiff returns 8-bit RGBA TIFF with premultiplied color values.
    # Retain that existing decoder contract while testing orientation and alpha.
    if extension in (".tif", ".tiff") and image.mode == "RGBA":
        pixels = np.asarray(expected).astype("uint32")
        pixels[..., :3] = (pixels[..., :3] * pixels[..., 3:4] + 127) // 255
        expected = Image.fromarray(pixels.astype("uint8"))
    if extension == ".gif":
        pixels = np.asarray(expected).copy()
        pixels[pixels[..., 3] == 0, :3] = 0
        expected = Image.fromarray(pixels)
    if orientation in transforms:
        expected = expected.transpose(transforms[orientation])
    reference = output / (name + "-expected.png")
    expected.save(reference)
    cases.append((name, path, reference, 2 if extension in (".jpg", ".jpeg") else 1 if extension in (".tif", ".tiff") and image.mode == "RGBA" else 0))

for extension in (".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".gif"):
    image = Image.fromarray(rgb).convert("L" if extension == ".jpeg" else "RGB")
    options = {"lossless": True} if extension == ".webp" else {}
    add_case(extension[1:] + "-pixels-rotated", image.transpose(Image.Transpose.ROTATE_180), extension, **options)
    if extension in (".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"):
        for orientation in range(1, 9):
            add_case(extension[1:] + "-exif-" + str(orientation), image, extension, orientation, **options)
for extension in (".png", ".tif", ".webp"):
    rgba = np.concatenate((rgb, ((x * 5 + 50).astype("uint8"))[..., None]), axis=-1)
    for orientation in (None, *range(1, 9)):
        add_case(extension[1:] + "-alpha-" + str(orientation), Image.fromarray(rgba), extension,
                 orientation, **({"lossless": True} if extension == ".webp" else {}))
for extension in (".png", ".tif"):
    for orientation in (None, *range(1, 9)):
        add_case(extension[1:] + "-16bit-" + str(orientation), Image.fromarray((x * 1000 + y * 300).astype("uint16")), extension, orientation)
for extension in (".jpg", ".png", ".bmp", ".tif", ".gif"):
    add_case(extension[1:] + "-gray", Image.fromarray((x * 4 + y * 2).astype("uint8")), extension)

# GIF transparency and animation use the first displayed frame, never all frames.
palette = Image.fromarray(rgb).quantize(colors=64)
palette.info["transparency"] = 0
add_case("gif-transparent", palette.convert("RGBA"), ".gif", transparency=0)
first = Image.fromarray(rgb)
second = first.transpose(Image.Transpose.ROTATE_180)
animated = output / "gif-animated.gif"
first.save(animated, save_all=True, append_images=[second], duration=100, loop=0)
with Image.open(animated) as frame:
    frame.convert("RGBA").save(output / "gif-animated-expected.png")
cases.append(("gif-first-frame", animated, output / "gif-animated-expected.png", 0))

# Test metadata byte order, optional WebP EXIF prefix and malformed offsets/counts.
for extension in (".png", ".webp"):
    clean = output / ("metadata-clean" + extension)
    Image.fromarray(rgb).save(clean, **({"lossless": True} if extension == ".webp" else {}))
    base = clean.read_bytes()
    payloads = {}
    for endian, marker in ((">", b"MM"), ("<", b"II")):
        tag = marker + struct.pack(endian + "HIH", 42, 8, 1)
        tag += struct.pack(endian + "HHIHHI", 274, 3, 1, 6, 0, 0)
        payloads["big-endian" if endian == ">" else "little-endian"] = (tag, 6)
    payloads["exif-prefix"] = (b"Exif\0\0" + payloads["little-endian"][0], 6)
    payloads["short-metadata"] = (b"II", 1)
    payloads["invalid-offset"] = (b"II" + struct.pack("<HI", 42, 0xffffffff), 1)
    payloads["missing-entry"] = (b"II" + struct.pack("<HIH", 42, 8, 65535), 1)
    for name, (payload, orientation) in payloads.items():
        case = extension[1:] + "-metadata-" + name
        if extension == ".png":
            chunk = b"eXIf" + payload
            chunk = struct.pack(">I", len(payload)) + chunk + struct.pack(">I", zlib.crc32(chunk))
            encoded = base[:33] + chunk + base[33:]
        else:
            chunk = b"EXIF" + struct.pack("<I", len(payload)) + payload + (b"\0" if len(payload) % 2 else b"")
            encoded = base + chunk
            encoded = encoded[:4] + struct.pack("<I", len(encoded) - 8) + encoded[8:]
        path = output / (case + extension)
        path.write_bytes(encoded)
        expected = Image.fromarray(rgb)
        if orientation == 6:
            expected = expected.transpose(Image.Transpose.ROTATE_270)
        reference = output / (case + "-expected.png")
        expected.save(reference)
        cases.append((case, path, reference, 0))

(output / "cases.tsv").write_text("\n".join("\t".join(map(str, case)) for case in cases), encoding="utf-8")
print("Generated", len(cases), "format/orientation cases")
