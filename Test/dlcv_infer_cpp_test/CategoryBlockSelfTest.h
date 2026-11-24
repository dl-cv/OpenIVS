#pragma once

static int RunCategoryBlockSelfTest(int argc, wchar_t** argv) {
    using namespace dlcv_infer::flow;
    if (argc != 3) throw std::invalid_argument("category-block-selftest <cases.json>");
    std::ifstream stream{std::filesystem::path(argv[2])};
    Json cases;
    stream >> cases;
    auto factory = ModuleRegistry::Get("post_process/category_block");
    if (!factory) throw std::runtime_error("category_block not registered");
    cv::Mat mat(4, 5, CV_8UC3, cv::Scalar::all(7));
    std::vector<ModuleImage> images{ModuleImage(mat, mat, TransformationState(5, 4), 0),
        ModuleImage(mat, mat, TransformationState(5, 4), 1)};
    for (const auto& test : cases) {
        auto module = factory(1, "", test.at("properties"), nullptr);
        const Json original = test.at("input");
        Json input = original;
        for (int run = 0; run < 2; ++run) {
            auto output = module->Process(images, input);
            if (output.ResultList != test.at("expected"))
                throw std::runtime_error(test.at("name").get<std::string>() + " output mismatch: " + output.ResultList.dump());
            if (input != original) throw std::runtime_error("upstream mutated");
            if (output.ImageList.size() != 2 || output.ImageList[1].InstanceId != images[1].InstanceId ||
                output.ImageList[1].ImageObject.data != mat.data || mat.at<cv::Vec3b>(0, 0)[0] != 7)
                throw std::runtime_error("image changed");
        }
        std::cout << "PASS " << test.at("name").get<std::string>() << std::endl;
    }
    return 0;
}
