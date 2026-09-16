#pragma once

#include <functional>
#include <stdexcept>
#include <string>
#include <unordered_set>
#include <vector>

namespace dlcv_infer {

class DllLoader;

namespace detail {

struct SharedIndexCandidate final {
    void* Module = nullptr;
    DllLoader* Loader = nullptr;
    std::function<int(int)> Query;
};

inline const SharedIndexCandidate& SelectSharedIndexCandidate(
    int index,
    const std::vector<SharedIndexCandidate>& candidates,
    int& indexType) {
    if (index < 0) {
        throw std::invalid_argument("共享 index 无效");
    }
    if (candidates.empty()) {
        throw std::invalid_argument("Model not found.");
    }

    bool hasSharedIndexCandidate = false;
    struct Match final {
        const SharedIndexCandidate* Candidate = nullptr;
        int Type = 0;
    };
    std::vector<Match> matches;
    std::unordered_set<void*> seenModules;
    for (const auto& candidate : candidates) {
        if (!candidate.Query) continue;
        if (candidate.Module != nullptr &&
            !seenModules.insert(candidate.Module).second) {
            continue;
        }
        hasSharedIndexCandidate = true;
        int candidateType = 0;
        try {
            candidateType = candidate.Query(index);
        } catch (const std::exception& ex) {
            throw std::runtime_error(std::string("查询共享 index 失败: ") + ex.what());
        } catch (...) {
            throw std::runtime_error("查询共享 index 发生异常");
        }
        if (candidateType == 0) continue;
        if (candidateType != 1 && candidateType != 2) {
            throw std::runtime_error("共享 index 类型查询返回未知值");
        }
        matches.push_back({&candidate, candidateType});
    }

    if (!hasSharedIndexCandidate) {
        throw std::domain_error(
            "dlcv_infer 不支持共享模型索引接口；仅可使用加载时返回的本地模型 index");
    }
    if (matches.empty()) {
        throw std::invalid_argument("共享 index 不可用");
    }
    if (matches.size() != 1) {
        throw std::runtime_error("共享 index 在多个推理 DLL 中有效，无法确定所属模块");
    }
    indexType = matches.front().Type;
    return *matches.front().Candidate;
}

} // namespace detail
} // namespace dlcv_infer
