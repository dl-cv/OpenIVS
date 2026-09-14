import re
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
LOADING_SOURCES = (
    "DlcvCsharpApi/flow/DvsModel.cs",
    "DlcvCsharpApi/flow/FlowGraphModel.cs",
    "DlcvCsharpApi/flow/modules/Models.cs",
    "dlcv_infer_cpp/flow/FlowGraphModel.cpp",
    "dlcv_infer_cpp/dlcv_infer.cpp",
)


class DvsMemoryLoadingSourceTest(unittest.TestCase):
    def test_loading_sources_do_not_write_model_files(self):
        forbidden_calls = re.compile(
            r"\b(?:GetTempPath[AW]?|create_directory|create_directories|mkdtemp|"
            r"ofstream|CreateFile[AW]?)\s*\(|"
            r"\b(?:File\.(?:WriteAllBytes|WriteAllText|Create)|"
            r"Directory\.CreateDirectory|ZipFile\.ExtractToDirectory)\s*\("
        )
        for relative_path in LOADING_SOURCES:
            with self.subTest(source=relative_path):
                source = (REPOSITORY_ROOT / relative_path).read_text(encoding="utf-8-sig")
                self.assertIsNone(forbidden_calls.search(source))

    def test_cpp_memory_loading_requires_the_binary_api(self):
        source = (REPOSITORY_ROOT / "dlcv_infer_cpp/dlcv_infer.cpp").read_text(encoding="utf-8-sig")
        self.assertIn("dlcv_load_model_binary", source)
        self.assertNotIn("ScopedTempModelFile", source)
        self.assertNotIn("LoadDvsArchiveWithTempFallback", source)


if __name__ == "__main__":
    unittest.main()
