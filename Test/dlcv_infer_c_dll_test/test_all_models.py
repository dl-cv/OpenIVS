import argparse
import ctypes
import json
import os
import sys
import time
from collections import Counter
from pathlib import Path

import cv2
import numpy as np


SUPPORTED_EXTENSIONS = {".dvt", ".dvo", ".dvst", ".dvso"}
DEFAULT_MODEL_ROOT = Path(r"Y:\测试模型")
DEFAULT_CONFIGURATION = "Debug"
DEFAULT_DEVICE = 0
DEFAULT_THRESHOLD = 0.5
DEFAULT_OUTPUT = Path(__file__).resolve().with_name(
    "dlcv_infer_c_dll_test_result.json"
)

DEFAULT_IMAGE_RULES = (
    (("猫狗",), "猫狗-狗.jpg"),
    (("气球",), "气球.jpg"),
    (("手机屏幕",), "手机屏幕.jpg"),
    (("引脚定位",), "引脚定位-目标检测.jpg"),
    (("ocr",), "OCR-472.jpg"),
    (("aoi",), "AOI-1.jpg"),
    (("模型1", "模型2", "模型3", "无cad"), "OK1.png"),
    (("无监督",), "1786969663716.jpg"),
)


class DlcvCImage(ctypes.Structure):
    _fields_ = [
        ("data_ptr", ctypes.c_longlong),
        ("height", ctypes.c_int),
        ("width", ctypes.c_int),
        ("channel", ctypes.c_int),
    ]


class DlcvCImageList(ctypes.Structure):
    _fields_ = [
        ("images", ctypes.POINTER(DlcvCImage)),
        ("n", ctypes.c_int),
    ]


class DlcvCMask(ctypes.Structure):
    _fields_ = [
        ("mask_ptr", ctypes.c_longlong),
        ("height", ctypes.c_int),
        ("width", ctypes.c_int),
    ]


class DlcvCObjectResult(ctypes.Structure):
    _fields_ = [
        ("category_id", ctypes.c_int),
        ("category_name", ctypes.c_void_p),
        ("score", ctypes.c_float),
        ("with_bbox", ctypes.c_bool),
        ("area", ctypes.c_float),
        ("x", ctypes.c_float),
        ("y", ctypes.c_float),
        ("w", ctypes.c_float),
        ("h", ctypes.c_float),
        ("with_mask", ctypes.c_bool),
        ("mask", DlcvCMask),
        ("with_angle", ctypes.c_bool),
        ("angle", ctypes.c_float),
        ("with_mean", ctypes.c_bool),
        ("foreground_mean", ctypes.c_double),
        ("background_mean", ctypes.c_double),
    ]


class DlcvCSampleResult(ctypes.Structure):
    _fields_ = [
        ("results", ctypes.POINTER(DlcvCObjectResult)),
        ("n", ctypes.c_int),
    ]


class DlcvCResult(ctypes.Structure):
    _fields_ = [
        ("code", ctypes.c_int),
        ("message", ctypes.c_void_p),
        ("sample_results", ctypes.POINTER(DlcvCSampleResult)),
        ("n", ctypes.c_int),
    ]


class TestFailure(RuntimeError):
    def __init__(self, stage, message):
        super().__init__(message)
        self.stage = stage


def configure_console():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8")
    if os.name == "nt":
        ctypes.windll.kernel32.SetConsoleOutputCP(65001)
        ctypes.windll.kernel32.SetConsoleCP(65001)


def decode_c_text(value):
    if not value:
        return ""
    if isinstance(value, bytes):
        raw = value
    else:
        raw = ctypes.string_at(value)
    for encoding in ("utf-8", "mbcs"):
        try:
            return raw.decode(encoding)
        except (UnicodeDecodeError, LookupError):
            pass
    return raw.decode("utf-8", errors="replace")


def repository_root():
    return Path(__file__).resolve().parents[2]


def resolve_dll_path(explicit_path, configuration):
    if explicit_path:
        path = Path(explicit_path).resolve()
        if not path.is_file():
            raise FileNotFoundError(f"C DLL 不存在: {path}")
        return path

    root = repository_root()
    candidates = (
        root / "dlcv_infer_c_dll" / configuration / "dlcv_infer_c_dll.dll",
        root / configuration / "dlcv_infer_c_dll.dll",
        root
        / "dlcv_infer_c_qt_demo"
        / configuration
        / "dlcv_infer_c_qt_demo"
        / "dlcv_infer_c_dll.dll",
    )
    for path in candidates:
        if path.is_file():
            return path.resolve()
    searched = "\n".join(str(path) for path in candidates)
    raise FileNotFoundError(f"未找到 dlcv_infer_c_dll.dll，已检查:\n{searched}")


