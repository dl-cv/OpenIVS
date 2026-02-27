#include "MainWindow.h"

#include <cmath>
#include <chrono>
#include <mutex>
#include <atomic>
#include <thread>
#include <vector>

#include <QComboBox>
#include <QCloseEvent>
#include <QDebug>
#include <QDesktopServices>
#include <QDoubleSpinBox>
#include <QFileDialog>
#include <QFileInfo>
#include <QGridLayout>
#include <QGuiApplication>
#include <QHBoxLayout>
#include <QIcon>
#include <QLabel>
#include <QCoreApplication>
#include <QMainWindow>
#include <QMetaObject>
#include <QMessageBox>
#include <QPlainTextEdit>
#include <QPointer>
#include <QPushButton>
#include <QScreen>
#include <QSpinBox>
#include <QSplitter>
#include <QTimer>
#include <QUrl>
#include <QVBoxLayout>
#include <QWidget>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "ImageViewerWidget.h"

namespace {
using json = nlohmann::json;

QString jsonToQStringPretty(const json& obj, int indent = 2) {
    return QString::fromUtf8(obj.dump(indent).c_str());
}

}  // namespace

MainWindow::MainWindow(QWidget* parent) : QMainWindow(parent) {
    setupUi();
    bindSignals();
    initializeDevicesAsync();
}

void MainWindow::closeEvent(QCloseEvent* event) {
    settings_.setValue("Geometry", saveGeometry());
    settings_.setValue("WindowState", saveState());
    stopPressureTest();
    model_.reset();
    dlcv_infer::Utils::FreeAllModels();
    QMainWindow::closeEvent(event);
}

