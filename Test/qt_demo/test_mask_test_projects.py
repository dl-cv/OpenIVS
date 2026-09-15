"""检查 Qt Mask 测试与 Demo 的构建分离。"""

from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
TEST_ROOT = ROOT / "Test" / "qt_demo"
NS = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}


class MaskTestProjectTests(unittest.TestCase):
    def test_demo_sources_and_projects_do_not_reference_mask_tests(self):
        for kind in ("c", "cpp"):
            folder = ROOT / f"dlcv_infer_{kind}_qt_demo"
            for path in folder.iterdir():
                if path.suffix not in (".cpp", ".h", ".vcxproj", ".filters"):
                    continue
                with self.subTest(path=path.name, kind=kind):
                    text = path.read_text(encoding="utf-8-sig")
                    for marker in ("MaskVisualizationSelfTest", "mask-visualization-selftest", "qt_mask_test"):
                        self.assertNotIn(marker, text)
                    if path.suffix == ".vcxproj":
                        tree = ET.fromstring(text)
                        for item in tree.findall(".//*[@Include]"):
                            self.assertNotIn("Test", Path(item.attrib["Include"].replace("\\", "/")).parts)

    def test_test_projects_reuse_production_widget_source(self):
        for kind in ("c", "cpp"):
            path = TEST_ROOT / f"dlcv_infer_{kind}_qt_mask_test.vcxproj"
            tree = ET.parse(path)
            with self.subTest(kind=kind):
                configs = tree.findall(".//m:ProjectConfiguration", NS)
                self.assertEqual({x.attrib["Include"] for x in configs}, {"Debug|x64", "Release|x64"})
                source = tree.find(".//m:DemoSourceDir", NS).text
                self.assertEqual(source, f"$(ProjectDir)..\\..\\dlcv_infer_{kind}_qt_demo")
                files = [x.attrib["Include"] for x in tree.findall(".//m:ClCompile[@Include]", NS)]
                self.assertEqual(files, ["main.cpp", "$(DemoSourceDir)\\ImageViewerWidget.cpp"])
                self.assertEqual(tree.findall(".//m:ProjectReference", NS), [])
                definition = tree.find(".//m:MaskTestDefinition", NS).text
                self.assertEqual(definition, f"DLCV_QT_{kind.upper()}_MASK_TEST")

    def test_runtime_uses_offscreen_without_inference_dll(self):
        tree = ET.parse(TEST_ROOT / "MaskVisualizationTest.targets")
        sources = ";".join(x.attrib["SourceFiles"] for x in tree.findall(".//m:Copy", NS))
        self.assertIn("qoffscreen", sources)
        self.assertNotIn("dlcv_infer", sources)
        self.assertIn('qputenv("QT_QPA_PLATFORM", "offscreen")', (TEST_ROOT / "main.cpp").read_text(encoding="utf-8"))

    def test_build_script_builds_only_four_qt_projects_serially(self):
        script = (TEST_ROOT / "1_编译测试.bat").read_text(encoding="ascii")
        commands = [line for line in script.splitlines() if line.startswith("python ")]
        self.assertEqual(len(commands), 4)
        self.assertEqual(script.count("if errorlevel 1 exit /b %errorlevel%"), 4)
        for kind in ("c", "cpp"):
            self.assertTrue(any(f"dlcv_infer_{kind}_qt_demo.vcxproj" in x for x in commands))
            self.assertTrue(any(f"dlcv_infer_{kind}_qt_mask_test.vcxproj" in x for x in commands))
        self.assertTrue(all("--configuration Release --platform x64 --target Build" in x for x in commands))


if __name__ == "__main__":
    unittest.main()