def resolve_cpp_dll_path(c_dll_path, configuration):
    root = repository_root()
    candidates = (
        c_dll_path.parent / "dlcv_infer_cpp_dll.dll",
        root / "dlcv_infer_cpp_dll" / configuration / "dlcv_infer_cpp_dll.dll",
        root / configuration / "dlcv_infer_cpp_dll.dll",
    )
    for path in candidates:
        if path.is_file():
            return path.resolve()
    searched = "\n".join(str(path) for path in candidates)
    raise FileNotFoundError(f"未找到 dlcv_infer_cpp_dll.dll，已检查:\n{searched}")


def add_runtime_directories(dll_path, configuration):
    root = repository_root()
    candidates = (
        dll_path.parent,
        root / "dlcv_infer_cpp_dll" / configuration,
        root / configuration,
        Path(r"C:\dlcv\bin"),
        Path(r"C:\dlcv\Lib\site-packages\dlcvpro_infer"),
        Path(r"C:\OpenCV\build\x64\vc16\bin"),
    )
    handles = []
    if os.name != "nt":
        return handles
    for directory in candidates:
        if directory.is_dir():
            handles.append(os.add_dll_directory(str(directory.resolve())))
    return handles


class DlcvCApi:
    def __init__(self, dll_path):
        self.library = ctypes.CDLL(str(dll_path))
        self._configure()

    def _configure(self):
        library = self.library
        library.dlcv_infer_cpp_load_model_c.argtypes = [ctypes.c_char_p, ctypes.c_int]
        library.dlcv_infer_cpp_load_model_c.restype = ctypes.c_int
        library.dlcv_infer_cpp_get_last_error_c.argtypes = []
        library.dlcv_infer_cpp_get_last_error_c.restype = ctypes.c_char_p
        library.dlcv_infer_cpp_free_model_c.argtypes = [ctypes.c_int]
        library.dlcv_infer_cpp_free_model_c.restype = ctypes.c_int
        library.dlcv_infer_cpp_get_model_info_c.argtypes = [ctypes.c_int]
        library.dlcv_infer_cpp_get_model_info_c.restype = ctypes.c_void_p
        library.dlcv_infer_cpp_infer_with_params_c.argtypes = [
            ctypes.c_int,
            ctypes.POINTER(DlcvCImageList),
            ctypes.c_char_p,
        ]
        library.dlcv_infer_cpp_infer_with_params_c.restype = DlcvCResult
        library.dlcv_infer_cpp_infer_json_c.argtypes = [
            ctypes.c_int,
            ctypes.POINTER(DlcvCImage),
            ctypes.c_char_p,
        ]
        library.dlcv_infer_cpp_infer_json_c.restype = ctypes.c_void_p
        library.dlcv_infer_cpp_free_model_result_c.argtypes = [
            ctypes.POINTER(DlcvCResult)
        ]
        library.dlcv_infer_cpp_free_model_result_c.restype = None
        library.dlcv_infer_cpp_free_string_c.argtypes = [ctypes.c_void_p]
        library.dlcv_infer_cpp_free_string_c.restype = None
        library.dlcv_infer_cpp_free_all_models_c.argtypes = []
        library.dlcv_infer_cpp_free_all_models_c.restype = None

    def last_error(self):
        return decode_c_text(self.library.dlcv_infer_cpp_get_last_error_c())


