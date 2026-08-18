#include <chrono>
#include <exception>
#include <iomanip>
#include <iostream>
#include <memory>
#include <string>

#include <QApplication>
#include <QFileInfo>
#include <QFont>
#include <QString>

#ifdef _WIN32
#include <windows.h>
#endif

#include "MainWindow.h"
#include "dlcv_infer.h"

namespace {

void initializeCommandLineOutput() {
#ifdef _WIN32
    SetConsoleOutputCP(65001);
    SetConsoleCP(65001);
#endif
}

void printUsage() {
    std::cout << "用法:\n";
    std::cout << "  C++测试程序2.exe --load-three-models <元件提取模型> <元件检测模型> <IC检测模型>\n\n";
    std::cout << "说明:\n";
    std::cout << "  三个模型按参数顺序串行加载，不加载预热模型，也不执行额外推理。\n";
    std::cout << "  加载过程中实时输出每个模型的耗时，完成后输出总耗时。\n";
}

bool isHelpArgument(const std::string& argument) {
    return argument == "--help" || argument == "-h" || argument == "/?";
}

void loadOneModel(
    const char* displayName,
    const QString& modelPath,
    std::unique_ptr<dlcv_infer::Model>& model,
    double& elapsedSeconds) {
    std::cout << "开始加载" << displayName << ": " << modelPath.toLocal8Bit().constData() << std::endl;
    const auto start = std::chrono::steady_clock::now();
    model = std::make_unique<dlcv_infer::Model>(modelPath.toStdWString(), 0);
    const auto end = std::chrono::steady_clock::now();
    elapsedSeconds = std::chrono::duration<double>(end - start).count();
    std::cout << displayName << "加载完成，耗时 " << std::fixed << std::setprecision(2) << elapsedSeconds << " 秒"
              << std::endl;
}

int runCommandLine(int argc, char* argv[]) {
    initializeCommandLineOutput();

    if (argc == 2 && isHelpArgument(argv[1])) {
        printUsage();
        return 0;
    }

    if (argc != 5 || std::string(argv[1]) != "--load-three-models") {
        std::cerr << "命令行参数错误。\n";
        printUsage();
        return 1;
    }

    const QString extractPath = QString::fromLocal8Bit(argv[2]);
    const QString componentPath = QString::fromLocal8Bit(argv[3]);
    const QString icPath = QString::fromLocal8Bit(argv[4]);

    if (!QFileInfo::exists(extractPath)) {
        std::cerr << "元件提取模型文件不存在: " << argv[2] << std::endl;
        return 1;
    }
    if (!QFileInfo::exists(componentPath)) {
        std::cerr << "元件检测模型文件不存在: " << argv[3] << std::endl;
        return 1;
    }
    if (!QFileInfo::exists(icPath)) {
        std::cerr << "IC检测模型文件不存在: " << argv[4] << std::endl;
        return 1;
    }

    try {
        std::unique_ptr<dlcv_infer::Model> extractModel;
        std::unique_ptr<dlcv_infer::Model> componentModel;
        std::unique_ptr<dlcv_infer::Model> icModel;
        double extractSeconds = 0.0;
        double componentSeconds = 0.0;
        double icSeconds = 0.0;
        loadOneModel("元件提取模型", extractPath, extractModel, extractSeconds);
        loadOneModel("元件检测模型", componentPath, componentModel, componentSeconds);
        loadOneModel("IC检测模型", icPath, icModel, icSeconds);
        const double totalSeconds = extractSeconds + componentSeconds + icSeconds;
        std::cout << "三个模型加载完成，总耗时 " << std::fixed << std::setprecision(2) << totalSeconds << " 秒"
                  << std::endl;
        (void)extractModel;
        (void)componentModel;
        (void)icModel;
        return 0;
    } catch (const std::exception& ex) {
        std::cerr << "加载失败: " << ex.what() << std::endl;
        return 2;
    }
}

}  // namespace

int main(int argc, char* argv[]) {
    if (argc > 1) {
        return runCommandLine(argc, argv);
    }

    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序2");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));
    QObject::connect(&app, &QCoreApplication::aboutToQuit, []() {
        dlcv_infer::Utils::FreeAllModels();
    });

    MainWindow w;
    w.show();
    return app.exec();
}
