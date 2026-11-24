#include <cstdlib>
#include <cstdio>
#include <QApplication>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QFont>
#include <QGuiApplication>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLibrary>
#include <QMessageBox>
#include <QPlainTextEdit>
#include <QProgressBar>
#include <QPushButton>
#include <QRegularExpression>
#include <QSettings>
#include <QStyleHints>
#include <QTimer>

#ifdef _WIN32
#include <windows.h>
#endif

#include "MainWindow.h"
#include "ImageViewerWidget.h"

namespace {
bool writeFile(const QString& path, const QByteArray& content) {
    QFile file(path);
    return file.open(QIODevice::WriteOnly | QIODevice::NewOnly)
        && file.write(content) == content.size() && file.flush();
}

QString normalizedPath(const QString& path) {
    const QFileInfo info(path);
    QString result = info.canonicalFilePath();
    if (result.isEmpty()) result = QDir(info.absolutePath()).canonicalPath() + "/" + info.fileName();
    result = QDir::cleanPath(result);
#ifdef _WIN32
    result = result.toCaseFolded();
#endif
    return result;
}

bool hasLinkedParent(QString path) {
    for (;;) {
        const QFileInfo info(path);
        if (info.isSymLink()) return true;
#ifdef _WIN32
        const DWORD attributes = GetFileAttributesW(reinterpret_cast<LPCWSTR>(path.utf16()));
        if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT)) return true;
#endif
        const QString parent = info.dir().absolutePath();
        if (parent == path) return false;
        path = parent;
    }
}

QPushButton* findButton(MainWindow& window, const QString& text) {
    for (auto* button : window.findChildren<QPushButton*>()) {
        if (button->text() == text) return button;
    }
    return nullptr;
}

bool readNumber(const QString& text, const QString& label, double& value) {
    const auto match = QRegularExpression(QRegularExpression::escape(label) + ": ([0-9]+(?:\\.[0-9]+)?)")
        .match(text);
    if (!match.hasMatch()) return false;
    value = match.captured(1).toDouble();
    return true;
}
}

