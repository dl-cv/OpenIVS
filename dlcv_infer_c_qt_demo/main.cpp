#include <QApplication>
#include <QFont>

#include "MainWindow.h"
#include "dlcv_infer_c_api.h"

int main(int argc, char* argv[]) {
    QApplication app(argc, argv);
    app.setApplicationName("C测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));

    QObject::connect(&app, &QCoreApplication::aboutToQuit, []() {
        dlcv_infer_cpp_free_all_models_c();
    });

    MainWindow window;
    window.show();
    return app.exec();
}

