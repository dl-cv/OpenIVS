#include "MainWindow.h"

#include <algorithm>
#include <cmath>
#include <exception>
#include <functional>

#include <QCloseEvent>
#include <QCoreApplication>
#include <QDialog>
#include <QFileDialog>
#include <QFileInfo>
#include <QGuiApplication>
#include <QHBoxLayout>
#include <QLabel>
#include <QLineEdit>
#include <QMainWindow>
#include <QMessageBox>
#include <QMetaObject>
#include <QPlainTextEdit>
#include <QPointer>
#include <QProgressBar>
#include <QPushButton>
#include <QScreen>
#include <QSpinBox>
#include <QSplitter>
#include <QTimer>
#include <QVBoxLayout>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "../dlcv_infer_cpp_qt_demo/ImageViewerWidget.h"

namespace {
using json = nlohmann::json;

QString jsonToQStringPretty(const json& obj, int indent = 2) {
    return QString::fromUtf8(obj.dump(indent).c_str());
}

void applyDialogInitialPath(QFileDialog& dialog, const QString& path) {
    if (path.isEmpty()) {
        return;
    }
    QFileInfo fileInfo(path);
    dialog.setDirectory(fileInfo.absolutePath());
    dialog.selectFile(fileInfo.fileName());
}

QString buildModelInfoText(const QString& title, const json& modelInfo) {
    if (modelInfo.contains("model_info")) {
        return title + "\n" + jsonToQStringPretty(modelInfo["model_info"], 2);
    }
    return title + "\n" + jsonToQStringPretty(modelInfo, 2);
}
}

MainWindow::MainWindow(QWidget* parent) : QMainWindow(parent) {
    setupUi();
    bindSignals();
    initializeDevicesAsync();
}

void MainWindow::closeEvent(QCloseEvent* event) {
    settings_.setValue("Geometry", saveGeometry());
    settings_.setValue("WindowState", saveState());
    stopPressureTest();
    if (inferenceThread_.joinable()) {
        inferenceThread_.join();
    }
    model1_.reset();
    model2_.reset();
    dlcv_infer::Utils::FreeAllModels();
    QMainWindow::closeEvent(event);
}

