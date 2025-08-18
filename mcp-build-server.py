#!/usr/bin/env python3
"""
MCP Build Server - 通用Visual Studio构建服务
支持编译.sln解决方案和.csproj项目文件，可配置MSBuild路径
"""

import asyncio
import json
import logging
import os
import subprocess
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Union

# 配置日志
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("mcp-build-server")

# MCP相关导入
try:
    from mcp.server import NotificationOptions, Server
    from mcp.types import TextContent, Tool
except ImportError:
    print("请安装MCP包: pip install mcp")
    sys.exit(1)

# 创建服务器实例
app = Server("mcp-build-server")

# 全局变量存储MSBuild路径
msbuild_path = None

def _find_msbuild_path() -> str:
    """自动查找MSBuild路径"""
    possible_paths = [
        r"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
        r"C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
    ]
    
    for path in possible_paths:
        if os.path.exists(path):
            return path
    
    # 如果找不到，尝试从PATH中查找
    try:
        result = subprocess.run(["where", "msbuild"], capture_output=True, text=True, shell=True)
        if result.returncode == 0:
            return result.stdout.strip().split('\n')[0]
    except:
        pass
    
    return "msbuild"  # 默认使用PATH中的msbuild

# 初始化MSBuild路径
msbuild_path = _find_msbuild_path()

@app.list_tools()
async def handle_list_tools() -> List[Tool]:
    """列出可用的构建工具"""
    return [
        Tool(
            name="build_solution",
            description="编译Visual Studio解决方案文件(.sln)",
            inputSchema={
                "type": "object",
                "properties": {
                    "solution_path": {
                        "type": "string",
                        "description": "解决方案文件路径(.sln) - 建议使用绝对路径"
                    },
                    "configuration": {
                        "type": "string",
                        "description": "构建配置",
                        "enum": ["Debug", "Release"],
                        "default": "Debug"
                    },
                    "platform": {
                        "type": "string",
                        "description": "目标平台",
                        "enum": ["x86", "x64", "Any CPU"],
                        "default": "x64"
                    },
                    "msbuild_path": {
                        "type": "string",
                        "description": "MSBuild.exe的路径（可选，覆盖默认路径）"
                    },
                    "verbosity": {
                        "type": "string",
                        "description": "输出详细程度",
                        "enum": ["quiet", "minimal", "normal", "detailed", "diagnostic"],
                        "default": "normal"
                    },
                    "targets": {
                        "type": "string",
                        "description": "构建目标（可选，如Build, Clean, Rebuild等）",
                        "default": "Build"
                    }
                },
                "required": ["solution_path"]
            }
        ),
        Tool(
            name="build_project",
            description="编译单个项目文件(.csproj)",
            inputSchema={
                "type": "object",
                "properties": {
                    "project_path": {
                        "type": "string",
                        "description": "项目文件路径(.csproj) - 建议使用绝对路径"
                    },
                    "configuration": {
                        "type": "string",
                        "description": "构建配置",
                        "enum": ["Debug", "Release"],
                        "default": "Debug"
                    },
                    "platform": {
                        "type": "string",
                        "description": "目标平台",
                        "enum": ["x86", "x64", "Any CPU"],
                        "default": "x64"
                    },
                    "msbuild_path": {
                        "type": "string",
                        "description": "MSBuild.exe的路径（可选，覆盖默认路径）"
                    },
                    "verbosity": {
                        "type": "string",
                        "description": "输出详细程度",
                        "enum": ["quiet", "minimal", "normal", "detailed", "diagnostic"],
                        "default": "normal"
                    },
                    "targets": {
                        "type": "string",
                        "description": "构建目标（可选，如Build, Clean, Rebuild等）",
                        "default": "Build"
                    }
                },
                "required": ["project_path"]
            }
        ),
        Tool(
            name="get_msbuild_info",
            description="获取当前MSBuild配置信息",
            inputSchema={
                "type": "object",
                "properties": {},
                "additionalProperties": False
            }
        ),
        Tool(
            name="set_msbuild_path",
            description="设置MSBuild.exe的路径",
            inputSchema={
                "type": "object",
                "properties": {
                    "msbuild_path": {
                        "type": "string",
                        "description": "MSBuild.exe的完整路径"
                    }
                },
                "required": ["msbuild_path"]
            }
        ),
        Tool(
            name="list_projects_in_solution",
            description="列出解决方案中的所有项目",
            inputSchema={
                "type": "object",
                "properties": {
                    "solution_path": {
                        "type": "string",
                        "description": "解决方案文件路径(.sln) - 建议使用绝对路径"
                    }
                },
                "required": ["solution_path"]
            }
        )
    ]

