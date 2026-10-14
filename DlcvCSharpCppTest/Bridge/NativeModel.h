#pragma once
#include <string>
namespace dlcv_infer { class Model; }

// 原生实现独立编译，避免 OpenCV 和模型内部同步类型进入托管编译单元。
class NativeModel final {
public:
    explicit NativeModel(int index);
    NativeModel(const std::wstring& path, int device);
    ~NativeModel();
    NativeModel(const NativeModel&) = delete;
    NativeModel& operator=(const NativeModel&) = delete;
    int Index() const;
    std::string Info();
    std::string DvsInfo();
private:
    dlcv_infer::Model* model_;
};
