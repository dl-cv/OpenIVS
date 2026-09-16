"""更新现有 OpenIVS 解决方案中的混编测试启动配置，不生成解决方案。"""
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET

BASE = Path(__file__).resolve().parent
ROOT = BASE.parent.parent
PROFILE = "C# 与 C++ 混编模型测试"
NS = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}


def generate():
    project = BASE / "DlcvCSharpCppTest.csproj"
    relative = os.path.relpath(project, ROOT)
    solution = (ROOT / "OpenIVS.sln").read_text(encoding="utf-8-sig")
    tree = ET.parse(project)
    guid = tree.find(".//m:ProjectGuid", NS).text
    if f'"{relative}", "{guid}"' not in solution:
        raise ValueError("现有 OpenIVS.sln 尚未包含 C# 测试工程")
    if tree.find(".//m:OutputType", NS).text != "WinExe":
        raise ValueError("启动工程必须为 WinForms 应用")
    target = ROOT / "OpenIVS.slnLaunch"
    profiles = json.loads(target.read_text(encoding="utf-8")) if target.exists() else []
    profiles = [profile for profile in profiles if profile["Name"] != PROFILE]
    profiles.append({"Name": PROFILE, "Projects": [{"Path": relative, "Action": "Start"}]})
    target.write_text(json.dumps(profiles, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    generate()
