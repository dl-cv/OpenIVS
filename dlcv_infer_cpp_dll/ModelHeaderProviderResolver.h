#pragma once

#include <string>
#include "dlcv_sntl_admin.h"

namespace sntl_admin {
    DogProvider ResolveModelHeaderProvider(const std::wstring& modelPath);
    DogProvider ResolveModelHeaderProvider(const std::string& modelPathUtf8OrGbk);

    /// <summary>
    /// 尝试从模型头解析明确指定的 dog_provider。
    /// 返回 true 表示模型头中明确写入了 dog_provider，outProvider 为解析值；
    /// 返回 false 表示模型头未指定 dog_provider（调用方应走自动检测逻辑）。
    /// </summary>
    bool TryResolveExplicitProvider(const std::wstring& modelPath, DogProvider& outProvider);
    bool TryResolveExplicitProvider(const std::string& modelPath, DogProvider& outProvider);
}
