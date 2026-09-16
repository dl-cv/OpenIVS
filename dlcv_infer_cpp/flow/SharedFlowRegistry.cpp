#include "SharedFlowRegistry.h"
#include "../dlcv_infer.h"
#include <cstring>
#include <algorithm>
#include <cctype>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <set>
#ifdef _WIN32
#include <Windows.h>
#else
#include <dlfcn.h>
#endif

namespace {
using json = nlohmann::json;
using IndexCall = int (OPENIVS_FLOW_CALL*)(int);
using AllocateCall = int (OPENIVS_FLOW_CALL*)();
template<class T> T symbol(void* module, const char* name) {
#ifdef _WIN32
    return reinterpret_cast<T>(GetProcAddress(static_cast<HMODULE>(module), name));
#else
    return reinterpret_cast<T>(dlsym(module, name));
#endif
}
struct FlowRecord {
    json data;
    std::vector<int> models;
    IndexCall unbind = nullptr;
    IndexCall query = nullptr;
    bool owner = true;
    int references = 0;
    ~FlowRecord() {
        for (int index : models) {
            try { unbind(index); } catch (...) {}
        }
    }
    bool valid() const {
        for (int index : models) if (query(index) != 1) return false;
        return true;
    }
};
using Key = std::pair<void*, int>;
std::mutex registryMutex;
std::map<Key, std::unique_ptr<FlowRecord>> records;
}

int OPENIVS_FLOW_CALL openivs_flow_register(void* module, const char* text) {
    try {
        if (!module || !text) return -1;
        const auto allocate = symbol<AllocateCall>(module, "dlcv_allocate_index_c");
        const auto bind = symbol<IndexCall>(module, "dlcv_bind_index_c");
        const auto unbind = symbol<IndexCall>(module, "dlcv_unbind_index_c");
        const auto query = symbol<IndexCall>(module, "dlcv_get_index_type_c");
        if (!allocate || !bind || !unbind || !query) return -1;
        auto record = std::make_unique<FlowRecord>();
        record->data = json::parse(text);
        record->unbind = unbind;
        record->query = query;
        const auto& data = record->data;
        if (!data.is_object() || data.value("schema_version", 0) != 1 ||
            !data.contains("pipeline") || !data.at("pipeline").is_object() ||
            !data.contains("model_bindings") || !data.at("model_bindings").is_array() ||
            !data.contains("provider") || !data.at("provider").is_string() ||
            !data.contains("source_path") || !data.at("source_path").is_string() ||
            !data.contains("flow_type") || !data.at("flow_type").is_string()) return -1;
        std::string provider = data.at("provider").get<std::string>();
        std::transform(provider.begin(), provider.end(), provider.begin(), [](unsigned char ch) {
            return static_cast<char>(std::tolower(ch));
        });
        if (provider != "sentinel" && provider != "virbox") return -1;
        const std::string type = data.at("flow_type").get<std::string>();
        if (type != "dvst" && type != "dvso") return -1;
        record->data["provider"] = provider;
        std::set<int> nodes, models;
        for (const auto& item : data.at("model_bindings")) {
            if (!item.is_object() || !item.contains("node_id") || !item.contains("model_index")) return -1;
            const auto& node = item.at("node_id");
            const auto& index = item.at("model_index");
            if (!node.is_number_integer() || node < 0 || node > std::numeric_limits<int>::max() ||
                !index.is_number_integer() || index < 0 || index > std::numeric_limits<int>::max()) return -1;
            if (!nodes.insert(node.get<int>()).second) return -1;
            models.insert(index.get<int>());
        }
        record->models.reserve(models.size());
        std::lock_guard<std::mutex> lock(registryMutex);
        for (int index : models) {
            if (query(index) != 1 || bind(index) != 0) return -1;
            record->models.push_back(index);
        }
        const int index = allocate();
        if (index < 0) return -1;
        if (provider != ((index & 0x100) ? "virbox" : "sentinel")) return -1;
        record->data["flow_index"] = index;
        if (!records.emplace(Key{module, index}, std::move(record)).second) return -1;
        return index;
    } catch (...) { return -1; }
}

int OPENIVS_FLOW_CALL openivs_flow_contains(void* module, int index) {
    try {
        std::lock_guard<std::mutex> lock(registryMutex);
        const auto it = records.find({module, index});
        return it != records.end() && it->second->valid() ? 1 : 0;
    } catch (...) { return -1; }
}

const char* OPENIVS_FLOW_CALL openivs_flow_get_info(void* module, int index) {
    try {
        std::lock_guard<std::mutex> lock(registryMutex);
        const auto it = records.find({module, index});
        json result = {{"code", 2}, {"message", "流程不存在或已释放"}};
        if (it != records.end() && it->second->valid()) {
            result = it->second->data;
            result["code"] = 0;
            result["index_type"] = "flow";
        }
        const std::string text = result.dump();
        auto copy = std::make_unique<char[]>(text.size() + 1);
        std::memcpy(copy.get(), text.c_str(), text.size() + 1);
        return copy.release();
    } catch (...) { return nullptr; }
}

int OPENIVS_FLOW_CALL openivs_flow_retain(void* module, int index) {
    try {
        std::lock_guard<std::mutex> lock(registryMutex);
        const auto it = records.find({module, index});
        if (it == records.end() || !it->second->valid() ||
            it->second->references == std::numeric_limits<int>::max()) return -1;
        ++it->second->references;
        return 0;
    } catch (...) { return -1; }
}

int OPENIVS_FLOW_CALL openivs_flow_release(void* module, int index, int owner) {
    try {
        std::unique_ptr<FlowRecord> removed;
        {
            std::lock_guard<std::mutex> lock(registryMutex);
            const auto it = records.find({module, index});
            if (it == records.end()) return -1;
            auto& record = *it->second;
            if (owner) {
                if (!record.owner) return -1;
                record.owner = false;
            } else {
                if (record.references == 0) return -1;
                --record.references;
            }
            if (!record.owner && record.references == 0) {
                removed = std::move(it->second);
                records.erase(it);
            }
        }
        return 0;
    } catch (...) { return -1; }
}

void OPENIVS_FLOW_CALL openivs_flow_free_all_models(void* module) {
    try {
        if (!module) return;
        using FreeAll = void (OPENIVS_FLOW_CALL*)();
        const auto freeAll = symbol<FreeAll>(module, "dlcv_free_all_models");
        if (!freeAll) return;
        std::lock_guard<std::mutex> lock(registryMutex);
        for (auto it = records.begin(); it != records.end();) {
            if (it->first.first == module) it = records.erase(it);
            else ++it;
        }
        freeAll();
    } catch (...) {}
}

void OPENIVS_FLOW_CALL openivs_flow_free_result(const char* result) { delete[] result; }
