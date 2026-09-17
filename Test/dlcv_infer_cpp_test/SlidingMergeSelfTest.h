#pragma once

#include <array>
#include <functional>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

#include "../../dlcv_infer_cpp/flow/ModuleRegistry.h"

static dlcv_infer::flow::Json SlidingMergeSelfTestDet(
    int categoryId, const std::string& categoryName, double score,
    double x, double y, double width, double height) {
    using dlcv_infer::flow::Json;
    return Json::object({
        {"category_id", categoryId},
        {"category_name", categoryName},
        {"score", score},
        {"bbox", Json::array({ x, y, width, height })},
        {"with_bbox", true},
        {"with_angle", false},
        {"angle", -100.0}
    });
}

static dlcv_infer::flow::Json SlidingMergeSelfTestEntry(
    int index, const dlcv_infer::flow::ModuleImage& image,
    const dlcv_infer::flow::Json& sampleResults) {
    using dlcv_infer::flow::Json;
    return Json::object({
        {"type", "local"},
        {"index", index},
        {"origin_index", image.OriginalIndex},
        {"transform", image.TransformState.ToJson()},
        {"sample_results", sampleResults}
    });
}

static dlcv_infer::flow::ModuleIO RunSlidingMergeSelfTestModule(
    const std::vector<dlcv_infer::flow::ModuleImage>& images,
    const std::vector<dlcv_infer::flow::Json>& sampleResults,
    const dlcv_infer::flow::Json& properties) {
    using namespace dlcv_infer::flow;
    if (images.size() != sampleResults.size()) {
        throw std::runtime_error("sliding merge selftest input size mismatch");
    }

    auto factory = ModuleRegistry::Get("post_process/sliding_merge");
    if (!factory) throw std::runtime_error("sliding merge module is not registered");

    Json inputResults = Json::array();
    for (size_t i = 0; i < images.size(); ++i) {
        inputResults.push_back(SlidingMergeSelfTestEntry(
            static_cast<int>(i), images[i], sampleResults[i]));
    }

    auto module = factory(1, "", properties, nullptr);
    return module->Process(images, inputResults);
}

static const dlcv_infer::flow::Json& SlidingMergeSelfTestSamples(
    const dlcv_infer::flow::ModuleIO& output) {
    if (!output.ResultList.is_array() || output.ResultList.size() != 1) {
        throw std::runtime_error("sliding merge output entry count mismatch");
    }
    const auto& entry = output.ResultList.at(0);
    if (!entry.is_object() || !entry.contains("sample_results") ||
        !entry.at("sample_results").is_array()) {
        throw std::runtime_error("sliding merge output sample_results is invalid");
    }
    return entry.at("sample_results");
}

static void SlidingMergeSelfTestExpectBbox(
    const dlcv_infer::flow::Json& det, const std::array<int, 4>& expected) {
    using dlcv_infer::flow::Json;
    const Json expectedJson = Json::array({ expected[0], expected[1], expected[2], expected[3] });
    if (!det.is_object() || !det.contains("bbox") || det.at("bbox") != expectedJson) {
        const std::string actual = det.is_object() && det.contains("bbox")
            ? det.at("bbox").dump()
            : std::string("missing");
        throw std::runtime_error("bbox=" + actual + ", expected=" + expectedJson.dump());
    }
}

static dlcv_infer::flow::ModuleImage SlidingMergeSelfTestWindow(
    const cv::Mat& currentImage, const cv::Mat& originalImage,
    const dlcv_infer::flow::TransformationState& state, int gridX) {
    using dlcv_infer::flow::ModuleImage;
    ModuleImage image(currentImage, originalImage, state, 0);
    image.SlidingMeta.Valid = true;
    image.SlidingMeta.GridX = gridX;
    image.SlidingMeta.GridY = 0;
    image.SlidingMeta.GridCols = 2;
    image.SlidingMeta.GridRows = 1;
    image.SlidingMeta.X = gridX * 10;
    image.SlidingMeta.Y = 0;
    image.SlidingMeta.W = currentImage.cols;
    image.SlidingMeta.H = currentImage.rows;
    return image;
}