@app.call_tool()
async def handle_call_tool(name: str, arguments: Dict[str, Any]) -> List[TextContent]:
    """处理工具调用"""
    
    if name == "build_solution":
        return await _build_solution(arguments)
    elif name == "build_project":
        return await _build_project(arguments)
    elif name == "get_msbuild_info":
        return await _get_msbuild_info()
    elif name == "set_msbuild_path":
        return await _set_msbuild_path(arguments)
    elif name == "list_projects_in_solution":
        return await _list_projects_in_solution(arguments)
    else:
        raise ValueError(f"未知工具: {name}")

async def _build_solution(args: Dict[str, Any]) -> List[TextContent]:
    """编译解决方案"""
    solution_path = args["solution_path"]
    configuration = args.get("configuration", "Debug")
    platform = args.get("platform", "x64")
    build_msbuild_path = args.get("msbuild_path", msbuild_path)
    verbosity = args.get("verbosity", "normal")
    targets = args.get("targets", "Build")

    if not os.path.exists(solution_path):
        return [TextContent(
            type="text",
            text=f"❌ 错误: 解决方案文件不存在: {solution_path}"
        )]

    if not solution_path.endswith('.sln'):
        return [TextContent(
            type="text",
            text=f"❌ 错误: 不是有效的解决方案文件: {solution_path}"
        )]

    return await _execute_msbuild(
        build_msbuild_path, solution_path, configuration, platform, verbosity, targets
    )

async def _build_project(args: Dict[str, Any]) -> List[TextContent]:
    """编译项目"""
    project_path = args["project_path"]
    configuration = args.get("configuration", "Debug")
    platform = args.get("platform", "x64")
    build_msbuild_path = args.get("msbuild_path", msbuild_path)
    verbosity = args.get("verbosity", "normal")
    targets = args.get("targets", "Build")

    if not os.path.exists(project_path):
        return [TextContent(
            type="text",
            text=f"❌ 错误: 项目文件不存在: {project_path}"
        )]

    if not project_path.endswith('.csproj'):
        return [TextContent(
            type="text",
            text=f"❌ 错误: 不是有效的项目文件: {project_path}"
        )]

    return await _execute_msbuild(
        build_msbuild_path, project_path, configuration, platform, verbosity, targets
    )

async def _execute_msbuild(
    build_msbuild_path: str, 
    file_path: str, 
    configuration: str, 
    platform: str, 
    verbosity: str,
    targets: str
) -> List[TextContent]:
    """执行MSBuild命令"""
    
    # 确保使用绝对路径
    absolute_file_path = os.path.abspath(file_path)
    
    cmd = [
        build_msbuild_path,
        absolute_file_path,
        f"/p:Configuration={configuration}",
        f"/p:Platform={platform}",
        f"/v:{verbosity}",
        f"/t:{targets}"
    ]

    try:
        logger.info(f"执行命令: {' '.join(cmd)}")
        
        # 设置工作目录为文件所在目录
        work_dir = os.path.dirname(absolute_file_path) if os.path.dirname(absolute_file_path) else os.getcwd()
        
        process = await asyncio.create_subprocess_exec(
            *cmd,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.STDOUT,
            cwd=work_dir
        )
        
        stdout, _ = await process.communicate()
        output = stdout.decode('utf-8', errors='replace')
        
        if process.returncode == 0:
            status = "✅ 构建成功"
            icon = "🎯"
        else:
            status = "❌ 构建失败"
            icon = "🚨"
        
        result_text = f"""{icon} MSBuild 构建结果

📁 文件: {absolute_file_path}
⚙️  配置: {configuration}
🔧 平台: {platform}
🎯 目标: {targets}
📊 状态: {status}
🔧 返回码: {process.returncode}

📋 构建输出:
{'-' * 80}
{output}
{'-' * 80}
"""
        
        return [TextContent(type="text", text=result_text)]
        
    except FileNotFoundError:
        return [TextContent(
            type="text",
            text=f"❌ 错误: 找不到MSBuild.exe: {build_msbuild_path}\n请检查路径或使用set_msbuild_path工具设置正确路径"
        )]
    except Exception as e:
        return [TextContent(
            type="text",
            text=f"❌ 构建过程中发生错误: {str(e)}"
        )]