void MainWindow::setupUi() {
    setWindowTitle("C++测试程序");
    setWindowIcon(QIcon(":/dlcv_demo_icon.svg"));
    setMinimumSize(860, 500);

    QWidget* centralWidget = new QWidget(this);
    auto* rootLayout = new QVBoxLayout(centralWidget);
    rootLayout->setContentsMargins(12, 12, 12, 12);
    rootLayout->setSpacing(8);

    buttonLoadModel_ = new QPushButton("加载模型", this);
    buttonGetModelInfo_ = new QPushButton("获取模型信息", this);
    buttonOpenImage_ = new QPushButton("打开图片推理", this);
    buttonInfer_ = new QPushButton("单次推理", this);
    buttonInferJson_ = new QPushButton("推理JSON", this);
    buttonPressureTest_ = new QPushButton("多线程测试", this);
    buttonFreeModel_ = new QPushButton("释放模型", this);
    buttonFreeAllModels_ = new QPushButton("释放所有模型", this);
    buttonDoc_ = new QPushButton("文档", this);

    labelDevice_ = new QLabel("选择显卡", this);
    labelBatchSize_ = new QLabel("batch_size", this);
    labelThreshold_ = new QLabel("threshold", this);
    labelThreadCount_ = new QLabel("线程数", this);

    comboDevice_ = new QComboBox(this);
    spinBatchSize_ = new QSpinBox(this);
    spinBatchSize_->setMinimum(1);
    spinBatchSize_->setMaximum(1024);
    spinBatchSize_->setValue(1);

    spinThreadCount_ = new QSpinBox(this);
    spinThreadCount_->setMinimum(1);
    spinThreadCount_->setMaximum(32);
    spinThreadCount_->setValue(1);

    spinThreshold_ = new QDoubleSpinBox(this);
    spinThreshold_->setDecimals(2);
    spinThreshold_->setSingleStep(0.05);
    spinThreshold_->setMinimum(0.05);
    spinThreshold_->setMaximum(1.0);
    spinThreshold_->setValue(0.05);

    constexpr int kControlHeight = 36;
    constexpr int kButtonMinWidth = 120;
    const std::vector<QPushButton*> buttons = {
        buttonLoadModel_,
        buttonGetModelInfo_,
        buttonOpenImage_,
        buttonInfer_,
        buttonInferJson_,
        buttonPressureTest_,
        buttonFreeModel_,
        buttonFreeAllModels_,
        buttonDoc_,
    };
    for (QPushButton* button : buttons) {
        button->setMinimumWidth(kButtonMinWidth);
        button->setFixedHeight(kControlHeight);
    }

    const std::vector<QLabel*> labels = {labelDevice_, labelBatchSize_, labelThreshold_, labelThreadCount_};
    for (QLabel* label : labels) {
        label->setFixedHeight(kControlHeight);
        label->setAlignment(Qt::AlignRight | Qt::AlignVCenter);
        label->setSizePolicy(QSizePolicy::Fixed, QSizePolicy::Fixed);
    }

    comboDevice_->setSizePolicy(QSizePolicy::Expanding, QSizePolicy::Fixed);
    comboDevice_->setFixedHeight(kControlHeight);
    spinBatchSize_->setFixedHeight(kControlHeight);
    spinThreshold_->setFixedHeight(kControlHeight);
    spinThreadCount_->setFixedHeight(kControlHeight);

    auto* topControlsLayout = new QVBoxLayout();
    topControlsLayout->setContentsMargins(0, 0, 0, 0);
    topControlsLayout->setSpacing(8);

    auto* row1Layout = new QHBoxLayout();
    row1Layout->setContentsMargins(0, 0, 0, 0);
    row1Layout->setSpacing(8);
    row1Layout->addWidget(buttonLoadModel_, 0, Qt::AlignVCenter);
    row1Layout->addWidget(labelDevice_, 0, Qt::AlignVCenter);
    row1Layout->addWidget(comboDevice_, 1, Qt::AlignVCenter);
    row1Layout->addWidget(buttonOpenImage_, 0, Qt::AlignVCenter);

    auto* row2Layout = new QHBoxLayout();
    row2Layout->setContentsMargins(0, 0, 0, 0);
    row2Layout->setSpacing(8);
    row2Layout->addWidget(buttonInfer_, 0, Qt::AlignVCenter);
    row2Layout->addWidget(buttonInferJson_, 0, Qt::AlignVCenter);
    row2Layout->addWidget(labelBatchSize_, 0, Qt::AlignVCenter);
    row2Layout->addWidget(spinBatchSize_, 0, Qt::AlignVCenter);
    row2Layout->addWidget(labelThreshold_, 0, Qt::AlignVCenter);
    row2Layout->addWidget(spinThreshold_, 0, Qt::AlignVCenter);
    row2Layout->addStretch(1);
    row2Layout->addWidget(buttonFreeModel_, 0, Qt::AlignVCenter);
    row2Layout->addWidget(buttonFreeAllModels_, 0, Qt::AlignVCenter);

    auto* row3Layout = new QHBoxLayout();
    row3Layout->setContentsMargins(0, 0, 0, 0);
    row3Layout->setSpacing(8);
    row3Layout->addWidget(buttonPressureTest_, 0, Qt::AlignVCenter);
    row3Layout->addWidget(labelThreadCount_, 0, Qt::AlignVCenter);
    row3Layout->addWidget(spinThreadCount_, 0, Qt::AlignVCenter);
    row3Layout->addStretch(1);
    row3Layout->addWidget(buttonDoc_, 0, Qt::AlignVCenter);
    row3Layout->addWidget(buttonGetModelInfo_, 0, Qt::AlignVCenter);

    topControlsLayout->addLayout(row1Layout);
    topControlsLayout->addLayout(row2Layout);
    topControlsLayout->addLayout(row3Layout);

    outputText_ = new QPlainTextEdit(this);
    outputText_->setReadOnly(true);

    imageViewer_ = new ImageViewerWidget(this);
    imageViewer_->setShowStatusText(false);
    imageViewer_->setShowVisualization(true);

    auto* splitter = new QSplitter(Qt::Horizontal, this);
    splitter->addWidget(outputText_);
    splitter->addWidget(imageViewer_);
    splitter->setStretchFactor(0, 0);
    splitter->setStretchFactor(1, 1);
    splitter->setSizes({360, 740});

    rootLayout->addLayout(topControlsLayout);
    rootLayout->addWidget(splitter, 1);

    setCentralWidget(centralWidget);

    if (settings_.contains("Geometry")) {
        restoreGeometry(settings_.value("Geometry").toByteArray());
        restoreState(settings_.value("WindowState").toByteArray());

        // 确保窗口在至少一个屏幕内可见，防止因更换显示器导致窗口处于屏幕外
        bool isVisible = false;
        const QRect currentGeometry = frameGeometry();
        for (QScreen* screen : QGuiApplication::screens()) {
            if (screen->availableGeometry().intersects(currentGeometry)) {
                isVisible = true;
                break;
            }
        }

        // 如果窗口完全在所有屏幕外，则重新居中显示
        if (!isVisible) {
            if (QScreen* screen = QGuiApplication::primaryScreen()) {
                const QRect available = screen->availableGeometry();
                move(available.center() - rect().center());
            }
        }
    } else if (QScreen* screen = QGuiApplication::primaryScreen()) {
        const QRect available = screen->availableGeometry();
        move(available.center() - rect().center());
    }
}

