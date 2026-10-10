import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib.parse import unquote, urlsplit


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
DOCUMENTATION_ROOT = REPOSITORY_ROOT / "docs"
LINK_PATTERN = re.compile(r"\]\((<[^>]+>|[^\s)]+)\)")


def local_link_targets(document):
    content = document.read_text(encoding="utf-8-sig")
    content = re.sub(r"(?ms)^```[^\n]*\n.*?^```[ \t]*$", "", content)
    for match in LINK_PATTERN.finditer(content):
        target = match.group(1).strip("<>")
        parsed = urlsplit(target)
        if parsed.scheme or parsed.netloc or not parsed.path:
            continue
        yield (document.parent / unquote(parsed.path)).resolve()


class DocumentationLayoutTest(unittest.TestCase):
    def test_root_markdown_is_limited_to_entry_and_rules(self):
        self.assertEqual(
            {path.name for path in REPOSITORY_ROOT.glob("*.md")},
            {"README.md", "AGENTS.md", "CLAUDE.md"},
        )
        self.assertFalse((REPOSITORY_ROOT / "README").exists())
        self.assertTrue((DOCUMENTATION_ROOT / "images/openivs.png").is_file())

    def test_documentation_index_covers_all_manuals(self):
        index = DOCUMENTATION_ROOT / "README.md"
        manuals = {path.resolve() for path in DOCUMENTATION_ROOT.glob("*.md")}
        manuals.remove(index.resolve())
        self.assertEqual(set(local_link_targets(index)), manuals)

    def test_relative_document_and_image_links_resolve(self):
        documents = list(DOCUMENTATION_ROOT.glob("*.md"))
        documents.extend(REPOSITORY_ROOT / name for name in ("README.md", "CLAUDE.md"))
        for document in documents:
            for target in local_link_targets(document):
                with self.subTest(document=document.name, target=target):
                    self.assertTrue(target.is_relative_to(REPOSITORY_ROOT))
                    self.assertTrue(target.is_file(), str(target))

    def test_project_document_item_tracks_the_central_manual(self):
        project_path = REPOSITORY_ROOT / "Test/DlcvCSharpCppTest/DlcvCSharpCppTest.csproj"
        project = ET.parse(project_path).getroot()
        namespace = {"msbuild": "http://schemas.microsoft.com/developer/msbuild/2003"}
        documents = []
        for item in project.findall(".//msbuild:None", namespace):
            include = item.attrib.get("Include", "")
            if include.endswith(".md"):
                documents.append((project_path.parent / include.replace("\\", "/")).resolve())
        self.assertEqual(documents, [(DOCUMENTATION_ROOT / "C#与C++混编测试开发文档.md").resolve()])
        self.assertTrue(documents[0].is_file())

    def test_automation_manual_documents_all_cli_options(self):
        source = (REPOSITORY_ROOT / "DlcvDemo/UiTestOptions.cs").read_text(encoding="utf-8-sig")
        manual = (DOCUMENTATION_ROOT / "C#测试程序自动化入口.md").read_text(encoding="utf-8")
        options = set(re.findall(r'case "(--[^"\n]+)"', source))
        self.assertTrue(options)
        for option in options:
            with self.subTest(option=option):
                self.assertIn(option, manual)

    def test_log_manual_covers_application_and_diagnostic_files(self):
        content = (DOCUMENTATION_ROOT / "01_日志.md").read_text(encoding="utf-8")
        for marker in (
            "dlcvInfer_c_api_debug.log",
            "DLCV_INFER_DIAG",
            "infer_debug.log",
            "OpenIVS-2026_yyyyMMdd.log",
            "EnableRuntimeLog",
            "RuntimeLogMaxFileCount=7",
            "ProductionLogs",
            "EnableProductionLog",
            "yyyyMMdd.csv",
            "log_yyyyMMdd_HHmmss.txt",
            "log_yyyyMMdd_HHmmss.log",
            "SimpleLoggerTest",
            "all-tests.log",
            "RunAllTests.ps1",
            "-managed.log",
            "-stdout.log",
            "-stderr.log",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, content)


if __name__ == "__main__":
    unittest.main()
