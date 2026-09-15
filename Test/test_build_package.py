import argparse
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


class BuildScopeTest(unittest.TestCase):
    def test_packaging_builds_only_the_csharp_product(self):
        repo_root = Path(__file__).resolve().parents[1]
        with (
            patch.object(build_package, "parse_args", return_value=argparse.Namespace(install=False)),
            patch.object(build_package.os, "chdir"),
            patch.object(build_package, "require_file"),
            patch.object(build_package, "run_step") as run_step,
            patch.object(build_package, "copy_package_files", return_value=[]),
            patch.object(build_package, "find_signing_certificate_thumbprint", return_value="test"),
            patch.object(build_package, "snapshot_wheels", return_value={}),
            patch.object(build_package, "find_generated_wheel", return_value=repo_root / "dist" / "test.whl"),
        ):
            self.assertEqual(0, build_package.main())

        built_projects = []
        for call in run_step.call_args_list:
            for argument in call.args[1]:
                if argument.endswith((".csproj", ".vcxproj", ".sln")):
                    built_projects.append(Path(argument))
        self.assertEqual([repo_root / "DlcvDemo" / "DlcvDemo.csproj"], built_projects)

    def test_regression_build_has_a_separate_serial_entry(self):
        repo_root = Path(__file__).resolve().parents[1]
        script = (repo_root / "Test" / "1_编译测试.bat").read_text(encoding="ascii")
        lines = script.splitlines()
        commands = [line for line in lines if line.startswith("python ")]
        self.assertEqual(3, len(commands))
        for command, project in zip(commands, ("dlcv_infer_cpp_test", "dlcv_infer_c_test", "DlcvCSharpTest")):
            self.assertIn(project, command)
            self.assertEqual("if errorlevel 1 exit /b %errorlevel%", lines[lines.index(command) + 1])
        self.assertNotIn("build_package.py", script)
        self.assertNotIn("start ", script.lower())


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


class NativeCAbiTestProjectTest(unittest.TestCase):
    def test_formal_c_abi_tests_are_compiled_without_test_exports(self):
        repo_root = Path(__file__).resolve().parents[1]
        project_path = repo_root / "Test" / "DlcvCSharpTest" / "DlcvCSharpTest.csproj"
        project = ET.parse(project_path)
        namespace = {"msbuild": "http://schemas.microsoft.com/developer/msbuild/2003"}
        compile_files = {item.attrib["Include"] for item in project.findall(".//msbuild:Compile", namespace)}
        self.assertIn("NativeCAbiSelfTests.cs", compile_files)
        source = "\n".join(path.read_text(encoding="utf-8-sig") for path in project_path.parent.glob("*.cs"))
        self.assertNotIn("dlcv_shared_index_test_", source)
        for export_name in ("dlcv_infer_cpp_load_model_c", "dlcv_infer_cpp_get_model_info_c", "dlcv_infer_cpp_infer_json_c", "dlcv_infer_cpp_free_model_c", "dlcv_infer_cpp_free_all_models_c"):
            self.assertIn(export_name, source)

    def test_production_sources_do_not_expose_shared_index_test_hooks(self):
        repo_root = Path(__file__).resolve().parents[1]
        for name in ("dlcv_infer.h", "dlcv_infer.cpp", "dlcv_infer_c_api.h", "dlcv_infer_c_api.cpp"):
            with self.subTest(source=name):
                source = (repo_root / "dlcv_infer_cpp" / name).read_text(encoding="utf-8-sig")
                self.assertNotIn("dlcv_shared_index_test_", source)
        header = (repo_root / "dlcv_infer_cpp/flow/modules/ModelModules.h").read_text(encoding="utf-8-sig")
        self.assertNotIn("ClearForFreeAllModels", header)

    def test_native_rules_use_existing_release_executable_and_exit_code(self):
        repo_root = Path(__file__).resolve().parents[1]
        program = (repo_root / "Test" / "DlcvCSharpTest" / "Program.cs").read_text(encoding="utf-8-sig")
        self.assertIn('ResolveRepoRoot(), "Release", "dlcv_infer_cpp_test.exe"', program)
        self.assertIn('Arguments = "shared-index-rules-selftest"', program)
        self.assertIn("process.ExitCode != 0", program)
        self.assertNotIn("CppSharedIndexTestIndexRules", program)

    def test_unified_test_count_remains_27(self):
        repo_root = Path(__file__).resolve().parents[1]
        program = (repo_root / "Test" / "DlcvCSharpTest" / "Program.cs").read_text(encoding="utf-8-sig")
        start = program.index("var tests = new List<UnifiedTestCase>")
        end = program.index("var results = new List<UnifiedTestResult>", start)
        self.assertEqual(27, program[start:end].count("new UnifiedTestCase("))
        self.assertIn("C++ 共享规则与正式 C ABI", program[start:end])

    def test_runner_uses_temp_logs_and_strict_utf8(self):
        repo_root = Path(__file__).resolve().parents[1]
        runner = (repo_root / "Test" / "DlcvCSharpTest" / "RunAllTests.ps1").read_text(encoding="utf-8-sig")
        self.assertIn("GetFullPath([IO.Path]::GetTempPath())", runner)
        self.assertIn("日志路径必须位于系统临时目录", runner)
        self.assertIn("UTF8Encoding(", runner)
        self.assertIn("Read-Utf8Text", runner)
        self.assertNotIn("bin\\x64\\Release\\DlcvCSharpTest-all-tests.log", runner)


if __name__ == "__main__":
    unittest.main()
