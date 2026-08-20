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
#include <tuple>
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

json BuildCountResultsFlow(const json& properties, int total, bool usePassBranch) {
    const int imageOutputIndex = usePassBranch ? 2 : 4;
    const int resultOutputIndex = imageOutputIndex + 1;
    json countOutputs = json::array();
    for (int i = 0; i < 8; i++) {
        json output = json::object();
        if (i == imageOutputIndex) {
            output["type"] = "image_chan";
            output["links"] = json::array({301});
        } else if (i == resultOutputIndex) {
            output["type"] = "result_chan";
            output["links"] = json::array({302});
        } else if (i == 6) {
            output["name"] = "count";
            output["type"] = "int";
            output["links"] = json::array();
        } else if (i == 7) {
            output["name"] = "ok";
            output["type"] = "bool";
            output["links"] = json::array();
        } else {
            output["type"] = (i % 2 == 0) ? "image_chan" : "result_chan";
            output["links"] = json::array();
        }
        countOutputs.push_back(std::move(output));
    }

    json nodes = json::array({
        json::object({
            {"id", 1},
            {"order", 1},
            {"type", "input/frontend_image"},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array()}}),
                json::object({{"type", "result_chan"}, {"links", json::array()}})
            })}
        })
    });

    json mergeInputs = json::array();
    for (int i = 0; i < total; i++) {
        const int imageInputLink = 100 + i * 2;
        const int resultInputLink = imageInputLink + 1;
        const int imageOutputLink = 200 + i * 2;
        const int resultOutputLink = imageOutputLink + 1;
        nodes[0]["outputs"][0]["links"].push_back(imageInputLink);
        nodes[0]["outputs"][1]["links"].push_back(resultInputLink);
        nodes.push_back(json::object({
            {"id", 2 + i},
            {"order", 2 + i},
            {"type", "input/build_results"},
            {"properties", json::object({
                {"category_id", 1},
                {"category_name", "target"},
                {"score", 0.99},
                {"bbox_x", 10.0 + i},
                {"bbox_y", 10.0 + i},
                {"bbox_w", 20.0},
                {"bbox_h", 20.0}
            })},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", imageInputLink}}),
                json::object({{"type", "result_chan"}, {"link", resultInputLink}})
            })},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({imageOutputLink})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({resultOutputLink})}})
            })}
        }));
        mergeInputs.push_back(json::object({{"type", "image_chan"}, {"link", imageOutputLink}}));
        mergeInputs.push_back(json::object({{"type", "result_chan"}, {"link", resultOutputLink}}));
    }

    nodes.push_back(json::object({
        {"id", 100},
        {"order", 100},
        {"type", "post_process/merge_results"},
        {"inputs", std::move(mergeInputs)},
        {"outputs", json::array({
            json::object({{"type", "image_chan"}, {"links", json::array({901})}}),
            json::object({{"type", "result_chan"}, {"links", json::array({902})}})
        })}
    }));
    nodes.push_back(json::object({
        {"id", 101},
        {"order", 101},
        {"type", "post_process/count_results"},
        {"properties", properties},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 901}}),
            json::object({{"type", "result_chan"}, {"link", 902}})
        })},
        {"outputs", std::move(countOutputs)}
    }));
    nodes.push_back(json::object({
        {"id", 102},
        {"order", 102},
        {"type", "output/return_json"},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 301}}),
            json::object({{"type", "result_chan"}, {"link", 302}})
        })},
        {"outputs", json::array()}
    }));
    return json::object({{"nodes", std::move(nodes)}});
}

