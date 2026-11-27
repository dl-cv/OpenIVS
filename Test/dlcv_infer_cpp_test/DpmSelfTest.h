#pragma once
#include "flow/modules/dpm/DpmEnhancer.h"
#include <future>

namespace dpm_test {
json WithoutTimings(const json& value) {
    if (value.is_object()) {
        json result=json::object();
        for(auto it=value.begin();it!=value.end();++it) {
            const auto& key=it.key();
            if(key.size()<3 || key.compare(key.size()-3,3,"_ms")!=0) result[key]=WithoutTimings(it.value());
        }
        return result;
    }
    if(value.is_array()) {
        json result=json::array();
        for(const auto& item:value) result.push_back(WithoutTimings(item));
        return result;
    }
    return value;
}
cv::Mat Load(const json& path, int flags = cv::IMREAD_GRAYSCALE) {
    std::ifstream input{std::filesystem::path(Utf8ToWide(path.get<std::string>())), std::ios::binary};
    std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
    auto image=cv::imdecode(bytes,flags);
    if(image.empty()) throw std::runtime_error("Missing DPM image");
    return image;
}
json ReadCrop(const json& item, const std::filesystem::path& outputDir, bool save) {
    auto gray=Load(item.at("path"));
    auto enhanced=dlcv_infer::flow::dpm::Enhance(gray,[](const cv::Mat&) {return std::string();});
    if(save) {
        std::vector<uint8_t> bytes;
        cv::imencode(".png",enhanced.image,bytes);
        std::ofstream file{outputDir/(item.at("name").get<std::string>()+".png"), std::ios::binary};
        file.write(reinterpret_cast<const char*>(bytes.data()),bytes.size());
    }
    cv::Mat padded(gray.rows,gray.cols+13,gray.type());
    auto roi=padded(cv::Rect(0,0,gray.cols,gray.rows));
    gray.copyTo(roi);
    dlcv_infer::flow::ModuleImage wrap(roi,roi,dlcv_infer::flow::TransformationState(roi.cols,roi.rows),7);
    auto factory=dlcv_infer::flow::ModuleRegistry::Get("features/barcode_reader");
    auto local=factory(1,"",{{"enhance",true},{"formats","DataMatrix"}},nullptr)->Process({wrap},json::array()).ResultList[0]["sample_results"];
    if(cv::norm(roi,gray,cv::NORM_INF)!=0) throw std::runtime_error("DPM modified input");
    std::string pixels;
    pixels.reserve(enhanced.image.total());
    for(size_t i=0;i<enhanced.image.total();++i) pixels.push_back(enhanced.image.data[i] ? '0':'1');
    return {{"name",item.at("name")},{"mode",enhanced.mode},{"pixels",pixels},{"ms",enhanced.totalMs},{"local",local}};
}
int Run(int argc, wchar_t** argv) {
    if(argc!=4) throw std::invalid_argument("dpm-selftest <manifest.json> <output.json>");
    std::ifstream file{std::filesystem::path(argv[2])};
    json manifest; file>>manifest;
    if(manifest.at("crops").size()<4) throw std::invalid_argument("DPM manifest requires at least four crops");
    auto outputDir=std::filesystem::path(argv[3]).parent_path();
    std::filesystem::create_directories(outputDir);
    auto start=std::chrono::steady_clock::now();
    std::vector<std::future<json>> futures;
    for(int i=0;i<4;++i) futures.push_back(std::async(std::launch::async,[&,i]{return ReadCrop(manifest["crops"][i],outputDir,false);}));
    json concurrent=json::array();
    for(auto& future:futures) concurrent.push_back(future.get());
    double concurrentMs=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
    json crops=json::array(),flows=json::array();
    for(const auto& item:manifest.at("crops")) crops.push_back(ReadCrop(item,outputDir,true));
    for(int i=0;i<4;++i) if(crops[i]["pixels"]!=concurrent[i]["pixels"] || crops[i]["mode"]!=concurrent[i]["mode"] ||
        WithoutTimings(crops[i]["local"])!=WithoutTimings(concurrent[i]["local"]))
        throw std::runtime_error("Concurrent DPM mismatch");
    if(manifest.contains("model")) {
        dlcv_infer::Model model(Utf8ToWide(manifest["model"].get<std::string>()),0);
        for(const auto& item:manifest.at("images")) {
            auto bgr=Load(item.at("path"),cv::IMREAD_COLOR);
            cv::Mat rgb; cv::cvtColor(bgr,rgb,cv::COLOR_BGR2RGB);
            model.InferOneOutJson(rgb);
            start=std::chrono::steady_clock::now();
            auto result=model.InferOneOutJson(rgb);
            double ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
            flows.push_back({{"name",item.at("name")},{"ms",ms},{"result",result}});
        }
    }
    std::ofstream destination{std::filesystem::path(argv[3])};
    destination<<json{{"crops",crops},{"flow",flows},{"concurrent_ms",concurrentMs}}.dump(2);
    if(!destination) throw std::runtime_error("Cannot write DPM report");
    std::cout<<"DPM C++ selftest: "<<crops.size()<<std::endl;
    return 0;
}
}
