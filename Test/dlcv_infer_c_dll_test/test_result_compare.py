import ctypes
import importlib.util
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
        "with_mean": False,
        "foreground_mean": 0.0,
        "background_mean": 0.0,
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
        model = root / "AOI-无CAD检测-20260721_120_50_s.dvst"
        selected = all_models.choose_image(model, root, {}, None)
        self.assertEqual(root / "OK1.png", selected)

    def test_common_aoi_uses_aoi_image(self):
        root = Path(r"Y:\测试模型")
        model = root / "AOI_120_50_s.dvst"
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
        objects[0].with_mean = False

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