void MainWindow::setupUi() {
    setWindowTitle("C++测试程序3");
    setMinimumSize(1200, 900);

    QWidget* centralWidget = new QWidget(this);
    auto* rootLayout = new QVBoxLayout(centralWidget);
    rootLayout->setContentsMargins(12, 12, 12, 12);
    rootLayout->setSpacing(8);

    editModel1Path_ = new QLineEdit(this);
    editModel2Path_ = new QLineEdit(this);
    editImagePath_ = new QLineEdit(this);

    buttonBrowseModel1_ = new QPushButton("浏览...", this);
    buttonBrowseModel2_ = new QPushButton("浏览...", this);
    buttonBrowseImage_ = new QPushButton("浏览...", this);
    buttonLoadModel1_ = new QPushButton("加载模型1", this);
    buttonLoadModel2_ = new QPushButton("加载模型2", this);
    buttonGetModelInfo_ = new QPushButton("获取模型信息", this);
    buttonInfer_ = new QPushButton("执行推理", this);
    buttonPressureTest_ = new QPushButton("速度测试", this);
    buttonReleaseModels_ = new QPushButton("释放模型", this);

    labelFixedCrop_ = new QLabel("固定裁图大小: 128 x 192", this);
    labelModel2Threads_ = new QLabel("模型2线程数(1-32)", this);
    labelStatus_ = new QLabel("状态: 空闲", this);

    spinModel2Threads_ = new QSpinBox(this);
    spinModel2Threads_->setRange(1, 32);
    spinModel2Threads_->setValue(4);

    progressBar_ = new QProgressBar(this);
    progressBar_->setRange(0, 100);
    progressBar_->setValue(0);

    constexpr int kControlHeight = 34;
    auto setupButton = [kControlHeight](QPushButton* b, int minWidth) {
        b->setMinimumWidth(minWidth);
        b->setFixedHeight(kControlHeight);
    };
    setupButton(buttonBrowseModel1_, 90);
    setupButton(buttonBrowseModel2_, 90);
    setupButton(buttonBrowseImage_, 90);
    setupButton(buttonLoadModel1_, 120);
    setupButton(buttonLoadModel2_, 120);
    setupButton(buttonGetModelInfo_, 120);
    setupButton(buttonInfer_, 120);
    setupButton(buttonPressureTest_, 120);
    setupButton(buttonReleaseModels_, 120);

    editModel1Path_->setPlaceholderText("请选择模型1路径");
    editModel2Path_->setPlaceholderText("请选择模型2路径");
    editImagePath_->setPlaceholderText("请选择图片路径");
    editModel1Path_->setText(settings_.value("LastModel1Path").toString());
    editModel2Path_->setText(settings_.value("LastModel2Path").toString());
    imagePath_ = settings_.value("LastImagePath").toString();
    editImagePath_->setText(imagePath_);
    editModel1Path_->setMinimumHeight(kControlHeight);
    editModel2Path_->setMinimumHeight(kControlHeight);
    editImagePath_->setMinimumHeight(kControlHeight);
    spinModel2Threads_->setMinimumHeight(kControlHeight);

    auto* row1 = new QHBoxLayout();
    row1->setSpacing(8);
    row1->addWidget(new QLabel("模型1路径(定位)", this));
    row1->addWidget(editModel1Path_, 1);
    row1->addWidget(buttonBrowseModel1_);
    row1->addWidget(buttonLoadModel1_);

    auto* row2 = new QHBoxLayout();
    row2->setSpacing(8);
    row2->addWidget(new QLabel("模型2路径(识别)", this));
    row2->addWidget(editModel2Path_, 1);
    row2->addWidget(buttonBrowseModel2_);
    row2->addWidget(buttonLoadModel2_);

    auto* row3 = new QHBoxLayout();
    row3->setSpacing(8);
    row3->addWidget(new QLabel("图片路径", this));
    row3->addWidget(editImagePath_, 1);
    row3->addWidget(buttonBrowseImage_);
    row3->addWidget(buttonInfer_);
    row3->addWidget(buttonPressureTest_);

    auto* row4 = new QHBoxLayout();
    row4->setSpacing(8);
    row4->addWidget(labelFixedCrop_);
    row4->addStretch(1);
    row4->addWidget(labelModel2Threads_);
    row4->addWidget(spinModel2Threads_);
    row4->addWidget(buttonGetModelInfo_);
    row4->addWidget(buttonReleaseModels_);

    auto* row5 = new QHBoxLayout();
    row5->setSpacing(8);
    row5->addWidget(new QLabel("推理进度", this));
    row5->addWidget(progressBar_, 1);
    row5->addWidget(labelStatus_, 1);

    outputText_ = new QPlainTextEdit(this);
    outputText_->setReadOnly(true);
    imageViewer_ = new ImageViewerWidget(this);
    imageViewer_->setShowStatusText(false);
    imageViewer_->setShowVisualization(true);

    auto* splitter = new QSplitter(Qt::Horizontal, this);
    splitter->addWidget(outputText_);
    splitter->addWidget(imageViewer_);
    splitter->setSizes({380, 780});
    splitter->setStretchFactor(0, 0);
    splitter->setStretchFactor(1, 1);

    rootLayout->addLayout(row1);
    rootLayout->addLayout(row2);
    rootLayout->addLayout(row3);
    rootLayout->addLayout(row4);
    rootLayout->addLayout(row5);
    rootLayout->addWidget(splitter, 1);
    setCentralWidget(centralWidget);

    if (settings_.contains("Geometry")) {
        restoreGeometry(settings_.value("Geometry").toByteArray());
        restoreState(settings_.value("WindowState").toByteArray());
        bool visible = false;
        const QRect geo = frameGeometry();
        for (QScreen* s : QGuiApplication::screens()) {
            if (s->availableGeometry().intersects(geo)) {
                visible = true;
                break;
            }
        }
        if (!visible) {
            if (QScreen* s = QGuiApplication::primaryScreen()) {
                move(s->availableGeometry().center() - rect().center());
            }
        }
    } else if (QScreen* s = QGuiApplication::primaryScreen()) {
        move(s->availableGeometry().center() - rect().center());
    }
}

void MainWindow::bindSignals() {
    connect(buttonBrowseModel1_, &QPushButton::clicked, this, &MainWindow::onBrowseModel1);
    connect(buttonBrowseModel2_, &QPushButton::clicked, this, &MainWindow::onBrowseModel2);
    connect(buttonBrowseImage_, &QPushButton::clicked, this, &MainWindow::onBrowseImage);
    connect(buttonLoadModel1_, &QPushButton::clicked, this, &MainWindow::onLoadModel1);
    connect(buttonLoadModel2_, &QPushButton::clicked, this, &MainWindow::onLoadModel2);
    connect(buttonGetModelInfo_, &QPushButton::clicked, this, &MainWindow::onGetModelInfo);
    connect(buttonInfer_, &QPushButton::clicked, this, &MainWindow::onInfer);
    connect(buttonPressureTest_, &QPushButton::clicked, this, &MainWindow::onPressureTestToggle);
    connect(buttonReleaseModels_, &QPushButton::clicked, this, &MainWindow::onReleaseModels);
}

void MainWindow::initializeDevicesAsync() {
    selectedDeviceId_ = -1;
    selectedDeviceName_ = "CPU";
    appendLog("设备自动选择: CPU");

    const QPointer<MainWindow> self(this);
    std::thread([self]() {
        std::vector<std::pair<QString, int>> devices;
        QString warning;
        dlcv_infer::Utils::KeepMaxClock();
        try {
            const json gpuInfo = dlcv_infer::Utils::GetGpuInfo();
            if (gpuInfo.contains("code") && gpuInfo["code"].is_number_integer() && gpuInfo["code"].get<int>() == 0
                && gpuInfo.contains("devices") && gpuInfo["devices"].is_array()) {
                for (const auto& d : gpuInfo["devices"]) {
                    if (!d.is_object()) {
                        continue;
                    }
                    int id = d.contains("device_id") ? d["device_id"].get<int>() : -1;
                    std::string name = d.contains("device_name") ? d["device_name"].get<std::string>() : std::string{};
                    devices.push_back({QString::fromUtf8(name.c_str()), id});
                }
            } else {
                warning = QString::fromUtf8(gpuInfo.dump(2).c_str());
            }
        } catch (const std::exception& e) {
            warning = QString::fromLocal8Bit(e.what());
        }

        QMetaObject::invokeMethod(
            QCoreApplication::instance(),
            [self, devices, warning]() {
                if (self.isNull()) {
                    return;
                }
                if (!devices.empty()) {
                    self->selectedDeviceName_ = devices.front().first;
                    self->selectedDeviceId_ = devices.front().second;
                    self->appendLog(
                        QString("设备自动选择: %1 (id=%2)").arg(self->selectedDeviceName_).arg(self->selectedDeviceId_));
                }
                if (!warning.isEmpty()) {
                    self->appendLog("GPU信息获取失败:\n" + warning);
                }
            },
            Qt::QueuedConnection);
    }).detach();
}

