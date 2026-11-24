#pragma once

#include <algorithm>
#include <exception>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>

namespace demo2 {

template <typename Work>
void RunModelTasks(int count, int numThreads, const Work& work) {
    if (count == 0) return;
    const int workersWanted = std::max(1, std::min(count, numThreads));
    if (workersWanted == 1) {
        for (int i = 0; i < count; ++i) work(i);
        return;
    }
    int nextIndex = 0;
    std::mutex errorMutex;
    std::exception_ptr error;
    const auto worker = [&]() {
        while (true) {
            int i;
            {
                std::lock_guard<std::mutex> lock(errorMutex);
                if (error || nextIndex >= count) return;
                i = nextIndex++;
            }
            try {
                work(i);
            } catch (...) {
                std::lock_guard<std::mutex> lock(errorMutex);
                if (!error) error = std::current_exception();
                return;
            }
        }
    };
    std::vector<std::thread> workers;
    workers.reserve(static_cast<size_t>(workersWanted));
    try {
        for (int i = 0; i < workersWanted; ++i) workers.emplace_back(worker);
    } catch (...) {
        std::lock_guard<std::mutex> lock(errorMutex);
        if (!error) error = std::current_exception();
    }
    for (auto& thread : workers) thread.join();
    if (error) std::rethrow_exception(error);
}

} // namespace demo2
