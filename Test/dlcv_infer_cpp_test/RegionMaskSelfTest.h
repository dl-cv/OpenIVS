#pragma once
#include <opencv2/imgproc.hpp>

// 固定输入的模块自测，不依赖模型和加密狗。
static int RunRegionMaskSelfTest() {
    using namespace dlcv_infer::flow;
    try {
        auto det = [](int x, int y, int w, int h, int left = 0, int right = -1) {
            Json rows = Json::array();
            for (int iy = 0; iy < h; ++iy) {
                Json row = Json::array();
                for (int ix = 0; ix < w; ++ix) row.push_back(ix >= left && (right < 0 || ix < right) ? 255 : 0);
                rows.push_back(row);
            }
            return Json{{"bbox", {x,y,w,h}}, {"mask_array", rows}, {"score", 0.9}};
        };
        auto entries = [](const Json& target) {
            return Json::array({Json{{"type", "local"}, {"index", 0}, {"origin_index", 0},
                {"transform", nullptr}, {"sample_results", Json::array({target})}}});
        };
        cv::Mat image(32,32,CV_8UC3,cv::Scalar::all(0));
        std::vector<ModuleImage> images{ModuleImage(image,image,TransformationState(32,32),0)};
        auto factory = ModuleRegistry::Get("post_process/result_filter_region");
        if (!factory) throw std::runtime_error("region module not registered");
        int count = 0;
        std::string regionMode = "any_bbox";
        bool global = false;
        std::string filterMode = "mask";
        auto check = [&](const std::string& name, Json target, Json regions, bool expected, const std::string& metric, double threshold) {
            Json props{{"filter_mode", filterMode}, {"overlap_threshold", threshold}, {"result_region_mode", regionMode}};
            if (!metric.empty()) props["metric"] = metric;
            auto module = (global ? ModuleRegistry::Get("post_process/result_filter_region_global") : factory)(1, "", props, nullptr);
            module->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{}, regions);
            auto input = entries(target);
            auto before = input;
            auto output = module->Process(images, input);
            if ((!output.ResultList.empty()) != expected || (!module->ExtraOutputs.at(0).ResultList.empty()) == expected)
                throw std::runtime_error(name + ": wrong branch");
            if (module->ScalarOutputsByName.at("has_positive").get<bool>() != expected)
                throw std::runtime_error(name + ": wrong scalar");
            auto result = expected ? output.ResultList : module->ExtraOutputs.at(0).ResultList;
            auto actual = result.at(0).at("sample_results").at(0);
            if (filterMode == "legacy") actual.erase("metadata");
            if (input != before || actual != target) throw std::runtime_error(name + ": geometry changed");
            std::cout << "PASS " << name << std::endl;
            ++count;
        };
        check("shape-not-bbox", det(8,9,10,10,0,4), entries(det(8,9,10,10,6,10)), false,"",0.5);
        check("default-ios-xywh",det(8,9,10,10),entries(det(8,9,2,2)),true,"",0.5);
        check("iou",det(8,9,10,10),entries(det(8,9,2,2)),false,"iou",0.5);
        check("inclusive-threshold",det(8,9,10,10),entries(det(13,9,10,10)),true,"IOS",0.5);
        check("above-threshold",det(8,9,10,10),entries(det(13,9,10,10)),false,"ios",0.51);
        check("zero-needs-intersection",det(8,9,10,10),entries(det(18,9,10,10)),false,"ios",0);
        const Json triangle = {{"bbox", {8, 9, 10, 10}}, {"polygon", {{8, 9}, {17, 9}, {8, 18}}}, {"score", 0.9}};
        const auto center = det(9,10,2,2), corner = det(16,17,2,2);
        auto emptyPolygonMask = triangle;
        emptyPolygonMask["mask_array"] = Json::array();
        for (const std::string mode : {"mask", "legacy"}) {
            filterMode = mode;
            check(mode + "-polygon-region-corner",corner,entries(triangle),false,"",0.5);
            check(mode + "-polygon-target-corner",triangle,entries(corner),false,"",0.5);
            check(mode + "-polygon-region-center",center,entries(triangle),true,"",0.5);
            check(mode + "-polygon-target-center",triangle,entries(center),true,"",0.5);
            check(mode + "-empty-mask-priority",center,entries(emptyPolygonMask),false,"",0.5);
            for (int value : {1, 127, 128}) {
                auto pixel = det(9,10,1,1);
                pixel["mask_array"][0][0] = value;
                check(mode + "-polygon-mask-" + std::to_string(value),pixel,entries(triangle),value > 127,"",0.5);
            }
        }
        filterMode = "mask";
        check("polygon-threshold-ios",triangle,entries(center),true,"ios",1);
        check("polygon-threshold-iou",triangle,entries(center),false,"iou",0.5);
        auto malformed = triangle;
        malformed["polygon"][0][0] = 1e9 + 1;
        for (const std::string mode : {"mask", "legacy"}) {
            auto module = factory(1, "", Json{{"filter_mode", mode}}, nullptr);
            module->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{}, entries(triangle));
            try { module->Process(images, entries(malformed)); throw std::runtime_error(mode + " invalid polygon accepted"); }
            catch (const std::invalid_argument&) {}
        }
        {
            TransformationState stateA(32,32), stateB(32,32);
            stateA.AffineMatrix2x3 = {1,0,-3,0,1,0};
            stateB.AffineMatrix2x3 = {1,0,-1,0,1,0};
            stateA.OutputSize = stateB.OutputSize = {32,32};
            auto entry = [&](const Json& item, int index, int origin, const Json& transform) {
                auto result = entries(item);
                result[0]["index"] = index;
                result[0]["origin_index"] = origin;
                result[0]["transform"] = transform;
                return result;
            };
            std::vector<ModuleImage> crops{ModuleImage(image,image,stateA,0), ModuleImage(image,image,stateB,0)};
            Json target{{"bbox", {1,1,3,3}}};
            const Json region{{"bbox", {3,1,3,3}}, {"polygon", {{3,1},{5,1},{5,3},{3,3}}}};
            auto module = factory(1, "", Json::object(), nullptr);
            module->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{}, entry(region, 1, 0, stateB.ToJson()));
            auto output = module->Process(crops, entry(target, 0, 0, stateA.ToJson()));
            if (output.ImageList.size() != 1 || output.ImageList[0].TransformState.AffineMatrix2x3 != stateA.AffineMatrix2x3)
                throw std::runtime_error("legacy polygon should match across crops of the same image");
            crops[1] = ModuleImage(image,image,stateA,1);
            target["bbox"] = region.at("bbox");
            module = factory(1, "", Json::object(), nullptr);
            module->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{}, entry(region, 1, 1, stateA.ToJson()));
            output = module->Process(crops, entry(target, 0, 0, stateA.ToJson()));
            if (!output.ImageList.empty() || module->ExtraOutputs.at(0).ImageList.at(0).OriginalIndex != 0)
                throw std::runtime_error("legacy polygon should not match another image with the same transform");
            auto regions = entry(target, 0, 0, stateA.ToJson());
            const Json farRegion{{"bbox", {22,22,3,3}}, {"polygon", {{22,22},{24,22},{24,24},{22,24}}}};
            regions.push_back(entry(farRegion, 1, 1, stateA.ToJson()).at(0));
            module = factory(1, "", Json::object(), nullptr);
            module->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{}, regions);
            output = module->Process(crops, entry(target, 1, 1, stateA.ToJson()));
            if (!output.ImageList.empty() || module->ExtraOutputs.at(0).ImageList.at(0).OriginalIndex != 1)
                throw std::runtime_error("legacy region rectangles must stay with their original image when polygons are present");
            module = factory(1, "", Json::object(), nullptr);
            output = module->Process(crops, entry(region, 0, 0, nullptr));
            const auto& original = output.ResultList.at(0).at("sample_results").at(0);
            if (original.at("polygon") != region.at("polygon") || original.at("bbox") != region.at("bbox"))
                throw std::runtime_error("legacy original polygon should not be converted twice");
            std::cout << "PASS legacy-polygon-transforms" << std::endl;
        }
        auto polygonOnly = triangle; polygonOnly.erase("bbox");
        check("polygon-without-bbox",center,entries(polygonOnly),true,"ios",1);
        auto missing = det(8,9,10,10); missing.erase("mask_array");
        check("missing-target-mask",missing,entries(det(8,9,10,10)),true,"",0.5);
        check("missing-region-mask",det(8,9,10,10),entries(missing),true,"",0.5);
        check("both-missing-iou",missing,entries(missing),true,"iou",1);
        check("fallback-ios",missing,entries(det(8,9,2,2)),true,"ios",1);
        check("fallback-iou",missing,entries(det(8,9,2,2)),false,"iou",0.5);
        check("fallback-shape",missing,entries(det(8,9,10,10,0,4)),false,"iou",0.5);
        auto nullMask = missing; nullMask["mask_array"] = nullptr; nullMask["mask_rle"] = nullptr;
        check("null-mask",nullMask,entries(missing),true,"iou",1);
        auto empty = missing; empty["mask_array"] = Json::array();
        check("empty-array-no-fallback",empty,entries(missing),false,"",0);
        check("empty-region-mask-no-fallback",missing,entries(empty),false,"",0);
        auto rle = missing; rle["mask_rle"] = {{"width",10},{"height",10},{"runs",{100}}};
        check("zero-rle-no-fallback",rle,entries(missing),false,"",0);
        rle["mask_rle"]["runs"] = {0,100};
        check("rle-with-missing-array",rle,entries(missing),true,"iou",1);
        for (const auto& bbox : std::vector<Json>{{-5,9,10,10},{0.2,9.2,4.6,9.6}}) {
            auto clipped = missing; clipped["bbox"] = bbox;
            check("bbox-clipping-rounding",clipped,entries(det(0,9,5,10)),true,"iou",1);
        }
        for (const auto& bbox : std::vector<Json>{Json::array(),{0,0,0,10},{0,0,-1,10},
            {40,40,10,10},{"bad",0,10,10},{std::numeric_limits<double>::infinity(),0,10,10}}) {
            auto invalid = missing; invalid["bbox"] = bbox;
            check("unusable-bbox",invalid,entries(missing),false,"",0);
        }
        check("missing-bbox",Json::object(),entries(missing),false,"",0);
        auto huge = missing; huge["bbox"] = {-1e12,-1e12,2e12,2e12};
        check("huge-bbox-clipped-before-allocation",huge,entries(det(0,0,32,32)),true,"iou",1);
        check("empty-mask",det(8,9,10,10,0,0),entries(det(8,9,10,10)),false,"",0.5);
        check("empty-region",det(8,9,10,10),Json::array(),false,"",0.5);
        check("source-clipping",det(-5,9,10,10),entries(det(0,9,5,10)),true,"",1);
        auto low = det(8,9,2,2); low["bbox"] = {8,9,10,10};
        check("resize-nearest",low,entries(det(8,9,10,10)),true,"",1);
        auto full = det(0,0,32,32,8,10); full["bbox"] = {8,0,2,32};
        check("full-image-mask",full,entries(det(8,0,2,32)),true,"",1);
        for (int value : {1,127,128}) {
            auto binary = det(8,9,2,2); binary["mask_array"] = {{value,value},{value,value}};
            check("foreground-"+std::to_string(value),binary,entries(det(8,9,2,2)),value>127,"",1);
        }
        regionMode = "top1_bbox";
        auto highMissing = missing; highMissing["score"] = 1; highMissing["bbox"] = {20,20,2,2};
        auto topRegions = entries(highMissing); topRegions[0]["sample_results"].push_back(det(8,9,2,2));
        check("top1-includes-bbox-fallback",det(8,9,2,2),topRegions,false,"",1);
        auto highFar = det(20,20,2,2); highFar["score"] = 1;
        topRegions = entries(det(8,9,2,2)); topRegions[0]["sample_results"].push_back(highFar);
        check("top1-highest-valid-score",det(8,9,2,2),topRegions,false,"",1);
        for (auto& d : topRegions[0]["sample_results"]) d.erase("score");
        check("top1-first-without-score",det(8,9,2,2),topRegions,true,"",1);
        regionMode = "any_bbox";
        global = true;
        check("global-mask-iou",det(8,9,10,10),entries(det(8,9,2,2)),false,"iou",0.5);
        check("global-bbox-fallback",missing,entries(missing),true,"iou",1);
        global = false;
        // Compare cropped window warping with the full original-canvas reference.
        for (const auto& spec : std::vector<std::pair<double,double>>{{17,1},{45,0.7},{-33,1.4},{90,1},{180,1},{0,-1}}) {
            auto affine = cv::getRotationMatrix2D(cv::Point2f(8,8),spec.first,spec.second<0?1:spec.second);
            if (spec.second<0) { affine.at<double>(0,0)=-1; affine.at<double>(0,2)=15; }
            auto forward = affine.clone();
            forward.at<double>(0,2) -= 2*affine.at<double>(0,0)+affine.at<double>(0,1);
            forward.at<double>(1,2) -= 2*affine.at<double>(1,0)+affine.at<double>(1,1);
            cv::Mat inverse,reference,source(16,16,CV_8UC1,cv::Scalar::all(0));
            cv::invertAffineTransform(forward,inverse);
            auto local = det(5,4,6,7);
            for (int iy=0;iy<7;++iy) for (int ix=0;ix<6;++ix) {
                unsigned char value = (iy<3 && ix<4 || iy>=2 && ix>=2)?255:0;
                local["mask_array"][iy][ix]=value; source.at<unsigned char>(4+iy,5+ix)=value;
            }
            cv::warpAffine(source,reference,inverse,cv::Size(32,32),cv::INTER_NEAREST,cv::BORDER_CONSTANT,cv::Scalar::all(0));
            auto expected = det(0,0,32,32);
            for (int iy=0;iy<32;++iy) for (int ix=0;ix<32;++ix) expected["mask_array"][iy][ix]=reference.at<unsigned char>(iy,ix);
            for (bool native : {false,true}) {
                Json transform{{"crop_box",{2,1,16,16}},{"output_size",{16,16}}};
                if (native) {
                    transform["original_width"]=32; transform["original_height"]=32;
                    transform["affine_2x3"]={forward.at<double>(0,0),forward.at<double>(0,1),forward.at<double>(0,2),forward.at<double>(1,0),forward.at<double>(1,1),forward.at<double>(1,2)};
                } else {
                    transform["original_size"]={32,32};
                    transform["affine_matrix"]={{affine.at<double>(0,0),affine.at<double>(0,1),affine.at<double>(0,2)},{affine.at<double>(1,0),affine.at<double>(1,1),affine.at<double>(1,2)}};
                }
                auto transformed = entries(local); transformed[0]["transform"]=transform;
                check("affine-"+std::to_string(spec.first)+"-"+std::to_string(spec.second)+(native?"-native":"-python"),expected,transformed,true,"iou",1);
                cv::Mat rectangle(16,16,CV_8UC1,cv::Scalar::all(0)),rectangleReference;
                rectangle(cv::Rect(5,4,6,7)).setTo(cv::Scalar(255));
                cv::warpAffine(rectangle,rectangleReference,inverse,cv::Size(32,32),cv::INTER_NEAREST,cv::BORDER_CONSTANT,cv::Scalar::all(0));
                auto expectedRectangle = det(0,0,32,32);
                for (int iy=0;iy<32;++iy) for (int ix=0;ix<32;++ix)
                    expectedRectangle["mask_array"][iy][ix]=rectangleReference.at<unsigned char>(iy,ix);
                transformed[0]["sample_results"][0].erase("mask_array");
                check("bbox-affine-"+std::to_string(spec.first)+"-"+std::to_string(spec.second)+(native?"-native":"-python"),expectedRectangle,transformed,true,"iou",1);
            }
        }
        // A region branch may reindex image 1 to index 0; origin remains authoritative.
        auto multiImages = images;
        multiImages.emplace_back(image,image,TransformationState(32,32),1);
        auto multiTargets = entries(det(8,9,2,2));
        auto second = multiTargets.at(0); second["index"] = 1; second["origin_index"] = 1;
        multiTargets.push_back(second);
        auto otherRegion = entries(det(8,9,2,2)); otherRegion[0]["origin_index"] = 1;
        auto isolated = factory(1,"",Json{{"filter_mode","mask"}},nullptr);
        isolated->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{},otherRegion);
        auto isolatedOutput = isolated->Process(multiImages,multiTargets);
        if (isolatedOutput.ResultList.size() != 1 || isolatedOutput.ResultList[0]["origin_index"] != 1
            || isolatedOutput.ImageList.size() != 1 || isolatedOutput.ImageList[0].OriginalIndex != 1
            || isolatedOutput.ResultList[0]["index"] != 0
            || isolated->ExtraOutputs[0].ResultList.size() != 1 || isolated->ExtraOutputs[0].ImageList[0].OriginalIndex != 0)
            throw std::runtime_error("origin-isolation-reindexed-region: wrong source image");
        std::cout << "PASS origin-isolation-reindexed-region" << std::endl; ++count;
        for (bool badTransform : {false,true}) {
            auto badInput = entries(missing);
            if (badTransform)
                badInput[0]["transform"] = {{"original_width",32},{"original_height",32},
                    {"output_size",{32,32}},{"affine_2x3",{0,0,0,0,0,0}}};
            else badInput[0]["sample_results"][0]["mask_array"] = Json::array({Json::array({256})});
            auto invalidModule = factory(1,"",Json{{"filter_mode","mask"}},nullptr);
            invalidModule->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{},entries(det(8,9,10,10)));
            bool failed=false;
            try { invalidModule->Process(images,badInput); } catch (const std::invalid_argument&) { failed=true; }
            if (!failed) throw std::runtime_error("invalid mask/transform silently replaced by bbox");
            std::cout << "PASS invalid-mask-or-transform-no-fallback" << std::endl; ++count;
        }
        for (Json invalid : std::vector<Json>{{{"metric","bad"}},{{"overlap_threshold",-1}},{{"overlap_threshold",2}},{{"overlap_threshold","nan"}}}) {
            invalid["filter_mode"]="mask";
            auto invalidModule = factory(1,"",invalid,nullptr);
            invalidModule->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{},entries(det(0,0,2,2)));
            bool failed=false;
            try { invalidModule->Process(images,entries(det(0,0,2,2))); } catch (const std::invalid_argument&) { failed=true; }
            if (!failed) throw std::runtime_error("invalid-mask-parameter accepted");
            std::cout << "PASS invalid-mask-parameter" << std::endl; ++count;
        }
        auto unknown = entries(det(8,9,2,2)); unknown[0]["origin_index"]=9;
        auto unknownModule = factory(1,"",Json{{"filter_mode","mask"}},nullptr);
        unknownModule->ExtraInputsIn.emplace_back(std::vector<ModuleImage>{},entries(det(8,9,2,2)));
        bool unknownRejected=false;
        try { unknownModule->Process(images,unknown); } catch (const std::invalid_argument&) { unknownRejected=true; }
        if (!unknownRejected) throw std::runtime_error("unknown origin accepted");
        std::cout << "PASS unknown-target-origin" << std::endl; ++count;
        auto module = factory(1,"",Json{{"filter_mode","mask"}},nullptr);
        bool rejected = false;
        try { module->Process(images,entries(det(0,0,2,2))); } catch (const std::invalid_argument&) { rejected = true; }
        if (!rejected) throw std::runtime_error("unconnected mask region accepted");
        std::cout << "Region mask selftest passed: " << (count+1) << std::endl;
        return 0;
    } catch (const std::exception& ex) { std::cerr << ex.what() << std::endl; return 1; }
}
