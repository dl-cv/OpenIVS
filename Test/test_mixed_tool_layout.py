"""检查混编窗口程序与自动测试的文件位置和构建引用。"""
import json
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "DlcvCSharpCppTest"
TESTS = ROOT / "Test" / "DlcvCSharpCppTest"
NS = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}
TEST_FILES = {"SelfTest.cs", "CommandLineTest.cs", "AppearanceSelfTest.cs",
              "MainForm.Testing.cs", "ModelPathSelfTest.cs",
              "check_startup_configuration.ps1", "run_tests.py"}


class MixedToolLayoutTests(unittest.TestCase):
    def test_window_program_is_outside_test_directory(self):
        for name in ("Program.cs", "MainForm.cs", "MainForm.Designer.cs", "MainForm.resx",
                     "ModelSession.cs", "DlcvCSharpCppTest.csproj"):
            self.assertTrue((APP / name).is_file(), name)
            self.assertFalse((TESTS / name).exists(), name)
        for name in TEST_FILES:
            self.assertTrue((TESTS / name).is_file(), name)
            self.assertFalse((APP / name).exists(), name)

    def test_project_compiles_existing_program_and_linked_tests(self):
        project = ET.parse(APP / "DlcvCSharpCppTest.csproj")
        compiled = {(APP / item.attrib["Include"].replace("\\", "/")).resolve()
                    for item in project.findall(".//m:Compile", NS)}
        for path in compiled:
            self.assertTrue(path.is_file(), str(path))
        self.assertTrue({TESTS / name for name in TEST_FILES if name.endswith(".cs")} <= compiled)
        self.assertIn((APP / "MainForm.cs").resolve(), compiled)
        self.assertIn((APP / "Program.cs").resolve(), compiled)

    def test_project_dependencies_resolve_to_original_sdk(self):
        for folder, name in ((APP, "DlcvCSharpCppTest.csproj"),
                             (APP / "Bridge", "DlcvCSharpCppBridge.vcxproj")):
            project = ET.parse(folder / name)
            references = {(folder / item.attrib["Include"].replace("\\", "/")).resolve()
                          for item in project.findall(".//m:ProjectReference", NS)}
            self.assertIn((ROOT / "dlcv_infer_cpp" / "dlcv_infer_cpp.vcxproj").resolve(), references)
            for path in references:
                self.assertTrue(path.is_file(), str(path))
        project = ET.parse(APP / "DlcvCSharpCppTest.csproj")
        imports = [item.attrib["Project"].replace("\\", "/")
                   for item in project.findall("m:Import", NS)]
        self.assertIn("../DlcvNativeRuntime.targets", imports)

    def test_solution_and_generated_profile_start_root_project(self):
        solution = (ROOT / "OpenIVS.sln").read_text(encoding="utf-8-sig")
        self.assertIn('"DlcvCSharpCppTest\\DlcvCSharpCppTest.csproj"', solution)
        self.assertNotIn('"Test\\DlcvCSharpCppTest\\DlcvCSharpCppTest.csproj"', solution)
        for name in ("DlcvCSharpCppTest.csproj", "Bridge/DlcvCSharpCppBridge.vcxproj"):
            guid = ET.parse(APP / name).find(".//m:ProjectGuid", NS).text
            self.assertNotRegex(solution, re.escape(guid) + r" = \{E80043D7-0F8B-407E-86DF-F8789A514C45\}")
        profiles = json.loads((ROOT / "OpenIVS.slnLaunch").read_text(encoding="utf-8"))
        selected = [profile for profile in profiles if profile["Name"] == "C# 与 C++ 混编模型测试"]
        self.assertEqual(len(selected), 1)
        self.assertEqual(selected[0]["Projects"], [
            {"Path": "DlcvCSharpCppTest\\DlcvCSharpCppTest.csproj", "Action": "Start"}])

    def test_icon_resource_reuses_repository_file(self):
        project = ET.parse(APP / "DlcvCSharpCppTest.csproj")
        icon = project.find(".//m:ApplicationIcon", NS).text.replace("\\", "/")
        self.assertEqual((APP / icon).resolve(), (ROOT / "logo.ico").resolve())
        resource = ET.parse(APP / "MainForm.resx")
        icon = resource.find("data[@name='$this.Icon']/value").text.split(";")[0].replace("\\", "/")
        self.assertEqual((APP / icon).resolve(), (ROOT / "logo.ico").resolve())


if __name__ == "__main__":
    unittest.main()