static int RunSlidingMergeSelfTest() {
    using namespace dlcv_infer::flow;

    int failures = 0;
    auto check = [&](const std::string& name, const std::function<void()>& test) {
        try {
            test();
            std::cout << "PASS " << name << std::endl;
        } catch (const std::exception& ex) {
            ++failures;
            std::cout << "FAIL " << name << ": " << ex.what() << std::endl;
        }
    };

    check("single-sliding-window-decimal-aabb", [&] {
        cv::Mat original(32, 32, CV_8UC3, cv::Scalar::all(0));
        TransformationState state(2048, 1024);
        const ModuleImage image = SlidingMergeSelfTestWindow(original, original, state, 0);
        Json dets = Json::array({ SlidingMergeSelfTestDet(
            1, "decimal", 0.9,
            1480.140380859375, 150.6482696533203,
            121.226806640625, 309.65008544921875) });

        const ModuleIO output = RunSlidingMergeSelfTestModule(
            { image }, { dets }, Json::object({ {"dedup_results", true} }));
        const Json& samples = SlidingMergeSelfTestSamples(output);
        if (samples.size() != 1) throw std::runtime_error("sample count mismatch");
        SlidingMergeSelfTestExpectBbox(samples.at(0), { 1480, 151, 121, 309 });
    });

    check("single-window-midpoint-to-even", [&] {
        cv::Mat original(32, 32, CV_8UC3, cv::Scalar::all(0));
        TransformationState state(64, 64);
        ModuleImage image(original, original, state, 0);
        Json dets = Json::array({
            SlidingMergeSelfTestDet(1, "positive-half", 0.8, 2.5, 4.5, 2.0, 2.0),
            SlidingMergeSelfTestDet(2, "negative-half", 0.7, -2.5, -4.5, 2.0, 2.0)
        });

        const ModuleIO output = RunSlidingMergeSelfTestModule(
            { image }, { dets }, Json::object({ {"dedup_results", false} }));
        const Json& samples = SlidingMergeSelfTestSamples(output);
        if (samples.size() != 2) throw std::runtime_error("sample count mismatch");
        SlidingMergeSelfTestExpectBbox(samples.at(0), { 2, 4, 2, 2 });
        SlidingMergeSelfTestExpectBbox(samples.at(1), { -2, -4, 2, 2 });
    });

    check("multi-window-first-seen-order", [&] {
        cv::Mat original(64, 128, CV_8UC3, cv::Scalar::all(0));
        cv::Mat current(64, 64, CV_8UC3, cv::Scalar::all(0));

        TransformationState state0(128, 64);
        state0.AffineMatrix2x3 = { 1, 0, 0, 0, 1, 0 };
        state0.OutputSize = { 64, 64 };
        TransformationState state1(128, 64);
        state1.AffineMatrix2x3 = { 1, 0, -10, 0, 1, 0 };
        state1.OutputSize = { 64, 64 };

        const ModuleImage window0 = SlidingMergeSelfTestWindow(current, original, state0, 0);
        const ModuleImage window1 = SlidingMergeSelfTestWindow(current, original, state1, 1);
        const Json dets0 = Json::array({
            SlidingMergeSelfTestDet(1, "first", 0.7, 8.5, 1.5, 4.0, 4.0),
            SlidingMergeSelfTestDet(2, "second", 0.6, -2.5, -3.5, 4.0, 4.0)
        });
        const Json dets1 = Json::array({
            SlidingMergeSelfTestDet(1, "first", 0.9, -1.5, 1.5, 4.0, 4.0),
            SlidingMergeSelfTestDet(2, "second", 0.8, -12.5, -3.5, 4.0, 4.0)
        });

        const ModuleIO output = RunSlidingMergeSelfTestModule(
            { window0, window1 }, { dets0, dets1 },
            Json::object({ {"dedup_results", true}, {"iou_threshold", 0.2}, {"task_type", "auto"} }));
        const Json& samples = SlidingMergeSelfTestSamples(output);
        if (samples.size() != 2) throw std::runtime_error("merged sample count mismatch");
        if (samples.at(0).value("category_name", std::string()) != "first" ||
            samples.at(1).value("category_name", std::string()) != "second") {
            throw std::runtime_error("merged output did not preserve first-seen member order");
        }
        SlidingMergeSelfTestExpectBbox(samples.at(0), { 8, 2, 4, 4 });
        SlidingMergeSelfTestExpectBbox(samples.at(1), { -2, -4, 4, 4 });
    });

    if (failures != 0) {
        std::cout << "Sliding merge selftest failed: " << failures << std::endl;
        return 1;
    }
    std::cout << "Sliding merge selftest passed" << std::endl;
    return 0;
}
