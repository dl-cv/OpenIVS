#pragma once

static int RunImageAngleSelfTest(int argc, wchar_t** argv) {
    using namespace dlcv_infer::flow;
    if (argc != 3) throw std::invalid_argument("image-angle-selftest <cases.json>");
    std::ifstream stream{std::filesystem::path(argv[2])};
    Json cases;
    stream >> cases;
    auto factory = ModuleRegistry::Get("post_process/image_angle");
    if (!factory) throw std::runtime_error("image_angle not registered");
    cv::Mat mat(4, 5, CV_8UC3, cv::Scalar::all(7));
    for (const auto& test : cases) {
        std::vector<ModuleImage> images;
        for (const auto& matrix : test.at("matrices")) {
            TransformationState state(5, 4);
            if (!matrix.is_null()) state.AffineMatrix2x3 = matrix.get<std::vector<double>>();
            images.emplace_back(mat, mat, state, 7);
        }
        auto module = factory(1, "", test.at("properties"), nullptr);
        const Json original = test.at("input");
        Json input = original;
        for (int run = 0; run < 2; ++run) {
            bool rejected = false;
            ModuleIO output;
            try { output = module->Process(images, input); }
            catch (const std::invalid_argument&) { rejected = true; }
            if (rejected != test.value("error", false)) throw std::runtime_error(test.at("name").get<std::string>() + " rejection mismatch");
            if (!rejected && (output.ResultList != test.at("expected") || output.ImageList.size() != images.size()))
                throw std::runtime_error(test.at("name").get<std::string>() + " output mismatch: " + output.ResultList.dump());
            if (!rejected) for (size_t i = 0; i < images.size(); ++i)
                if (output.ImageList[i].InstanceId != images[i].InstanceId || output.ImageList[i].ImageObject.data != mat.data)
                    throw std::runtime_error("image changed");
            if (input != original || mat.at<cv::Vec3b>(0, 0)[0] != 7) throw std::runtime_error("upstream mutated");
        }
        std::cout << "PASS " << test.at("name").get<std::string>() << std::endl;
    }
    return 0;
}