def load_image_map(path, model_root):
    if path is None:
        return {}
    source_path = Path(path)
    data = json.loads(source_path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("图片映射文件必须是 JSON 对象")
    result = {}
    for model_name, image_value in data.items():
        image_path = Path(image_value)
        if not image_path.is_absolute():
            image_path = model_root / image_path
        result[str(model_name).lower()] = image_path
    return result


def choose_image(model_path, model_root, image_map, default_image):
    exact = image_map.get(model_path.name.lower())
    if exact is not None:
        return exact

    model_name = model_path.stem.lower()
    for keywords, image_name in DEFAULT_IMAGE_RULES:
        if any(keyword in model_name for keyword in keywords):
            return model_root / image_name

    if default_image is not None:
        path = Path(default_image)
        return path if path.is_absolute() else model_root / path
    raise TestFailure("图片映射", f"没有找到测试图片: {model_path.name}")


def decode_image(path):
    if not path.is_file():
        raise TestFailure("图片读取", f"测试图片不存在: {path}")
    encoded = np.fromfile(str(path), dtype=np.uint8)
    image = cv2.imdecode(encoded, cv2.IMREAD_UNCHANGED)
    if image is None:
        raise TestFailure("图片读取", f"图片解码失败: {path}")
    if image.dtype != np.uint8:
        image = cv2.convertScaleAbs(image)

    if image.ndim == 2:
        output = image
    elif image.shape[2] == 3:
        output = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    elif image.shape[2] == 4:
        output = cv2.cvtColor(image, cv2.COLOR_BGRA2RGB)
    else:
        raise TestFailure("图片读取", f"不支持的图片通道: {image.shape}")
    return np.ascontiguousarray(output)


def make_c_image(image):
    channel = 1 if image.ndim == 2 else int(image.shape[2])
    return DlcvCImage(
        int(image.ctypes.data),
        int(image.shape[0]),
        int(image.shape[1]),
        channel,
    )


def run_model(api, model_path, image_path, device_id, params):
    row = {
        "模型": str(model_path),
        "扩展名": model_path.suffix.lower(),
        "图片": str(image_path),
        "加载": False,
        "模型信息": False,
        "结构化推理": False,
        "JSON推理": False,
        "释放": False,
        "样本数": None,
        "目标数": None,
        "错误阶段": "",
        "错误": "",
    }
    model_index = -1

    try:
        started = time.perf_counter()
        model_index = api.library.dlcv_infer_cpp_load_model_c(
            str(model_path).encode("utf-8"), device_id
        )
        row["加载耗时秒"] = round(time.perf_counter() - started, 3)
        if model_index < 0:
            raise TestFailure("加载", api.last_error())
        row["加载"] = True

        info_ptr = api.library.dlcv_infer_cpp_get_model_info_c(model_index)
        if not info_ptr:
            raise TestFailure("模型信息", api.last_error())
        try:
            json.loads(decode_c_text(info_ptr))
            row["模型信息"] = True
        finally:
            api.library.dlcv_infer_cpp_free_string_c(info_ptr)

        image = decode_image(image_path)
        c_image = make_c_image(image)
        image_list = DlcvCImageList(ctypes.pointer(c_image), 1)

        started = time.perf_counter()
        result = api.library.dlcv_infer_cpp_infer_with_params_c(
            model_index, ctypes.byref(image_list), params
        )
        row["结构化推理耗时秒"] = round(time.perf_counter() - started, 3)
        try:
            if result.code != 0:
                raise TestFailure("结构化推理", decode_c_text(result.message))
            row["结构化推理"] = True
            row["样本数"] = result.n
            row["目标数"] = (
                result.sample_results[0].n
                if result.n > 0 and bool(result.sample_results)
                else 0
            )
        finally:
            api.library.dlcv_infer_cpp_free_model_result_c(ctypes.byref(result))

        started = time.perf_counter()
        json_ptr = api.library.dlcv_infer_cpp_infer_json_c(
            model_index, ctypes.byref(c_image), params
        )
        row["JSON推理耗时秒"] = round(time.perf_counter() - started, 3)
        if not json_ptr:
            raise TestFailure("JSON推理", api.last_error())
        try:
            json.loads(decode_c_text(json_ptr))
            row["JSON推理"] = True
        finally:
            api.library.dlcv_infer_cpp_free_string_c(json_ptr)
    except TestFailure as exc:
        row["错误阶段"] = exc.stage
        row["错误"] = str(exc)
    except Exception as exc:
        row["错误阶段"] = "执行"
        row["错误"] = str(exc)
    finally:
        if model_index >= 0:
            row["释放"] = (
                api.library.dlcv_infer_cpp_free_model_c(model_index) == 0
            )
            if not row["释放"] and not row["错误阶段"]:
                row["错误阶段"] = "模型释放"
                row["错误"] = "模型释放返回失败"

    row["通过"] = all(
        row[name]
        for name in ("加载", "模型信息", "结构化推理", "JSON推理", "释放")
    )
    return row


def build_parser():
    parser = argparse.ArgumentParser(
        description="使用 dlcv_infer_c_dll C 接口验证目录中的支持格式模型"
    )
    parser.add_argument(
        "--model-root",
        default=str(DEFAULT_MODEL_ROOT),
        help="模型和测试图片目录，默认 Y:\\测试模型",
    )
    parser.add_argument(
        "--dll",
        help="dlcv_infer_c_dll.dll 路径；未指定时按构建目录查找",
    )
    parser.add_argument(
        "--configuration",
        choices=("Debug", "Release"),
        default=DEFAULT_CONFIGURATION,
        help="构建配置，默认 Debug",
    )
    parser.add_argument(
        "--device", type=int, default=DEFAULT_DEVICE, help="设备编号，默认 0"
    )
    parser.add_argument(
        "--threshold",
        type=float,
        default=DEFAULT_THRESHOLD,
        help="推理阈值，默认 0.5",
    )
    parser.add_argument(
        "--with-mask", action="store_true", help="返回 mask，默认关闭"
    )
    parser.add_argument(
        "--calc-mean", action="store_true", help="计算前景和背景均值，默认关闭"
    )
    parser.add_argument(
        "--image-map",
        help="模型文件名到图片路径的 JSON 映射文件",
    )
    parser.add_argument(
        "--default-image",
        help="没有内置或 JSON 映射时使用的图片",
    )
    parser.add_argument(
        "--output",
        default=str(DEFAULT_OUTPUT),
        help="结果 JSON 路径，默认写入脚本所在目录",
    )
    return parser


def main():
    configure_console()
    args = build_parser().parse_args()
    model_root = Path(args.model_root).resolve()
    if not model_root.is_dir():
        print(f"模型目录不存在: {model_root}", file=sys.stderr)
        return 2

    try:
        dll_path = resolve_dll_path(args.dll, args.configuration)
        cpp_dll_path = resolve_cpp_dll_path(dll_path, args.configuration)
        runtime_handles = add_runtime_directories(dll_path, args.configuration)
        cpp_library = ctypes.CDLL(str(cpp_dll_path))
        api = DlcvCApi(dll_path)
        image_map = load_image_map(args.image_map, model_root)
    except Exception as exc:
        print(f"初始化失败: {exc}", file=sys.stderr)
        return 2

    models = sorted(
        (
            path
            for path in model_root.iterdir()
            if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS
        ),
        key=lambda path: path.name.lower(),
    )
    if not models:
        print("没有找到支持格式模型", file=sys.stderr)
        return 2

    params = json.dumps(
        {
            "threshold": args.threshold,
            "with_mask": args.with_mask,
            "calc_mean": args.calc_mean,
            "batch_size": 1,
        },
        ensure_ascii=False,
    ).encode("utf-8")

    print(f"C DLL: {dll_path}")
    print(f"C++ DLL: {cpp_dll_path}")
    print(f"模型目录: {model_root}")
    print(f"模型数量: {len(models)}")
    print("支持格式: .dvt、.dvo、.dvst、.dvso")

    started = time.perf_counter()
    rows = []
    free_all_error = ""
    try:
        for index, model_path in enumerate(models, 1):
            try:
                image_path = choose_image(
                    model_path,
                    model_root,
                    image_map,
                    args.default_image,
                )
            except TestFailure as exc:
                row = {
                    "模型": str(model_path),
                    "扩展名": model_path.suffix.lower(),
                    "图片": "",
                    "加载": False,
                    "模型信息": False,
                    "结构化推理": False,
                    "JSON推理": False,
                    "释放": False,
                    "样本数": None,
                    "目标数": None,
                    "错误阶段": exc.stage,
                    "错误": str(exc),
                    "通过": False,
                }
            else:
                row = run_model(
                    api,
                    model_path,
                    image_path,
                    args.device,
                    params,
                )
            rows.append(row)

            if row["通过"]:
                print(
                    f"[{index}/{len(models)}] 通过 {model_path.name} "
                    f"目标={row['目标数']} "
                    f"加载={row['加载耗时秒']:.3f}s "
                    f"结构化={row['结构化推理耗时秒']:.3f}s "
                    f"JSON={row['JSON推理耗时秒']:.3f}s"
                )
            else:
                print(
                    f"[{index}/{len(models)}] 失败 {model_path.name} "
                    f"阶段={row['错误阶段']} 错误={row['错误']}"
                )
    finally:
        try:
            api.library.dlcv_infer_cpp_free_all_models_c()
        except OSError as exc:
            free_all_error = str(exc)
            print(f"释放全部模型失败: {free_all_error}", file=sys.stderr)

    duration = round(time.perf_counter() - started, 3)
    passed = sum(1 for row in rows if row["通过"])
    extension_counts = Counter(row["扩展名"] for row in rows)
    summary = {
        "C DLL": str(dll_path),
        "C++ DLL": str(cpp_dll_path),
        "模型目录": str(model_root),
        "文件数": len(rows),
        "扩展名统计": dict(sorted(extension_counts.items())),
        "全部步骤通过": passed,
        "失败": len(rows) - passed,
        "释放全部模型": not free_all_error,
        "释放全部模型错误": free_all_error,
        "总耗时秒": duration,
    }
    report = {"汇总": summary, "结果": rows}

    if args.output:
        output_path = Path(args.output).resolve()
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(
            json.dumps(report, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        print(f"结果文件: {output_path}")

    print(
        f"汇总: 总数={len(rows)} 通过={passed} "
        f"失败={len(rows) - passed} 耗时={duration:.3f}s"
    )
    del cpp_library
    del runtime_handles
    return 0 if passed == len(rows) and not free_all_error else 1


if __name__ == "__main__":
    raise SystemExit(main())
