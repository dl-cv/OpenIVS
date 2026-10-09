"""通过临时 PYTHONPATH 中的假 pip 验证批处理调用与状态，不实际安装。"""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == "nt", "批处理测试仅在 Windows 执行")
class InstallTargetTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="openivs-install-")
        self.root = Path(temp.name).resolve()
        self.assertTrue(self.root.is_relative_to(Path(tempfile.gettempdir()).resolve()))
        self.addCleanup(temp.cleanup)
        self.package = self.root / "package with spaces"
        self.package.mkdir()
        self.dist = self.package / "dist"
        self.dist.mkdir()
        self.launcher = self.root / "caller"
        self.launcher.mkdir()
        self.modules = self.root / "fake-modules"
        self.modules.mkdir()
        self.log = self.root / "pip-calls.jsonl"
        self.script = self.package / "install.bat"
        self.script.write_bytes((ROOT / "2_安装新版.bat").read_bytes())
        (self.modules / "pip.py").write_text(
            "import json, os, sys\n"
            "with open(os.environ['FAKE_PIP_LOG'], 'a', encoding='utf-8') as stream:\n"
            "    stream.write(json.dumps({'cwd': os.getcwd(), 'argv': sys.argv[1:]}) + '\\n')\n"
            "key = 'FAKE_PIP_UNINSTALL_EXIT' if sys.argv[1] == 'uninstall' else 'FAKE_PIP_EXIT'\n"
            "sys.exit(int(os.environ[key]))\n", encoding="utf-8")
        self.env = os.environ.copy()
        self.env.update({
            "PATH": str(Path(sys.executable).parent) + os.pathsep
                + str(Path(os.environ["SystemRoot"]) / "System32"),
            "PYTHONPATH": str(self.modules),
            "PYTHONNOUSERSITE": "1",
            "PYTHONDONTWRITEBYTECODE": "1",
            "FAKE_PIP_LOG": str(self.log),
            "FAKE_PIP_EXIT": "0",
            "FAKE_PIP_UNINSTALL_EXIT": "0",
            "LATEST": "invalid-inherited-wheel.whl",
        })

    def wheel(self, name="latest package.whl", timestamp=20):
        path = self.dist / name
        path.write_bytes(b"test wheel name only")
        os.utime(path, (timestamp, timestamp))
        return name

    def run_script(self, arguments="", exit_code=0, uninstall_exit_code=0):
        self.env["FAKE_PIP_EXIT"] = str(exit_code)
        self.env["FAKE_PIP_UNINSTALL_EXIT"] = str(uninstall_exit_code)
        command = 'call "{}" {} < nul'.format(self.script, arguments)
        return subprocess.run(
            '"{}" /d /c {}'.format(os.environ["ComSpec"], command),
            cwd=self.launcher, env=self.env, capture_output=True, timeout=10,
        )

    def calls(self):
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text(encoding="utf-8").splitlines()]

    def expected_call(self, *args):
        return {"cwd": str(self.dist), "argv": list(args)}

    def test_target_uses_latest_wheel_without_uninstall_or_pause(self):
        self.wheel("old package.whl", 10)
        latest = self.wheel()
        (self.dist / "not a wheel.whl").mkdir()
        target = self.root / "target packages & extras"
        result = self.run_script('--target "{}"'.format(target))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls(), [self.expected_call(
            "install", "--no-deps", "--upgrade", "--target", str(target), latest)])
        self.assertEqual(result.stdout, b"")
        self.assertFalse(target.exists())

    def test_target_propagates_pip_error(self):
        self.wheel()
        result = self.run_script('--target "{}"'.format(self.root / "target"), 29)
        self.assertEqual(result.returncode, 29, result.stdout + result.stderr)
        self.assertEqual(len(self.calls()), 1)
        self.assertEqual(result.stdout, b"")

    def test_relative_target_is_resolved_before_entering_dist(self):
        latest = self.wheel()
        result = self.run_script('--target "relative packages"')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        target = self.launcher / "relative packages"
        self.assertEqual(self.calls(), [self.expected_call(
            "install", "--no-deps", "--upgrade", "--target", str(target), latest)])

    def test_target_requires_path(self):
        for args in ('--target', '--target ""'):
            with self.subTest(args=args):
                result = self.run_script(args)
                self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                self.assertEqual(self.calls(), [])
                self.assertEqual(result.stdout, b"")

    def test_missing_dist_fails_without_pip(self):
        self.dist.rmdir()
        result = self.run_script('--target "{}"'.format(self.root / "target"))
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertEqual(self.calls(), [])

    def test_missing_wheel_fails_without_pip(self):
        for args in ('', '--target "{}"'.format(self.root / "target")):
            with self.subTest(args=args):
                result = self.run_script(args)
                self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
                self.assertEqual(self.calls(), [])

    def test_default_keeps_uninstall_install_and_pause(self):
        latest = self.wheel()
        result = self.run_script()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.calls(), [
            self.expected_call("uninstall", "-y", "dlcvpro_infer_csharp"),
            self.expected_call("install", "-U", latest),
        ])
        self.assertTrue(result.stdout.strip())

    def test_default_returns_install_error_after_pause(self):
        self.wheel()
        result = self.run_script(exit_code=31)
        self.assertEqual(result.returncode, 31, result.stdout + result.stderr)
        self.assertEqual(len(self.calls()), 2)
        self.assertTrue(result.stdout.strip())


if __name__ == "__main__":
    unittest.main()
