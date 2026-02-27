#include <QApplication>
#include <QFont>

#include "MainWindow.h"

int main(int argc, char* argv[]) {
    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));

    MainWindow w;
    w.show();
    return app.exec();
}
