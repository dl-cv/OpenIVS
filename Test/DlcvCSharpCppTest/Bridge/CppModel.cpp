#include "CppModel.h"
#include "NativeModel.h"
#include <exception>
#include <msclr/lock.h>

using namespace System;
using namespace System::Runtime::InteropServices;
using namespace System::Text;

namespace {
    String^ FromUtf8(const std::string& value) {
        if (value.empty()) return String::Empty;
        auto bytes = gcnew array<Byte>(static_cast<int>(value.size()));
        Marshal::Copy(IntPtr(const_cast<char*>(value.data())), bytes, 0, bytes->Length);
        return (gcnew UTF8Encoding(false, true))->GetString(bytes);
    }
}

namespace DlcvCSharpCppBridge {
    CppModel::CppModel(int modelIndex) : model_(nullptr), sync_(gcnew Object()) {
        if (modelIndex < 0) throw gcnew ArgumentOutOfRangeException("modelIndex");
        try { model_ = new NativeModel(modelIndex); }
        catch (const std::exception& ex) {
            throw gcnew InvalidOperationException(FromUtf8(ex.what()));
        }
    }

    CppModel::~CppModel() {
        msclr::lock guard(sync_);
        this->!CppModel();
    }

    CppModel::!CppModel() {
        NativeModel* released = model_;
        model_ = nullptr;
        delete released;
    }

    void CppModel::CheckLoaded() {
        if (model_ == nullptr) throw gcnew ObjectDisposedException("CppModel", "C++ 模型已释放");
    }

    int CppModel::ModelIndex::get() {
        msclr::lock guard(sync_);
        CheckLoaded();
        return model_->Index();
    }

    String^ CppModel::GetModelInfo() {
        msclr::lock guard(sync_);
        CheckLoaded();
        try { return FromUtf8(model_->Info()); }
        catch (const std::exception& ex) {
            throw gcnew InvalidOperationException(FromUtf8(ex.what()));
        }
    }
}
