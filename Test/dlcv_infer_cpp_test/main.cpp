#include <windows.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstdio>
#include <exception>
#include <fstream>
#include <functional>
#include <iomanip>
#include <iostream>
#include <memory>
#include <sstream>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "../../dlcv_infer_cpp_dll/ImageInputUtils.h"
#include "../../dlcv_infer_cpp_dll/flow/FlowGraphModel.h"
#include "dlcv_infer.h"

namespace {
using json = nlohmann::json;
using Clock = std::chrono::steady_clock;

std::string Safe(const std::string& s) {
    std::string out = s.empty() ? "-" : s;
    for (auto& ch : out) {
        if (ch == '|') ch = '/';
        if (ch == '\n' || ch == '\r') ch = ' ';
    }
    return out;
}

std::string ToFixed(double v, int precision) {
    std::ostringstream oss;
    oss << std::fixed << std::setprecision(precision) << v;
    return oss.str();
}

std::string WideToUtf8(const std::wstring& w) {
    if (w.empty()) return {};
    int bytes = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) return {};
    std::string out(static_cast<size_t>(bytes), '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), out.data(), bytes, nullptr, nullptr);
    return out;
}

std::wstring Utf8ToWide(const std::string& s) {
    if (s.empty()) return {};
    int chars = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), nullptr, 0);
    if (chars <= 0) return {};
    std::wstring out(static_cast<size_t>(chars), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), out.data(), chars);
    return out;
}

void DisposeResultMasks(dlcv_infer::Result& out) {
    for (auto& sr : out.sampleResults) {
        for (auto& o : sr.results) {
            if (!o.mask.empty()) o.mask.release();
        }
    }
}

std::string BuildTempRectCorrectionDir() {
    char tempPath[MAX_PATH] = {0};
    const DWORD n = GetTempPathA(static_cast<DWORD>(sizeof(tempPath)), tempPath);
    std::string base = (n > 0 && n < sizeof(tempPath)) ? std::string(tempPath) : std::string(".\\");
    const char last = base.empty() ? '\0' : base.back();
    if (last != '\\' && last != '/') base.push_back('\\');
    std::string dir = base + "dlcv_rect_image_correction_" + std::to_string(GetCurrentProcessId());
    CreateDirectoryA(dir.c_str(), nullptr);
    return dir;
}

std::string JoinPathA(const std::string& dir, const std::string& name) {
    if (dir.empty()) return name;
    const char last = dir.back();
    if (last == '\\' || last == '/') return dir + name;
    return dir + "\\" + name;
}

void DeleteFilesWithSuffix(const std::string& dir, const std::string& suffixWithExt) {
    WIN32_FIND_DATAA data{};
    const std::string pattern = JoinPathA(dir, "*");
    HANDLE h = FindFirstFileA(pattern.c_str(), &data);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
        const std::string name = data.cFileName;
        if (name.size() >= suffixWithExt.size() &&
            name.compare(name.size() - suffixWithExt.size(), suffixWithExt.size(), suffixWithExt) == 0) {
            DeleteFileA(JoinPathA(dir, name).c_str());
        }
    } while (FindNextFileA(h, &data));
    FindClose(h);
}

cv::Mat LoadSingleFileWithSuffix(const std::string& dir, const std::string& suffixWithExt) {
    cv::Mat loaded;
    int matchCount = 0;
    WIN32_FIND_DATAA data{};
    const std::string pattern = JoinPathA(dir, "*");
    HANDLE h = FindFirstFileA(pattern.c_str(), &data);
    if (h == INVALID_HANDLE_VALUE) return loaded;
    do {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) continue;
        const std::string name = data.cFileName;
        if (name.size() >= suffixWithExt.size() &&
            name.compare(name.size() - suffixWithExt.size(), suffixWithExt.size(), suffixWithExt) == 0) {
            matchCount += 1;
            loaded = cv::imread(JoinPathA(dir, name), cv::IMREAD_UNCHANGED);
        }
    } while (FindNextFileA(h, &data));
    FindClose(h);
    return matchCount == 1 ? loaded : cv::Mat();
}

