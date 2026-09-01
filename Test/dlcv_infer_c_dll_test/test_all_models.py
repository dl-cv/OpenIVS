import argparse
import ctypes
import json
import math
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
    "dlcv_infer_c_api_test_result.json"
)
DEFAULT_EXPECTED_FAILURES = Path(__file__).resolve().with_name(
    "expected_failures.json"
)

DEFAULT_IMAGE_RULES = (
    (("猫狗",), "猫狗-狗.jpg"),
    (("气球",), "气球.jpg"),
    (("手机屏幕",), "手机屏幕.jpg"),
    (("引脚定位",), "引脚定位-目标检测.jpg"),
    (("ocr",), "OCR-472.jpg"),
    (("模型1", "模型2", "模型3", "无cad"), "OK1.png"),
    (("aoi",), "AOI-1.jpg"),
    (("无监督",), "1786969663716.jpg"),
)

FLOAT_TOLERANCES = {
    "score": 1e-5,
    "area": 1e-3,
    "bbox": 1e-3,
    "angle": 1e-3,
    "foreground_mean": 1e-6,
    "background_mean": 1e-6,
}
FLOAT_RELATIVE_TOLERANCE = 1e-6


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
            raise FileNotFoundError(f"dlcv_infer_cpp.dll 不存在: {path}")
        return path

    root = repository_root()
    candidates = (
        root / "dlcv_infer_cpp" / configuration / "dlcv_infer_cpp.dll",
        root / configuration / "dlcv_infer_cpp.dll",
    )
    for path in candidates:
        if path.is_file():
            return path.resolve()
    searched = "\n".join(str(path) for path in candidates)
    raise FileNotFoundError(f"未找到 dlcv_infer_cpp.dll，已检查:\n{searched}")


