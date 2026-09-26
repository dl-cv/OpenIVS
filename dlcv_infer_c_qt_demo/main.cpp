#include <QApplication>
#include <QByteArray>
#include <QFont>
#include <QFontDatabase>
#include <QStringList>

#include <cstdio>
#include <iostream>

#ifdef _WIN32
#include <Windows.h>
#endif

#include "CliRunner.h"
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

}  // namespace

int main(int argc, char* argv[]) {
#ifdef _WIN32
    if (argc > 1) {
        InitializeConsoleForCommandLine();
    }
#endif

    if (argc > 1 && QByteArray(argv[1]) == "ui-test" &&
        qgetenv("QT_QPA_PLATFORM").toLower() != "offscreen") {
        std::cerr << "ui-test requires QT_QPA_PLATFORM=offscreen\n";
        return 2;
    }

    QApplication app(argc, argv);
    app.setApplicationName("C测试程序");
    app.setOrganizationName("dlcv");
    if (argc > 1 && QByteArray(argv[1]) == "ui-test") {
        const int fontId = QFontDatabase::addApplicationFont(QStringLiteral("C:/Windows/Fonts/msyh.ttc"));
        if (fontId < 0 || QFontDatabase::applicationFontFamilies(fontId).isEmpty()) {
            std::cerr << "ui-test: unable to load the system Chinese font\n";
            return 1;
        }
        app.setFont(QFont(QFontDatabase::applicationFontFamilies(fontId).front(), 9));
    } else {
        app.setFont(QFont("Microsoft YaHei", 9));
    }

    const QStringList args = app.arguments();
    if (args.size() > 1) {
        return RunCliCommand(args);
    }

    MainWindow window;
    window.show();
    return app.exec();
}