bool RunCountResultsFlowCase(const json& properties, int total, bool expectedOk, std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "count_results.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCountResultsFlow(properties, total, expectedOk).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        cv::Mat image(64, 64, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (!inferRoot.is_object() || inferRoot.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const json results = inferRoot.contains("result_list") ? inferRoot.at("result_list") : json::array();
        const int branchCount = CountBBoxDedupDetections(results);
        if (branchCount != total) {
            error = "count_results branch mismatch, actual=" + std::to_string(branchCount) +
                ", expected=" + std::to_string(total) + ", root=" + inferRoot.dump();
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

bool RunCountResultsInvalidRangeCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "count_results_invalid.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCountResultsFlow(
            json::object({{"only_local", true}, {"min_count", 3}, {"max_count", 2}}),
            2,
            true).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
        cv::Mat image(64, 64, CV_8UC3, cv::Scalar(0, 255, 0));
        const json inferRoot = model.InferInternal(std::vector<cv::Mat>{image, image}, json::object());
        if (inferRoot.is_object() && inferRoot.value("code", 0) == 0) {
            error = "min_count > max_count did not fail: " + inferRoot.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (...) {
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunCountResultsSelfTest() {
    auto fail = [](const std::string& message) -> int {
        std::cout << "count_results selftest failed: " << message << "\n";
        return 1;
    };

    const std::vector<std::tuple<json, int, bool>> cases = {
        {json::object(), 1, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 4}}), 2, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 4}}), 4, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 2}}), 2, true},
        {json::object({{"only_local", true}, {"min_count", 2}, {"max_count", 4}}), 1, false},
        {json::object({{"only_local", true}, {"count_type", "equal"}, {"only_count", 2}}), 2, true},
        {json::object({{"only_local", true}, {"count_type", "greater"}, {"min_count", 2}}), 2, false},
        {json::object({{"only_local", true}, {"count_type", "greater"}, {"min_count", 2}}), 3, true},
        {json::object({{"only_local", true}, {"count_type", "less"}, {"max_count", 2}}), 2, false},
        {json::object({{"only_local", true}, {"count_type", "less"}, {"max_count", 2}}), 1, true},
        {json::object({{"only_local", true}, {"count_type", "legacy_unknown"}, {"min_count", 2}}), 2, true},
        {json::object({{"only_local", true}, {"only_count", 99}, {"min_count", 2}}), 3, true}
    };

    std::string error;
    for (const auto& testCase : cases) {
        if (!RunCountResultsFlowCase(
                std::get<0>(testCase),
                std::get<1>(testCase),
                std::get<2>(testCase),
                error)) {
            return fail(error);
        }
    }
    if (!RunCountResultsInvalidRangeCase(error)) return fail(error);

    std::cout << "count_results selftest passed\n";
    return 0;
}


json BuildCategoryCountCheckFlow(const json& rules, int total) {
    json nodes = json::array({
        json::object({
            {"id", 1},
            {"order", 1},
            {"type", "input/frontend_image"},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array()}}),
                json::object({{"type", "result_chan"}, {"links", json::array()}})
            })}
        })
    });

    json mergeInputs = json::array();
    for (int i = 0; i < total; ++i) {
        const int imageInputLink = 100 + i * 2;
        const int resultInputLink = imageInputLink + 1;
        const int imageOutputLink = 200 + i * 2;
        const int resultOutputLink = imageOutputLink + 1;
        nodes[0]["outputs"][0]["links"].push_back(imageInputLink);
        nodes[0]["outputs"][1]["links"].push_back(resultInputLink);
        nodes.push_back(json::object({
            {"id", 2 + i},
            {"order", 2 + i},
            {"type", "input/build_results"},
            {"properties", json::object({
                {"category_id", 1},
                {"category_name", "黑块"},
                {"score", 0.99},
                {"bbox_x", 10.0 + i * 30.0},
                {"bbox_y", 10.0},
                {"bbox_w", 20.0},
                {"bbox_h", 20.0}
            })},
            {"inputs", json::array({
                json::object({{"type", "image_chan"}, {"link", imageInputLink}}),
                json::object({{"type", "result_chan"}, {"link", resultInputLink}})
            })},
            {"outputs", json::array({
                json::object({{"type", "image_chan"}, {"links", json::array({imageOutputLink})}}),
                json::object({{"type", "result_chan"}, {"links", json::array({resultOutputLink})}})
            })}
        }));
        mergeInputs.push_back(json::object({{"type", "image_chan"}, {"link", imageOutputLink}}));
        mergeInputs.push_back(json::object({{"type", "result_chan"}, {"link", resultOutputLink}}));
    }

    nodes.push_back(json::object({
        {"id", 100},
        {"order", 100},
        {"type", "post_process/merge_results"},
        {"inputs", std::move(mergeInputs)},
        {"outputs", json::array({
            json::object({{"type", "image_chan"}, {"links", json::array({901})}}),
            json::object({{"type", "result_chan"}, {"links", json::array({902})}})
        })}
    }));
    nodes.push_back(json::object({
        {"id", 101},
        {"order", 101},
        {"type", "post_process/category_count_check"},
        {"properties", json::object({{"rules", rules}})},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 901}}),
            json::object({{"type", "result_chan"}, {"link", 902}})
        })},
        {"outputs", json::array({
            json::object({{"type", "image_chan"}, {"links", json::array({301})}}),
            json::object({{"type", "result_chan"}, {"links", json::array({302})}}),
            json::object({{"name", "ok"}, {"type", "bool"}, {"links", json::array()}}),
            json::object({{"name", "reason"}, {"type", "string"}, {"links", json::array()}})
        })}
    }));
    nodes.push_back(json::object({
        {"id", 102},
        {"order", 102},
        {"type", "output/return_json"},
        {"inputs", json::array({
            json::object({{"type", "image_chan"}, {"link", 301}}),
            json::object({{"type", "result_chan"}, {"link", 302}})
        })},
        {"outputs", json::array()}
    }));
    return json::object({{"nodes", std::move(nodes)}});
}

