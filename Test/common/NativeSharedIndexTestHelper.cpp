#include "NativeSharedIndexTestHelper.h"

#include <windows.h>
#include <TlHelp32.h>

#include <algorithm>
#include <cstdint>
#include <exception>
#include <iostream>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <typeinfo>
#include <utility>
#include <vector>

#include "../../dlcv_infer_cpp/SharedIndexResolver.h"
#include "../../dlcv_infer_cpp/dlcv_infer.h"
#include "../../dlcv_infer_cpp/flow/modules/ModelModules.h"

namespace {

std::mutex& OwnedModelsMutex() {
    static std::mutex mutex;
    return mutex;
}

std::map<int, std::shared_ptr<dlcv_infer::Model>>& OwnedModels() {
    static std::map<int, std::shared_ptr<dlcv_infer::Model>> models;
    return models;
}

std::vector<dlcv_infer::detail::SharedIndexCandidate> CollectLoadedCandidates() {
    const HANDLE snapshot = CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,
        GetCurrentProcessId());
    if (snapshot == INVALID_HANDLE_VALUE) {
        throw std::runtime_error("无法枚举当前进程模块");
    }

    std::vector<dlcv_infer::detail::SharedIndexCandidate> candidates;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    BOOL hasEntry = Module32FirstW(snapshot, &entry);
    while (hasEntry) {
        const bool isTarget = _wcsicmp(entry.szModule, L"dlcv_infer.dll") == 0 ||
            _wcsicmp(entry.szModule, L"dlcv_infer_v.dll") == 0;
        if (isTarget) {
            const auto duplicate = std::find_if(
                candidates.begin(), candidates.end(),
                [&](const auto& candidate) { return candidate.Module == entry.hModule; });
            if (duplicate == candidates.end()) {
                using SharedJsonFunction = const char* (DLCV_INFER_NATIVE_CALL*)(const char*);
                using FreeResultFunction = void (DLCV_INFER_NATIVE_CALL*)(const char*);
                const auto query = reinterpret_cast<SharedJsonFunction>(
                    GetProcAddress(entry.hModule, "dlcv_get_index_type"));
                const auto freeResult = reinterpret_cast<FreeResultFunction>(
                    GetProcAddress(entry.hModule, "dlcv_free_result"));
                dlcv_infer::detail::SharedIndexCandidate candidate;
                candidate.Module = entry.hModule;
                if (query != nullptr && freeResult != nullptr) {
                    candidate.Query = [query, freeResult](int index) {
                        const std::string request = dlcv_infer::json{{"model_index", index}}.dump();
                        const char* resultPtr = query(request.c_str());
                        if (resultPtr == nullptr) throw std::runtime_error("索引类型查询未返回结果");
                        dlcv_infer::json result;
                        try {
                            result = dlcv_infer::json::parse(resultPtr);
                            freeResult(resultPtr);
                        } catch (...) {
                            freeResult(resultPtr);
                            throw;
                        }
                        if (!result.is_object() || !result.contains("code") ||
                            !result.at("code").is_number_integer()) {
                            throw std::runtime_error("索引类型查询返回结构无效");
                        }
                        const int code = result.at("code").get<int>();
                        if (code == 2) return 0;
                        if (code != 0 || !result.contains("model_index") ||
                            !result.at("model_index").is_number_integer() ||
                            result.at("model_index").get<int>() != index ||
                            !result.contains("resource_type") ||
                            !result.at("resource_type").is_string()) {
                            throw std::runtime_error("索引类型查询失败");
                        }
                        const std::string type = result.at("resource_type").get<std::string>();
                        if (type == "model") return 1;
                        if (type == "dvs") return 2;
                        throw std::runtime_error("索引类型查询返回未知资源类型");
                    };
                }
                candidates.push_back(std::move(candidate));
            }
        }
        hasEntry = Module32NextW(snapshot, &entry);
    }

