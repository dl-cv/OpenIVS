#include <QApplication>
#include <QFont>

#include "MainWindow.h"
#include "dlcv_infer.h"

int main(int argc, char* argv[]) {
    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));
    QObject::connect(&app, &QCoreApplication::aboutToQuit, []() {
        dlcv_infer::Utils::FreeAllModels();
    });

    MainWindow w;
    w.show();
    return app.exec();
}