int RunImagePrepCheck() {
    auto fail = [](const std::string& message) -> int {
        std::cout << "imageprepcheck failed: " << message << "\n";
        return 1;
    };

    {
        cv::Mat gray16(1, 3, CV_16UC1);
        gray16.at<std::uint16_t>(0, 0) = 0;
        gray16.at<std::uint16_t>(0, 1) = 256;
        gray16.at<std::uint16_t>(0, 2) = 512;
        const cv::Mat rgb = dlcv_infer::image_input::NormalizeInferInputImage(gray16, 3);
        if (rgb.type() != CV_8UC3) {
            return fail("16-bit grayscale to RGB type mismatch");
        }
        const cv::Vec3b p0 = rgb.at<cv::Vec3b>(0, 0);
        const cv::Vec3b p1 = rgb.at<cv::Vec3b>(0, 1);
        const cv::Vec3b p2 = rgb.at<cv::Vec3b>(0, 2);
        if (p0 != cv::Vec3b(0, 0, 0) || p1 != cv::Vec3b(1, 1, 1) || p2 != cv::Vec3b(2, 2, 2)) {
            return fail("16-bit grayscale to RGB pixel value mismatch");
        }
    }

    {
        cv::Mat bgra(1, 1, CV_8UC4);
        bgra.at<cv::Vec4b>(0, 0) = cv::Vec4b(10, 20, 30, 200);
        const cv::Mat rgb = dlcv_infer::image_input::NormalizeInferInputImage(bgra, 3);
        if (rgb.type() != CV_8UC3) {
            return fail("BGRA to RGB type mismatch");
        }
        const cv::Vec3b pixel = rgb.at<cv::Vec3b>(0, 0);
        if (pixel != cv::Vec3b(30, 20, 10)) {
            return fail("BGRA to RGB pixel order mismatch");
        }
    }

    {
        cv::Mat rgb(1, 1, CV_8UC3);
        rgb.at<cv::Vec3b>(0, 0) = cv::Vec3b(30, 20, 10);
        cv::Mat expectedGray;
        cv::cvtColor(rgb, expectedGray, cv::COLOR_RGB2GRAY);
        const cv::Mat gray = dlcv_infer::image_input::NormalizeInferInputImage(rgb, 1);
        if (gray.type() != CV_8UC1) {
            return fail("RGB to gray type mismatch");
        }
        if (gray.at<std::uint8_t>(0, 0) != expectedGray.at<std::uint8_t>(0, 0)) {
            return fail("RGB to gray pixel value mismatch");
        }
    }

    std::cout << "imageprepcheck passed\n";
    return 0;
}

