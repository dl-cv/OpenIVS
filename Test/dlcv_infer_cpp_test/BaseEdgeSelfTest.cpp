#include "BaseEdgeSelfTest.h"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>
#include "flow/ModuleRegistry.h"
#include "dlcv_infer.h"

static void MatchBaseEdge(const dlcv_infer::flow::Json& actual, const dlcv_infer::flow::Json& expected) {
    if (expected.is_object()) {
        for (auto it = expected.begin(); it != expected.end(); ++it) MatchBaseEdge(actual.at(it.key()), it.value());
    } else if (expected.is_array()) {
        if (!actual.is_array() || actual.size() != expected.size()) throw std::runtime_error("array size mismatch");
        for (size_t i = 0; i < expected.size(); ++i) MatchBaseEdge(actual[i], expected[i]);
    } else if (expected.is_number()) {
        if (!actual.is_number() || !std::isfinite(actual.get<double>()) || std::abs(actual.get<double>() - expected.get<double>()) > 1e-9) throw std::runtime_error("number mismatch");
    } else if (actual != expected) throw std::runtime_error("value mismatch: " + actual.dump() + " != " + expected.dump());
}

int RunBaseEdgeSelfTest(int argc, wchar_t** argv) {
    using namespace dlcv_infer::flow;
    if (argc != 4) throw std::invalid_argument("base-edge-selftest <cases.json> <report.json>");
    std::ifstream input{std::filesystem::path(argv[2])};
    Json tests; input >> tests;
    Json report = Json::array();
    int failures = 0;
    for (const auto& test : tests) {
        Json item{{"name", test.at("name")}};
        try {
            auto factory = ModuleRegistry::Get(test.at("type").get<std::string>());
            if (!factory && !test.contains("model_path")) throw std::runtime_error("module not registered");
            cv::Mat image;
            if (test.contains("image_path")) {
                std::ifstream file(std::filesystem::u8path(test.at("image_path").get<std::string>()), std::ios::binary);
                const std::vector<char> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
                image = cv::imdecode(bytes, cv::IMREAD_COLOR);
                cv::cvtColor(image, image, cv::COLOR_BGR2RGB);
            } else image = cv::Mat(test.value("height", 420), test.value("width", 320), CV_8UC3, cv::Scalar::all(test.value("background", 110)));
            if (test.contains("model_path")) {
                dlcv_infer::Model model(std::filesystem::u8path(test.at("model_path").get<std::string>()).wstring(), test.value("device", 0));
                const Json parameters{{"threshold", 0.5}, {"with_mask", false}};
                const auto structured = model.Infer(image, parameters);
                bool ok = false;
                std::vector<std::string> reasons;
                const bool inspection = dlcv_infer::Model::GetLastInspectionStatus(ok, reasons);
                const auto json = model.InferOneOutJson(image, parameters);
                MatchBaseEdge(json, test.at("expected"));
                const auto& detections = json.at("result_list");
                const auto& sample = structured.sampleResults.at(0);
                if (sample.results.size() != detections.size() || !inspection || ok != json.at("ok")) throw std::runtime_error("structured count/status mismatch");
                for (size_t i = 0; i < detections.size(); ++i) {
                    if (dlcv_infer::convertGbkToUtf8(sample.results[i].categoryName) != detections[i].at("category_name")) throw std::runtime_error("structured category mismatch");
                    MatchBaseEdge(sample.results[i].bbox, detections[i].at("bbox"));
                }
                MatchBaseEdge(reasons, json.at("reason"));
                MatchBaseEdge(sample.extraInfo, json.at("extra_info"));
                item["results"] = json; item["passed"] = true; report.push_back(item);
                std::cout << "PASS " << test.at("name") << '\n';
                continue;
            }
            for (const auto& rect : test.value("rectangles", Json::array())) {
                const auto& rgb = rect[4];
                const auto color = rgb.is_array() ? cv::Scalar(rgb[0].get<int>(), rgb[1].get<int>(), rgb[2].get<int>()) : cv::Scalar::all(rgb.get<int>());
                cv::rectangle(image, cv::Point(rect[0].get<int>(), rect[1].get<int>()), cv::Point(rect[2].get<int>(), rect[3].get<int>()), color, -1);
            }
            std::vector<ModuleImage> images;
            for (const auto& wrap : test.value("images", Json::array({Json::object()}))) {
                auto state = wrap.contains("transform") ? TransformationState::FromJson(wrap.at("transform")) : TransformationState(image.cols, image.rows);
                images.emplace_back(image, image, state, wrap.value("origin_index", 0));
            }
            const Json results = test.value("results", Json::array());
            const Json before = results;
            auto module = factory(1, "", test.value("properties", Json::object()), nullptr);
            bool rejected = false;
            ModuleIO output;
            try { output = module->Process(images, results); }
            catch (const std::invalid_argument&) { if (!test.value("reject", false)) throw; rejected = true; }
            if (test.value("reject", false) && !rejected) throw std::runtime_error("invalid config accepted");
            if (results != before) throw std::runtime_error("input mutated");
            if (!rejected) { item["results"] = output.ResultList; MatchBaseEdge(output.ResultList, test.at("expected")); }
            item["passed"] = true;
        } catch (const std::exception& ex) { ++failures; item["passed"] = false; item["error"] = ex.what(); }
        std::cout << (item.at("passed") == true ? "PASS " : "FAIL ") << item.dump() << '\n';
        report.push_back(std::move(item));
    }
    std::ofstream out{std::filesystem::path(argv[3])}; out << report.dump(2);
    std::cout << "base-edge-selftest: " << tests.size() - failures << "/" << tests.size() << " passed\n";
    return failures == 0 ? 0 : 1;
}
