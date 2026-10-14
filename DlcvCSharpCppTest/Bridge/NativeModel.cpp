#include "NativeModel.h"
#include "dlcv_infer.h"

NativeModel::NativeModel(int index)
    : model_(new dlcv_infer::Model(dlcv_infer::CreateModelFromIndex(index))) {}
NativeModel::NativeModel(const std::wstring& path, int device)
    : model_(new dlcv_infer::Model(path, device)) {}
NativeModel::~NativeModel() { delete model_; }
int NativeModel::Index() const { return model_->modelIndex; }
std::string NativeModel::Info() { return model_->GetModelInfo().dump(2); }
std::string NativeModel::DvsInfo() { return model_->GetDvsModelInfo().dump(2); }
