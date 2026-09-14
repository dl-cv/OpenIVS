#include <QApplication>
#include <QFileInfo>
#include <QFont>
#include <QStringList>

#include <cstdio>
#include <iostream>

#ifdef _WIN32
#include <Windows.h>
#endif

#include "CliRunner.h"
#include "DisplayResult.h"
#include "ImageViewerWidget.h"
#include "../Test/qt_demo/MaskVisualizationSelfTest.h"
#include "MainWindow.h"

namespace {

#ifdef _WIN32
bool IsUsableStandardHandle(DWORD standardHandle) {
    const HANDLE handle = GetStdHandle(standardHandle);
    if (handle == nullptr || handle == INVALID_HANDLE_VALUE) {
        return false;
    }
    SetLastError(ERROR_SUCCESS);
    const DWORD fileType = GetFileType(handle);
    return fileType != FILE_TYPE_UNKNOWN || GetLastError() == ERROR_SUCCESS;
}

void InitializeConsoleForCommandLine() {
    const bool hasStdout = IsUsableStandardHandle(STD_OUTPUT_HANDLE);
    const bool hasStderr = IsUsableStandardHandle(STD_ERROR_HANDLE);
    if (hasStdout && hasStderr) {
        return;
    }
    if (!AttachConsole(ATTACH_PARENT_PROCESS) && GetLastError() != ERROR_ACCESS_DENIED) {
        return;
    }

    FILE* stream = nullptr;
    if (!hasStdout) {
        freopen_s(&stream, "CONOUT$", "w", stdout);
    }
    if (!hasStderr) {
        freopen_s(&stream, "CONOUT$", "w", stderr);
    }
    std::ios::sync_with_stdio(true);

    DWORD consoleMode = 0;
    if (GetConsoleMode(GetStdHandle(STD_OUTPUT_HANDLE), &consoleMode) ||
        GetConsoleMode(GetStdHandle(STD_ERROR_HANDLE), &consoleMode)) {
        SetConsoleOutputCP(CP_UTF8);
    }
}
#endif

bool ParseMaskSelfTestOutput(const QStringList& args, QString& outputPath, QString& error) {
    bool hasOutput = false;
    for (int i = 2; i < args.size(); ++i) {
        const QString option = args.at(i);
        if (i + 1 >= args.size()) {
            error = QStringLiteral("missing value for %1").arg(option);
            return false;
        }
        const QString value = args.at(++i);
        if (option != QStringLiteral("--output")) {
            error = QStringLiteral("unknown option: %1").arg(option);
            return false;
        }
        if (hasOutput) {
            error = QStringLiteral("duplicate option: --output");
            return false;
        }
        outputPath = QFileInfo(value).absoluteFilePath();
        hasOutput = true;
    }
    if (!hasOutput || outputPath.isEmpty()) {
        error = QStringLiteral("--output is required");
        return false;
    }
    return true;
}

}  // namespace

int main(int argc, char* argv[]) {
#ifdef _WIN32
    if (argc > 1) {
        InitializeConsoleForCommandLine();
    }
#endif

    QApplication app(argc, argv);
    app.setApplicationName("C测试程序");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));

    const QStringList args = app.arguments();
    if (args.size() > 1) {
        if (args.at(1) == QStringLiteral("mask-visualization-selftest")) {
            if (args.contains(QStringLiteral("--help"))) {
                PrintCliHelp(args.at(0));
                return 0;
            }
            QString outputPath;
            QString error;
            if (!ParseMaskSelfTestOutput(args, outputPath, error)) {
                std::cerr << "error: " << error.toUtf8().constData() << "\n";
                PrintCliHelp(args.at(0));
                return 2;
            }
            return RunQtMaskVisualizationSelfTest(outputPath, DisplayObjectResult{});
        }
        return RunCliCommand(args);
    }

    MainWindow window;
    window.show();
    return app.exec();
}
