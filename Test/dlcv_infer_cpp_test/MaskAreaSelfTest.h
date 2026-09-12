#pragma once

class MaskAreaParseModel : public dlcv_infer::Model {
public:
    using dlcv_infer::Model::ParseToStructResult;
    using dlcv_infer::Model::ParseToStructResultPreservingOriginalMask;
    using dlcv_infer::Model::ParseInferOneOutJsonResults;
};

static int RunMaskAreaSelfTest() {
    using namespace dlcv_infer::flow;
    int failures = 0;
    auto check = [&](const std::string& name, const std::function<void()>& test) {
        try { test(); std::cout << "PASS " << name << std::endl; }
        catch (const std::exception& e) { ++failures; std::cout << "FAIL " << name << ": " << e.what() << std::endl; }
    };
    MaskAreaParseModel model;
    cv::Mat mask(4, 4, CV_8UC1, cv::Scalar(255));
    for (const std::string scenario : {"resize", "unchanged-size", "empty-mask", "no-mask", "preserve-original"}) {
        check(scenario, [&] {
            mask.setTo(cv::Scalar(scenario == "empty-mask" ? 0 : 255));
            const bool withMask = scenario != "no-mask";
            const double size = scenario == "unchanged-size" ? 4 : 2.8;
            Json obj{{"category_id", 0}, {"category_name", "target"}, {"score", 0.9}, {"area", 99},
                {"bbox", {0, 0, size, size}}, {"with_mask", withMask},
                {"mask", {{"width", 4}, {"height", 4}, {"mask_ptr", reinterpret_cast<uintptr_t>(mask.data)}}}};
            Json raw{{"sample_results", Json::array({Json{{"results", Json::array({obj})}}})}};
            const bool preserve = scenario == "preserve-original";
            auto result = preserve ? model.ParseToStructResultPreservingOriginalMask(raw) : model.ParseToStructResult(raw);
            const auto& output = result.sampleResults.at(0).results.at(0);
            const int expectedSize = preserve ? 4 : static_cast<int>(std::llround(size)); // 保留 C++ 历史取整规则。
            const int expectedArea = !withMask ? 99 : scenario == "empty-mask" ? 0 : expectedSize * expectedSize;
            if (output.area != expectedArea) throw std::runtime_error("area=" + std::to_string(output.area) + ", expected=" + std::to_string(expectedArea));
            if (withMask && (output.mask.cols != expectedSize || output.mask.rows != expectedSize)) throw std::runtime_error("mask geometry changed");
            if (withMask && output.area != CalculateMaskArea(MatToMaskInfo(output.mask))) throw std::runtime_error("RLE area mismatch");
        });
    }
    auto checkSharedMaskArea = [&](const std::string& name, const cv::Mat& mask,
        bool withMask, double bboxWidth, double bboxHeight, double expectedArea) {
        check(name, [&] {
            const cv::Mat originalMask = mask.clone();
            Json object{{"category_id", 0}, {"category_name", "target"}, {"score", 0.9}, {"area", 99},
                {"bbox", {0, 0, bboxWidth, bboxHeight}}, {"with_mask", withMask},
                {"mask", {{"width", mask.cols}, {"height", mask.rows},
                    {"mask_ptr", reinterpret_cast<uintptr_t>(mask.data)}}}};
            Json raw{{"sample_results", Json::array({Json{{"results", Json::array({object})}}})}};

            const auto structured = model.ParseToStructResult(raw).sampleResults.at(0).results.at(0);
            const Json jsonResults = model.ParseInferOneOutJsonResults(
                raw.at("sample_results").at(0).at("results"));
            const double jsonArea = jsonResults.at(0).at("area").get<double>();
            if (structured.area != expectedArea || jsonArea != expectedArea ||
                jsonArea != static_cast<double>(structured.area)) {
                throw std::runtime_error("shared area mismatch");
            }
            if (cv::norm(mask, originalMask, cv::NORM_INF) != 0.0) {
                throw std::runtime_error("input mask changed");
            }
        });
    };

    checkSharedMaskArea("json-4x4-to-2x2", mask, true, 2, 2, 4);
    checkSharedMaskArea("json-noninteger-bbox-rounding", mask, true, 2.6, 3.4, 9);

    check("json-contour-uses-rounded-grid", [&] {
        Json object{{"category_id", 0}, {"category_name", "target"}, {"score", 0.9}, {"area", 99},
            {"bbox", {0, 0, 2.6, 3.4}}, {"with_mask", true},
            {"mask", {{"width", mask.cols}, {"height", mask.rows},
                {"mask_ptr", reinterpret_cast<uintptr_t>(mask.data)}}}};
        Json raw{{"sample_results", Json::array({Json{{"results", Json::array({object})}}})}};
        const Json jsonResults = model.ParseInferOneOutJsonResults(
            raw.at("sample_results").at(0).at("results"));
        const auto& points = jsonResults.at(0).at("mask");
        bool hasBottomRight = false;
        for (const auto& point : points) {
            if (point.at("x").get<int>() == 2 && point.at("y").get<int>() == 2) {
                hasBottomRight = true;
                break;
            }
        }
        if (!hasBottomRight) throw std::runtime_error("contour still uses truncated bbox size");
    });

    cv::Mat sparseMask = cv::Mat::zeros(4, 4, CV_8UC1);
    sparseMask.at<unsigned char>(1, 1) = 255;
    cv::Mat linearMask;
    cv::resize(sparseMask, linearMask, cv::Size(3, 3), 0, 0, cv::INTER_LINEAR);
    if (cv::countNonZero(linearMask) != 4) {
        ++failures;
        std::cout << "FAIL sparse-mask-linear-reference: unexpected reference area" << std::endl;
    } else {
        std::cout << "PASS sparse-mask-linear-reference" << std::endl;
    }
    checkSharedMaskArea("json-sparse-mask-nearest", sparseMask, true, 3, 3, 1);

    cv::Mat emptyMask = cv::Mat::zeros(4, 4, CV_8UC1);
    checkSharedMaskArea("json-empty-mask", emptyMask, true, 2, 2, 0);
    checkSharedMaskArea("json-no-mask-keeps-area", mask, false, 2, 2, 99);

    for (bool hasArea : {true, false}) {
        check(hasArea ? "merge-overwrites-area" : "merge-fills-area", [&] {
            cv::Mat image(16, 16, CV_8UC3, cv::Scalar::all(0));
            cv::Mat smallMask(2, 2, CV_8UC1, cv::Scalar(255));
            std::vector<ModuleImage> images;
            Json entries = Json::array();
            for (int i = 0; i < 2; ++i) {
                ModuleImage wrap(image, image, TransformationState(16, 16), 0);
                wrap.SlidingMeta.Valid = true;
                wrap.SlidingMeta.GridX = i;
                images.push_back(wrap);
                Json det{{"category_id", 0}, {"category_name", "target"}, {"score", 0.9},
                    {"bbox", {2 + i, 2, 4, 4}}, {"with_mask", true}, {"mask_rle", MatToMaskInfo(smallMask)}};
                if (hasArea) { det["area"] = 99; det["mask_area"] = 99; }
                entries.push_back(Json{{"type", "local"}, {"index", i}, {"origin_index", 0}, {"sample_results", Json::array({det})}});
            }
            auto factory = ModuleRegistry::Get("post_process/sliding_merge");
            if (!factory) throw std::runtime_error("sliding merge not registered");
            auto module = factory(1, "", Json::object(), nullptr);
            auto result = module->Process(images, entries);
            const auto& dets = result.ResultList.at(0).at("sample_results");
            if (dets.size() != 1) throw std::runtime_error("expected one union");
            const auto& output = dets.at(0);
            if (CalculateMaskArea(output.at("mask_rle")) != 20) throw std::runtime_error("union geometry changed");
            if (output.value("area", -1.0) != 20) throw std::runtime_error("merged area must be 20");
            if (hasArea && output.value("mask_area", -1.0) != 20) throw std::runtime_error("cached mask area must be 20");
        });
    }
    std::cout << "Mask area selftest failures: " << failures << std::endl;
    return failures == 0 ? 0 : 1;
}
