import os
import subprocess
import sys


def ngen_dll(dll_path):
    ngen = os.path.join(
        os.environ.get("WINDIR", r"C:\Windows"),
        "Microsoft.NET",
        "Framework64",
        "v4.0.30319",
        "ngen.exe",
    )
    if not os.path.exists(ngen):
        print(f"[NGEN] 未找到 ngen.exe: {ngen}")
        return
    if not os.path.exists(dll_path):
        print(f"[NGEN] DLL 不存在: {dll_path}")
        return
    print(f"[NGEN] {dll_path}")
    try:
        subprocess.run([ngen, "install", dll_path], check=False)
    except Exception as e:
        print(f"[NGEN] 失败: {e}")


def get_install_path():
    try:
        result = subprocess.run(
            [sys.executable, "-m", "pip", "show", "dlcvpro_infer_csharp"],
            capture_output=True, text=True, check=False,
        )
        for line in result.stdout.splitlines():
            if line.startswith("Location:"):
                loc = line.split(":", 1)[1].strip()
                return os.path.join(loc, "dlcvpro_infer_csharp")
    except Exception:
        pass

    try:
        import dlcvpro_infer_csharp
    except ImportError:
        return None

    return (
        dlcvpro_infer_csharp.__path__[0]
        if hasattr(dlcvpro_infer_csharp, "__path__")
        else os.path.dirname(dlcvpro_infer_csharp.__file__)
    )


def main():
    base = get_install_path()
    if base is None:
        print("[NGEN] 未安装 dlcvpro_infer_csharp，跳过")
        return

    dlls = [
        "DlcvCsharpApi.dll",
        "OpenCvSharp.dll",
        "Newtonsoft.Json.dll",
    ]

    for dll in dlls:
        ngen_dll(os.path.join(base, dll))


if __name__ == "__main__":
    main()
