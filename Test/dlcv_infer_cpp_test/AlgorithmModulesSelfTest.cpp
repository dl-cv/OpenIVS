#include "AlgorithmModulesSelfTest.h"

#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#include "dlcv_infer.h"
#include "flow/FlowGraphModel.h"
#include "flow/ModuleRegistry.h"

// DLL 图级测试：MergeResults 的两路 image 都来自节点 3，results 分别来自 3/4。
static dlcv_infer::flow::Json BuildAlgorithmSharedInputFlow(bool distance, bool keepDetections, bool expectedOk, bool invalidDistance, bool legacy, bool reference = false) {
    using dlcv_infer::flow::Json;
    auto input = [](const char* type, int link) { return Json{{"type", type}, {"link", link}}; };
    auto output = [](const char* type, Json links) { return Json{{"type", type}, {"links", std::move(links)}}; };
    auto build = [&](int id, const char* name, double x, double y, Json outputs) {
        return Json{{"id", id}, {"order", id}, {"type", "input/build_results"},
            {"properties", {{"category_name", name}, {"score", 0.9}, {"bbox_x", x}, {"bbox_y", y},
                {"bbox_w", 2}, {"bbox_h", 2}}},
            {"inputs", {input("image_chan", id == 3 ? 11 : 31)}}, {"outputs", std::move(outputs)}};
    };
    Json nodes = Json::array({
        {{"id", 1}, {"order", 1}, {"type", "input/frontend_image"},
            {"outputs", {output("image_chan", {11})}}},
        build(3, "A", 9, 9, {output("image_chan", {31, 51, 53, 81, 83}), output("result_chan", {52, 82})}),
        build(4, invalidDistance ? "A" : "B", 12, 13, {output("image_chan", Json::array()), output("result_chan", {54, 84})}),
        {{"id", 5}, {"order", 5}, {"type", "post_process/merge_results"},
            {"inputs", {input("image_chan", 51), input("result_chan", 52), input("image_chan", 53), input("result_chan", 54)}},
            {"outputs", {output("image_chan", {61}), output("result_chan", {62})}}},
        {{"id", 6}, {"order", 6}, {"type", distance ? "post_process/offset_distance" : (legacy ? "post_process/wire_sequence_check" : "post_process/sequence_check")},
            {"properties", distance ? Json{{"rules", {{{"operator", "<"}, {"value", expectedOk ? 6 : 4}}}}} : Json{{"categories", expectedOk ? Json::array({"A", "B"}) : Json::array({"B", "A"})}}},
            {"inputs", {input("image_chan", 61), input("result_chan", 62)}},
            {"outputs", {output("image_chan", {71}), output("result_chan", {72})}}}
    });
    if (reference) {
        nodes[0]["outputs"][0]["links"].push_back(12);
        nodes[4]["inputs"].push_back(input("image_chan", 13));
        nodes[4]["properties"]["categories"] = expectedOk ? Json::array({"B", "A"}) : Json::array({"A", "B"});
        nodes.push_back({{"id", 2}, {"order", 2}, {"type", "pre_process/image_flip"},
            {"inputs", {input("image_chan", 12)}}, {"outputs", {output("image_chan", {13})}}});
    }
    if (keepDetections) nodes.push_back({{"id", 7}, {"order", 7}, {"type", "post_process/merge_results"},
        {"inputs", {input("image_chan", 71), input("result_chan", 72), input("image_chan", 81), input("result_chan", 82),
            input("image_chan", 83), input("result_chan", 84)}},
        {"outputs", {output("image_chan", {91}), output("result_chan", {92})}}});
    nodes.push_back({{"id", 8}, {"order", 8}, {"type", "output/return_json"},
        {"inputs", {input("image_chan", keepDetections ? 91 : 71), input("result_chan", keepDetections ? 92 : 72)}},
        {"outputs", Json::array()}});
    return Json{{"nodes", std::move(nodes)}};
}