async def _get_msbuild_info() -> List[TextContent]:
    """获取MSBuild信息"""
    info_text = f"""🔧 MSBuild 配置信息

📍 当前MSBuild路径: {msbuild_path}
✅ 路径存在: {os.path.exists(msbuild_path)}

🔍 支持的功能:
• 编译Visual Studio解决方案(.sln)
• 编译单个项目文件(.csproj)
• 支持Debug/Release配置
• 支持x86/x64/Any CPU平台
• 可配置构建详细程度
• 支持不同构建目标(Build/Clean/Rebuild等)
"""
    
    try:
        # 尝试获取MSBuild版本信息
        process = await asyncio.create_subprocess_exec(
            msbuild_path, "/version",
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE
        )
        stdout, stderr = await process.communicate()
        
        if process.returncode == 0:
            version_info = stdout.decode('utf-8', errors='replace')
            info_text += f"\n📝 MSBuild版本信息:\n{version_info}"
        else:
            info_text += f"\n⚠️  无法获取版本信息: {stderr.decode('utf-8', errors='replace')}"
            
    except Exception as e:
        info_text += f"\n❌ 获取版本信息时出错: {str(e)}"
    
    return [TextContent(type="text", text=info_text)]

async def _set_msbuild_path(args: Dict[str, Any]) -> List[TextContent]:
    """设置MSBuild路径"""
    global msbuild_path
    new_path = args["msbuild_path"]
    
    if not os.path.exists(new_path):
        return [TextContent(
            type="text",
            text=f"❌ 错误: 指定的MSBuild路径不存在: {new_path}"
        )]
    
    if not new_path.lower().endswith('msbuild.exe'):
        return [TextContent(
            type="text",
            text=f"❌ 错误: 指定的文件不是MSBuild.exe: {new_path}"
        )]
    
    old_path = msbuild_path
    msbuild_path = new_path
    
    return [TextContent(
        type="text",
        text=f"✅ MSBuild路径已更新\n旧路径: {old_path}\n新路径: {new_path}"
    )]

async def _list_projects_in_solution(args: Dict[str, Any]) -> List[TextContent]:
    """列出解决方案中的项目"""
    solution_path = args["solution_path"]
    
    if not os.path.exists(solution_path):
        return [TextContent(
            type="text",
            text=f"❌ 错误: 解决方案文件不存在: {solution_path}"
        )]
    
    try:
        with open(solution_path, 'r', encoding='utf-8-sig') as f:
            content = f.read()
        
        projects = []
        lines = content.split('\n')
        
        for line in lines:
            line = line.strip()
            if line.startswith('Project(') and '.csproj' in line:
                # 解析项目行: Project("{GUID}") = "ProjectName", "Path\ProjectName.csproj", "{ProjectGUID}"
                parts = line.split('"')
                if len(parts) >= 6:
                    project_name = parts[3]  # 项目名称在第4个引号中
                    project_path = parts[5]  # 项目路径在第6个引号中
                    projects.append(f"• {project_name} ({project_path})")
        
        if projects:
            result_text = f"📁 解决方案: {os.path.basename(solution_path)}\n\n🎯 包含的项目:\n" + "\n".join(projects)
        else:
            result_text = f"📁 解决方案: {os.path.basename(solution_path)}\n\n⚠️  未找到任何C#项目"
        
        return [TextContent(type="text", text=result_text)]
        
    except Exception as e:
        return [TextContent(
            type="text",
            text=f"❌ 读取解决方案文件时出错: {str(e)}"
        )]

async def main():
    """主函数"""
    # 使用stdio传输
    from mcp.server.stdio import stdio_server
    
    async with stdio_server() as (read_stream, write_stream):
        await app.run(
            read_stream,
            write_stream,
            app.create_initialization_options()
        )

if __name__ == "__main__":
    asyncio.run(main())