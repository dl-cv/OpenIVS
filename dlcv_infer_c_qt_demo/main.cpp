#include <QApplication>
#include <QFont>

#include <cstdio>
#include <cstring>

#include "DlcvInferApi.h"
#include "MainWindow.h"

int main(int argc, char* argv[]) {
    if (argc == 2 && std::strcmp(argv[1], "--check-c-api-exports") == 0) {
        DlcvInferApi api;
        if (api.load()) {
            std::puts("C API 导出检查通过");
            return 0;
        }
        std::fwprintf(stderr, L"C API 导出检查失败：%ls\n", api.lastError().c_str());
        return 1;
    }

    QApplication app(argc, argv);
    app.setApplicationName("C测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));

    MainWindow window;
    window.show();
    return app.exec();
}