int RunRectImageCorrectionSelfTest() {
    auto fail = [](const std::string& message) -> int {
        std::cout << "rect_image_correction selftest failed: " << message << "\n";
        return 1;
    };

    const std::string saveDir = BuildTempRectCorrectionDir();
    const std::string suffix = "_rect_image_correction_test";
    DeleteFilesWithSuffix(saveDir, suffix + ".png");

    const std::string flowPath = JoinPathA(saveDir, "rect_image_correction_flow.json");
    json flow = json::object();
    flow["nodes"] = json::array({
        {
            {"id", 1},
            {"order", 1},
            {"type", "input/frontend_image"},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({102})}})
            })}
        },
        {
            {"id", 2},
            {"order", 2},
            {"type", "pre_process/rect_image_correction"},
            {"properties", json::object({{"rotate_direction", "clockwise"}})},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", 101}}),
                json::object({{"type", "result_chan"}, {"link", 102}})
            })},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({202})}})
            })}
        },
        {
            {"id", 3},
            {"order", 3},
            {"type", "output/save_image"},
            {"properties", json::object({{"save_path", saveDir}, {"suffix", suffix}, {"format", "png"}})},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", 201}}),
                json::object({{"type", "result_chan"}, {"link", 202}})
            })},
            {"outputs", json::array()}
        }
    });

    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) return fail("cannot write temp flow file");
        ofs << flow.dump(2);
    }

    cv::Mat tall(3, 2, CV_8UC3);
    for (int y = 0; y < tall.rows; ++y) {
        for (int x = 0; x < tall.cols; ++x) {
            tall.at<cv::Vec3b>(y, x) = cv::Vec3b(static_cast<uchar>(10 + x), static_cast<uchar>(20 + y), 30);
        }
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            return fail(std::string("flow load failed: ") + loadReport.dump());
        }
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{tall}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            return fail(std::string("flow infer failed: ") + inferRoot.dump());
        }
    } catch (const std::exception& ex) {
        return fail(std::string("exception: ") + ex.what());
    }

    const cv::Mat saved = LoadSingleFileWithSuffix(saveDir, suffix + ".png");
    if (saved.empty()) {
        return fail("corrected image not saved");
    }
    if (saved.cols != 3 || saved.rows != 2) {
        return fail("portrait image not rotated to landscape");
    }

    DeleteFilesWithSuffix(saveDir, suffix + ".png");
    DeleteFileA(flowPath.c_str());
    std::cout << "rect_image_correction selftest passed\n";
    return 0;
}

int CountBBoxDedupDetections(const json& results) {
    if (!results.is_array()) return 0;
    int count = 0;
    for (const auto& entry : results) {
        if (entry.is_object() && entry.contains("sample_results") && entry.at("sample_results").is_array()) {
            count += static_cast<int>(entry.at("sample_results").size());
            continue;
        }
        if (entry.is_object() && entry.contains("bbox")) {
            count += 1;
        }
    }
    return count;
}

