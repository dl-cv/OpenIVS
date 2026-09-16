#pragma once
class NativeModel;

namespace DlcvCSharpCppBridge {
    public ref class CppModel sealed {
    public:
        CppModel(int modelIndex);
        ~CppModel();
        !CppModel();
        property int ModelIndex { int get(); }
        System::String^ GetModelInfo();
    private:
        NativeModel* model_;
        System::Object^ sync_;
        void CheckLoaded();
    };
}