int MainWindow::selectedDeviceId() const {
    return selectedDeviceId_;
}

bool MainWindow::ensureModel1Loaded() const {
    if (model1_) {
        return true;
    }
    QMessageBox::warning(const_cast<MainWindow*>(this), "提示", "请先加载模型1！");
    return false;
}

bool MainWindow::ensureModel2Loaded() const {
    if (model2_) {
        return true;
    }
    QMessageBox::warning(const_cast<MainWindow*>(this), "提示", "请先加载模型2！");
    return false;
}

bool MainWindow::ensureImageSelected() const {
    if (!imagePath_.isEmpty()) {
        return true;
    }
    QMessageBox::warning(const_cast<MainWindow*>(this), "提示", "请先选择图片文件！");
    return false;
}

bool MainWindow::loadCurrentImage(cv::Mat& bgrImage, cv::Mat& rgbImage) const {
    bgrImage = cv::imread(imagePath_.toLocal8Bit().toStdString(), cv::IMREAD_COLOR);
    if (bgrImage.empty()) {
        return false;
    }
    cv::cvtColor(bgrImage, rgbImage, cv::COLOR_BGR2RGB);
    return true;
}

void MainWindow::appendLog(const QString& text) {
    const QString old = outputText_->toPlainText();
    outputText_->setPlainText(old.isEmpty() ? text : (old + "\n" + text));
}

void MainWindow::setStatus(const QString& text, int progressValue) {
    progressBar_->setValue(std::max(0, std::min(100, progressValue)));
    labelStatus_->setText("状态: " + text);
}

void MainWindow::reportError(const QString& title, const QString& detail) {
    appendLog(title + "\n" + detail);
    QMessageBox::critical(this, "错误", title + ": " + detail);
}

void MainWindow::setControlsEnabled(bool enabled) {
    buttonBrowseModel1_->setEnabled(enabled);
    buttonBrowseModel2_->setEnabled(enabled);
    buttonBrowseImage_->setEnabled(enabled);
    buttonLoadModel1_->setEnabled(enabled);
    buttonLoadModel2_->setEnabled(enabled);
    buttonGetModelInfo_->setEnabled(enabled);
    buttonInfer_->setEnabled(enabled);
    spinModel2Threads_->setEnabled(enabled);
    buttonReleaseModels_->setEnabled(enabled);
}