void MainWindow::bindSignals() {
    connect(buttonLoadModel_, &QPushButton::clicked, this, &MainWindow::onLoadModel);
    connect(buttonGetModelInfo_, &QPushButton::clicked, this, &MainWindow::onGetModelInfo);
    connect(buttonOpenImage_, &QPushButton::clicked, this, &MainWindow::onOpenImageInfer);
    connect(buttonInfer_, &QPushButton::clicked, this, &MainWindow::onInfer);
    connect(buttonInferJson_, &QPushButton::clicked, this, &MainWindow::onInferJson);
    connect(buttonPressureTest_, &QPushButton::clicked, this, &MainWindow::onPressureTest);
    connect(buttonFreeModel_, &QPushButton::clicked, this, &MainWindow::onFreeModel);
    connect(buttonFreeAllModels_, &QPushButton::clicked, this, &MainWindow::onFreeAllModels);
    connect(buttonDoc_, &QPushButton::clicked, this, &MainWindow::onOpenDoc);
}

void MainWindow::initializeDevicesAsync() {
    const QPointer<MainWindow> self(this);
    std::thread([self]() {
        struct GpuDeviceItem {
            QString name;
            int id = -1;
        };

        std::vector<GpuDeviceItem> gpuDevices;
        QString warning;
        try
        {
            const json gpuInfo = dlcv_infer::Utils::GetGpuInfo();
            if (gpuInfo.contains("code") && gpuInfo["code"].is_number_integer() && gpuInfo["code"].get<int>() == 0
                && gpuInfo.contains("devices") && gpuInfo["devices"].is_array())
            {
                for (const auto& d : gpuInfo["devices"])
                {
                    if (!d.is_object()) {
                        continue;
                    }
                    const int id = d.contains("device_id") ? d["device_id"].get<int>() : -1;
                    const std::string nameUtf8 = d.contains("device_name") ? d["device_name"].get<std::string>() : std::string{};
                    gpuDevices.push_back({ QString::fromUtf8(nameUtf8.c_str()), id });
                }
            } else
            {
                warning = QString::fromUtf8(gpuInfo.dump(2).c_str());
            }
        }
        catch (const std::exception& e)
        {
            warning = QString::fromLocal8Bit(e.what());
        }

        QMetaObject::invokeMethod(
            QCoreApplication::instance(),
            [self, gpuDevices, warning]() {
                if (self.isNull()) {
                    return;
                }

                self->comboDevice_->clear();
                self->deviceNameToId_.clear();

                self->comboDevice_->addItem("CPU");
                self->deviceNameToId_.insert("CPU", -1);
                for (const auto& device : gpuDevices) {
                    self->comboDevice_->addItem(device.name);
                    self->deviceNameToId_.insert(device.name, device.id);
                }

                self->comboDevice_->setCurrentIndex(gpuDevices.empty() ? 0 : 1);
                if (!warning.isEmpty()) {
                    self->outputText_->setPlainText("GPU信息获取失败：\n" + warning);
                }
            },
            Qt::QueuedConnection);
    }).detach();
}

int MainWindow::selectedDeviceId() const {
    const QString selectedName = comboDevice_->currentText();
    if (deviceNameToId_.contains(selectedName)) {
        return deviceNameToId_.value(selectedName);
    }
    return -1;
}

bool MainWindow::ensureModelLoaded() {
    if (model_) {
        return true;
    }
    QMessageBox::warning(this, "提示", "请先加载模型文件！");
    return false;
}

