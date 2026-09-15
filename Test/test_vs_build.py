"""检查项目构建命令中的依赖还原范围。"""
import argparse
import importlib.util
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / ".cursor/skills/vs-build/scripts/build.py"
spec = importlib.util.spec_from_file_location("vs_build", SCRIPT)
build = importlib.util.module_from_spec(spec)
spec.loader.exec_module(build)


class RestoreCommandTest(unittest.TestCase):
    def command(self, path, target="Build"):
        args = argparse.Namespace(configuration="Debug", platform="x64", target=target, verbosity="minimal")
        return build.build_command(Path("MSBuild.exe"), path, args)

    def test_package_reference_build_and_rebuild_restore(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "app.csproj"
            path.write_text('<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><ItemGroup><PackageReference Include="Example" Version="1.0" /></ItemGroup></Project>', encoding="utf-8")
            for target in ("Build", "Rebuild"):
                command = self.command(path, target)
                self.assertEqual(1, command.count("/restore"))
                self.assertIn("/p:Platform=x64", command)
                self.assertIn("/p:Configuration=Debug", command)
                self.assertIn("/t:" + target, command)
                self.assertNotIn("/t:Restore;Build", command)

    def test_clean_and_restore_do_not_add_restore_switch(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "app.csproj"
            path.write_text('<Project><ItemGroup><PackageReference Include="Example" /></ItemGroup></Project>', encoding="utf-8")
            for target in ("Clean", "Restore"):
                self.assertNotIn("/restore", self.command(path, target))

    def test_packages_config_project_keeps_original_command(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "app.csproj"
            path.write_text('<Project><ItemGroup><None Include="packages.config" /></ItemGroup></Project>', encoding="utf-8")
            self.assertNotIn("/restore", self.command(path))

    def test_native_and_solution_commands_are_unchanged(self):
        for name in ("native.vcxproj", "example.sln"):
            self.assertNotIn("/restore", self.command(Path(name)))


if __name__ == "__main__":
    unittest.main()