bool ReadCategoryCheckStatus(
    const json& root,
    size_t sampleIndex,
    bool& ok,
    std::vector<std::string>& reasons) {
    const json* statusToken = &root;
    try {
        if (!(root.contains("ok") && root.at("ok").is_boolean())) {
            if (!root.contains("result_list") || !root.at("result_list").is_array() ||
                sampleIndex >= root.at("result_list").size()) return false;
            statusToken = &root.at("result_list").at(sampleIndex);
        }
        if (!statusToken->is_object() || !statusToken->contains("ok") ||
            !statusToken->at("ok").is_boolean()) return false;
        ok = statusToken->at("ok").get<bool>();
        reasons.clear();
        if (statusToken->contains("reason") && statusToken->at("reason").is_array()) {
            for (const auto& reason : statusToken->at("reason")) {
                if (reason.is_string()) reasons.push_back(reason.get<std::string>());
            }
        }
        return true;
    } catch (...) {
        return false;
    }
}

bool RunCategoryCountCheckFlowCase(
    const json& rules,
    int total,
    int imageCount,
    bool expectedOk,
    const std::string& expectedReason,
    std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "category_count_check.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write temp flow file";
            return false;
        }
        ofs << BuildCategoryCountCheckFlow(rules, total).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        const cv::Mat image(64, 320, CV_8UC3, cv::Scalar(0, 255, 0));
        std::vector<cv::Mat> images(static_cast<size_t>(imageCount), image);
        const json root = model.InferInternal(images, json::object());
        if (!root.is_object() || root.value("code", 1) != 0) {
            error = std::string("flow infer failed: ") + root.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }

        for (int i = 0; i < imageCount; ++i) {
            bool ok = false;
            std::vector<std::string> reasons;
            if (!ReadCategoryCheckStatus(root, static_cast<size_t>(i), ok, reasons)) {
                error = "missing inspection status: " + root.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
            if (ok != expectedOk) {
                error = "inspection ok mismatch: " + root.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
            if (expectedReason.empty()) {
                if (!reasons.empty()) {
                    error = "unexpected inspection reason: " + root.dump();
                    DeleteFileA(flowPath.c_str());
                    return false;
                }
            } else if (reasons.size() != 1 || reasons.front() != expectedReason) {
                error = "inspection reason mismatch: " + root.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
        }

        if (imageCount == 1) {
            const json oneOut = model.InferOneOutJson(image, json::object());
            if (!oneOut.is_object() || oneOut.value("ok", !expectedOk) != expectedOk ||
                !oneOut.contains("result_list") || !oneOut.at("result_list").is_array()) {
                error = "InferOneOutJson wrapper mismatch: " + oneOut.dump();
                DeleteFileA(flowPath.c_str());
                return false;
            }
        }
    } catch (const std::exception& ex) {
        error = std::string("exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

bool RunCategoryCountCheckLegacyCompatibilityCase(std::string& error) {
    const std::string tempDir = BuildTempRectCorrectionDir();
    const std::string flowPath = JoinPathA(tempDir, "category_count_check_legacy.json");
    {
        std::ofstream ofs(flowPath, std::ios::binary);
        if (!ofs) {
            error = "cannot write legacy temp flow file";
            return false;
        }
        ofs << BuildCountResultsFlow(json::object(), 1, true).dump(2);
    }

    try {
        dlcv_infer::flow::FlowGraphModel model;
        const json loadReport = model.Load(flowPath, 0);
        if (!loadReport.is_object() || loadReport.value("code", 1) != 0) {
            error = std::string("legacy flow load failed: ") + loadReport.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
        const cv::Mat image(64, 64, CV_8UC3, cv::Scalar(0, 255, 0));
        const json root = model.InferInternal(std::vector<cv::Mat>{image}, json::object());
        if (root.contains("ok") || root.contains("reason")) {
            error = "legacy root unexpectedly contains inspection status: " + root.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
        const json oneOut = model.InferOneOutJson(image, json::object());
        if (!oneOut.is_array()) {
            error = "legacy InferOneOutJson is not array: " + oneOut.dump();
            DeleteFileA(flowPath.c_str());
            return false;
        }
    } catch (const std::exception& ex) {
        error = std::string("legacy exception: ") + ex.what();
        DeleteFileA(flowPath.c_str());
        return false;
    }

    DeleteFileA(flowPath.c_str());
    return true;
}

int RunCategoryCountCheckSelfTest() {
    auto fail = [](const std::string& message) -> int {
        std::cout << "category_count_check selftest failed: " << message << "\n";
        return 1;
    };

    const json equalOne = json::array({
        json::object({{"category", "黑块"}, {"operator", "equal"}, {"expect", 1}})
    });
    const json equalEight = json::array({
        json::object({{"category", "黑块"}, {"operator", "equal"}, {"expect", 8}})
    });
    const std::string countOneReason = "类别黑块期望=8,实际1";
    std::string error;
    if (!RunCategoryCountCheckFlowCase(equalOne, 1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(equalEight, 7, 1, false, countOneReason, error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "gt"}, {"expect", 0}})}),
            2, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "lt"}, {"expect", 2}})}),
            1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", ""}, {"operator", "equal"}, {"expect", 1}})}),
            2, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(equalOne.dump(), 1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "invalid"}, {"expect", 1}})}),
            1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckFlowCase(
            json::array({json::object({{"category", "黑块"}, {"operator", "equal"}, {"expect", "bad"}})}),
            1, 1, true, std::string(), error)) return fail(error);
    if (!RunCategoryCountCheckLegacyCompatibilityCase(error)) return fail(error);

    std::cout << "category_count_check selftest passed\n";
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