bool MainWindow::ensureImageSelected() {
    if (!imagePath_.isEmpty()) {
        return true;
    }
    QMessageBox::warning(this, "提示", "请先选择图片文件！");
    return false;
}

bool MainWindow::loadCurrentImage(cv::Mat& bgrImage, cv::Mat& rgbImage, bool silentOnDecodeFail) const {
    if (imagePath_.isEmpty()) {
        return false;
    }
    // Windows 下 OpenCV 的 imread(std::string) 走本地窄字符路径，Qt 这里需要用本地编码而不是 UTF-8。
    bgrImage = cv::imread(imagePath_.toLocal8Bit().toStdString(), cv::IMREAD_COLOR);
    if (bgrImage.empty()) {
        if (silentOnDecodeFail) {
            qWarning("图像解码失败！");
        }
        return false;
    }
    cv::cvtColor(bgrImage, rgbImage, cv::COLOR_BGR2RGB);
    return true;
}

void MainWindow::reportError(const QString& title, const QString& detail) {
    outputText_->setPlainText(title + "\n" + detail);
    QMessageBox::critical(this, "错误", title + ": " + detail);
}

QString MainWindow::formatResultText(const dlcv_infer::Result& output) const {
    constexpr double kPi = 3.14159265358979323846;

    if (output.sampleResults.empty() || output.sampleResults.front().results.empty()) {
        return "No Result";
    }

    QString text;
    const auto& results = output.sampleResults.front().results;
    for (const auto& obj : results) {
        text += QString("%1, ").arg(QString::fromLocal8Bit(obj.categoryName.c_str()));
        text += QString("Score: %1, ").arg(obj.score * 100.0f, 0, 'f', 1);
        text += QString("Area: %1, ").arg(obj.area, 0, 'f', 1);
        if (obj.withAngle) {
            const double angleDegree = obj.angle * 180.0 / kPi;
            text += QString("Angle: %1, ").arg(angleDegree, 0, 'f', 1);
        }
        if (!obj.bbox.empty()) {
            text += "Bbox: [";
            for (size_t i = 0; i < obj.bbox.size(); ++i) {
                text += QString::number(obj.bbox[i], 'f', 1);
                if (i + 1 < obj.bbox.size()) {
                    text += ", ";
                }
            }
            text += "], ";
        }
        if (obj.withMask && !obj.mask.empty()) {
            text += QString("Mask size: %1x%2, ").arg(obj.mask.cols).arg(obj.mask.rows);
        }
        text += "\n";
    }
    return text;
}

