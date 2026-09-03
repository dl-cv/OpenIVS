import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
C_API_SOURCE = REPOSITORY_ROOT / "dlcv_infer_cpp" / "dlcv_infer_c_api.cpp"
CPP_MODEL_SOURCE = REPOSITORY_ROOT / "dlcv_infer_cpp" / "dlcv_infer.cpp"
CSHARP_LOADER_SOURCE = REPOSITORY_ROOT / "DlcvCsharpApi" / "DllLoader.cs"


def read_source(path):
    return path.read_text(encoding="utf-8")


def extract_block(source, signature):
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 0
    for index in range(brace, len(source)):
        char = source[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise AssertionError(f"代码块不完整: {signature}")


class ModelIndexSourceTest(unittest.TestCase):
    def test_flow_models_are_not_identified_by_a_fixed_numeric_range(self):
        source = read_source(C_API_SOURCE)
        classifier = extract_block(source, "static bool IsFlowModelIndex(")
        self.assertNotRegex(
            classifier,
            r"modelIndex\s*>=\s*kFirstFlowModelIndex",
            "普通模型索引持续递增到固定数值后会被误判为流程模型。",
        )
        load_function = extract_block(source, "int dlcv_infer_cpp_load_model_c(")
        self.assertNotIn(
            "!isFlowModel && idx >= kFirstFlowModelIndex",
            load_function,
            "普通模型累计加载达到固定数值后会被拒绝。",
        )


class DllLoaderSourceTest(unittest.TestCase):
    def test_binary_model_loading_does_not_replace_an_existing_loader(self):
        source = read_source(CSHARP_LOADER_SOURCE)
        method = extract_block(
            source,
            "internal static DllLoader ForModel(byte[] modelData, string modelName)",
        )
        self.assertNotIn(
            "_instance = CreateLoader(needed.Value);",
            method,
            "内存模型加载仍会更换已经选定的原生 DLL。",
        )

    def test_loader_created_without_authorization_can_recover_later(self):
        source = read_source(CSHARP_LOADER_SOURCE)
        method = extract_block(source, "public static void EnsureForModel(string modelPath)")
        self.assertRegex(
            method,
            r"_instance\s*==\s*null\s*\|\|\s*"
            r"_instance\.LoadedDogProvider\s*==\s*DogProvider\.None",
            "首次检查没有授权时会保存空加载器，后续检测到授权仍不会加载原生 DLL。",
        )


class CppDllLoaderSourceTest(unittest.TestCase):
    def test_loader_created_without_authorization_can_recover_later(self):
        source = read_source(CPP_MODEL_SOURCE)
        method = extract_block(source, "DllLoader& DllLoader::Instance()")
        self.assertRegex(
            method,
            r"!instance\s*\|\|\s*"
            r"instance->GetDogProvider\(\)\s*==\s*"
            r"sntl_admin::DogProvider::Unknown",
            "C++ 首次检查没有授权时会保存空加载器，后续检测到授权仍不会加载原生 DLL。",
        )


class PortableSourceTest(unittest.TestCase):
    def test_c_api_translation_unit_avoids_unconditional_msvc_crt_calls(self):
        source = read_source(C_API_SOURCE)
        unsupported_calls = [
            token for token in ("fopen_s(", "_strdup(") if token in source
        ]
        self.assertEqual(
            [],
            unsupported_calls,
            "Linux 构建启用包装层时会编译该文件，当前仍包含未分平台处理的 MSVC CRT 调用。",
        )


if __name__ == "__main__":
    unittest.main()