int RunThreeModelLoadTiming(int argc, wchar_t* argv[]) {
    if (argc != 5) {
        std::cout << "用法: dlcv_infer_cpp_test.exe load-three-models <元件提取模型> <元件检测模型> <IC检测模型>\n";
        return 2;
    }

    struct ModelSpec {
        const char* name;
        std::wstring path;
    };

    const std::vector<ModelSpec> specs = {
        {"元件提取模型", argv[2]},
        {"元件检测模型", argv[3]},
        {"IC检测模型", argv[4]},
    };

    std::vector<std::unique_ptr<dlcv_infer::Model>> models;
    models.reserve(specs.size());
    double totalSeconds = 0.0;

    try {
        for (const auto& spec : specs) {
            std::cout << "开始加载" << spec.name << ": " << WideToUtf8(spec.path) << "\n" << std::flush;
            const auto start = Clock::now();
            auto model = std::make_unique<dlcv_infer::Model>(spec.path, 0);
            const double elapsedSeconds = std::chrono::duration<double>(Clock::now() - start).count();
            totalSeconds += elapsedSeconds;
            std::cout << spec.name << "加载完成，耗时 " << ToFixed(elapsedSeconds, 2) << " 秒\n" << std::flush;
            models.push_back(std::move(model));
        }

        std::cout << "三个模型加载完成，总耗时 " << ToFixed(totalSeconds, 2) << " 秒\n" << std::flush;
        return 0;
    } catch (const std::exception& ex) {
        std::cout << "模型加载失败: " << ex.what() << "\n" << std::flush;
        return 1;
    }
}