    const DWORD error = GetLastError();
    CloseHandle(snapshot);
    if (!hasEntry && error != ERROR_NO_MORE_FILES) {
        throw std::runtime_error("枚举当前进程模块失败: " + std::to_string(error));
    }
    return candidates;
}

template <typename ExceptionType>
bool ExpectResolverFailure(
    const std::vector<dlcv_infer::detail::SharedIndexCandidate>& candidates,
    int index) {
    int indexType = 0;
    try {
        (void)dlcv_infer::detail::SelectSharedIndexCandidate(index, candidates, indexType);
    } catch (const std::exception& ex) {
        return typeid(ex) == typeid(ExceptionType);
    } catch (...) {
        return false;
    }
    return false;
}

} // namespace

namespace dlcv_test {

int LoadOwnedModel(const std::wstring& modelPath, int deviceId) {
    if (modelPath.empty()) return -1;
    try {
        auto owner = std::make_shared<dlcv_infer::Model>(modelPath, deviceId);
        const int index = owner->modelIndex;
        if (index == -1) return -1;
        std::lock_guard<std::mutex> lock(OwnedModelsMutex());
        const auto inserted = OwnedModels().emplace(index, std::move(owner));
        return inserted.second ? index : -1;
    } catch (const std::exception& ex) {
        std::cerr << "共享索引测试加载失败: " << ex.what() << std::endl;
        return -1;
    } catch (...) {
        std::cerr << "共享索引测试加载失败: 未知异常" << std::endl;
        return -1;
    }
}

bool ReleaseOwnedModel(int modelIndex) noexcept {
    std::shared_ptr<dlcv_infer::Model> owner;
    {
        std::lock_guard<std::mutex> lock(OwnedModelsMutex());
        const auto found = OwnedModels().find(modelIndex);
        if (found == OwnedModels().end()) return false;
        owner = std::move(found->second);
        OwnedModels().erase(found);
    }
    try {
        owner->FreeModel();
    } catch (const std::exception& ex) {
        std::cerr << "共享索引测试底层释放信息: " << ex.what() << std::endl;
    } catch (...) {
        std::cerr << "共享索引测试底层释放信息: 未知异常" << std::endl;
    }
    return true;
}

std::string GetBorrowedModelInfoResult(int modelIndex) {
    dlcv_infer::json response;
    try {
        dlcv_infer::Model model;
        model.modelIndex = modelIndex;
        model.OwnModelIndex = false;
        response["code"] = 0;
        response["index"] = modelIndex;
        response["model_info"] = model.GetModelInfo();
    } catch (const std::exception& ex) {
        response["code"] = 1;
        response["message"] = ex.what();
    } catch (...) {
        response["code"] = 1;
        response["message"] = "shared index info failed";
    }
    return response.dump();
}

int QueryLoadedSharedIndexType(int modelIndex) {
    try {
        const auto candidates = CollectLoadedCandidates();
        int indexType = 0;
        (void)dlcv_infer::detail::SelectSharedIndexCandidate(
            modelIndex, candidates, indexType);
        return indexType;
    } catch (const std::invalid_argument&) {
        return 0;
    } catch (const std::domain_error&) {
        return 0;
    }
}

int RunSharedIndexResolverSelfTest() {
    int firstTag = 0;
    int secondTag = 0;
    int selectedType = 0;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> firstCandidates = {
        {&firstTag, nullptr, [](int value) { return value == 256 ? 1 : 0; }},
        {&secondTag, nullptr, [](int) { return 0; }}
    };
    if (dlcv_infer::detail::SelectSharedIndexCandidate(
            256, firstCandidates, selectedType).Module != &firstTag ||
        selectedType != 1) {
        return -1;
    }

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> secondCandidates = {
        {&firstTag, nullptr, [](int) { return 0; }},
        {&secondTag, nullptr, [](int value) { return value == 0 ? 2 : 0; }}
    };
    if (dlcv_infer::detail::SelectSharedIndexCandidate(
            0, secondCandidates, selectedType).Module != &secondTag ||
        selectedType != 2) {
        return -2;
    }

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> negativeCandidates = {
        {&firstTag, nullptr, [](int) { return 2; }}
    };
    for (int invalidIndex : {-1, -2, std::numeric_limits<int>::min()}) {
        if (!ExpectResolverFailure<std::invalid_argument>(negativeCandidates, invalidIndex)) return -12;
    }

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> noResultCandidates = {
        {&firstTag, nullptr, [](int) { return 0; }}
    };
    if (!ExpectResolverFailure<std::invalid_argument>(noResultCandidates, 0)) return -3;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> ambiguousCandidates = {
        {&firstTag, nullptr, [](int) { return 1; }},
        {&secondTag, nullptr, [](int) { return 2; }}
    };
    if (!ExpectResolverFailure<std::runtime_error>(ambiguousCandidates, 0)) return -5;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> unknownCandidates = {
        {&firstTag, nullptr, [](int) { return -1; }}
    };
    if (!ExpectResolverFailure<std::runtime_error>(unknownCandidates, 0)) return -6;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> throwingCandidates = {
        {&firstTag, nullptr, [](int) -> int { throw std::runtime_error("query failed"); }}
    };
    if (!ExpectResolverFailure<std::runtime_error>(throwingCandidates, 0)) return -7;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> domainErrorCandidates = {
        {&firstTag, nullptr, [](int) { return 1; }},
        {&secondTag, nullptr, [](int) -> int {
            throw std::domain_error("query failure is not missing capability");
        }}
    };
    if (!ExpectResolverFailure<std::runtime_error>(domainErrorCandidates, 0)) return -8;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> nonstandardErrorCandidates = {
        {&firstTag, nullptr, [](int) -> int { throw 7; }}
    };
    if (!ExpectResolverFailure<std::runtime_error>(nonstandardErrorCandidates, 0)) return -9;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> noInterfaceCandidates = {
        {&firstTag, nullptr, {}}
    };
    if (!ExpectResolverFailure<std::domain_error>(noInterfaceCandidates, 0)) return -10;

    const std::vector<dlcv_infer::detail::SharedIndexCandidate> emptyCandidates;
    if (!ExpectResolverFailure<std::invalid_argument>(emptyCandidates, 0)) return -11;
    return 0;
}


int RunFlowModelIndexRulesSelfTest() {
    using dlcv_infer::json;
    using dlcv_infer::flow::detail::ReadModelIndexProperty;

    if (ReadModelIndexProperty(json::object()) != -1) return -1;
    if (ReadModelIndexProperty(json{{"model_index", 0}}) != 0) return -2;
    if (ReadModelIndexProperty(
            json{{"model_index", std::numeric_limits<int>::max()}}) !=
        std::numeric_limits<int>::max()) {
        return -3;
    }

    const json invalidValues[] = {
        -1,
        static_cast<std::uint64_t>(std::numeric_limits<int>::max()) + 1,
        0.0,
        "0",
        true,
        nullptr
    };
    for (const auto& value : invalidValues) {
        bool rejected = false;
        try {
            (void)ReadModelIndexProperty(json{{"model_index", value}});
        } catch (const std::invalid_argument&) {
            rejected = true;
        } catch (...) {
            return -4;
        }
        if (!rejected) return -5;
    }

    json pipeline = {
        {"nodes", json::array({
            json{{"id", 1}, {"type", "model/det"}, {"properties", {
                {"model_path", "model.dvt"}, {"model_index", std::numeric_limits<int>::max()}
            }}},
            json{{"id", 2}, {"type", "post/result"}, {"properties", {
                {"model_index", 7}
            }}}
        })}
    };
    dlcv_infer::flow::detail::RemoveArchiveModelIndexes(pipeline);
    const auto& nodes = pipeline.at("nodes");
    if (nodes.at(0).at("properties").contains("model_index")) return -6;
    if (nodes.at(0).at("properties").value("model_path", std::string()) != "model.dvt") return -7;
    if (nodes.at(1).at("properties").value("model_index", -1) != 7) return -8;
    return 0;
}

} // namespace dlcv_test