// 合成目标使用 C++ Flow 的 xywh / cxcywh+angle，不需要客户模型或图片。
int RunAlgorithmModulesSelfTest() {
    using namespace dlcv_infer::flow;
    try {
        int checks = 0;
        auto check = [&](bool ok, const std::string& name) {
            if (!ok) throw std::runtime_error(name);
            ++checks;
        };
        auto sequence = ModuleRegistry::Get("post_process/sequence_check");
        auto legacySequence = ModuleRegistry::Get("post_process/wire_sequence_check");
        check(static_cast<bool>(legacySequence), "legacy sequence type not registered");
        auto offset = ModuleRegistry::Get("post_process/offset_distance");
        check(static_cast<bool>(sequence), "sequence_check not registered");
        check(static_cast<bool>(offset), "offset_distance not registered");
        const cv::Mat original(200, 300, CV_8UC3, cv::Scalar::all(17));
        const TransformationState identity(300, 200);
        const std::vector<ModuleImage> images{ModuleImage(original, original, identity, 7)};
        auto det = [](const std::string& name, double x, double y = 10, double score = 0.9) {
            return Json{{"category_name", name}, {"score", score}, {"bbox", {x - 1, y - 1, 2, 2}}};
        };
        auto local = [](Json detections, int index = 0, Json transform = Json()) {
            return Json{{"type", "local"}, {"index", index}, {"transform", transform},
                {"sample_results", std::move(detections)}};
        };
        int runNumber = 0;
        auto run = [&](bool distance, const std::vector<ModuleImage>& inputImages, const Json& entries,
            const Json& props = Json::object(), bool legacy = false) {
            const Json before = entries;
            const Json propertiesBefore = props;
            std::vector<cv::Mat> pixels;
            for (const auto& image : inputImages) pixels.push_back(image.OriginalImage.clone());
            auto module = (distance ? offset : (legacy ? legacySequence : sequence))(1, "", props, nullptr);
            ++runNumber;
            ModuleIO output;
            try { output = module->Process(inputImages, entries); }
            catch (const std::invalid_argument& error) {
                throw std::invalid_argument("case " + std::to_string(runNumber) + ": " + error.what() + "; entries=" + entries.dump());
            }
            check(entries == before && props == propertiesBefore && module->Properties == props, "input JSON mutated");
            for (size_t i = 0; i < inputImages.size(); ++i)
                check(cv::norm(inputImages[i].OriginalImage, pixels[i], cv::NORM_INF) == 0, "original pixels mutated");
            return output;
        };
        auto expect = [&](const ModuleIO& out, const std::vector<std::string>& labels, bool distance,
            const std::vector<std::string>& reasons = {}) {
            check(out.ImageList.size() == labels.size() && out.ResultList.size() == labels.size(), "output count");
            for (size_t i = 0; i < labels.size(); ++i) {
                const auto& image = out.ImageList[i];
                const auto& entry = out.ResultList.at(i);
                const auto& result = entry.at("sample_results");
                const bool ok = labels[i].rfind("OK", 0) == 0;
                check(image.ImageObject.data == image.OriginalImage.data, "output must be original image");
                check(image.TransformState.ToJson() == identity.ToJson(), "output transform must be identity");
                check(entry.at("type") == "local" && entry.at("index") == i &&
                    entry.at("origin_index") == image.OriginalIndex, "output identity");
                check(entry.at("transform") == identity.ToJson(), "result transform");
                check(entry.at("originating_module") == (distance ? "post_process/offset_distance" : "post_process/sequence_check"), "module type");
                check(entry.at("ok") == ok, "inspection status");
                check(entry.at("reason").is_string() && !entry.at("reason").get<std::string>().empty(), "reason must remain a nonempty string");
                if (distance) {
                    const auto text = labels[i].substr(3);
                    check(entry.at("reason") == (text == "Nan" ? "距离：NaN" : "距离：" + text + " px"), "distance reason matches label precision");
                }
                if (!reasons.empty()) check(entry.at("reason") == reasons.at(i), "sequence reason");
                check(result.size() == 1 && result[0].at("category_name") == labels[i] &&
                    result[0].at("category_id") == (ok ? 0 : 1) && result[0].at("score") == 1.0, "classification");
                check(result[0].at("bbox") == (distance ? Json::array({150.0, 100.0, 300.0, 200.0, 0.0}) :
                    Json::array({0, 0, 300, 200})), "full-image bbox");
            }
        };
        auto rejects = [&](bool distance, const std::vector<ModuleImage>& imgs, const Json& entries, const Json& props) {
            bool rejected = false;
            try { run(distance, imgs, entries, props); }
            catch (const std::invalid_argument&) { rejected = true; }
            check(rejected, "invalid input must throw invalid_argument");
        };
        for (const bool vertical : {false, true}) {
            auto module = sequence(1, "", Json{{"categories", {"B", "A"}},
                {"direction", vertical ? "vertical" : "horizontal"}}, nullptr);
            auto state = identity;
            state.CropBox = {30, 20, 200, 150};
            state.AffineMatrix2x3 = vertical ? std::vector<double>{0, 1, -20, -1, 0, 330}
                : std::vector<double>{-1, 0, 330, 0, 1, -20};
            auto crop = identity;
            crop.AffineMatrix2x3 = {1, 0, -100, 0, 1, 0};
            const auto entries = Json::array({local(Json::array({det("A", 40)})), local(Json::array({det("B", 10)}), 0, crop.ToJson())});
            module->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{ModuleImage(cv::Mat(), original, state, 7), ModuleImage()}, Json::array());
            expect(module->Process(images, entries), {"OK"}, false, {"期望顺序：B → A\n当前顺序：B → A"});
            check(module->ExtraInputsIn[0].ImageList[0].ImageObject.empty(), "reference pixels must stay lazy");
            module->ExtraInputsIn.clear();
            module->ExtraInputsIn.emplace_back();
            module->Properties["categories"] = {"A", "B"};
            expect(module->Process(images, entries), {"OK"}, false);
        }
        const Json rb{{"categories", {"红", "蓝"}}};
        const Json pair = Json::array({det("A", 10, 10), det("B", 13, 14)});
        // reason 与分类独立：原始 entry 保持字符串，保留完整排序、重复和空序列。
        const Json rgb{{"categories", {"红", "绿", "蓝"}}};
        const Json rbg = Json::array({local(Json::array({det("绿", 30), det("红", 10), det("蓝", 20)}))});
        for (bool empty : {false, true}) for (bool ok : {false, true}) {
            const Json entries = empty ? Json::array() : rbg;
            const Json props{{"categories", empty ? (ok ? Json::array() : Json::array({"红"})) :
                (ok ? Json::array({"红", "蓝", "绿"}) : Json::array({"红", "绿", "蓝"}))}};
            const auto legacy = run(false, images, entries, props, true);
            const std::string expected = empty ? (ok ? "（空）" : "红") : (ok ? "红 → 蓝 → 绿" : "红 → 绿 → 蓝");
            const std::string current = empty ? "（空）" : "红 → 蓝 → 绿";
            expect(legacy, {ok ? "OK" : "NG"}, false, {"期望顺序：" + expected + "\n当前顺序：" + current});
            check(legacy.ResultList == run(false, images, entries, props).ResultList, "legacy result matches canonical");
        }
        expect(run(false, images, rbg, rgb), {"NG"}, false, {"期望顺序：红 → 绿 → 蓝\n当前顺序：红 → 蓝 → 绿"});
        expect(run(false, images, rbg, {{"categories", {"红", "蓝", "绿"}}}), {"OK"}, false,
            {"期望顺序：红 → 蓝 → 绿\n当前顺序：红 → 蓝 → 绿"});
        expect(run(false, images, Json::array({local(Json::array({det("蓝", 0, 80), det("红", 100, 20)}))}),
            {{"categories", {"红", "蓝"}}, {"direction", "vertical"}}), {"OK"}, false,
            {"期望顺序：红 → 蓝\n当前顺序：红 → 蓝"});
        expect(run(false, images, Json::array({local(Json::array({det("红", 10), det("红", 20), det("蓝", 30)}))}),
            {{"categories", {"红", "红", "蓝"}}}), {"OK"}, false, {"期望顺序：红 → 红 → 蓝\n当前顺序：红 → 红 → 蓝"});
        expect(run(false, images, Json::array()), {"OK"}, false, {"期望顺序：（空）\n当前顺序：（空）"});
        expect(run(false, images, Json::array(), rgb), {"NG"}, false, {"期望顺序：红 → 绿 → 蓝\n当前顺序：（空）"});
        expect(run(false, images, rbg), {"NG"}, false, {"期望顺序：（空）\n当前顺序：红 → 蓝 → 绿"});
        expect(run(false, images, Json::array({local(Json::array({det("蓝", 80), det("红", 20)}))}), rb), {"OK"}, false);
        expect(run(false, images, Json::array({local(Json::array({det("蓝", 0, 80), det("红", 100, 20)}))}),
            {{"categories", {"红", "蓝"}}, {"direction", "vertical"}}), {"OK"}, false);
        for (const Json& detections : std::vector<Json>{Json::array(), Json::array({det("红", 10)}),
            {det("蓝", 10), det("红", 20)}, {det("红", 10), det("蓝", 20), det("蓝", 30)}})
            expect(run(false, images, Json::array({local(detections)}), rb), {"NG"}, false);
        expect(run(false, images, Json::array({local(Json::array({det("红", 10), det("红", 20), det("蓝", 30)}))}),
            {{"categories", {"红", "红", "蓝"}}}), {"OK"}, false);
        expect(run(false, images, Json::array({local(Json::array({det("红", 10), det("蓝", 30)}))}),
            {{"categories", {"红", "红", "蓝"}}}), {"NG"}, false);
        expect(run(false, images, Json::array({local(Json::array({det("红", 10), det("蓝", 10)}))}), rb), {"OK"}, false);
        auto wide = det("蓝", 50); wide["bbox"] = {0, 0, 100, 20};
        expect(run(false, images, Json::array({local(Json::array({wide, det("红", 10)}))}), rb), {"OK"}, false);
        for (const auto& name : {"Red", " Red "})
            expect(run(false, images, Json::array({local(Json::array({det("red", 10)}))}), {{"categories", {name}}}), {"NG"}, false);
        for (const Json& empty : std::vector<Json>{Json(), Json::array(), Json::array({local(Json::array())}), Json::array({local(Json())}), {{{"type", "global"}}}}) {
            expect(run(false, images, empty), {"OK"}, false);
            expect(run(false, images, empty, rb), {"NG"}, false);
        }
        expect(run(false, {}, Json::array()), {}, false);
        expect(run(true, {}, Json::array()), {}, true);
        expect(run(true, images, Json::array()), {"NG-Nan"}, true);
        expect(run(true, images, Json::array({local(pair)})), {"OK-5"}, true);
        for (const Json& rules : std::vector<Json>{Json::array(), {{{"operator", ">"}, {"value", 4}}},
            {{{"operator", "<"}, {"value", 6}}}, {{{"operator", ">"}, {"value", 4}}, {{"operator", "<"}, {"value", 6}}}})
            expect(run(true, images, Json::array({local(pair)}), {{"rules", rules}}), {"OK-5"}, true);
        for (const Json& rules : std::vector<Json>{{{{"operator", ">"}, {"value", 5}}},
            {{{"operator", "<"}, {"value", 5}}}, {{{"operator", ">"}, {"value", 4}}, {{"operator", "<"}, {"value", 5}}}})
            expect(run(true, images, Json::array({local(pair)}), {{"rules", rules}}), {"NG-5"}, true);
        expect(run(true, images, Json::array({local(Json::array({det("A", 10), det("B", 11.0000004)}))}),
            {{"rules", {{{"operator", ">"}, {"value", 1}}}}}), {"OK-1"}, true);
        expect(run(true, images, Json::array({local(Json::array({det("A", 10, 10), det("B", 11, 11)}))})), {"OK-1.414214"}, true);
        expect(run(true, images, Json::array({local(Json::array({det("A", 10), det("B", 10)}))})), {"OK-0"}, true);
        expect(run(true, images, Json::array({local(Json::array({det("A", 50, 50, 0.1), det("A", 10, 10, 0.99)})),
            local(Json::array({det("B", 13, 14, 0.95), det("B", 80, 80, 0.3)}))})), {"OK-5"}, true);
        expect(run(true, images, Json::array({local(Json::array({det("A", 10, 10), det("A", 90, 90), det("B", 13, 14)}))})), {"OK-5"}, true);
        for (const Json& detections : std::vector<Json>{Json::array(), Json::array({det("A", 1)}), {det("A", 1), det("A", 3)},
            {det("A", 1), det("B", 2), det("C", 3)}})
            expect(run(true, images, Json::array({local(detections)})), {"NG-Nan"}, true);

        // 每条 local 的逆仿射独立计算；CropBox 只是 C++ 的元信息，不重复平移。
        auto translated = identity; translated.AffineMatrix2x3 = {1, 0, -100, 0, 1, -80};
        translated.CropBox = {100, 80, 100, 100}; translated.OutputSize = {100, 100};
        auto scaled = identity; scaled.AffineMatrix2x3 = {2, 0, 0, 0, 2, 0};
        auto rotated = identity; rotated.AffineMatrix2x3 = {0, -1, 200, 1, 0, 0};
        const std::vector<ModuleImage> crops{ModuleImage(original, original, translated, 7),
            ModuleImage(original, original, scaled, 7), ModuleImage(original, original, rotated, 7)};
        const Json cropEntries = {local(Json::array({det("蓝", 10, 10)}), 0, translated.ToJson()),
            local(Json::array({det("红", 40, 40)}), 1, scaled.ToJson()), local(Json::array({det("绿", 150, 50)}), 2, rotated.ToJson())};
        for (const auto& direction : {"horizontal", "vertical"})
            expect(run(false, crops, cropEntries, {{"categories", {"红", "绿", "蓝"}}, {"direction", direction}}), {"OK"}, false);
        check(crops[0].OriginalIndex == 7 && crops[0].TransformState.ToJson() == translated.ToJson(), "wrapper mutated");
        expect(run(false, {crops[0]}, Json::array({local(Json::array({det("蓝", 10)}), 0, translated.ToJson()), local(Json::array({det("红", 60)}))}), rb), {"OK"}, false);
        auto nonuniform = identity; nonuniform.AffineMatrix2x3 = {2, 0, 6, 0, 3, 9};
        expect(run(true, images, Json::array({local(Json::array({det("A", 10, 27), det("B", 16, 39)}), 0, nonuniform.ToJson())})), {"OK-5"}, true);
        expect(run(true, {crops[0]}, Json::array({local(pair)})), {"OK-5"}, true);
        auto cropA = identity; cropA.AffineMatrix2x3 = {2, 0, -20, 0, 2, -40};
        auto cropB = identity; cropB.AffineMatrix2x3 = {0, -1, 70, 1, 0, -30};
        expect(run(true, images, Json::array({local(Json::array({det("A", 4, 6)}), 0, cropA.ToJson()),
            local(Json::array({det("B", 23, 5)}), 0, cropB.ToJson())})), {"OK-33.24154"}, true);
        auto selected = det("A", 10, 10, 0.99);
        selected["bbox"] = {10, 10, 2, 2}; selected["with_angle"] = true; selected["angle"] = 0.7;
        for (const Json& bbox : std::vector<Json>{{10, 10, 2, 2}, {10, 10, 2, 2, 0.7},
            {{"cx", 10}, {"cy", 10}, {"w", 2}, {"h", 2}, {"theta", 0.7}},
            {{"x1", 9}, {"y1", 9}, {"x2", 11}, {"y2", 11}}}) {
            selected["bbox"] = bbox;
            expect(run(true, images, Json::array({local(Json::array({selected, det("B", 13, 14)}))})), {"OK-5"}, true);
        }
        // 精确二进制中点：Python 六位格式采用偶数舍入，但规则比较未舍入距离。
        auto midpointA = det("A", 0), midpointB = det("B", 0);
        midpointA["bbox"] = {0.0, 0.0, 2.0, 2.0, 0.0};
        midpointB["bbox"] = {0.0078125, 0.0, 2.0, 2.0, 0.0};
        expect(run(true, images, Json::array({local(Json::array({midpointA, midpointB}))}),
            {{"rules", {{{"operator", ">"}, {"value", 0.007812}}}}}), {"OK-0.007812"}, true);
        expect(run(true, images, Json::array({local(Json::array({midpointA, midpointB}))}),
            {{"rules", {{{"operator", "<"}, {"value", 0.0078125}}}}}), {"NG-0.007812"}, true);
        auto red = det("红", 0), blue = det("蓝", 0);
        red["bbox"] = {100.000001, 50, 10, 10, 0.7}; blue["bbox"] = {100.000002, 50, 10, 10, 0.7};
        expect(run(false, images, Json::array({local(Json::array({blue, red}))}), rb), {"OK"}, false);

        // 同像素、同 OriginalIndex、同 transform 仍为不同原图；同原图不同索引则合并。
        const cv::Mat other = original.clone();
        const std::vector<ModuleImage> separate{images[0], ModuleImage(other, other, identity, 7)};
        expect(run(true, separate, Json::array({local(Json::array({det("A", 10)}), 0), local(Json::array({det("B", 13, 14)}), 1)})), {"NG-Nan", "NG-Nan"}, true);
        auto second = local(Json::array({det("蓝", 10), det("红", 20)}), 1); second.erase("index"); second["image_index"] = 1;
        expect(run(false, separate, Json::array({local(Json::array({det("红", 10), det("蓝", 20)})), second}), rb), {"OK", "NG"}, false);
        const std::vector<ModuleImage> aliases{images[0], ModuleImage(original, original, identity, 9)};
        auto a = local(Json::array({det("A", 10, 10)})); a["origin_index"] = 7;
        auto b = local(Json::array({det("B", 13, 14)}), 1); b["origin_index"] = 9;
        auto merged = run(true, aliases, Json::array({a, b})); expect(merged, {"OK-5"}, true);
        check(merged.ImageList[0].OriginalIndex == 7 && merged.ImageList[0].ImageObject.data == original.data, "first origin not preserved");
        a["index"] = nullptr; b["index"] = 20;
        expect(run(true, aliases, Json::array({a, b})), {"OK-5"}, true);
        a.erase("origin_index"); b.erase("origin_index"); b["index"] = nullptr;
        expect(run(true, aliases, Json::array({a, b})), {"OK-5"}, true);

        // 真正 MergeResults：两路 image 引用同一个上游 wrapper，results 各来自不同支路。
        auto mergeFactory = ModuleRegistry::Get("post_process/merge_results");
        check(static_cast<bool>(mergeFactory), "merge_results not registered");
        auto merge = mergeFactory(2, "", Json::object(), nullptr);
        merge->ExtraInputsIn.emplace_back(images, Json::array({local(Json::array({det("B", 13, 14)}))}));
        auto combined = merge->Process(images, Json::array({local(Json::array({det("A", 10, 10)}))}));
        check(combined.ImageList.size() == 2 && combined.ImageList[0].OriginalIndex == 0 &&
            combined.ImageList[1].OriginalIndex == 1 && images[0].OriginalIndex == 7, "MergeResults value-wrapper numbering");
        expect(run(true, combined.ImageList, combined.ResultList), {"OK-5"}, true);
        for (auto& entry : combined.ResultList) { entry["image_index"] = entry["index"]; entry.erase("index"); }
        expect(run(true, combined.ImageList, combined.ResultList), {"OK-5"}, true);

        for (bool distance : {false, true}) {
            rejects(distance, {}, Json::array({local(Json::array())}), Json::object());
            rejects(distance, separate, Json::array({{{"type", "local"}, {"sample_results", Json::array()}}}), Json::object());
            for (const Json& change : std::vector<Json>{{{"index", 20}}, {{"index", true}}, {{"index", -1}},
                {{"index", 0}, {"image_index", 1}}, {{"index", 0}, {"origin_index", 9}}}) {
                auto entry = local(Json::array()); entry.update(change);
                rejects(distance, separate, Json::array({entry}), Json::object());
            }
            auto conflict = local(Json::array()); conflict["origin_index"] = 0;
            rejects(distance, aliases, Json::array({conflict}), Json::object());
            for (const Json& state : std::vector<Json>{Json::object(), {{"original_width", 1}, {"original_height", 1}},
                {{"original_width", 300}, {"original_height", 200}, {"affine_2x3", {0,0,0,0,0,0}}},
                {{"original_width", 300}, {"original_height", 200}, {"affine_2x3", {1,0,0,0,1}}}})
                rejects(distance, images, Json::array({local(pair, 0, state)}), Json::object());
            // Native transform 可省略默认字段；出现时严格校验形状、尺寸和矩阵。
            for (const Json& patch : std::vector<Json>{
                {{"crop_box", {-1, 0, 100, 100}}}, {{"crop_box", {0, -1, 100, 100}}},
                {{"crop_box", {0, 0, 0, 100}}}, {{"crop_box", {0, 0, 100, -1}}},
                {{"crop_box", {0, 0, 100}}}, {{"crop_box", {0.5, 0, 100, 100}}},
                {{"output_size", {0, 100}}}, {{"output_size", {100, -1}}},
                {{"output_size", {100}}}, {{"output_size", {100, 0.5}}},
                {{"original_width", 0}}, {{"original_height", -1}},
                {{"affine_2x3", {1, 0, 0, 0, 1, std::numeric_limits<double>::infinity()}}}}) {
                auto state = identity.ToJson(); state.update(patch);
                rejects(distance, images, Json::array({local(pair, 0, state)}), Json::object());
            }
            rejects(distance, images, Json::array({nullptr}), Json::object());
            rejects(distance, images, Json::array({local(Json::object())}), Json::object());
            rejects(distance, images, Json::array({local(Json::array({nullptr}))}), Json::object());
        }
        for (const Json& props : std::vector<Json>{{{"categories", "红"}}, {{"categories", {1}}}, {{"direction", "diagonal"}}})
            rejects(false, images, Json::array(), props);
        for (const Json& rules : std::vector<Json>{Json(), Json::object(), "[]", {nullptr}, {Json::object()},
            {{{"operator", ">="}, {"value", 0}}}, {{{"operator", ">"}, {"value", -1}}},
            {{{"operator", ">"}, {"value", true}}}, {{{"operator", ">"}, {"value", "invalid"}}}})
            rejects(true, images, Json::array(), {{"rules", rules}});
        for (const Json& bbox : std::vector<Json>{Json(), Json::array(), {1,2,3}, {1,1,-2,2}, {1,1,2,2,"nan"}}) {
            auto invalid = det("A", 10, 10, 1); invalid["bbox"] = bbox;
            expect(run(true, images, Json::array({local(Json::array({invalid, det("A", 10, 10, 0.1), det("B", 13, 14)}))})), {"NG-Nan"}, true);
            rejects(false, images, Json::array({local(Json::array({invalid}))}), {{"categories", {"A"}}});
        }
        for (const Json& score : std::vector<Json>{Json(), true, "invalid", "inf", "nan"}) {
            auto invalid = det("A", 10, 10); invalid["score"] = score;
            expect(run(true, images, Json::array({local(Json::array({invalid, det("A", 10, 10, 0.1), det("B", 13, 14)}))})), {"NG-Nan"}, true);
        }
        for (const Json& category : std::vector<Json>{Json(), "", 42}) {
            auto invalid = det("A", 10, 10); invalid["category_name"] = category;
            expect(run(true, images, Json::array({local(Json::array({invalid, det("B", 13, 14)}))})), {"NG-Nan"}, true);
        }
        auto ignoredBbox = det("A", 100, 100, 0.1); ignoredBbox.erase("bbox");
        expect(run(true, images, Json::array({local(Json::array({ignoredBbox, det("A", 10, 10), det("B", 13, 14)}))})), {"OK-5"}, true);
        ignoredBbox["score"] = "nan";
        expect(run(true, images, Json::array({local(Json::array({ignoredBbox, det("A", 10, 10), det("B", 13, 14)}))})), {"NG-Nan"}, true);
        expect(run(true, separate, Json::array()), {"NG-Nan", "NG-Nan"}, true);
        expect(run(false, separate, Json::array()), {"OK", "OK"}, false);

        // 通过公开 DLL API 执行，避免仅验证 EXE 内独立注册表。
        for (bool distance : {false, true}) for (int alias = 0; alias < (distance ? 1 : 2); ++alias)
        for (int reference = 0; reference < (distance ? 1 : 2); ++reference)
        for (bool keepDetections : {false, true}) for (int outcome = 0; outcome < (distance ? 3 : 2); ++outcome) {
            const bool expectedOk = outcome == 1;
            const bool invalidDistance = outcome == 2;
            const auto path = std::filesystem::temp_directory_path() /
                (std::string("algorithm-shared-input-") + (distance ? "offset" : "sequence") + (alias ? "-legacy" : "") + (keepDetections ? "-keep.json" : ".json"));
            auto archivePath = path;
            archivePath.replace_extension(".dvst");
            const auto pipeline = BuildAlgorithmSharedInputFlow(distance, keepDetections, expectedOk, invalidDistance, alias != 0, reference != 0).dump(2);
            {
                std::ofstream file(path, std::ios::binary);
                check(static_cast<bool>(file), "cannot write synthetic flow");
                file << pipeline;
                file.close();
                check(static_cast<bool>(file), "cannot save synthetic flow");
            }
            FlowGraphModel model;
            const auto loaded = model.Load(path.string(), 0);
            check(loaded.value("code", 1) == 0, "DLL graph load: " + loaded.dump());
            const auto root = model.InferInternal({original}, Json::object());
            check(root.value("code", 1) == 0, "DLL graph infer: " + root.dump());
            const std::string label = distance ? (invalidDistance ? "NG-Nan" : (expectedOk ? "OK-5" : "NG-5")) : (expectedOk ? "OK" : "NG");
            const std::string reason = distance ? (invalidDistance ? "距离：NaN" : "距离：5 px") :
                (reference ? (expectedOk ? "期望顺序：B → A\n当前顺序：B → A" : "期望顺序：A → B\n当前顺序：B → A") :
                (expectedOk ? "期望顺序：A → B\n当前顺序：A → B" : "期望顺序：B → A\n当前顺序：A → B"));
            auto verify = [&](const Json& result, bool normalized = false) {
                check(result.is_object() && result.contains("ok") && result.at("ok") == expectedOk, "graph inspection status");
                check(result.at("reason") == reason || result.at("reason") == Json::array({reason}), "graph reason");
                const auto& detections = result.at("result_list");
                check(detections.is_array() && detections.size() == (keepDetections ? 3 : 1), "graph detection count");
                bool classification = false, a = false, b = false;
                for (const auto& detection : detections) {
                    const auto name = detection.at("category_name").get<std::string>();
                    if (name == label) {
                        classification = true;
                        check(detection.at("score") == 1.0 && detection.at("category_id") == (expectedOk ? 0 : 1), "graph classification");
                        check(detection.at("bbox") == (distance ? (normalized ? Json::array({150, 100, 300, 200}) :
                            Json::array({150, 100, 300, 200, 0})) : Json::array({0, 0, 300, 200})), "graph full-image bbox");
                        if (normalized && distance)
                            check(detection.at("with_angle") == true && detection.at("angle") == 0, "public rotated bbox");
                    } else if (name == "A") a = true;
                    else if (name == "B") b = true;
                    else check(false, "unexpected graph detection");
                }
                check(classification && (!keepDetections || (a && (b == !invalidDistance))), "graph retained original detections");
            };
            verify(root);
            verify(model.InferOneOutJson(original, Json::object()));
            {
                const Json header{{"file_list", {"pipeline.json"}}, {"file_size", {pipeline.size()}}};
                std::ofstream archive(archivePath, std::ios::binary);
                check(static_cast<bool>(archive), "cannot write synthetic archive");
                archive << "DV\n" << header.dump() << "\n" << pipeline;
                archive.close();
                check(static_cast<bool>(archive), "cannot save synthetic archive");
            }
            dlcv_infer::Model publicModel(archivePath.string(), 0);
            const auto publicJson = publicModel.InferOneOutJson(original, Json::object());
            verify(publicJson, true);
            check(publicJson.at("reason") == Json::array({reason}), "public JSON reason normalization");
            bool inspectionOk = !expectedOk;
            std::vector<std::string> inspectionReasons;
            check(dlcv_infer::Model::GetLastInspectionStatus(inspectionOk, inspectionReasons), "public DLL inspection available");
            check(inspectionOk == expectedOk && inspectionReasons == std::vector<std::string>{reason}, "public DLL inspection matches output");
        }
        std::cout << "algorithm-modules-selftest passed: " << checks << " assertions" << std::endl;
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "algorithm-modules-selftest failed: " << error.what() << std::endl;
        return 1;
    }
}