int RunCalcMeanSelfTest() {
    auto fail = [](const std::string& message) -> int {
        std::cout << "calc_mean 自测失败：" << message << "\n";
        return 1;
    };

    const dlcv_infer::ObjectResult defaultResult(
        1, "default", 0.9f, 1.0f,
        std::vector<double>{1.0, 2.0, 3.0, 4.0}, false, cv::Mat());
    if (defaultResult.withMean || defaultResult.foregroundMean != 0.0 || defaultResult.backgroundMean != 0.0) {
        return fail("默认均值不符合 false/0.0 语义");
    }

    const dlcv_infer::ObjectResult resultWithMean(
        2, "explicit", 0.8f, 2.0f,
        std::vector<double>{5.0, 6.0, 7.0, 8.0}, false, cv::Mat(),
        false, false, -100.0f, true, 12.5, 34.75);
    if (!resultWithMean.withMean
        || resultWithMean.foregroundMean != 12.5
        || resultWithMean.backgroundMean != 34.75) {
        return fail("显式均值字段映射错误");
    }

    class ParseProbe final : public dlcv_infer::Model {
    public:
        using dlcv_infer::Model::ParseToStructResult;
    };

    const json resultJson = {
        {"sample_results", json::array({
            {
                {"results", json::array({
                    {
                        {"category_id", 3},
                        {"category_name", "mean"},
                        {"score", 0.7},
                        {"area", 4.0},
                        {"bbox", json::array({0.0, 0.0, 2.0, 2.0})},
                        {"with_mask", false},
                        {"mask", {{"width", 0}, {"height", 0}, {"mask_ptr", 0}}},
                        {"with_mean", true},
                        {"foreground_mean", 56.25},
                        {"background_mean", 78.5}
                    }
                })}
            }
        })}
    };
    ParseProbe probe;
    const dlcv_infer::Result parsed = probe.ParseToStructResult(resultJson);
    if (parsed.sampleResults.size() != 1 || parsed.sampleResults[0].results.size() != 1) {
        return fail("结构化结果数量错误");
    }
    const auto& parsedObject = parsed.sampleResults[0].results[0];
    if (!parsedObject.withMean
        || parsedObject.foregroundMean != 56.25
        || parsedObject.backgroundMean != 78.5) {
        return fail("结构化均值解析错误");
    }

    json missingMeanJson = resultJson;
    json& missingMeanObject = missingMeanJson["sample_results"][0]["results"][0];
    missingMeanObject.erase("with_mean");
    missingMeanObject.erase("foreground_mean");
    missingMeanObject.erase("background_mean");
    const dlcv_infer::Result parsedWithoutMean = probe.ParseToStructResult(missingMeanJson);
    const auto& objectWithoutMean = parsedWithoutMean.sampleResults[0].results[0];
    if (objectWithoutMean.withMean
        || objectWithoutMean.foregroundMean != 0.0
        || objectWithoutMean.backgroundMean != 0.0) {
        return fail("缺少均值字段时的默认值错误");
    }

    std::cout << "calc_mean 自测通过\n";
    return 0;
}
}  // namespace

int wmain(int argc, wchar_t* argv[]) {
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);

    if (argc >= 2 && std::wstring(argv[1]) == L"imageprepcheck") {
        return RunImagePrepCheck();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"rect-image-correction-selftest") {
        return RunRectImageCorrectionSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"bbox-iou-dedup-selftest") {
        return RunBBoxIoUDedupSelfTest();
    }


    if (argc >= 2 && std::wstring(argv[1]) == L"count-results-selftest") {
        return RunCountResultsSelfTest();
    }


    if (argc >= 2 && std::wstring(argv[1]) == L"category-count-check-selftest") {
        return RunCategoryCountCheckSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"image-generation-expand-selftest") {
        return RunImageGenerationExpandSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"cross-model-label-merge-selftest") {
        return RunCrossModelLabelMergeSelfTest();
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"load-three-models") {
        return RunThreeModelLoadTiming(argc, argv);
    }

    if (argc >= 2 && std::wstring(argv[1]) == L"calc-mean-selftest") {
        return RunCalcMeanSelfTest();
    }

    std::cout << "Usage: " << (argc >= 1 ? WideToUtf8(argv[0]) : "dlcv_infer_cpp_test") << " <subcommand>\n";
    std::cout << "Available subcommands:\n";
    std::cout << "  imageprepcheck\n";
    std::cout << "  rect-image-correction-selftest\n";
    std::cout << "  bbox-iou-dedup-selftest\n";
    std::cout << "  count-results-selftest\n";
    std::cout << "  category-count-check-selftest\n";
    std::cout << "  image-generation-expand-selftest\n";
    std::cout << "  cross-model-label-merge-selftest\n";
    std::cout << "  load-three-models <extractModelPath> <componentModelPath> <icModelPath>\n";
    std::cout << "  calc-mean-selftest\n";
    return 2;
}
