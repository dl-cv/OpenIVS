#pragma once

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
        auto check = [&](const std::string& name, Json target, Json regions, bool expected, const std::string& metric, double threshold) {
            Json props{{"filter_mode", "mask"}, {"overlap_threshold", threshold}};
            if (!metric.empty()) props["metric"] = metric;
            auto module = factory(1, "", props, nullptr);
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
        auto module = factory(1,"",Json{{"filter_mode","mask"}},nullptr);
        bool rejected = false;
        try { module->Process(images,entries(det(0,0,2,2))); } catch (const std::invalid_argument&) { rejected = true; }
        if (!rejected) throw std::runtime_error("unconnected mask region accepted");
        std::cout << "Region mask selftest passed: " << (count+1) << std::endl;
        return 0;
    } catch (const std::exception& ex) { std::cerr << ex.what() << std::endl; return 1; }
}