void MainWindow::onBrowseModel1() {
    QFileDialog dialog(this, "选择模型1");
    dialog.setNameFilter("AI模型 (*.dvt *.dvo *.dvp *.dvst *.dvso *.dvsp);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);
    applyDialogInitialPath(dialog, editModel1Path_->text().trimmed());
    if (dialog.exec() != QDialog::Accepted) {
        return;
    }
    editModel1Path_->setText(dialog.selectedFiles().front());
    onLoadModel1();
}

void MainWindow::onBrowseModel2() {
    QFileDialog dialog(this, "选择模型2");
    dialog.setNameFilter("AI模型 (*.dvt *.dvo *.dvp *.dvst *.dvso *.dvsp);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);
    applyDialogInitialPath(dialog, editModel2Path_->text().trimmed());
    if (dialog.exec() != QDialog::Accepted) {
        return;
    }
    editModel2Path_->setText(dialog.selectedFiles().front());
    onLoadModel2();
}

void MainWindow::onBrowseImage() {
    QFileDialog dialog(this, "选择图片文件");
    dialog.setNameFilter("图片文件 (*.jpg *.jpeg *.png *.bmp *.gif *.tiff *.tif);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);
    applyDialogInitialPath(dialog, editImagePath_->text().trimmed());
    if (dialog.exec() != QDialog::Accepted) {
        return;
    }
    imagePath_ = dialog.selectedFiles().front();
    editImagePath_->setText(imagePath_);
    onInfer();
}

void MainWindow::onLoadModel1() {
    stopPressureTest();
    if (inferenceRunning_.load()) {
        QMessageBox::information(this, "提示", "当前正在推理，请稍后再加载模型。");
        return;
    }
    const QString modelPath = editModel1Path_->text().trimmed();
    if (modelPath.isEmpty()) {
        QMessageBox::warning(this, "提示", "请先选择模型1路径。");
        return;
    }
    try {
        model1_ = std::make_unique<dlcv_infer::Model>(modelPath.toStdWString(), selectedDeviceId());
        settings_.setValue("LastModel1Path", modelPath);
        appendLog("模型1加载成功:\n" + modelPath);
        onGetModelInfo();
    } catch (const std::exception& e) {
        model1_.reset();
        reportError("模型1加载失败", QString::fromLocal8Bit(e.what()));
    }
}

void MainWindow::onLoadModel2() {
    stopPressureTest();
    if (inferenceRunning_.load()) {
        QMessageBox::information(this, "提示", "当前正在推理，请稍后再加载模型。");
        return;
    }
    const QString modelPath = editModel2Path_->text().trimmed();
    if (modelPath.isEmpty()) {
        QMessageBox::warning(this, "提示", "请先选择模型2路径。");
        return;
    }
    try {
        model2_ = std::make_unique<dlcv_infer::Model>(modelPath.toStdWString(), selectedDeviceId());
        settings_.setValue("LastModel2Path", modelPath);
        appendLog("模型2加载成功:\n" + modelPath);
        onGetModelInfo();
    } catch (const std::exception& e) {
        model2_.reset();
        reportError("模型2加载失败", QString::fromLocal8Bit(e.what()));
    }
}

void MainWindow::onGetModelInfo() {
    if (!model1_ && !model2_) {
        QMessageBox::warning(this, "提示", "请先加载模型1或模型2！");
        return;
    }

    QStringList sections;
    try {
        if (model1_) {
            sections.push_back(buildModelInfoText("模型1信息:", model1_->GetModelInfo()));
        }
        if (model2_) {
            sections.push_back(buildModelInfoText("模型2信息:", model2_->GetModelInfo()));
        }
    } catch (const std::exception& e) {
        reportError("获取模型信息失败", QString::fromLocal8Bit(e.what()));
        return;
    }

    outputText_->setPlainText(sections.join("\n\n"));
}

void MainWindow::onInfer() {
    if (pressureRunning_.load()) {
        QMessageBox::information(this, "提示", "速度测试运行中，请先停止。");
        return;
    }
    if (inferenceRunning_.exchange(true)) {
        QMessageBox::information(this, "提示", "当前正在推理。");
        return;
    }
    imagePath_ = editImagePath_->text().trimmed();
    if (!ensureModel1Loaded() || !ensureModel2Loaded() || !ensureImageSelected()) {
        inferenceRunning_.store(false);
        return;
    }
    settings_.setValue("LastImagePath", imagePath_);
    if (inferenceThread_.joinable()) {
        inferenceThread_.join();
    }

    cv::Mat bgrImage;
    cv::Mat rgbImage;
    if (!loadCurrentImage(bgrImage, rgbImage)) {
        inferenceRunning_.store(false);
        reportError("推理失败", "图像解码失败！");
        return;
    }

    setControlsEnabled(false);
    setStatus("读取图片", 5);
    const int model2Threads = spinModel2Threads_->value();
    const QString imagePath = imagePath_;
    const QString model1Path = editModel1Path_->text().trimmed();
    const QString model2Path = editModel2Path_->text().trimmed();
    dlcv_infer::Model* model1Ptr = model1_.get();
    dlcv_infer::Model* model2Ptr = model2_.get();

    inferenceThread_ = std::thread([this, bgrImage, rgbImage, model2Threads, imagePath, model1Path, model2Path, model1Ptr, model2Ptr]() {
        QString errorTitle;
        QString errorDetail;
        PipelineRunResult runResult;
        double elapsedMs = 0.0;
        try {
            const auto start = std::chrono::steady_clock::now();
            runResult = runPipeline(
                rgbImage,
                *model1Ptr,
                *model2Ptr,
                model2Threads,
                [this](const PipelineProgressInfo& p) {
                    QMetaObject::invokeMethod(
                        this,
                        [this, p]() { setStatus(p.stage, p.percent); },
                        Qt::QueuedConnection);
                });
            const auto end = std::chrono::steady_clock::now();
            elapsedMs = std::chrono::duration<double, std::milli>(end - start).count();
        } catch (const std::exception& e) {
            errorTitle = "推理失败";
            errorDetail = QString::fromLocal8Bit(e.what());
        }

        QMetaObject::invokeMethod(
            this,
            [this, bgrImage, imagePath, model1Path, model2Path, runResult, elapsedMs, errorTitle, errorDetail]() {
                inferenceRunning_.store(false);
                setControlsEnabled(true);

                if (!errorTitle.isEmpty()) {
                    setStatus("空闲", 0);
                    reportError(errorTitle, errorDetail);
                    return;
                }

                currentBgrImage_ = bgrImage;
                imageViewer_->setImageAndResults(currentBgrImage_, runResult.finalObjects);

                QString text;
                text += QString("图片: %1\n").arg(imagePath);
                text += QString("模型1: %1\n").arg(model1Path);
                text += QString("模型2: %1\n").arg(model2Path);
                text += QString("固定裁图大小: %1 x %2\n").arg(kFixedCropWidth).arg(kFixedCropHeight);
                text += QString("模型1目标数: %1\n").arg(runResult.model1ObjectCount);
                text += QString("有效裁图数: %1\n").arg(runResult.cropCount);
                text += QString("模型2最大Batch: %1\n").arg(runResult.model2BatchLimit);
                text += QString("模型2线程数: %1\n").arg(runResult.model2ThreadCount);
                text += QString("最终结果数: %1\n").arg(runResult.finalResultCount);
                text += QString("推理耗时: %1 ms\n").arg(elapsedMs, 0, 'f', 2);

                auto buildObjectLocationText = [](const dlcv_infer::ObjectResult& obj) -> QString {
                    if (!obj.withBbox || obj.bbox.size() < 4) {
                        return "rect=(N/A)";
                    }

                    const bool isRotated = obj.withAngle || obj.bbox.size() >= 5;
                    if (isRotated) {
                        return QString("rbox=(cx=%1, cy=%2, w=%3, h=%4, angle=%5)")
                            .arg(obj.bbox[0], 0, 'f', 1)
                            .arg(obj.bbox[1], 0, 'f', 1)
                            .arg(obj.bbox[2], 0, 'f', 1)
                            .arg(obj.bbox[3], 0, 'f', 1)
                            .arg(obj.angle, 0, 'f', 3);
                    }

                    return QString("rect=(%1, %2, %3, %4)")
                        .arg(obj.bbox[0], 0, 'f', 1)
                        .arg(obj.bbox[1], 0, 'f', 1)
                        .arg(obj.bbox[2], 0, 'f', 1)
                        .arg(obj.bbox[3], 0, 'f', 1);
                };

                if (runResult.finalObjects.empty()) {
                    text += "\n未检测到最终结果。\n";
                } else {
                    text += "\n结果明细:\n";
                    for (int i = 0; i < static_cast<int>(runResult.finalObjects.size()); ++i) {
                        const auto& obj = runResult.finalObjects[i];
                        text += QString("[%1] %2  score=%3  %4\n")
                                    .arg(i + 1)
                                    .arg(QString::fromLocal8Bit(obj.categoryName.c_str()))
                                    .arg(obj.score, 0, 'f', 2)
                                    .arg(buildObjectLocationText(obj));
                    }
                }

                if (!runResult.logs.isEmpty()) {
                    text += "\n日志:\n";
                    text += runResult.logs.join("\n");
                    text += "\n";
                }
                outputText_->setPlainText(text);
                setStatus("推理完成", 100);
            },
            Qt::QueuedConnection);
    });
}

void MainWindow::onPressureTestToggle() {
    if (pressureRunning_.load()) {
        stopPressureTest();
    } else {
        startPressureTest();
    }
}

void MainWindow::onReleaseModels() {
    stopPressureTest();
    if (inferenceRunning_.load()) {
        QMessageBox::information(this, "提示", "当前正在推理，暂不能释放模型。");
        return;
    }
    model1_.reset();
    model2_.reset();
    dlcv_infer::Utils::FreeAllModels();
    appendLog("模型已释放");
}

void MainWindow::onPressureTick() {
    updatePressureStatistics();
}

void MainWindow::startPressureTest() {
    if (pressureRunning_.load()) {
        return;
    }
    if (inferenceRunning_.load()) {
        QMessageBox::information(this, "提示", "当前正在推理，请稍后。");
        return;
    }
    if (!ensureModel1Loaded() || !ensureModel2Loaded() || !ensureImageSelected()) {
        return;
    }

    cv::Mat bgrImage;
    cv::Mat rgbImage;
    if (!loadCurrentImage(bgrImage, rgbImage)) {
        reportError("启动速度测试失败", "图像解码失败！");
        return;
    }

    if (inferenceThread_.joinable()) {
        inferenceThread_.join();
    }
    if (pressureThread_.joinable()) {
        pressureThread_.join();
    }

    pressureRgbImage_ = rgbImage;
    pressureThreadSnapshot_ = spinModel2Threads_->value();
    pressureStopRequested_.store(false);
    pressureRuns_.store(0);
    pressureTotalLatencyUs_.store(0);
    pressureLastResultCount_.store(0);
    pressureLastRuns_ = 0;
    pressureStartTime_ = std::chrono::steady_clock::now();
    pressureLastTickTime_ = pressureStartTime_;
    {
        std::lock_guard<std::mutex> lock(pressureErrorMutex_);
        pressureErrorDetail_.clear();
    }

    pressureRunning_.store(true);
    buttonPressureTest_->setText("停止");
    setControlsEnabled(false);
    if (pressureTimer_ == nullptr) {
        pressureTimer_ = new QTimer(this);
        pressureTimer_->setInterval(500);
        connect(pressureTimer_, &QTimer::timeout, this, &MainWindow::onPressureTick);
    }
    pressureTimer_->start();

    dlcv_infer::Model* model1Ptr = model1_.get();
    dlcv_infer::Model* model2Ptr = model2_.get();
    const cv::Mat rgb = pressureRgbImage_;
    const int model2Threads = pressureThreadSnapshot_;

    pressureThread_ = std::thread([this, model1Ptr, model2Ptr, rgb, model2Threads]() {
        while (!pressureStopRequested_.load()) {
            try {
                const auto t0 = std::chrono::high_resolution_clock::now();
                const PipelineRunResult result = runPipeline(
                    rgb,
                    *model1Ptr,
                    *model2Ptr,
                    model2Threads,
                    [](const PipelineProgressInfo&) {});
                const auto t1 = std::chrono::high_resolution_clock::now();
                const long long us = std::chrono::duration_cast<std::chrono::microseconds>(t1 - t0).count();
                pressureRuns_.fetch_add(1);
                pressureTotalLatencyUs_.fetch_add(us);
                pressureLastResultCount_.store(result.finalResultCount);
            } catch (const std::exception& e) {
                {
                    std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                    pressureErrorDetail_ = QString::fromLocal8Bit(e.what());
                }
                pressureStopRequested_.store(true);
                QMetaObject::invokeMethod(
                    this,
                    [this]() {
                        stopPressureTest();
                        QString detail;
                        {
                            std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                            detail = pressureErrorDetail_;
                        }
                        reportError("速度测试过程中发生错误", detail);
                    },
                    Qt::QueuedConnection);
                return;
            }
        }
    });
}

void MainWindow::stopPressureTest() {
    if (!pressureRunning_.load()) {
        return;
    }
    pressureStopRequested_.store(true);
    if (pressureTimer_ != nullptr) {
        pressureTimer_->stop();
    }
    if (pressureThread_.joinable()) {
        pressureThread_.join();
    }
    pressureRunning_.store(false);
    buttonPressureTest_->setText("速度测试");
    setControlsEnabled(!inferenceRunning_.load());
    setStatus("空闲", 0);
}

void MainWindow::updatePressureStatistics() {
    if (!pressureRunning_.load()) {
        return;
    }
    const auto now = std::chrono::steady_clock::now();
    const double elapsedSeconds = std::chrono::duration<double>(now - pressureStartTime_).count();
    const long long runs = pressureRuns_.load();
    const long long totalUs = pressureTotalLatencyUs_.load();
    const double avgMs = runs > 0 ? (static_cast<double>(totalUs) / 1000.0 / static_cast<double>(runs)) : 0.0;
    const double tickSeconds = std::chrono::duration<double>(now - pressureLastTickTime_).count();
    const long long deltaRuns = runs - pressureLastRuns_;
    const double rate = tickSeconds > 1e-9 ? static_cast<double>(deltaRuns) / tickSeconds : 0.0;
    pressureLastRuns_ = runs;
    pressureLastTickTime_ = now;

    QString text;
    text += "速度测试统计:\n";
    text += QString("模型2线程数: %1\n").arg(pressureThreadSnapshot_);
    text += QString("运行时间: %1 秒\n").arg(elapsedSeconds, 0, 'f', 2);
    text += QString("完成轮次: %1\n").arg(runs);
    text += QString("平均耗时: %1 ms\n").arg(avgMs, 0, 'f', 2);
    text += QString("实时速率: %1 次/秒\n").arg(rate, 0, 'f', 2);
    text += QString("最近一次结果数: %1\n").arg(pressureLastResultCount_.load());
    outputText_->setPlainText(text);
    setStatus("速度测试运行中", 0);
}

int MainWindow::normalizeThreadCount(int requested) {
    if (requested < 1) {
        return 1;
    }
    if (requested > 32) {
        return 32;
    }
    return requested;
}

int MainWindow::getModelMaxBatchSize(dlcv_infer::Model& model) {
    try {
        const json info = model.GetModelInfo();
        int best = 1;
        std::function<void(const json&)> walk = [&](const json& node) {
            if (node.is_object()) {
                auto pullInt = [&](const char* key) {
                    if (!node.contains(key)) {
                        return;
                    }
                    const auto& v = node.at(key);
                    if (v.is_number_integer()) {
                        best = std::max(best, std::max(1, v.get<int>()));
                    } else if (v.is_number_float()) {
                        best = std::max(best, std::max(1, static_cast<int>(std::llround(v.get<double>()))));
                    } else if (v.is_string()) {
                        try {
                            best = std::max(best, std::max(1, std::stoi(v.get<std::string>())));
                        } catch (...) {
                        }
                    }
                };
                pullInt("max_batch_size");
                pullInt("max_batch");
                pullInt("batch_size");
                for (auto it = node.begin(); it != node.end(); ++it) {
                    walk(it.value());
                }
            } else if (node.is_array()) {
                for (const auto& x : node) {
                    walk(x);
                }
            }
        };
        walk(info);
        return std::max(1, best);
    } catch (...) {
        return 1;
    }
}

std::vector<std::vector<MainWindow::CenteredCropContext>> MainWindow::splitIntoChunks(
    const std::vector<CenteredCropContext>& source,
    int chunkSize) {
    std::vector<std::vector<CenteredCropContext>> chunks;
    if (source.empty()) {
        return chunks;
    }
    const int n = std::max(1, chunkSize);
    for (int i = 0; i < static_cast<int>(source.size()); i += n) {
        int count = std::min(n, static_cast<int>(source.size()) - i);
        chunks.emplace_back(source.begin() + i, source.begin() + i + count);
    }
    return chunks;
}

bool MainWindow::tryMapObjectByTranslate(
    const dlcv_infer::ObjectResult& localObj,
    double dx,
    double dy,
    dlcv_infer::ObjectResult& mappedObj) {
    if (localObj.bbox.size() < 4) {
        return false;
    }
    mappedObj = localObj;
    mappedObj.bbox = localObj.bbox;
    mappedObj.bbox[0] += dx;
    mappedObj.bbox[1] += dy;
    mappedObj.withBbox = true;
    return true;
}

bool MainWindow::tryClampObjectToImage(
    const dlcv_infer::ObjectResult& inputObj,
    int imageW,
    int imageH,
    dlcv_infer::ObjectResult& outputObj) {
    if (inputObj.bbox.size() < 4 || imageW <= 0 || imageH <= 0) {
        return false;
    }
    outputObj = inputObj;
    outputObj.bbox = inputObj.bbox;
    const bool isRotated = inputObj.withAngle || inputObj.bbox.size() == 5;
    if (isRotated) {
        const double cx = inputObj.bbox[0];
        const double cy = inputObj.bbox[1];
        const double w = inputObj.bbox[2];
        const double h = inputObj.bbox[3];
        if (w <= 0.0 || h <= 0.0) {
            return false;
        }
        const double left = cx - w * 0.5;
        const double right = cx + w * 0.5;
        const double top = cy - h * 0.5;
        const double bottom = cy + h * 0.5;
        const double cl = std::max(0.0, left);
        const double ct = std::max(0.0, top);
        const double cr = std::min(static_cast<double>(imageW), right);
        const double cb = std::min(static_cast<double>(imageH), bottom);
        if (cr <= cl || cb <= ct) {
            return false;
        }
        outputObj.bbox[0] = (cl + cr) * 0.5;
        outputObj.bbox[1] = (ct + cb) * 0.5;
        outputObj.bbox[2] = cr - cl;
        outputObj.bbox[3] = cb - ct;
    } else {
        const double x = inputObj.bbox[0];
        const double y = inputObj.bbox[1];
        const double w = inputObj.bbox[2];
        const double h = inputObj.bbox[3];
        if (w <= 0.0 || h <= 0.0) {
            return false;
        }
        const double left = std::max(0.0, x);
        const double top = std::max(0.0, y);
        const double right = std::min(static_cast<double>(imageW), x + w);
        const double bottom = std::min(static_cast<double>(imageH), y + h);
        if (right <= left || bottom <= top) {
            return false;
        }
        outputObj.bbox[0] = left;
        outputObj.bbox[1] = top;
        outputObj.bbox[2] = right - left;
        outputObj.bbox[3] = bottom - top;
    }
    outputObj.withBbox = true;
    return true;
}

cv::Point2d MainWindow::getObjectCenter(const dlcv_infer::ObjectResult& obj) {
    if (obj.bbox.size() < 4) {
        return cv::Point2d(0.0, 0.0);
    }
    const bool isRotated = obj.withAngle || obj.bbox.size() == 5;
    if (isRotated) {
        return cv::Point2d(obj.bbox[0], obj.bbox[1]);
    }
    return cv::Point2d(obj.bbox[0] + obj.bbox[2] * 0.5, obj.bbox[1] + obj.bbox[3] * 0.5);
}

MainWindow::CenteredCropContext MainWindow::buildCenteredCropContext(
    const cv::Mat& fullImageRgb,
    const cv::Point2d& center,
    int cropW,
    int cropH) {
    CenteredCropContext out;
    const int requestLeft = static_cast<int>(std::llround(center.x - cropW * 0.5));
    const int requestTop = static_cast<int>(std::llround(center.y - cropH * 0.5));

    const cv::Rect requested(requestLeft, requestTop, cropW, cropH);
    const cv::Rect imageRect(0, 0, fullImageRgb.cols, fullImageRgb.rows);
    const cv::Rect src = requested & imageRect;
    if (src.width <= 0 || src.height <= 0) {
        out.isValid = false;
        out.invalidReason = "裁图完全落在图像外";
        return out;
    }

    cv::Mat crop(cropH, cropW, fullImageRgb.type(), cv::Scalar::all(0));
    const cv::Rect dst(src.x - requestLeft, src.y - requestTop, src.width, src.height);
    fullImageRgb(src).copyTo(crop(dst));

    out.isValid = true;
    out.cropRgb = crop;
    out.requestedRect = requested;
    out.translateX = static_cast<double>(requestLeft);
    out.translateY = static_cast<double>(requestTop);
    return out;
}

dlcv_infer::Result MainWindow::buildDisplayResult(const std::vector<dlcv_infer::ObjectResult>& finalObjects) {
    std::vector<dlcv_infer::SampleResult> samples;
    samples.emplace_back(finalObjects);
    return dlcv_infer::Result(std::move(samples));
}

MainWindow::PipelineRunResult MainWindow::runPipeline(
    const cv::Mat& fullImageRgb,
    dlcv_infer::Model& model1,
    dlcv_infer::Model& model2,
    int requestedModel2Threads,
    const std::function<void(const PipelineProgressInfo&)>& progressCallback) {
    auto report = [&progressCallback](int percent, const QString& stage) {
        if (progressCallback) {
            progressCallback(PipelineProgressInfo{std::max(0, std::min(100, percent)), stage});
        }
    };

    PipelineRunResult runResult;
    json params;
    params["with_mask"] = false;

    report(10, "模型1整图推理");
    const dlcv_infer::Result model1Result = model1.Infer(fullImageRgb, params);

    std::vector<dlcv_infer::ObjectResult> model1Objects;
    if (!model1Result.sampleResults.empty()) {
        for (const auto& obj : model1Result.sampleResults.front().results) {
            dlcv_infer::ObjectResult clamped = obj;
            if (tryClampObjectToImage(obj, fullImageRgb.cols, fullImageRgb.rows, clamped)) {
                model1Objects.push_back(clamped);
            }
        }
    }
    runResult.model1ObjectCount = static_cast<int>(model1Objects.size());

    report(22, "按模型1结果在原图裁图");
    std::vector<CenteredCropContext> cropContexts;
    cropContexts.reserve(model1Objects.size());
    for (const auto& obj : model1Objects) {
        CenteredCropContext ctx = buildCenteredCropContext(fullImageRgb, getObjectCenter(obj), kFixedCropWidth, kFixedCropHeight);
        if (ctx.isValid) {
            cropContexts.push_back(std::move(ctx));
        } else {
            runResult.logs.push_back(QString("跳过目标[%1]: %2")
                                         .arg(QString::fromLocal8Bit(obj.categoryName.c_str()))
                                         .arg(ctx.invalidReason));
        }
    }
    runResult.cropCount = static_cast<int>(cropContexts.size());
    if (runResult.model1ObjectCount == 0) {
        runResult.logs.push_back("模型1未检测到目标。");
    }

    int batchLimit = getModelMaxBatchSize(model2);
    runResult.model2BatchLimit = batchLimit;
    const auto chunks = splitIntoChunks(cropContexts, batchLimit);
    const int total = static_cast<int>(cropContexts.size());

    const int normalized = normalizeThreadCount(requestedModel2Threads);
    const int threadCount = std::min(normalized, std::max(1, static_cast<int>(chunks.size())));
    runResult.model2ThreadCount = threadCount;
    if (normalized > threadCount && normalized > 1) {
        runResult.logs.push_back(
            QString("模型2并发度已限制为 %1（batch 段数=%2）。").arg(threadCount).arg(static_cast<int>(chunks.size())));
    }
    report(30, QString("模型2线程池推理（线程=%1）").arg(threadCount));

    if (chunks.empty()) {
        runResult.displayResult = buildDisplayResult(runResult.finalObjects);
        runResult.finalResultCount = 0;
        report(100, "推理完成");
        return runResult;
    }

    std::vector<std::vector<dlcv_infer::ObjectResult>> partials(chunks.size());
    std::atomic<int> processed{0};
    std::mutex logMutex;

    auto processChunk = [&](int chunkIndex) {
        const auto& chunk = chunks[chunkIndex];
        std::vector<cv::Mat> mats;
        mats.reserve(chunk.size());
        for (const auto& c : chunk) {
            mats.push_back(c.cropRgb);
        }

        dlcv_infer::Result batchResult(std::vector<dlcv_infer::SampleResult>{});
        try {
            batchResult = model2.InferBatch(mats, params);
            for (int i = 0; i < static_cast<int>(chunk.size()); ++i) {
                if (i >= static_cast<int>(batchResult.sampleResults.size())) {
                    continue;
                }
                for (const auto& localObj : batchResult.sampleResults[i].results) {
                    dlcv_infer::ObjectResult mapped = localObj;
                    if (!tryMapObjectByTranslate(localObj, chunk[i].translateX, chunk[i].translateY, mapped)) {
                        continue;
                    }
                    dlcv_infer::ObjectResult clamped = mapped;
                    if (tryClampObjectToImage(mapped, fullImageRgb.cols, fullImageRgb.rows, clamped)) {
                        partials[chunkIndex].push_back(clamped);
                    }
                }
            }
        } catch (const std::exception& e) {
            std::lock_guard<std::mutex> guard(logMutex);
            int begin = chunkIndex * batchLimit + 1;
            runResult.logs.push_back(
                QString("模型2 batch 推理失败(从第 %1 张开始，共 %2 张): %3")
                    .arg(begin)
                    .arg(static_cast<int>(chunk.size()))
                    .arg(QString::fromLocal8Bit(e.what())));
        }

        const int done = processed.fetch_add(static_cast<int>(chunk.size())) + static_cast<int>(chunk.size());
        int percent = 30 + static_cast<int>(std::llround(55.0 * static_cast<double>(done) / std::max(1, total)));
        report(percent, QString("模型2 batch 推理 %1/%2").arg(done).arg(total));
    };

    if (threadCount <= 1) {
        for (int i = 0; i < static_cast<int>(chunks.size()); ++i) {
            processChunk(i);
        }
    } else {
        std::vector<std::thread> workers;
        workers.reserve(threadCount);
        for (int t = 0; t < threadCount; ++t) {
            workers.emplace_back([&, t]() {
                for (int c = t; c < static_cast<int>(chunks.size()); c += threadCount) {
                    processChunk(c);
                }
            });
        }
        for (auto& w : workers) {
            w.join();
        }
    }

    for (const auto& p : partials) {
        runResult.finalObjects.insert(runResult.finalObjects.end(), p.begin(), p.end());
    }
    runResult.finalResultCount = static_cast<int>(runResult.finalObjects.size());
    runResult.displayResult = buildDisplayResult(runResult.finalObjects);
    report(95, "整理显示结果");
    report(100, "推理完成");
    return runResult;
}
