"""根据项目引用生成混编测试解决方案及共享启动配置。"""
import json
import os
from pathlib import Path
import xml.etree.ElementTree as ET

BASE = Path(__file__).resolve().parent
ROOT = BASE.parent.parent
NS = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}
PROFILE = "C# 与 C++ 混编模型测试"


def generate():
    projects = []
    seen = set()

    def collect(path):
        path = path.resolve()
        if path in seen:
            return
        seen.add(path)
        tree = ET.parse(path)
        guid = tree.find(".//m:ProjectGuid", NS).text
        kind = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC" if path.suffix == ".csproj" else "8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942"
        projects.append((kind, path.stem, os.path.relpath(path, BASE), guid))
        for reference in tree.findall(".//m:ProjectReference", NS):
            collect(path.parent / reference.attrib["Include"])

    collect(BASE / "DlcvCSharpCppTest.csproj")
    lines = ["Microsoft Visual Studio Solution File, Format Version 12.00", "# Visual Studio Version 17"]
    for kind, name, path, guid in projects:
        lines += [f'Project("{{{kind}}}") = "{name}", "{path}", "{guid}"', "EndProject"]
    lines += ["Global", "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution"]
    lines += [f"\t\t{config}|x64 = {config}|x64" for config in ("Debug", "Release")]
    lines += ["\tEndGlobalSection", "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution"]
    for _, _, _, guid in projects:
        for config in ("Debug", "Release"):
            for kind in ("ActiveCfg", "Build.0"):
                lines.append(f"\t\t{guid}.{config}|x64.{kind} = {config}|x64")
    lines += ["\tEndGlobalSection", "EndGlobal"]
    (BASE / "DlcvCSharpCppTest.sln").write_text("\n".join(lines) + "\n", encoding="utf-8-sig", newline="\r\n")
    for directory, stem in ((BASE, "DlcvCSharpCppTest"), (ROOT, "OpenIVS")):
        target = directory / (stem + ".slnLaunch")
        profiles = json.loads(target.read_text(encoding="utf-8")) if target.exists() else []
        profiles = [profile for profile in profiles if profile["Name"] != PROFILE]
        profiles.append({"Name": PROFILE, "Projects": [{"Path": os.path.relpath(BASE / "DlcvCSharpCppTest.csproj", directory), "Action": "Start"}]})
        target.write_text(json.dumps(profiles, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    generate()
