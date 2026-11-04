#pragma once

#include <cstdlib>
#include <QApplication>
#include <QDir>
#include <QFile>
#include <QFileDialog>
#include <QJsonDocument>
#include <QJsonObject>
#include <QPushButton>
#include <QSettings>
#include <QTimer>

namespace model_dialog_test {

inline void prepare(const QString& directory) {
    if (!QDir(directory).exists())
        qFatal("模型筛选测试目录不存在");
    QSettings::setDefaultFormat(QSettings::IniFormat);
    QSettings::setPath(QSettings::IniFormat, QSettings::UserScope,
        QDir::tempPath() + "/model-dialog-test-settings");
    QSettings settings("dlcv", "DlcvDemoQt2");
    settings.clear();
    settings.setValue("LastExtractModelPath", QDir(directory).filePath("筛选样本.dvt"));
}

inline void open(QWidget& window, const QString& output) {
    QTimer::singleShot(30000, qApp, []() { std::exit(0); });
    QTimer::singleShot(700, &window, [&window, output]() {
        QTimer::singleShot(700, &window, [output]() {
            for (QWidget* widget : QApplication::topLevelWidgets()) {
                auto* dialog = qobject_cast<QFileDialog*>(widget);
                if (!dialog || !dialog->isVisible()) continue;
                QFile file(output);
                if (!file.open(QIODevice::WriteOnly)) qFatal("无法写入文件筛选测试结果");
                file.write(QJsonDocument(QJsonObject{
                    {"status", "model-dialog-open"},
                    {"filter", dialog->selectedNameFilter()},
                    {"directory", dialog->directory().absolutePath()}
                }).toJson());
                return;
            }
            qFatal("正式模型选择窗口未打开");
        });
        for (QPushButton* button : window.findChildren<QPushButton*>()) {
            if (button->text() == "浏览...") {
                button->click();
                return;
            }
        }
        qFatal("未找到模型浏览按钮");
    });
}

}