json BuildBBoxDedupFlow(bool crossModel) {
    json dedupProps = json::object({{"iou_threshold", 0.5}, {"per_category", true}});
    if (!crossModel) dedupProps["cross_model"] = false;

    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.99},
                    {"bbox_x1", 10.0},
                    {"bbox_y1", 10.0},
                    {"bbox_x2", 110.0},
                    {"bbox_y2", 110.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.88},
                    {"bbox_x1", 20.0},
                    {"bbox_y1", 20.0},
                    {"bbox_x2", 100.0},
                    {"bbox_y2", 100.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "post_process/merge_results"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}}),
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({401})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({402})}})
                })}
            }),
            json::object({
                {"id", 5},
                {"order", 5},
                {"type", "post_process/bbox_iou_dedup"},
                {"properties", dedupProps},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 401}}),
                    json::object({{"type", "result_chan"}, {"link", 402}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({501})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({502})}})
                })}
            }),
            json::object({
                {"id", 6},
                {"order", 6},
                {"type", "output/return_json"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 501}}),
                    json::object({{"type", "result_chan"}, {"link", 502}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

json BuildBBoxDedupNoneVsIdentityFlow() {
    const json dedupProps = json::object({
        {"metric", "iou"},
        {"iou_threshold", 0.5},
        {"per_category", true},
        {"cross_model", true}
    });

    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.99},
                    {"bbox_x", 10.0},
                    {"bbox_y", 10.0},
                    {"bbox_w", 100.0},
                    {"bbox_h", 100.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "target"},
                    {"score", 0.88},
                    {"bbox_x", 20.0},
                    {"bbox_y", 20.0},
                    {"bbox_w", 80.0},
                    {"bbox_h", 80.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "post_process/sliding_merge"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({401})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({402})}})
                })}
            }),
            json::object({
                {"id", 5},
                {"order", 5},
                {"type", "post_process/merge_results"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}}),
                    json::object({{"type", "image_chan"}, {"link", 401}}),
                    json::object({{"type", "result_chan"}, {"link", 402}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({501})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({502})}})
                })}
            }),
            json::object({
                {"id", 6},
                {"order", 6},
                {"type", "post_process/bbox_iou_dedup"},
                {"properties", dedupProps},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 501}}),
                    json::object({{"type", "result_chan"}, {"link", 502}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({601})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({602})}})
                })}
            }),
            json::object({
                {"id", 7},
                {"order", 7},
                {"type", "output/return_json"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 601}}),
                    json::object({{"type", "result_chan"}, {"link", 602}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

bool RunBBoxIoUDedupFlowCase(bool crossModel, int expectedCount, std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, crossModel ? "bbox_dedup_cross.json" : "bbox_dedup_strict.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildBBoxDedupFlow(crossModel).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(320, 320, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        const int kept = CountBBoxDedupDetections(results);
        if (kept != expectedCount) {
            error = std::string(crossModel ? "default cross_model=true" : "cross_model=false") +
                " kept count mismatch, actual=" + std::to_string(kept) +
                ", expected=" + std::to_string(expectedCount) +
                ", root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

bool RunBBoxIoUDedupNoneVsIdentityCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "bbox_dedup_none_vs_identity.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildBBoxDedupNoneVsIdentityFlow().dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(320, 320, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        const int kept = CountBBoxDedupDetections(results);
        if (kept != 1) {
            error = "null/identity transform not grouped, actual=" + std::to_string(kept) +
                ", expected=1, root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunBBoxIoUDedupSelfTest() {
    auto fail = [](const std::string& message) -> int {
        std::cout << "bbox_iou_dedup selftest failed: " << message << "\n";
        return 1;
    };

    std::string error;
    if (!RunBBoxIoUDedupFlowCase(true, 1, error)) return fail(error);
    if (!RunBBoxIoUDedupFlowCase(false, 2, error)) return fail(error);
    if (!RunBBoxIoUDedupNoneVsIdentityCase(error)) return fail(error);

    std::cout << "bbox_iou_dedup selftest passed\n";
    return 0;
}

json BuildImageGenerationExpandFlow(const std::string& saveDir,
                                    const std::string& suffix,
                                    const json& cropProperties,
                                    const json& resultProperties) {
    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", resultProperties},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "features/image_generation"},
                {"properties", cropProperties},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "output/save_image"},
                {"properties", json::object({{"save_path", saveDir}, {"suffix", suffix}, {"format", "png"}})},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

bool AssertImageGenerationCrop(const std::string& caseName,
                               const std::string& tempDir,
                               const json& cropProperties,
                               const json& resultProperties,
                               int expectedWidth,
                               int expectedHeight,
                               std::string& error,
                               int imageWidth = 200,
                               int imageHeight = 200) {
    const std::string suffix = "_image_generation_expand_" + std::to_string(std::hash<std::string>{}(caseName));
    const std::string flowPath = JoinPathA(tempDir, suffix + ".json");
    DeleteFilesWithSuffix(tempDir, suffix + ".png");

    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = caseName + " cannot write temp flow file";
            return false;
        }
        ofs << BuildImageGenerationExpandFlow(tempDir, suffix, cropProperties, resultProperties).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = caseName + " flow load failed: " + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(imageHeight, imageWidth, CV_8UC3, cv::Scalar(0, 0, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = caseName + " flow infer failed: " + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = caseName + " exception: " + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    const cv::Mat saved = LoadSingleFileWithSuffix(tempDir, suffix + ".png");
    DeleteFilesWithSuffix(tempDir, suffix + ".png");
    DeleteFileA(flowPath.c_str());
    if (saved.empty()) {
        error = caseName + " cropped image not saved";
        return false;
    }

    if (saved.cols != expectedWidth || saved.rows != expectedHeight) {
        error = caseName + " crop size mismatch, actual=" + std::to_string(saved.cols) + "x" +
                std::to_string(saved.rows) + ", expected=" + std::to_string(expectedWidth) +
                "x" + std::to_string(expectedHeight);
        return false;
    }

    return true;
}

bool RunImageGenerationExpandRegression(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const json axisResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_x", 50.0},
        {"bbox_y", 60.0},
        {"bbox_w", 40.0},
        {"bbox_h", 20.0}
    });

    if (!AssertImageGenerationCrop(
            "pixel_expand",
            tempDir,
            json::object({{"crop_expand", 5}, {"crop_shape", json::array()}, {"min_size", 1}}),
            axisResultProps,
            50,
            30,
            error)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "percent_expand_axis",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 10}, {"crop_shape", json::array()}, {"min_size", 1}}),
            axisResultProps,
            48,
            24,
            error)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "percent_no_round_to_32",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 50}, {"crop_shape", json::array()}, {"min_size", 1}}),
            axisResultProps,
            80,
            40,
            error)) {
        return false;
    }

    const json largeAxisResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_x", 50.0},
        {"bbox_y", 50.0},
        {"bbox_w", 200.0},
        {"bbox_h", 200.0}
    });
    if (!AssertImageGenerationCrop(
            "percent_default_pixel_limit",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 20}, {"crop_shape", json::array()}, {"min_size", 1}}),
            largeAxisResultProps,
            264,
            264,
            error,
            320,
            320)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "percent_custom_pixel_limit",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 20}, {"crop_expand_percent_limit", 10}, {"crop_shape", json::array()}, {"min_size", 1}}),
            largeAxisResultProps,
            220,
            220,
            error,
            320,
            320)) {
        return false;
    }

    if (!AssertImageGenerationCrop(
            "fixed_size_priority",
            tempDir,
            json::object({{"crop_expand", 5}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 10}, {"crop_shape", json::array({30, 25})}, {"min_size", 1}}),
            axisResultProps,
            30,
            25,
            error)) {
        return false;
    }

    const json rotatedResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_cx", 100.0},
        {"bbox_cy", 100.0},
        {"bbox_w", 40.0},
        {"bbox_h", 20.0},
        {"with_angle", true},
        {"angle", 0.0}
    });
    if (!AssertImageGenerationCrop(
            "percent_expand_rotated",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 10}, {"crop_shape", json::array()}, {"min_size", 1}}),
            rotatedResultProps,
            48,
            24,
            error)) {
        return false;
    }

    const json largeRotatedResultProps = json::object({
        {"category_id", 1},
        {"category_name", "target"},
        {"score", 0.99},
        {"bbox_cx", 160.0},
        {"bbox_cy", 160.0},
        {"bbox_w", 200.0},
        {"bbox_h", 200.0},
        {"with_angle", true},
        {"angle", 0.0}
    });
    if (!AssertImageGenerationCrop(
            "rotated_percent_default_pixel_limit",
            tempDir,
            json::object({{"crop_expand", 0}, {"crop_expand_mode", "percent"}, {"crop_expand_percent", 20}, {"crop_shape", json::array()}, {"min_size", 1}}),
            largeRotatedResultProps,
            264,
            264,
            error,
            320,
            320)) {
        return false;
    }

    return true;
}

