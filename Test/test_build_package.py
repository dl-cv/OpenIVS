import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest.mock import patch

import build_package


class CopyPackageFilesTest(unittest.TestCase):
    def test_only_copies_declared_package_files(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir).resolve()
            output_dir = repo_root / "DlcvDemo" / "bin"
            staging_dir = repo_root / build_package.PACKAGE_NAME
            rpc_exe_source = repo_root / "installed" / build_package.RPC_EXE_NAME
            output_dir.mkdir(parents=True)
            rpc_exe_source.parent.mkdir(parents=True)

            for file_name in build_package.PACKAGE_OUTPUT_FILE_NAMES:
                (output_dir / file_name).write_bytes(file_name.encode("utf-8"))

            rpc_exe_source.write_bytes(b"rpc executable")
            (output_dir / "AIModelRPC.exe").write_bytes(b"old rpc executable")
            (output_dir / "DlcvDemo.exe").write_bytes(b"unexpected executable")
            (output_dir / "DlcvDemo.exe.config").write_bytes(b"unexpected config")
            (output_dir / "old-runtime.dll").write_bytes(b"unexpected library")
            (repo_root / "hasp_26146.ini").write_text("test", encoding="utf-8")

            with (
                patch.object(build_package, "REPO_ROOT", repo_root),
                patch.object(build_package, "DEMO_OUTPUT_DIR", output_dir),
                patch.object(build_package, "STAGING_DIR", staging_dir),
                patch.object(build_package, "RPC_EXE_SOURCE", rpc_exe_source),
            ):
                signed_executables = build_package.copy_package_files()

            expected_names = set(build_package.PACKAGE_OUTPUT_FILE_NAMES)
            expected_names.update({build_package.RPC_EXE_NAME, "hasp_26146.ini"})
            actual_names = {path.name for path in staging_dir.iterdir()}

            self.assertEqual(expected_names, actual_names)
            self.assertEqual(
                list(build_package.SIGNED_EXE_NAMES),
                [path.name for path in signed_executables],
            )
            self.assertEqual(
                b"rpc executable",
                (staging_dir / build_package.RPC_EXE_NAME).read_bytes(),
            )
            self.assertNotIn("DlcvDemo.exe", actual_names)
            self.assertNotIn("DlcvDemo.exe.config", actual_names)
            self.assertNotIn("old-runtime.dll", actual_names)


class TestProjectDependenciesTest(unittest.TestCase):
    def test_reflection_demo_projects_are_build_dependencies(self):
        repo_root = Path(__file__).resolve().parents[1]
        namespace = {"msbuild": "http://schemas.microsoft.com/developer/msbuild/2003"}
        test_project = ET.parse(repo_root / "Test" / "DlcvCSharpTest" / "DlcvCSharpTest.csproj")
        references = {
            item.attrib["Include"]: item
            for item in test_project.findall(".//msbuild:ProjectReference", namespace)
        }

        for name in ("DlcvDemo", "DlcvDemo2"):
            with self.subTest(project=name):
                relative_path = f"..\\..\\{name}\\{name}.csproj"
                self.assertIn(relative_path, references)
                reference = references[relative_path]
                demo_project = ET.parse(repo_root / name / f"{name}.csproj")
                expected_guid = demo_project.findtext(".//msbuild:ProjectGuid", namespaces=namespace)
                self.assertEqual(expected_guid, reference.findtext("msbuild:Project", namespaces=namespace))
                self.assertEqual("false", reference.findtext("msbuild:ReferenceOutputAssembly", namespaces=namespace))
                self.assertEqual("true", reference.findtext("msbuild:BuildReference", namespaces=namespace))


class CompleteTestRunnerOptionsTest(unittest.TestCase):
    def test_explicit_native_library_pair_is_forwarded(self):
        repo_root = Path(__file__).resolve().parents[1]
        runner = (repo_root / "Test" / "DlcvCSharpTest" / "RunAllTests.ps1").read_text(encoding="utf-8-sig")
        self.assertIn("[string]$SentinelDllPath", runner)
        self.assertIn("[string]$VirboxDllPath", runner)
        self.assertIn("$hasSentinelDll -ne $hasVirboxDll", runner)
        self.assertIn("[IO.Path]::GetFullPath($SentinelDllPath)", runner)
        self.assertIn("[IO.Path]::GetFullPath($VirboxDllPath)", runner)
        self.assertIn("-ArgumentList $allTestsArguments", runner)
        self.assertNotIn("$env:PATH", runner)


if __name__ == "__main__":
    unittest.main()
