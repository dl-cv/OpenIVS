#include <QApplication>
#include <QFont>

#include <iostream>

#include "MainWindow.h"
#include "dlcv_infer.h"

#ifdef _WIN32
#include <Windows.h>
#else
#include <dlfcn.h>
#endif

static std::string GetCppDllPath() {
#ifdef _WIN32
    char path[MAX_PATH];
    HMODULE hModule = nullptr;
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           reinterpret_cast<LPCSTR>(&dlcv_infer::Utils::FreeAllModels), &hModule)) {
        if (GetModuleFileNameA(hModule, path, MAX_PATH) > 0) {
            return path;
        }
    }
#else
    Dl_info info;
    if (dladdr(reinterpret_cast<void*>(&dlcv_infer::Utils::FreeAllModels), &info) && info.dli_fname) {
        return info.dli_fname;
    }
#endif
    return "";
}

int main(int argc, char* argv[]) {
    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));
    QObject::connect(&app, &QCoreApplication::aboutToQuit, []() {
        dlcv_infer::Utils::FreeAllModels();
    });

    std::cout << "[dlcv_infer_cpp_dll] " << GetCppDllPath() << std::endl;

    MainWindow w;
    w.show();
    return app.exec();
}