int main(int argc, char* argv[]) {
    QApplication app(argc, argv);
    app.setApplicationName("C++测试程序2");
    app.setOrganizationName("dlcv");
    app.setFont(QFont("Microsoft YaHei", 9));
    QGuiApplication::styleHints()->setColorScheme(Qt::ColorScheme::Dark);
    const auto args = app.arguments();
    if (args.size() != 2) return 2;
    QFile configFile(args[1]);
    if (!configFile.open(QIODevice::ReadOnly)) return 2;
    QJsonParseError parseError;
    const auto document = QJsonDocument::fromJson(configFile.readAll(), &parseError);
    if (parseError.error != QJsonParseError::NoError || !document.isObject()) return 2;
    const auto config = document.object();
    const QString output = config.value("output").toString();
    const auto rejectOutput = []() {
        std::fputs("输出路径无效、已存在、包含目录链接或与输入文件重合\n", stderr);
        return 2;
    };
    if (output.isEmpty() || !QFileInfo(output).isAbsolute()
        || !QDir(QFileInfo(output).absolutePath()).exists()) return rejectOutput();
    QStringList inputs{QFileInfo(configFile).absoluteFilePath()};
    for (const auto* field : {"extractModel", "componentModel", "icModel", "noCadModel", "image", "sdkLibrary"}) {
        const QString input = config.value(field).toString();
        if (!input.isEmpty()) inputs.append(input);
    }
    for (const QString& path : {output, output + ".log"}) {
        if (QFileInfo::exists(path) || hasLinkedParent(QDir::cleanPath(QFileInfo(path).absoluteFilePath())))
            return rejectOutput();
        for (const QString& input : inputs) {
            if (normalizedPath(path) == normalizedPath(input)) return rejectOutput();
        }
    }
    QString loadLog;
    QJsonObject sdkReport{{"sdk_library", QJsonValue::Null}, {"workspace_limit", QJsonValue::Null}};
    auto fail = [&](const QString& message, const QString& uiLog = QString()) {
        writeFile(output + ".log", (loadLog + "\n" + uiLog).toUtf8());
        QJsonObject result = sdkReport;
        result.insert("success", false);
        result.insert("error", message);
        result.insert("load_log", loadLog);
        result.insert("ui_log", uiLog);
        writeFile(output, QJsonDocument(result).toJson());
        // 推理线程可能仍在运行，由采集 Job 统一结束子进程。
        std::_Exit(1);
    };
    QLibrary sdkLibrary;
    if (config.contains("workspaceLimit")) {
        const auto limitValue = config.value("workspaceLimit");
        const int limit = limitValue.toInt(0);
        if (!limitValue.isDouble() || limitValue.toDouble() != limit || limit < 1 || limit > 32)
            fail("workspaceLimit 须为1到32的整数");
        const QFileInfo sdkInfo(config.value("sdkLibrary").toString());
        if (!sdkInfo.isAbsolute() || !sdkInfo.isFile()) fail("sdkLibrary 须为有效的SDK动态库绝对路径");
        sdkLibrary.setFileName(sdkInfo.canonicalFilePath());
        if (!sdkLibrary.load()) fail("SDK动态库加载失败: " + sdkLibrary.errorString());
        sdkReport.insert("sdk_library", sdkLibrary.fileName());
#ifdef _WIN32
        using SetWorkspaceLimit = int (__stdcall*)(int);
#else
        using SetWorkspaceLimit = int (*)(int);
#endif
        const auto setWorkspaceLimit = reinterpret_cast<SetWorkspaceLimit>(
            sdkLibrary.resolve("dlcv_set_trt_workspace_limit"));
        if (!setWorkspaceLimit) fail("SDK动态库缺少 dlcv_set_trt_workspace_limit: " + sdkLibrary.errorString());
        const int status = setWorkspaceLimit(limit);
        if (status != 0) fail(QString("设置 workspaceLimit 失败，返回值: %1").arg(status));
        sdkReport.insert("workspace_limit", limit);
    }
    const char* fields[] = {"extractModel", "componentModel", "icModel", "noCadModel", "image"};
    const char* settingsKeys[] = {"LastExtractModelPath", "LastComponentDetectModelPath",
        "LastIcDetectModelPath", "LastNoCadModelPath", "LastImagePath"};
    for (const auto* field : fields) {
        const QFileInfo info(config.value(field).toString());
        if (!info.isAbsolute() || !info.isFile()) fail(QString("输入文件不存在: %1").arg(field));
    }
    const int width = config.value("windowWidth").toInt(2560);
    const int height = config.value("windowHeight").toInt(2560);
    const int overlap = config.value("overlap").toInt(1024);
    if (width < 1 || width > 30000 || height < 1 || height > 30000 || overlap < 0
        || overlap >= width || overlap >= height) fail("滑窗参数超出范围");
    const QString settingsPath = QFileInfo(output).absolutePath() + "/demo2-ui-settings";
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope, settingsPath);
    QSettings::setPath(QSettings::IniFormat, QSettings::SystemScope, settingsPath);
    {
        QSettings settings("dlcv", "DlcvDemoQt2");
        settings.clear();
        for (int i = 0; i < 5; ++i) settings.setValue(settingsKeys[i], config.value(fields[i]).toString());
        settings.setValue("SlidingWindowWidth", width);
        settings.setValue("SlidingWindowHeight", height);
        settings.setValue("SlidingOverlapX", overlap);
        settings.setValue("SlidingOverlapY", overlap);
        settings.sync();
        if (settings.status() != QSettings::NoError) fail("测试设置写入失败");
    }

    MainWindow window;
    ImageViewerWidget* viewer = nullptr;
    for (auto* widget : window.findChildren<QWidget*>()) {
        if ((viewer = dynamic_cast<ImageViewerWidget*>(widget))) break;
    }
    if (!viewer) fail("正式窗口缺少图像显示控件");
    viewer->setShowLabelText(false);
    auto* load = findButton(window, "一键加载四个模型");
    auto* infer = findButton(window, "执行推理");
    auto* log = window.findChild<QPlainTextEdit*>();
    auto* progress = window.findChild<QProgressBar*>();
    if (!load || !infer || !log || !progress) fail("正式窗口缺少推理控件");
    // 前后版本使用相同客户区和 Qt 比例，保留正式标题栏及布局。
    window.resize(1192, 955);
    window.show();

    QTimer timeout;
    timeout.setSingleShot(true);
    QObject::connect(&timeout, &QTimer::timeout, &app, [&]() { fail("模型加载或推理超过30分钟", log->toPlainText()); });
    timeout.start(30 * 60 * 1000);
    QTimer poll;
    poll.setInterval(200);
    enum class Stage { Start, Loading, Inferring, Finished };
    Stage stage = Stage::Start;
    QObject::connect(&poll, &QTimer::timeout, &app, [&]() {
        for (auto* widget : QApplication::topLevelWidgets()) {
            auto* message = qobject_cast<QMessageBox*>(widget);
            if (message && message->isVisible()) fail(message->text(), log->toPlainText());
        }
        const QString text = log->toPlainText();
        if (stage == Stage::Start && load->isEnabled()) {
            stage = Stage::Loading;
            load->click();
        } else if (stage == Stage::Loading && load->isEnabled()) {
            loadLog = text;
            if (!text.contains("四个模型加载完成") || text.contains("加载中断")) fail("四个模型加载失败", text);
            if (!infer->isEnabled()) fail("执行推理按钮未启用", text);
            stage = Stage::Inferring;
            infer->click();
        } else if (stage == Stage::Inferring && infer->isEnabled()) {
            if (text.startsWith("执行推理失败:")) fail("执行推理失败", text);
            if (progress->value() != 100) return;
            QJsonObject result = sdkReport;
            result.insert("success", true);
            result.insert("load_log", loadLog);
            result.insert("ui_log", text);
            const char* labels[] = {"滑窗数量", "元件提取模型合并后目标数", "元件检测模型结果数",
                "IC检测模型结果数", "最终结果数", "推理耗时"};
            const char* keys[] = {"sliding_window_count", "merged_extract_count", "component_result_count",
                "ic_result_count", "final_result_count", "elapsed_ms"};
            for (int i = 0; i < 6; ++i) {
                double value = 0;
                if (!readNumber(text, labels[i], value)) fail(QString("结果日志缺少: %1").arg(labels[i]), text);
                result.insert(keys[i], value);
            }
            if (!writeFile(output + ".log", (loadLog + "\n" + text).toUtf8())
                || !writeFile(output, QJsonDocument(result).toJson())) fail("推理结果写入失败", text);
            stage = Stage::Finished;
            poll.stop();
            timeout.stop();
            window.setWindowTitle("C++测试程序2 - 已完成");
        }
    });
    poll.start();
    return app.exec();
}
