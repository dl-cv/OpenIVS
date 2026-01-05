import codecs
import os
import re
import sys


def _read_text_preserve_bom(path: str) -> tuple[str, bool]:
    raw = open(path, "rb").read()
    has_bom = raw.startswith(codecs.BOM_UTF8)
    text = raw.decode("utf-8-sig" if has_bom else "utf-8")
    return text, has_bom


def _write_text_preserve_bom(path: str, text: str, has_bom: bool) -> None:
    raw = text.encode("utf-8")
    if has_bom:
        raw = codecs.BOM_UTF8 + raw
    with open(path, "wb") as f:
        f.write(raw)


def _extract_version_from_setup_py(setup_py_path: str) -> str:
    text, _ = _read_text_preserve_bom(setup_py_path)
    m = re.search(r"(?m)^\s*version\s*=\s*['\"]([^'\"]+)['\"]\s*$", text)
    if not m:
        raise RuntimeError(f"无法在 {setup_py_path} 中找到 version = 'x.y.z.w' 形式的版本号")
    version = m.group(1).strip()
    if not re.match(r"^\d+(\.\d+){3}$", version):
        raise RuntimeError(f"版本号格式不符合 C# AssemblyVersion 要求(必须是 4 段数字): {version}")
    return version


def _sync_assembly_info(assembly_info_path: str, version: str) -> bool:
    text, has_bom = _read_text_preserve_bom(assembly_info_path)

    def repl(attr: str) -> tuple[str, int]:
        pattern = rf'(?m)^\s*\[assembly:\s*{attr}\("([^"]*)"\)\]\s*$'
        return re.subn(pattern, f'[assembly: {attr}("{version}")]', text)

    changed = False

    # AssemblyVersion
    new_text, n = re.subn(
        r'(?m)^\s*\[assembly:\s*AssemblyVersion\("([^"]*)"\)\]\s*$',
        f'[assembly: AssemblyVersion("{version}")]',
        text,
    )
    if n > 0:
        text = new_text
        changed = True

    # AssemblyFileVersion
    new_text, n = re.subn(
        r'(?m)^\s*\[assembly:\s*AssemblyFileVersion\("([^"]*)"\)\]\s*$',
        f'[assembly: AssemblyFileVersion("{version}")]',
        text,
    )
    if n > 0:
        text = new_text
        changed = True

    # AssemblyInformationalVersion（产品版本更直观；如果没有就插入）
    info_pat = r'(?m)^\s*\[assembly:\s*AssemblyInformationalVersion\("([^"]*)"\)\]\s*$'
    if re.search(info_pat, text):
        new_text, n = re.subn(
            info_pat,
            f'[assembly: AssemblyInformationalVersion("{version}")]',
            text,
        )
        if n > 0:
            text = new_text
            changed = True
    else:
        newline = "\r\n" if "\r\n" in text else "\n"
        insert_line = f'[assembly: AssemblyInformationalVersion("{version}")]'
        # 优先插在 AssemblyFileVersion 后面；没有就插在 AssemblyVersion 后面；都没有就追加到文件末尾
        if re.search(r'(?m)^\s*\[assembly:\s*AssemblyFileVersion\("([^"]*)"\)\]\s*$', text):
            text = re.sub(
                r'(?m)^\s*(\[assembly:\s*AssemblyFileVersion\("([^"]*)"\)\]\s*)$',
                r"\1" + newline + insert_line,
                text,
                count=1,
            )
            changed = True
        elif re.search(r'(?m)^\s*\[assembly:\s*AssemblyVersion\("([^"]*)"\)\]\s*$', text):
            text = re.sub(
                r'(?m)^\s*(\[assembly:\s*AssemblyVersion\("([^"]*)"\)\]\s*)$',
                r"\1" + newline + insert_line,
                text,
                count=1,
            )
            changed = True
        else:
            if not text.endswith(("\n", "\r\n")):
                text += newline
            text += insert_line + newline
            changed = True

    if changed:
        _write_text_preserve_bom(assembly_info_path, text, has_bom)
    return changed


def main() -> int:
    repo_root = os.path.dirname(os.path.abspath(__file__))
    setup_py = os.path.join(repo_root, "setup.py")
    assembly_info = os.path.join(repo_root, "DlcvDemo", "Properties", "AssemblyInfo.cs")

    version = _extract_version_from_setup_py(setup_py)

    if not os.path.exists(assembly_info):
        print(f"[sync_assembly_version] 未找到目标文件: {assembly_info}", file=sys.stderr)
        return 2

    changed = _sync_assembly_info(assembly_info, version)
    print(
        f"[sync_assembly_version] DlcvDemo AssemblyInfo 版本已{'更新' if changed else '确认一致'} -> {version}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

