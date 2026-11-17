#include <QApplication>
#include <QFont>
#include <QStringList>
#include <exception>
#include <iostream>

#include "ImageViewerWidget.h"
#include "MaskVisualizationSelfTest.h"

#if defined(DLCV_QT_C_MASK_TEST)
#include "ImageFileReader.h"
#include <QFile>
#include <QTextStream>
#include <QTemporaryFile>

int CheckImageFiles(const QString& manifest) {
    QFile cases(manifest);
    if (!cases.open(QIODevice::ReadOnly)) return 2;
    QTextStream lines(&cases);
    QTemporaryFile copy;
    if (!copy.open()) return 2;
    int failures = 0;
    while (!lines.atEnd()) {
        const QStringList fields = lines.readLine().split('\t');
        try {
            QFile input(fields.at(1)), reference(fields.at(2));
            if (!input.open(QIODevice::ReadOnly) || !reference.open(QIODevice::ReadOnly))
                throw std::runtime_error("cannot open image");
            const QByteArray bytes = input.readAll(), expectedBytes = reference.readAll();
            const cv::Mat expected = cv::imdecode(
                std::vector<uchar>(expectedBytes.begin(), expectedBytes.end()), cv::IMREAD_UNCHANGED);
            copy.resize(0);
            copy.seek(0);
            copy.write(bytes);
            copy.flush();
            copy.seek(0);
            for (const QByteArray& data : {bytes, copy.readAll()}) {
                const cv::Mat actual = ImageFileReader::Decode(data);
                if (actual.empty() || actual.type() != expected.type() || actual.size() != expected.size()
                    || cv::norm(actual, expected, cv::NORM_INF) > fields.at(3).toInt())
                    throw std::runtime_error("pixel/type/orientation mismatch");
            }
            std::cout << "PASS\t" << fields.at(0).toStdString() << "\n";
        } catch (const std::exception& error) {
            ++failures;
            std::cout << "FAIL\t" << fields.at(0).toStdString() << "\t" << error.what() << "\n";
        }
    }
    return failures == 0 ? 0 : 1;
}
#endif

int main(int argc, char* argv[]) {
    // 使用离屏平台验证真实控件，不创建桌面窗口。
    qputenv("QT_QPA_PLATFORM", "offscreen");
    QApplication app(argc, argv);
    app.setFont(QFont("Microsoft YaHei", 9));
    const QStringList args = app.arguments();
#if defined(DLCV_QT_C_MASK_TEST)
    if (args.size() == 3 && args.at(1) == "--image-cases") return CheckImageFiles(args.at(2));
#endif
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
