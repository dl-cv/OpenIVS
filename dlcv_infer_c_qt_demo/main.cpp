#include <QApplication>
#include <QFont>

#include "MainWindow.h"

int main(int argc, char* argv[]) {
    QApplication app(argc, argv);
    app.setApplicationName("C测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));

    MainWindow window;
    window.show();
    return app.exec();
}
