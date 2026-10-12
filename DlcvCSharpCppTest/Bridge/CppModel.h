#pragma once
class NativeModel;

namespace DlcvCSharpCppBridge {
    public ref class CppModel sealed {
    public:
        CppModel(int modelIndex);
        CppModel(System::String^ modelPath, int device);
        ~CppModel();
        !CppModel();
        property int ModelIndex { int get(); }
        System::String^ GetModelInfo();
        System::String^ GetDvsModelInfo();
    private:
        NativeModel* model_;
        System::Object^ sync_;
        void CheckLoaded();
    };
}