int RunImageGenerationExpandSelfTest() {
    std::cout << "==== image_generation expand selftest ====\n";
    std::string error;
    if (!RunImageGenerationExpandRegression(error)) {
        std::cout << "image_generation expand selftest failed: " << error << "\n";
        return 1;
    }

    std::cout << "image_generation expand selftest passed\n";
    return 0;
}

json BuildCrossModelLabelMergeFlow() {
    return json::object({
        {"nodes", json::array({
            json::object({
                {"id", 1},
                {"order", 1},
                {"type", "input/frontend_image"},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({101})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({102})}})
                })}
            }),
            json::object({
                {"id", 2},
                {"order", 2},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "base"},
                    {"score", 0.99},
                    {"bbox_x", 50.0},
                    {"bbox_y", 50.0},
                    {"bbox_w", 40.0},
                    {"bbox_h", 20.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({201})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({202})}})
                })}
            }),
            json::object({
                {"id", 3},
                {"order", 3},
                {"type", "features/image_generation"},
                {"properties", json::object({{"crop_expand", 0}, {"min_size", 1}})},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 201}}),
                    json::object({{"type", "result_chan"}, {"link", 202}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({302})}})
                })}
            }),
            json::object({
                {"id", 4},
                {"order", 4},
                {"type", "input/build_results"},
                {"properties", json::object({
                    {"category_id", 1},
                    {"category_name", "suffix"},
                    {"score", 0.99},
                    {"bbox_x", 50.0},
                    {"bbox_y", 50.0},
                    {"bbox_w", 40.0},
                    {"bbox_h", 20.0}
                })},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 101}}),
                    json::object({{"type", "result_chan"}, {"link", 102}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({401})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({402})}})
                })}
            }),
            json::object({
                {"id", 5},
                {"order", 5},
                {"type", "post_process/cross_model_label_merge"},
                {"properties", json::object({{"fixed_text", "-"}})},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 302}}),
                    json::object({{"type", "image_chan"}, {"link", 301}}),
                    json::object({{"type", "result_chan"}, {"link", 402}})
                })},
                {"outputs", json::array({
                    json::object({{"type", "image_chan"}, {"links", json::array({501})}}),
                    json::object({{"type", "result_chan"}, {"links", json::array({502})}})
                })}
            }),
            json::object({
                {"id", 6},
                {"order", 6},
                {"type", "output/return_json"},
                {"inputs", json::array({
                    json::object({{"type", "image_chan"}, {"link", 501}}),
                    json::object({{"type", "result_chan"}, {"link", 502}})
                })},
                {"outputs", json::array()}
            })
        })}
    });
}

bool RunCrossModelLabelMergeCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "cross_model_label_merge.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCrossModelLabelMergeFlow().dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(200, 200, CV_8UC3, cv::Scalar(0, 0, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        if (!results.is_array() || results.size() != 1) {
            error = "result count mismatch, actual=" + std::to_string(results.size()) + ", expected=1, root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json& det = results.at(0);
        if (!det.is_object() || !det.contains("category_name") || !det.at("category_name").is_string()) {
            error = "detection result missing category_name, det=" + det.dump() + ", root=" + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const std::string cat = det.at("category_name").get<std::string>();
        if (cat != "base-suffix") {
            error = "merged label mismatch, actual=" + cat + ", expected=base-suffix";
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunCrossModelLabelMergeSelfTest() {
    std::string error;
    if (!RunCrossModelLabelMergeCase(error)) {
        std::cout << "cross_model_label_merge selftest failed: " << error << "\n";
        return 1;
    }
    std::cout << "cross_model_label_merge selftest passed\n";
    return 0;
}
}  // namespace

int main(int argc, char* argv[]) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    if (argc >= 2 && std::string(argv[1]) == "imageprepcheck") {
        return RunImagePrepCheck();
    }

    if (argc >= 2 && std::string(argv[1]) == "rect-image-correction-selftest") {
        return RunRectImageCorrectionSelfTest();
    }

    if (argc >= 2 && std::string(argv[1]) == "bbox-iou-dedup-selftest") {
        return RunBBoxIoUDedupSelfTest();
    }

    if (argc >= 2 && std::string(argv[1]) == "image-generation-expand-selftest") {
        return RunImageGenerationExpandSelfTest();
    }

    if (argc >= 2 && std::string(argv[1]) == "cross-model-label-merge-selftest") {
        return RunCrossModelLabelMergeSelfTest();
    }

    std::cout << "Usage: " << (argc >= 1 ? argv[0] : "dlcv_infer_cpp_test") << " <subcommand>\n";
    std::cout << "Available subcommands:\n";
    std::cout << "  imageprepcheck\n";
    std::cout << "  rect-image-correction-selftest\n";
    std::cout << "  bbox-iou-dedup-selftest\n";
    std::cout << "  image-generation-expand-selftest\n";
    std::cout << "  cross-model-label-merge-selftest\n";
    return 2;
}
