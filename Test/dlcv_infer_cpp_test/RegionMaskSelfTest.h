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
        auto check = [&](const std::string& name, Json target, Json regions, bool expected, const std::string& metric, double threshold) {
            Json props{{"filter_mode", "mask"}, {"overlap_threshold", threshold}, {"result_region_mode", regionMode}};
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
            if (input != before || result.at(0).at("sample_results").at(0) != target)
                throw std::runtime_error(name + ": geometry changed");
            std::cout << "PASS " << name << std::endl;
            ++count;
        };
        check("shape-not-bbox", det(8,9,10,10,0,4), entries(det(8,9,10,10,6,10)), false,"",0.5);
        check("default-ios-xywh",det(8,9,10,10),entries(det(8,9,2,2)),true,"",0.5);
        check("iou",det(8,9,10,10),entries(det(8,9,2,2)),false,"iou",0.5);
        check("inclusive-threshold",det(8,9,10,10),entries(det(13,9,10,10)),true,"IOS",0.5);
        check("above-threshold",det(8,9,10,10),entries(det(13,9,10,10)),false,"ios",0.51);
        check("zero-needs-intersection",det(8,9,10,10),entries(det(18,9,10,10)),false,"ios",0);
        auto missing = det(8,9,10,10); missing.erase("mask_array");
        check("missing-target-mask",missing,entries(det(8,9,10,10)),false,"",0.5);
        check("missing-region-mask",det(8,9,10,10),entries(missing),false,"",0.5);
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
        auto highMissing = missing; highMissing["score"] = 1;
        auto topRegions = entries(highMissing); topRegions[0]["sample_results"].push_back(det(8,9,2,2));
        check("top1-skips-missing-mask",det(8,9,2,2),topRegions,true,"",1);
        auto highFar = det(20,20,2,2); highFar["score"] = 1;
        topRegions = entries(det(8,9,2,2)); topRegions[0]["sample_results"].push_back(highFar);
        check("top1-highest-valid-score",det(8,9,2,2),topRegions,false,"",1);
        for (auto& d : topRegions[0]["sample_results"]) d.erase("score");
        check("top1-first-without-score",det(8,9,2,2),topRegions,true,"",1);
        regionMode = "any_bbox";
        global = true;
        check("global-mask-iou",det(8,9,10,10),entries(det(8,9,2,2)),false,"iou",0.5);
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