def add_runtime_directories(dll_path, configuration):
    root = repository_root()
    candidates = (
        dll_path.parent,
        root / "dlcv_infer_cpp" / configuration,
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


def load_expected_failures(path):
    if not path:
        return {}
    source_path = Path(path)
    data = json.loads(source_path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("预期失败配置必须是 JSON 对象")

    result = {}
    for model_name, value in data.items():
        if not isinstance(value, dict):
            raise ValueError(f"预期失败配置无效: {model_name}")
        stage = value.get("阶段")
        error_contains = value.get("错误包含")
        if not isinstance(stage, str) or not stage:
            raise ValueError(f"预期失败阶段无效: {model_name}")
        if not isinstance(error_contains, str) or not error_contains:
            raise ValueError(f"预期失败错误文本无效: {model_name}")
        result[str(model_name).lower()] = {
            "阶段": stage,
            "错误包含": error_contains,
        }
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


def normalize_number(value, field_name):
    try:
        number = float(value)
    except (TypeError, ValueError) as exc:
        raise TestFailure("结果解析", f"{field_name} 不是数值: {value!r}") from exc
    if not math.isfinite(number):
        raise TestFailure("结果解析", f"{field_name} 不是有限数值: {number!r}")
    return number


def normalize_bbox(value, with_bbox):
    if not with_bbox:
        return [0.0, 0.0, 0.0, 0.0]
    if not isinstance(value, (list, tuple)) or len(value) < 4:
        raise TestFailure("结果解析", f"with_bbox=true 时 bbox 无效: {value!r}")
    return [normalize_number(value[index], f"bbox[{index}]") for index in range(4)]


def normalize_prediction_object(value):
    if not isinstance(value, dict):
        raise TestFailure("结果解析", f"预测对象不是 JSON 对象: {value!r}")

    with_bbox = bool(value.get("with_bbox", False))
    with_angle = bool(value.get("with_angle", False))
    with_mean = bool(value.get("with_mean", False))
    return {
        "category_id": int(value.get("category_id", 0)),
        "category_name": str(value.get("category_name", "")),
        "score": normalize_number(value.get("score", 0.0), "score"),
        "with_bbox": with_bbox,
        "area": normalize_number(value.get("area", 0.0), "area"),
        "bbox": normalize_bbox(value.get("bbox", ()), with_bbox),
        "with_mask": bool(value.get("with_mask", False)),
        "with_angle": with_angle,
        "angle": (
            normalize_number(value.get("angle", -100.0), "angle")
            if with_angle
            else -100.0
        ),
        "with_mean": with_mean,
        "foreground_mean": (
            normalize_number(value.get("foreground_mean", 0.0), "foreground_mean")
            if with_mean
            else 0.0
        ),
        "background_mean": (
            normalize_number(value.get("background_mean", 0.0), "background_mean")
            if with_mean
            else 0.0
        ),
    }


def prediction_sort_key(value):
    return (
        value["category_id"],
        value["category_name"],
        value["with_bbox"],
        tuple(round(number, 6) for number in value["bbox"]),
        round(value["score"], 6),
        value["with_angle"],
        round(value["angle"], 6),
        value["with_mean"],
    )


def normalize_sample(objects):
    if not isinstance(objects, list):
        raise TestFailure("结果解析", "预测结果必须是数组")
    normalized = [normalize_prediction_object(value) for value in objects]
    return sorted(normalized, key=prediction_sort_key)


def copy_structured_result(result):
    samples = []
    if result.n < 0:
        raise TestFailure("结果解析", f"结构化结果样本数无效: {result.n}")
    if result.n > 0 and not bool(result.sample_results):
        raise TestFailure("结果解析", "结构化结果样本指针为空")

    for sample_index in range(result.n):
        sample = result.sample_results[sample_index]
        if sample.n < 0:
            raise TestFailure(
                "结果解析", f"结构化结果第 {sample_index} 个样本目标数无效: {sample.n}"
            )
        if sample.n > 0 and not bool(sample.results):
            raise TestFailure(
                "结果解析", f"结构化结果第 {sample_index} 个样本目标指针为空"
            )

        objects = []
        for object_index in range(sample.n):
            value = sample.results[object_index]
            objects.append(
                {
                    "category_id": int(value.category_id),
                    "category_name": decode_c_text(value.category_name),
                    "score": float(value.score),
                    "with_bbox": bool(value.with_bbox),
                    "area": float(value.area),
                    "bbox": [float(value.x), float(value.y), float(value.w), float(value.h)],
                    "with_mask": bool(value.with_mask),
                    "with_angle": bool(value.with_angle),
                    "angle": float(value.angle),
                    "with_mean": bool(value.with_mean),
                    "foreground_mean": float(value.foreground_mean),
                    "background_mean": float(value.background_mean),
                }
            )
        samples.append(normalize_sample(objects))
    return samples


def normalize_json_result(value):
    if isinstance(value, dict):
        if "result_list" in value:
            value = value["result_list"]
        elif "sample_results" in value:
            samples = value["sample_results"]
            if not isinstance(samples, list):
                raise TestFailure("结果解析", "JSON sample_results 必须是数组")
            return [normalize_sample(sample.get("results", [])) for sample in samples]
        else:
            raise TestFailure("结果解析", "JSON 结果缺少 result_list 或 sample_results")

    if not isinstance(value, list):
        raise TestFailure("结果解析", "JSON 推理结果必须是数组或结果对象")

    if value and all(isinstance(item, dict) and "result_list" in item for item in value):
        return [normalize_sample(item["result_list"]) for item in value]
    return [normalize_sample(value)]


def compare_predictions(structured_samples, json_samples):
    differences = []
    if len(structured_samples) != len(json_samples):
        return [
            f"样本数不一致: 结构化={len(structured_samples)} JSON={len(json_samples)}"
        ]

    exact_fields = (
        "category_id",
        "category_name",
        "with_bbox",
        "with_mask",
        "with_angle",
        "with_mean",
    )
    number_fields = (
        "score",
        "area",
        "angle",
        "foreground_mean",
        "background_mean",
    )
    for sample_index, (structured, json_values) in enumerate(
        zip(structured_samples, json_samples)
    ):
        if len(structured) != len(json_values):
            differences.append(
                f"样本 {sample_index} 目标数不一致: "
                f"结构化={len(structured)} JSON={len(json_values)}"
            )
            continue

        for object_index, (left, right) in enumerate(zip(structured, json_values)):
            prefix = f"样本 {sample_index} 目标 {object_index}"
            for field_name in exact_fields:
                if left[field_name] != right[field_name]:
                    differences.append(
                        f"{prefix} {field_name} 不一致: "
                        f"结构化={left[field_name]!r} JSON={right[field_name]!r}"
                    )
            for field_name in number_fields:
                if not math.isclose(
                    left[field_name],
                    right[field_name],
                    rel_tol=FLOAT_RELATIVE_TOLERANCE,
                    abs_tol=FLOAT_TOLERANCES[field_name],
                ):
                    differences.append(
                        f"{prefix} {field_name} 不一致: "
                        f"结构化={left[field_name]!r} JSON={right[field_name]!r} "
                        f"容差={FLOAT_TOLERANCES[field_name]}"
                    )
            for coordinate_index, (left_value, right_value) in enumerate(
                zip(left["bbox"], right["bbox"])
            ):
                if not math.isclose(
                    left_value,
                    right_value,
                    rel_tol=FLOAT_RELATIVE_TOLERANCE,
                    abs_tol=FLOAT_TOLERANCES["bbox"],
                ):
                    differences.append(
                        f"{prefix} bbox[{coordinate_index}] 不一致: "
                        f"结构化={left_value!r} JSON={right_value!r} "
                        f"容差={FLOAT_TOLERANCES['bbox']}"
                    )
    return differences


def evaluate_expected_outcome(row, model_name, expected_failures):
    expected = expected_failures.get(model_name.lower())
    row["预期失败"] = expected is not None
    row["预期失败条件"] = expected or {}
    if expected is None:
        row["符合预期"] = row["通过"]
        row["测试状态"] = "通过" if row["通过"] else "意外失败"
        return

    if row["通过"]:
        row["符合预期"] = False
        row["测试状态"] = "预期失败未发生"
        return

    row["符合预期"] = (
        row["错误阶段"] == expected["阶段"]
        and expected["错误包含"] in row["错误"]
    )
    row["测试状态"] = "预期失败" if row["符合预期"] else "意外失败"


def run_model(api, model_path, image_path, device_id, params):
    row = {
        "模型": str(model_path),
        "扩展名": model_path.suffix.lower(),
        "图片": str(image_path),
        "加载": False,
        "模型信息": False,
        "结构化推理": False,
        "JSON推理": False,
        "结果一致": False,
        "释放": False,
        "样本数": None,
        "目标数": None,
        "错误阶段": "",
        "错误": "",
        "结构化C预测": [],
        "JSON C预测": [],
        "统一预测": [],
        "一致性差异": [],
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
            structured_predictions = copy_structured_result(result)
            row["结构化推理"] = True
            row["样本数"] = result.n
            row["目标数"] = (
                result.sample_results[0].n
                if result.n > 0 and bool(result.sample_results)
                else 0
            )
            row["结构化C预测"] = structured_predictions
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
            json_predictions = normalize_json_result(
                json.loads(decode_c_text(json_ptr))
            )
            row["JSON推理"] = True
        finally:
            api.library.dlcv_infer_cpp_free_string_c(json_ptr)

        row["JSON C预测"] = json_predictions
        row["一致性差异"] = compare_predictions(
            structured_predictions, json_predictions
        )
        row["结果一致"] = not row["一致性差异"]
        if not row["结果一致"]:
            raise TestFailure("结果一致性", "; ".join(row["一致性差异"][:10]))
        row["统一预测"] = structured_predictions
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
        for name in (
            "加载",
            "模型信息",
            "结构化推理",
            "JSON推理",
            "结果一致",
            "释放",
        )
    )
    return row


def build_parser():
    parser = argparse.ArgumentParser(
        description="使用 dlcv_infer_cpp.dll 的 C 接口验证目录中的支持格式模型"
    )
    parser.add_argument(
        "--model-root",
        default=str(DEFAULT_MODEL_ROOT),
        help="模型和测试图片目录，默认 Y:\\测试模型",
    )
    parser.add_argument(
        "--dll",
        help="dlcv_infer_cpp.dll 路径；未指定时按构建目录查找",
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
        "--expected-failures",
        default=str(DEFAULT_EXPECTED_FAILURES),
        help="预期失败 JSON 配置；传入空字符串可禁用",
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
    model_root = Path(args.model_root)
    if not model_root.is_dir():
        print(f"模型目录不存在: {model_root}", file=sys.stderr)
        return 2

    try:
        dll_path = resolve_dll_path(args.dll, args.configuration)
        runtime_handles = add_runtime_directories(dll_path, args.configuration)
        api = DlcvCApi(dll_path)
        image_map = load_image_map(args.image_map, model_root)
        expected_failures = load_expected_failures(args.expected_failures)
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

    print(f"DLL: {dll_path}")
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
                    "结果一致": False,
                    "释放": False,
                    "样本数": None,
                    "目标数": None,
                    "错误阶段": exc.stage,
                    "错误": str(exc),
                    "结构化C预测": [],
                    "JSON C预测": [],
                    "统一预测": [],
                    "一致性差异": [],
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
            evaluate_expected_outcome(row, model_path.name, expected_failures)
            rows.append(row)

            if row["测试状态"] == "通过":
                print(
                    f"[{index}/{len(models)}] 通过 {model_path.name} "
                    f"目标={row['目标数']} "
                    "一致=是 "
                    f"加载={row['加载耗时秒']:.3f}s "
                    f"结构化={row['结构化推理耗时秒']:.3f}s "
                    f"JSON={row['JSON推理耗时秒']:.3f}s"
                )
            elif row["测试状态"] == "预期失败":
                print(
                    f"[{index}/{len(models)}] 预期失败 {model_path.name} "
                    f"阶段={row['错误阶段']} 错误={row['错误']}"
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
    expected_failed = sum(1 for row in rows if row["测试状态"] == "预期失败")
    unexpected_failed = sum(1 for row in rows if row["测试状态"] == "意外失败")
    unexpected_passed = sum(
        1 for row in rows if row["测试状态"] == "预期失败未发生"
    )
    matched = sum(1 for row in rows if row["符合预期"])
    extension_counts = Counter(row["扩展名"] for row in rows)
    summary = {
        "DLL": str(dll_path),
        "模型目录": str(model_root),
        "文件数": len(rows),
        "扩展名统计": dict(sorted(extension_counts.items())),
        "全部步骤通过": passed,
        "结果一致": sum(1 for row in rows if row["结果一致"]),
        "预期失败": expected_failed,
        "意外失败": unexpected_failed,
        "预期失败未发生": unexpected_passed,
        "符合预期": matched,
        "释放全部模型": not free_all_error,
        "释放全部模型错误": free_all_error,
        "总耗时秒": duration,
        "数值比较": {
            "相对容差": FLOAT_RELATIVE_TOLERANCE,
            "绝对容差": FLOAT_TOLERANCES,
        },
        "mask比较": "默认关闭 mask；只比较 with_mask 状态，不比较 mask 内容",
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
        f"汇总: 总数={len(rows)} 通过={passed} 预期失败={expected_failed} "
        f"意外失败={unexpected_failed} 预期失败未发生={unexpected_passed} "
        f"耗时={duration:.3f}s"
    )
    del runtime_handles
    return 0 if matched == len(rows) and not free_all_error else 1


if __name__ == "__main__":
    raise SystemExit(main())
