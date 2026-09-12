import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
MODEL_HEADER = REPOSITORY_ROOT / "dlcv_infer_cpp" / "dlcv_infer.h"
MODEL_SOURCE = REPOSITORY_ROOT / "dlcv_infer_cpp" / "dlcv_infer.cpp"
C_API_SOURCE = REPOSITORY_ROOT / "dlcv_infer_cpp" / "dlcv_infer_c_api.cpp"
MASK_UTILS_SOURCE = REPOSITORY_ROOT / "dlcv_infer_cpp" / "MaskUtils.h"


def read_source(path):
    return path.read_text(encoding="utf-8")


class MaskSemanticsSourceTest(unittest.TestCase):
    def test_existing_infer_batch_signature_is_unchanged(self):
        header = read_source(MODEL_HEADER)
        self.assertRegex(
            header,
            r"Result\s+InferBatch\(const std::vector<cv::Mat>& image_list,\s*"
            r"const json& params_json = nullptr\);",
        )

    def test_cpp_and_c_paths_select_different_mask_processing(self):
        source = read_source(MODEL_SOURCE)
        self.assertRegex(
            source,
            r"Model::InferBatch\([^{}]+\)\s*\{\s*"
            r"return InferBatchInternal\(image_list, params_json, false\);",
        )
        self.assertRegex(
            source,
            r"Model::InferBatchPreservingOriginalMask\([^{}]+\)\s*\{\s*"
            r"return InferBatchInternal\(image_list, params_json, true\);",
        )
        self.assertRegex(
            source,
            r"if \(!preserveOriginalMask\)\s*\{\s*"
            r"mask_img = mask_utils::ResizeMaskToBboxGrid\(mask_img, bbox\);\s*\}",
        )

        c_api_source = read_source(C_API_SOURCE)
        self.assertIn(
            "entry->model->InferBatchPreservingOriginalMask(mats, params)",
            c_api_source,
        )

    def test_shared_mask_helper_keeps_input_checks_and_nearest_interpolation(self):
        source = read_source(MASK_UTILS_SOURCE)
        self.assertRegex(source, r"if \(mask\.empty\(\)\) return cv::Mat\(\);")
        self.assertRegex(source, r"if \(bbox\.size\(\) < 4\) return mask;")
        self.assertRegex(
            source,
            r"if \(bboxWidth <= 0 \|\| bboxHeight <= 0 \|\|\s*"
            r"\(mask\.cols == bboxWidth && mask\.rows == bboxHeight\)\)\s*"
            r"\{\s*return mask;\s*\}",
        )
        self.assertRegex(
            source,
            r"cv::resize\(mask, output, cv::Size\(bboxWidth, bboxHeight\), "
            r"0, 0, cv::INTER_NEAREST\);",
        )

    def test_compatibility_alias_uses_the_structured_c_entry(self):
        source = read_source(C_API_SOURCE)
        self.assertRegex(
            source,
            r"dlcv_infer_cpp_infer_c\([^{}]+\)\s*\{\s*"
            r"return dlcv_infer_cpp_infer_with_params_c\("
            r"model_index, image_list, nullptr\);",
        )
        self.assertRegex(
            source,
            r"dlcv_infer_c\([^{}]+\)\s*\{\s*"
            r"DlcvCResult result = dlcv_infer_cpp_infer_c\(model_index, image_list\);",
        )


if __name__ == "__main__":
    unittest.main()
