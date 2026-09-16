import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class CSharpSharedIndexSourceTest(unittest.TestCase):
    def test_loader_uses_only_final_five_new_exports(self):
        source = (ROOT / "DlcvCsharpApi" / "DllLoader.cs").read_text(encoding="utf-8-sig")
        for name in (
            "dlcv_register_dvs_model_c",
            "dlcv_get_dvs_model_c",
            "dlcv_get_index_type_c",
            "dlcv_bind_index_c",
            "dlcv_get_all_models",
        ):
            self.assertIn(name, source)
        for removed in (
            "dlcv_unbind_index_c",
            "dlcv_get_model_info_c",
            "dlcv_register_flow_c",
            "dlcv_get_flow_info_c",
            "dlcv_free_flow_c",
            "openivs_flow_",
            "SharedFlowRegistry",
        ):
            self.assertNotIn(removed, source)
        selection_start = source.index("GetSingleLoadedLoaderForDvsRegistration")
        selection_end = source.index("internal static List<DllLoader> GetLoadedLoaders", selection_start)
        selection = source[selection_start:selection_end]
        self.assertIn("_instance._moduleHandle == IntPtr.Zero", selection)
        self.assertIn("CreateLoader(DogProvider.Sentinel)", selection)
        self.assertNotIn("无法登记不含子模型的 DVS", selection)

    def test_csharp_project_has_no_native_flow_registry_dependency(self):
        project_path = ROOT / "DlcvCsharpApi" / "DlcvCsharpApi.csproj"
        project_text = project_path.read_text(encoding="utf-8-sig")
        self.assertFalse((ROOT / "DlcvCsharpApi" / "flow" / "SharedFlowRegistry.cs").exists())
        self.assertNotIn("SharedFlowRegistry.cs", project_text)
        self.assertNotIn("dlcv_infer_cpp.vcxproj", project_text)
        self.assertNotIn("CollectFlowRuntime", project_text)
        ET.parse(project_path)

    def test_model_registers_final_dvs_descriptor_and_uses_unified_free(self):
        source = (ROOT / "DlcvCsharpApi" / "Model.cs").read_text(encoding="utf-8-sig")
        start = source.index("private void InitializeDvsMode")
        end = source.index("private void InitializeDvtMode", start)
        registration = source[start:end]
        for field in (
            '["schema_version"]',
            '["dvs_type"]',
            '["model_path"]',
            '["device_id"]',
            '["pipeline"]',
            '["model_bindings"]',
        ):
            self.assertIn(field, registration)
        self.assertNotIn('["provider"]', registration)
        self.assertNotIn('["flow_type"]', registration)
        self.assertNotIn('["source_path"]', registration)
        self.assertIn("RegisterDvsModel", registration)
        self.assertIn("FreeModelIndex", source)
        self.assertNotIn("UnbindIndex", source)
        self.assertNotIn("AllocateFlowModelIndex", source)
        self.assertIn("CreateBorrowedDvsChild", source)

    def test_get_all_models_preserves_module_snapshots(self):
        source = (ROOT / "DlcvCsharpApi" / "Utils.cs").read_text(encoding="utf-8-sig")
        self.assertIn("public static JObject GetAllModels()", source)
        self.assertIn('["modules"] = modules', source)
        self.assertIn('snapshot["module_path"]', source)
        self.assertIn("底层模型快照缺少有效状态字段", source)
        self.assertIn("底层模型快照结构无效", source)
        self.assertIn("DllLoader.GetLoadedLoaders()", source)
        self.assertNotIn('snapshot["provider"] =', source)
        self.assertNotIn("DllLoader.Instance.GetAllModels", source)


    def test_model_info_keeps_managed_indexes_and_full_dvs_descriptor(self):
        source = (ROOT / "DlcvCsharpApi" / "Model.cs").read_text(encoding="utf-8-sig")
        dvt_start = source.index("private JObject GetModelInfoDvt")
        dvt_end = source.index("public Tuple<JObject, IntPtr> InferInternal", dvt_start)
        dvt = source[dvt_start:dvt_end]
        self.assertIn('resultObject["model_index"] = modelIndex', dvt)
        self.assertIn('resultObject["code"].Value<int>() == 0', dvt)

        dvs_start = source.index("public JObject GetDvsModelInfo()")
        dvs_end = source.index("private JObject GetModelInfoDvp", dvs_start)
        dvs = source[dvs_start:dvs_end]
        self.assertIn("_dllLoader.GetDvsModel(modelIndex)", dvs)
        for field in (
            '"loaded_model_meta"',
            '"model_info"',
            '"input_model_node_id"',
            '"output_model_node_id"',
        ):
            self.assertIn(field, dvs)

    def test_csharp_package_no_longer_copies_native_flow_wrapper(self):
        source = (ROOT / "build_package.py").read_text(encoding="utf-8-sig")
        names_start = source.index("PACKAGE_OUTPUT_FILE_NAMES")
        names_end = source.index("SIGNED_EXE_NAMES", names_start)
        package_names = source[names_start:names_end]
        self.assertNotIn('"dlcv_infer_cpp.dll"', package_names)
        self.assertNotIn('"opencv_world4100.dll"', package_names)

    def test_csharp_tests_target_final_shared_index_design(self):
        program = (ROOT / "Test" / "DlcvCSharpTest" / "Program.cs").read_text(encoding="utf-8-sig")
        for name in (
            "dlcv_register_dvs_model_c",
            "dlcv_get_dvs_model_c",
            "dlcv_get_index_type_c",
            "dlcv_bind_index_c",
            "dlcv_get_all_models",
            '"all-models"',
            '"empty-dvs-first-load-selftest"',
            "exceptionalFreeCount",
            "DVS 普通兼容信息未保留子模型 index",
        ):
            self.assertIn(name, program)
        for removed in (
            "shared-index-compat-selftest",
            "empty-flow-index-selftest",
            "provider-switch-flow-selftest",
            "openivs_flow_",
            "dlcv_unbind_index_c",
            "dlcv_get_model_info_c",
            "SharedFlowRegistry",
        ):
            self.assertNotIn(removed, program)

    def test_mixed_dvs_info_checks_each_owners_index(self):
        directory = ROOT / "Test" / "DlcvCSharpCppTest"
        cases = (
            ("CommandLineTest.cs", "csharp", "cpp", "session"),
            ("SelfTest.cs", "csharpDvs", "cppDvs", "session"),
            ("MainForm.Testing.cs", "csharpDvsInfo", "cppDvsInfo", "form.Session"),
        )
        for filename, csharp, cpp, session in cases:
            with self.subTest(filename=filename):
                source = (directory / filename).read_text(encoding="utf-8-sig")
                self.assertIn(f'CheckDvsInfoShape({csharp}, {session}.CSharpModelIndex, "C#")', source)
                self.assertIn(f'CheckDvsInfoShape({cpp}, {session}.CppModelIndex, "C++")', source)
                self.assertNotIn(f'{cpp}["nodes"]', source)
        command = (directory / "CommandLineTest.cs").read_text(encoding="utf-8-sig")
        self.assertIn('info["model_index"]?.Value<int>() == index', command)
        self.assertIn('session.CSharpModelIndex == index && session.CSharpCreatedFromIndex', command)
        self.assertIn('session.CppModelIndex == index', command)

    def test_mixed_bridge_exposes_dvs_info_and_final_index_factory(self):
        bridge = (ROOT / "Test" / "DlcvCSharpCppTest" / "Bridge" / "NativeModel.cpp").read_text(
            encoding="utf-8-sig"
        )
        managed = (ROOT / "Test" / "DlcvCSharpCppTest" / "Bridge" / "CppModel.cpp").read_text(
            encoding="utf-8-sig"
        )
        command = (ROOT / "Test" / "DlcvCSharpCppTest" / "CommandLineTest.cs").read_text(
            encoding="utf-8-sig"
        )
        self.assertIn("CreateModelFromIndex", bridge)
        self.assertIn("GetDvsModelInfo", bridge)
        self.assertIn("GetDvsModelInfo", managed)
        self.assertIn("Utils.GetAllModels()", command)
        self.assertIn('type == "dvs"', command)
        self.assertNotIn('type == "flow"', command)


if __name__ == "__main__":
    unittest.main()
