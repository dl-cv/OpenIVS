#include <QApplication>
#include <QFont>
#include <QStringList>
#include <exception>
#include <iostream>

#include "ImageViewerWidget.h"
#include "MaskVisualizationSelfTest.h"

int main(int argc, char* argv[]) {
    // 使用离屏平台验证真实控件，不创建桌面窗口。
    qputenv("QT_QPA_PLATFORM", "offscreen");
    QApplication app(argc, argv);
    app.setFont(QFont("Microsoft YaHei", 9));
    const QStringList args = app.arguments();
    const auto printHelp = []() {
        std::cout << "Usage: qt_mask_test --output <pngPath>\n";
    };
    if (args.size() == 2 && args.at(1) == QStringLiteral("--help")) {
        printHelp();
        return 0;
    }
    if (args.size() != 3 || args.at(1) != QStringLiteral("--output")) {
        printHelp();
        return 2;
    }
    try {
#if defined(DLCV_QT_C_MASK_TEST)
        return RunQtMaskVisualizationSelfTest(args.at(2), DisplayObjectResult{});
#elif defined(DLCV_QT_CPP_MASK_TEST)
        return RunQtMaskVisualizationSelfTest(args.at(2),
            dlcv_infer::ObjectResult(0, "", 0.99f, 0.0f, {}, false, cv::Mat()));
#else
#error A Qt mask test project must select its production result type.
#endif
    } catch (const std::exception& error) {
        std::cerr << error.what() << "\n";
        return 1;
    }
}
