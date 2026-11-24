#pragma once

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <iostream>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#include "../../dlcv_infer_cpp_qt_demo2/ParallelInference.h"

inline int RunDemo2ParallelSelfTest() {
    using demo2::RunModelTasks;
    try {
        const auto check = [](bool condition, const char* message) {
            if (!condition) throw std::runtime_error(message);
        };
        const auto caller = std::this_thread::get_id();
        std::vector<int> serial;
        RunModelTasks(9, 1, [&](int index) {
            check(std::this_thread::get_id() == caller, "单线程须在调用线程执行");
            serial.push_back(index);
        });
        check(serial == std::vector<int>({0, 1, 2, 3, 4, 5, 6, 7, 8}), "单线程任务顺序发生变化");
        RunModelTasks(0, 32, [&](int) { throw std::runtime_error("空任务不应执行"); });
        RunModelTasks(1, 32, [&](int index) {
            check(index == 0 && std::this_thread::get_id() == caller, "任务数须限制实际线程数");
        });

        struct ActiveWork {
            std::atomic<int>& active;
            explicit ActiveWork(std::atomic<int>& count) : active(count) { ++active; }
            ~ActiveWork() { --active; }
        };
        for (const int threadCount : {2, 4, 32}) {
            std::atomic<int> active{0};
            std::atomic<int> peak{0};
            std::mutex mutex;
            std::condition_variable ready;
            int arrived = 0;
            std::vector<int> results(67, -1);
            RunModelTasks(static_cast<int>(results.size()), threadCount, [&](int index) {
                ActiveWork work(active);
                const int current = active.load();
                int observed = peak.load();
                while (observed < current && !peak.compare_exchange_weak(observed, current)) {}
                if (index < threadCount) {
                    std::unique_lock<std::mutex> lock(mutex);
                    ++arrived;
                    ready.notify_all();
                    if (!ready.wait_for(lock, std::chrono::seconds(5), [&]() { return arrived == threadCount; })) {
                        throw std::runtime_error("工作线程未能同时进入任务");
                    }
                }
                results[static_cast<size_t>(index)] = index * 3 + 7;
            });
            check(active.load() == 0 && peak.load() == threadCount, "实际并发数量错误或工作线程未结束");
            for (int index = 0; index < static_cast<int>(results.size()); ++index) {
                check(results[static_cast<size_t>(index)] == index * 3 + 7, "并行结果未按原索引保存");
            }
        }

        {
            std::mutex mutex;
            std::condition_variable ready;
            bool secondDone = false;
            std::vector<int> completionOrder;
            std::vector<int> results(9, -1);
            RunModelTasks(static_cast<int>(results.size()), 2, [&](int index) {
                std::unique_lock<std::mutex> lock(mutex);
                if (index == 0 && !ready.wait_for(lock, std::chrono::seconds(5), [&]() { return secondDone; })) {
                    throw std::runtime_error("第二个任务未完成");
                }
                results[static_cast<size_t>(index)] = index;
                completionOrder.push_back(index);
                if (index == 1) {
                    secondDone = true;
                    ready.notify_all();
                }
            });
            check(completionOrder.front() == 1, "测试须覆盖完成顺序变化");
            check(results == serial, "完成顺序变化影响了原输入顺序");
        }

        {
            std::atomic<int> active{0};
            std::atomic<bool> otherFinished{false};
            std::atomic<int> started{0};
            std::mutex mutex;
            std::condition_variable ready;
            int arrived = 0;
            bool caught = false;
            try {
                RunModelTasks(20, 2, [&](int index) {
                    ActiveWork work(active);
                    ++started;
                    {
                        std::unique_lock<std::mutex> lock(mutex);
                        ++arrived;
                        ready.notify_all();
                        if (!ready.wait_for(lock, std::chrono::seconds(5), [&]() { return arrived == 2; })) {
                            throw std::runtime_error("异常测试工作线程未就绪");
                        }
                    }
                    if (index == 0) throw std::logic_error("预期的任务异常");
                    std::this_thread::sleep_for(std::chrono::milliseconds(20));
                    otherFinished.store(true);
                });
            } catch (const std::logic_error& error) {
                caught = std::string(error.what()) == "预期的任务异常";
            }
            check(caught && active.load() == 0 && otherFinished.load(), "须等待其他工作线程结束后抛出实际异常");
            check(started.load() == 2, "异常后不得领取尚未开始的任务");
        }
        std::cout << "测试程序2外层并发测试通过" << std::endl;
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "测试程序2外层并发测试失败: " << error.what() << std::endl;
        return 1;
    }
}
