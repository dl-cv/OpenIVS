import ctypes
import importlib.util
import json
import re
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().with_name("test_all_models.py")
MODULE_SPEC = importlib.util.spec_from_file_location("dlcv_c_all_models", MODULE_PATH)
all_models = importlib.util.module_from_spec(MODULE_SPEC)
MODULE_SPEC.loader.exec_module(all_models)


def make_prediction(**overrides):
    value = {
        "category_id": 2,
        "category_name": "目标",
        "score": 0.75,
        "with_bbox": True,
        "area": 120.0,
        "bbox": [10.0, 20.0, 30.0, 40.0],
        "with_mask": False,
        "with_angle": False,
        "angle": -100.0,
        "extra_info": {},
    }
    value.update(overrides)
    return value


class ModelSelectionTest(unittest.TestCase):
    def test_excludes_only_unsupported_rotated_dvo(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ("AOI-旋转框检测_s.dvo", "AOI-旋转框检测_s.dvt", "其他.dvo"):
                (root / name).touch()
            (root / "图片.png").touch()
            (root / "目录.dvo").mkdir()
            models, excluded = all_models.discover_models(root)
            self.assertEqual(
                ["AOI-旋转框检测_s.dvt", "其他.dvo"],
                [path.name for path in models],
            )
            self.assertEqual(1, len(excluded))
            self.assertEqual("AOI-旋转框检测_s.dvo", Path(excluded[0]["模型"]).name)
            self.assertIn("MMCVRoIAlignRotated", excluded[0]["原因"])

    def test_exclusion_ignores_filename_case(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "aoi-旋转框检测_S.DVO").touch()
            models, excluded = all_models.discover_models(root)
            self.assertEqual([], models)
            self.assertEqual(1, len(excluded))

    def test_default_expected_failures_is_empty(self):
        self.assertEqual(
            {}, all_models.load_expected_failures(all_models.DEFAULT_EXPECTED_FAILURES)
        )


class ImageSelectionTest(unittest.TestCase):
    def test_aoi_without_cad_uses_ok_image(self):
        root = Path(r"Y:\测试模型")
        model = root / "AOI-无CAD检测_PLUS_s.dvst"
        selected = all_models.choose_image(model, root, {}, None)
        self.assertEqual(root / "OK1.png", selected)

    def test_common_aoi_uses_aoi_image(self):
        root = Path(r"Y:\测试模型")
        model = root / "AOI-元件提取_PLUS_s.dvst"
        selected = all_models.choose_image(model, root, {}, None)
        self.assertEqual(root / "AOI-1.jpg", selected)


class ResultNormalizationTest(unittest.TestCase):
    def test_json_array_and_flow_wrapper_have_same_shape(self):
        prediction = make_prediction()
        regular = all_models.normalize_json_result([prediction])
        flow = all_models.normalize_json_result({"result_list": [prediction]})
        self.assertEqual(regular, flow)

    def test_structured_result_is_copied_and_normalized(self):
        name_buffer = ctypes.create_string_buffer("目标".encode("utf-8"))
        objects = (all_models.DlcvCObjectResult * 1)()
        objects[0].category_id = 2
        objects[0].category_name = ctypes.cast(name_buffer, ctypes.c_void_p).value
        objects[0].score = 0.75
        objects[0].with_bbox = True
        objects[0].area = 120.0
        objects[0].x = 10.0
        objects[0].y = 20.0
        objects[0].w = 30.0
        objects[0].h = 40.0
        objects[0].with_mask = False
        objects[0].with_angle = False
        objects[0].angle = 0.0
        objects[0].extra_info = None

        samples = (all_models.DlcvCSampleResult * 1)()
        samples[0].results = objects
        samples[0].n = 1
        result = all_models.DlcvCResult()
        result.code = 0
        result.sample_results = samples
        result.n = 1

        copied = all_models.copy_structured_result(result)
        objects[0].category_id = 99

        self.assertEqual(2, copied[0][0]["category_id"])
        self.assertEqual("目标", copied[0][0]["category_name"])
        self.assertEqual(-100.0, copied[0][0]["angle"])


class PredictionComparisonTest(unittest.TestCase):
    def test_float_values_inside_tolerance_match(self):
        structured = [[make_prediction(score=0.750004, bbox=[10.0005, 20, 30, 40])]]
        json_values = [[make_prediction(score=0.75, bbox=[10.0, 20, 30, 40])]]
        self.assertEqual([], all_models.compare_predictions(structured, json_values))

    def test_float_values_outside_tolerance_do_not_match(self):
        structured = [[make_prediction(bbox=[10.01, 20, 30, 40])]]
        json_values = [[make_prediction(bbox=[10.0, 20, 30, 40])]]
        differences = all_models.compare_predictions(structured, json_values)
        self.assertTrue(any("bbox[0]" in value for value in differences))

    def test_mask_content_is_not_part_of_stable_fields(self):
        structured = [[make_prediction(with_mask=False)]]
        json_values = [[make_prediction(with_mask=False, mask=[1, 2, 3])]]
        self.assertEqual([], all_models.compare_predictions(structured, json_values))



class ExtraInfoResultTest(unittest.TestCase):
    def copy_prediction(self, extra):
        buffer = (ctypes.create_string_buffer(json.dumps(extra, ensure_ascii=False).encode("utf-8"))
                  if extra is not None else None)
        objects = (all_models.DlcvCObjectResult * 1)()
        objects[0].score = 0.75
        objects[0].extra_info = ctypes.cast(buffer, ctypes.c_void_p).value if buffer else None
        samples = (all_models.DlcvCSampleResult * 1)()
        samples[0].results = objects
        samples[0].n = 1
        result = all_models.DlcvCResult()
        result.sample_results = samples
        result.n = 1
        copied = all_models.copy_structured_result(result)
        if buffer:
            ctypes.memset(ctypes.addressof(buffer), 0, ctypes.sizeof(buffer))
        return copied

    def test_c_abi_fields_match_public_header(self):
        root = MODULE_PATH.parents[2]
        header = (root / "dlcv_infer_cpp/dlcv_infer_c_api.h").read_text(encoding="utf-8-sig")
        body = re.search(r"typedef struct DlcvCObjectResult \{(.*?)\} DlcvCObjectResult;", header, re.S).group(1)
        body = re.sub(r"//[^\n]*|/\*.*?\*/", "", body, flags=re.S)
        declarations = re.findall(r"(int|char\s*\*|float|bool|DlcvCMask)\s+(\w+(?:\s*,\s*\w+)*)\s*;", body)
        types = {"int": ctypes.c_int, "char*": ctypes.c_void_p, "float": ctypes.c_float,
                 "bool": ctypes.c_bool, "DlcvCMask": all_models.DlcvCMask}
        expected = [(name.strip(), types[kind.replace(" ", "")])
                    for kind, names in declarations for name in names.split(",")]
        self.assertEqual(expected, all_models.DlcvCObjectResult._fields_)
        self.assertEqual("extra_info", expected[-1][0])
        self.assertNotIn("with_mean", [name for name, kind in expected])
        self.assertEqual(80, ctypes.sizeof(all_models.DlcvCObjectResult))
        self.assertEqual(72, all_models.DlcvCObjectResult.extra_info.offset)

    def test_independent_statistics_groups_and_other_extensions_survive_copy(self):
        business = {"polyline": [[1, 2], [3, 4]], "标签": "合格", "nested": {"enabled": True}}
        for mean, median in ((False, False), (True, False), (False, True), (True, True)):
            with self.subTest(mean=mean, median=median):
                extra = dict(business)
                if mean:
                    extra.update(with_mean=True, foreground_mean=0.0, background_mean=None)
                if median:
                    extra.update(with_median=True, foreground_median=None, background_median=7.5)
                copied = self.copy_prediction(extra)
                normalized = all_models.normalize_json_result([{"score": 0.75, "extra_info": extra}])
                self.assertEqual(extra, copied[0][0]["extra_info"])
                self.assertEqual([], all_models.compare_predictions(copied, normalized))
                self.assertEqual(business["nested"], copied[0][0]["extra_info"]["nested"])
                self.assertFalse(any(key in copied[0][0] for key in ("with_mean", "foreground_mean", "background_mean",
                    "with_median", "foreground_median", "background_median")))

    def test_enabled_empty_sampling_is_not_disabled_or_numeric_zero(self):
        extra = {"with_mean": False, "foreground_mean": None, "background_mean": None,
                 "with_median": False, "foreground_median": None, "background_median": None}
        copied = self.copy_prediction(extra)
        same = all_models.normalize_json_result([{"score": 0.75, "extra_info": extra}])
        self.assertEqual([], all_models.compare_predictions(copied, same))
        self.assertTrue(all_models.compare_predictions(copied, self.copy_prediction({})))
        zero = {**extra, "with_mean": True, "foreground_mean": 0}
        differences = all_models.compare_predictions(copied, self.copy_prediction(zero))
        self.assertTrue(any("foreground_mean" in difference for difference in differences))

    def test_missing_extension_pointer_has_no_statistics(self):
        copied = self.copy_prediction(None)
        self.assertEqual({}, copied[0][0]["extra_info"])
        self.assertEqual([], all_models.compare_predictions(copied,
            all_models.normalize_json_result([{"score": 0.75}])))

    def test_both_groups_and_business_values_are_compared(self):
        extra = {"with_mean": True, "foreground_mean": 12.5, "background_mean": None,
                 "with_median": True, "foreground_median": 10, "background_median": None,
                 "polyline": [[1, 2], [3, 4]], "标签": "合格"}
        left = self.copy_prediction(extra)
        for key, replacement in (("foreground_mean", 12.6), ("foreground_median", 10.1),
                                 ("background_median", 0), ("标签", "不合格"),
                                 ("polyline", [[1, 2], [3, 5]])):
            with self.subTest(key=key):
                right = self.copy_prediction({**extra, key: replacement})
                self.assertTrue(any(key in difference for difference in
                    all_models.compare_predictions(left, right)))
        close = self.copy_prediction({**extra, "foreground_median": 10.000001})
        self.assertEqual([], all_models.compare_predictions(left, close))

    def test_extension_copy_and_comparison_keep_json_numeric_and_boolean_semantics(self):
        original = {"nested": {"value": 1, "flag": True}, "polyline": [[1, 2], [3, 4]]}
        normalized = all_models.normalize_json_result([{"extra_info": original}])
        original["nested"]["value"] = 99
        self.assertEqual(1, normalized[0][0]["extra_info"]["nested"]["value"])
        same = all_models.normalize_json_result([{"extra_info": {
            "nested": {"value": 1.0, "flag": True}, "polyline": [[1, 2], [3, 4]]}}])
        self.assertEqual([], all_models.compare_predictions(normalized, same))
        same[0][0]["extra_info"]["nested"]["flag"] = 1
        self.assertTrue(all_models.compare_predictions(normalized, same))

    def test_top_level_statistics_and_malformed_groups_are_rejected(self):
        invalid = [
            {"with_mean": True, "foreground_mean": 1, "background_mean": None},
            {"extra_info": {"with_mean": True}},
            {"extra_info": {"with_mean": 1, "foreground_mean": 1, "background_mean": None}},
            {"extra_info": {"with_mean": False, "foreground_mean": 0, "background_mean": None}},
            {"extra_info": {"with_median": True, "foreground_median": "NaN", "background_median": None}},
            {"extra_info": {"with_mean": True, "foreground_mean": True, "background_mean": None}},
            {"extra_info": None}, {"extra_info": []},
        ]
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(all_models.TestFailure):
                all_models.normalize_prediction_object(value)

    def test_invalid_utf8_and_non_object_c_extensions_are_rejected(self):
        for data in (b"\xff", b"[1]", b"null", b"{broken}"):
            buffer = ctypes.create_string_buffer(data)
            with self.subTest(data=data), self.assertRaises(all_models.TestFailure):
                all_models.parse_extra_info(ctypes.addressof(buffer))


class ExpectedFailureTest(unittest.TestCase):
    def test_known_failure_matches_stage_and_message(self):
        row = {
            "通过": False,
            "错误阶段": "加载",
            "错误": "缺少 MMCVRoIAlignRotated 实现",
        }
        expected = {
            "aoi-旋转框检测_s.dvo": {
                "阶段": "加载",
                "错误包含": "MMCVRoIAlignRotated",
            }
        }
        all_models.evaluate_expected_outcome(
            row, "AOI-旋转框检测_s.dvo", expected
        )
        self.assertTrue(row["符合预期"])
        self.assertEqual("预期失败", row["测试状态"])

    def test_unexpected_success_is_reported(self):
        row = {"通过": True, "错误阶段": "", "错误": ""}
        expected = {
            "a.dvo": {"阶段": "加载", "错误包含": "missing operation"}
        }
        all_models.evaluate_expected_outcome(row, "a.dvo", expected)
        self.assertFalse(row["符合预期"])
        self.assertEqual("预期失败未发生", row["测试状态"])

if __name__ == "__main__":
    unittest.main()