void MainWindow::onLoadModel() {
    stopPressureTest();
    model_.reset();

    QFileDialog dialog(this, "选择模型");
    dialog.setNameFilter("AI模型 (*.dvt *.dvo *.dvr *.dvst);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);

    const QString lastPath = settings_.value("LastModelPath").toString();
    if (!lastPath.isEmpty()) {
        QFileInfo fileInfo(lastPath);
        dialog.setDirectory(fileInfo.absolutePath());
        dialog.selectFile(fileInfo.fileName());
    }

    if (dialog.exec() != QDialog::Accepted) {
        return;
    }

    const QString selectedModelPath = dialog.selectedFiles().front();

    settings_.setValue("LastModelPath", selectedModelPath);

    try
    {
        const std::string modelPathLocal8 = selectedModelPath.toLocal8Bit().toStdString();
        model_ = std::make_unique<dlcv_infer::Model>(modelPathLocal8, selectedDeviceId());
    }
    catch (const std::exception& e)
    {
        model_.reset();
        outputText_->setPlainText(QString::fromLocal8Bit(e.what()));
        return;
    }

    onGetModelInfo();
}

void MainWindow::onOpenImageInfer() {
    stopPressureTest();

    if (!ensureModelLoaded()) {
        return;
    }

    QFileDialog dialog(this, "选择图片文件");
    dialog.setNameFilter("图片文件 (*.jpg *.jpeg *.png *.bmp *.gif *.tiff *.tif);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);

    const QString lastPath = settings_.value("LastImagePath").toString();
    if (!lastPath.isEmpty()) {
        QFileInfo fileInfo(lastPath);
        dialog.setDirectory(fileInfo.absolutePath());
        dialog.selectFile(fileInfo.fileName());
    }

    if (dialog.exec() != QDialog::Accepted) {
        return;
    }

    imagePath_ = dialog.selectedFiles().front();
    settings_.setValue("LastImagePath", imagePath_);
    onInfer();
}

void MainWindow::onInfer() {
    if (pressureTestRunning_) {
        return;
    }

    if (!ensureModelLoaded() || !ensureImageSelected()) {
        return;
    }

    cv::Mat bgrImage;
    cv::Mat rgbImage;
    if (!loadCurrentImage(bgrImage, rgbImage, false)) {
        reportError("推理失败", "图像解码失败！");
        return;
    }

    const int batchSize = spinBatchSize_->value();
    std::vector<cv::Mat> imageList;
    imageList.reserve(batchSize);
    const cv::Mat& inferImage = rgbImage;
    for (int i = 0; i < batchSize; ++i) {
        imageList.push_back(inferImage);
    }

    dlcv_infer::Result output({}); // placeholder
    double elapsedMs = 0.0;
    try
    {
        json params;
        params["threshold"] = spinThreshold_->value();
        params["with_mask"] = true;

        const auto start = std::chrono::steady_clock::now();
        output = model_->InferBatch(imageList, params);
        const auto end = std::chrono::steady_clock::now();
        elapsedMs = std::chrono::duration<double, std::milli>(end - start).count();
    }
    catch (const std::exception& e)
    {
        reportError("推理失败", QString::fromLocal8Bit(e.what()));
        return;
    }

    currentBgrImage_ = bgrImage;
    const std::vector<dlcv_infer::ObjectResult> firstResults =
        output.sampleResults.empty() ? std::vector<dlcv_infer::ObjectResult>{} : output.sampleResults.front().results;
    imageViewer_->setImageAndResults(currentBgrImage_, firstResults);

    QString text;
    text += QString("推理时间: %1ms\n\n").arg(elapsedMs, 0, 'f', 2);
    text += QString("输入: RGB\n\n");
    text += "推理结果:\n";
    text += formatResultText(output);
    outputText_->setPlainText(text);
}

void MainWindow::onInferJson() {
    if (pressureTestRunning_) {
        return;
    }

    if (!ensureModelLoaded() || !ensureImageSelected()) {
        return;
    }

    cv::Mat bgrImage;
    cv::Mat rgbImage;
    if (!loadCurrentImage(bgrImage, rgbImage, true)) {
        return;
    }

    const cv::Mat& inferImage = rgbImage;

    try
    {
        json params;
        params["threshold"] = spinThreshold_->value();
        params["with_mask"] = true;

        const json resultArray = model_->InferOneOutJson(inferImage, params);
        if (resultArray.empty()) {
            json debugObj;
            debugObj["input"] = "RGB";
            debugObj["one_out"] = resultArray;
            outputText_->setPlainText(jsonToQStringPretty(debugObj, 2));
            return;
        }
        outputText_->setPlainText(jsonToQStringPretty(resultArray, 4));
    }
    catch (const std::exception& e)
    {
        reportError("推理JSON失败", QString::fromLocal8Bit(e.what()));
        return;
    }
}

void MainWindow::onPressureTest() {
    if (pressureTestRunning_) {
        stopPressureTest();
        return;
    }
    startPressureTest();
}

void MainWindow::startPressureTest() {
    if (pressureTestRunning_) {
        return;
    }

    if (!ensureModelLoaded() || !ensureImageSelected()) {
        return;
    }

    cv::Mat bgrImage;
    cv::Mat rgbImage;
    if (!loadCurrentImage(bgrImage, rgbImage, false)) {
        reportError("启动压力测试失败", "图像解码失败！");
        return;
    }

    pressureThreadCount_ = spinThreadCount_->value();
    pressureBatchSize_ = spinBatchSize_->value();
    pressureThreshold_ = spinThreshold_->value();
    pressureBaseImage_ = rgbImage;

    pressureStopRequested_.store(false, std::memory_order_relaxed);
    pressureError_.store(false, std::memory_order_relaxed);
    pressureCompletedRequests_.store(0, std::memory_order_relaxed);
    pressureTotalLatencyUs_.store(0, std::memory_order_relaxed);
    {
        std::lock_guard<std::mutex> lock(pressureErrorMutex_);
        pressureErrorDetail_.clear();
    }

    pressureStartTime_ = std::chrono::steady_clock::now();
    pressureLastTickTime_ = pressureStartTime_;
    pressureLastCompletedRequests_ = 0;

    if (pressureTimer_ == nullptr) {
        pressureTimer_ = new QTimer(this);
        pressureTimer_->setInterval(500);
        connect(pressureTimer_, &QTimer::timeout, this, &MainWindow::updatePressureTestStatistics);
    } else {
        pressureTimer_->stop();
    }

    pressureTestRunning_ = true;
    buttonPressureTest_->setText("停止");
    setUiEnabledForPressureTest(false);

    pressureThreads_.clear();
    pressureThreads_.reserve(static_cast<size_t>(pressureThreadCount_));

    const int batchSize = pressureBatchSize_;
    const double threshold = pressureThreshold_;
    const cv::Mat baseImage = pressureBaseImage_;
    dlcv_infer::Model* const modelPtr = model_.get();

    for (int t = 0; t < pressureThreadCount_; ++t) {
        pressureThreads_.emplace_back([this, modelPtr, batchSize, threshold, baseImage]() {
            cv::Mat threadImage = baseImage.clone();
            if (threadImage.empty()) {
                const QString detail = "输入图像为空。";
                if (!pressureError_.exchange(true)) {
                    {
                        std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                        pressureErrorDetail_ = detail;
                    }
                    pressureStopRequested_.store(true, std::memory_order_relaxed);
                    QMetaObject::invokeMethod(
                        this,
                        [this]() {
                            if (!pressureTestRunning_) {
                                return;
                            }
                            stopPressureTest();
                            QString detailCopy;
                            {
                                std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                                detailCopy = pressureErrorDetail_;
                            }
                            reportError("压力测试过程中发生错误", detailCopy);
                        },
                        Qt::QueuedConnection);
                }
                return;
            }

            if (modelPtr == nullptr) {
                const QString detail = "模型未加载。";
                if (!pressureError_.exchange(true)) {
                    {
                        std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                        pressureErrorDetail_ = detail;
                    }
                    pressureStopRequested_.store(true, std::memory_order_relaxed);
                    QMetaObject::invokeMethod(
                        this,
                        [this]() {
                            if (!pressureTestRunning_) {
                                return;
                            }
                            stopPressureTest();
                            QString detailCopy;
                            {
                                std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                                detailCopy = pressureErrorDetail_;
                            }
                            reportError("压力测试过程中发生错误", detailCopy);
                        },
                        Qt::QueuedConnection);
                }
                return;
            }

            json params;
            params["threshold"] = threshold;
            params["with_mask"] = false;

            std::vector<cv::Mat> images;
            images.reserve(batchSize);
            for (int i = 0; i < batchSize; ++i) {
                images.push_back(threadImage);
            }

            while (!pressureStopRequested_.load(std::memory_order_relaxed)) {
                const auto start = std::chrono::high_resolution_clock::now();
                try
                {
                    (void)modelPtr->InferBatch(images, params);
                }
                catch (const std::exception& e)
                {
                    const QString errorDetail = QString("[input=RGB] %1").arg(QString::fromLocal8Bit(e.what()));
                    if (!pressureError_.exchange(true)) {
                        {
                            std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                            pressureErrorDetail_ = errorDetail;
                        }
                        pressureStopRequested_.store(true, std::memory_order_relaxed);
                        QMetaObject::invokeMethod(
                            this,
                            [this]() {
                                if (!pressureTestRunning_) {
                                    return;
                                }
                                stopPressureTest();
                                QString detailCopy;
                                {
                                    std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                                    detailCopy = pressureErrorDetail_;
                                }
                                reportError("压力测试过程中发生错误", detailCopy);
                            },
                            Qt::QueuedConnection);
                    }
                    return;
                }
                const auto end = std::chrono::high_resolution_clock::now();

                const long long latencyUs =
                    std::chrono::duration_cast<std::chrono::microseconds>(end - start).count();
                pressureTotalLatencyUs_.fetch_add(latencyUs, std::memory_order_relaxed);
                pressureCompletedRequests_.fetch_add(1, std::memory_order_relaxed);
            }
        });
    }

    pressureTimer_->start();
}

void MainWindow::stopPressureTest() {
    if (!pressureTestRunning_) {
        return;
    }

    pressureStopRequested_.store(true, std::memory_order_relaxed);

    if (pressureTimer_ != nullptr) {
        pressureTimer_->stop();
    }

    for (std::thread& worker : pressureThreads_) {
        if (worker.joinable()) {
            worker.join();
        }
    }
    pressureThreads_.clear();

    pressureTestRunning_ = false;
    buttonPressureTest_->setText("多线程测试");
    setUiEnabledForPressureTest(true);
}

void MainWindow::updatePressureTestStatistics() {
    if (!pressureTestRunning_) {
        return;
    }

    const auto now = std::chrono::steady_clock::now();
    const double elapsedSeconds = std::chrono::duration<double>(now - pressureStartTime_).count();

    const long long completedRequests = pressureCompletedRequests_.load(std::memory_order_relaxed);
    const long long totalLatencyUs = pressureTotalLatencyUs_.load(std::memory_order_relaxed);

    const double averageLatencyMs =
        completedRequests > 0 ? (static_cast<double>(totalLatencyUs) / 1000.0 / static_cast<double>(completedRequests))
                              : 0.0;

    const double tickSeconds = std::chrono::duration<double>(now - pressureLastTickTime_).count();
    const long long deltaRequests = completedRequests - pressureLastCompletedRequests_;
    const double recentRate =
        tickSeconds > 1e-9 ? (static_cast<double>(deltaRequests) * static_cast<double>(pressureBatchSize_) / tickSeconds)
                           : 0.0;

    pressureLastTickTime_ = now;
    pressureLastCompletedRequests_ = completedRequests;

    QString text;
    text += "压力测试统计:\n";
    text += QString("线程数: %1\n").arg(pressureThreadCount_);
    text += QString("批量大小: %1\n").arg(pressureBatchSize_);
    text += QString("运行时间: %1 秒\n").arg(elapsedSeconds, 0, 'f', 2);
    text += QString("完成请求: %1\n").arg(completedRequests * static_cast<long long>(pressureBatchSize_));
    text += QString("平均延迟: %1ms\n").arg(averageLatencyMs, 0, 'f', 2);
    text += QString("实时速率: %1 请求/秒\n").arg(recentRate, 0, 'f', 2);
    outputText_->setPlainText(text);
}

void MainWindow::setUiEnabledForPressureTest(bool enabled) {
    buttonLoadModel_->setEnabled(enabled);
    buttonGetModelInfo_->setEnabled(enabled);
    buttonOpenImage_->setEnabled(enabled);
    buttonInfer_->setEnabled(enabled);
    buttonInferJson_->setEnabled(enabled);
    comboDevice_->setEnabled(enabled);
    spinBatchSize_->setEnabled(enabled);
    spinThreshold_->setEnabled(enabled);
    spinThreadCount_->setEnabled(enabled);
}

void MainWindow::onGetModelInfo() {
    if (!ensureModelLoaded()) {
        return;
    }

    json modelInfo;
    try
    {
        modelInfo = model_->GetModelInfo();
    }
    catch (const std::exception& e)
    {
        outputText_->setPlainText(QString::fromLocal8Bit(e.what()));
        return;
    }

    if (modelInfo.contains("model_info")) {
        outputText_->setPlainText(jsonToQStringPretty(modelInfo["model_info"], 2));
    } else {
        outputText_->setPlainText(jsonToQStringPretty(modelInfo, 2));
    }
}

void MainWindow::onFreeModel() {
    stopPressureTest();

    model_.reset();
    outputText_->setPlainText("模型已释放");
}

void MainWindow::onFreeAllModels() {
    stopPressureTest();

    model_.reset();
    dlcv_infer::Utils::FreeAllModels();
    outputText_->setPlainText("所有模型已释放");
}

void MainWindow::onOpenDoc() {
    QDesktopServices::openUrl(QUrl("https://docs.dlcv.com.cn/deploy/sdk/csharp_sdk"));
}
