#include "MainWindow.h"

#include <algorithm>
#include <cmath>
#include <string>

#include <QCheckBox>
#include <QCloseEvent>
#include <QComboBox>
#include <QCoreApplication>
#include <QDesktopServices>
#include <QDoubleSpinBox>
#include <QFileDialog>
#include <QFileInfo>
#include <QGuiApplication>
#include <QHBoxLayout>
#include <QLabel>
#include <QMessageBox>
#include <QMetaObject>
#include <QPlainTextEdit>
#include <QPointer>
#include <QPushButton>
#include <QScreen>
#include <QSizePolicy>
#include <QSpinBox>
#include <QSplitter>
#include <QTimer>
#include <QUrl>
#include <QVBoxLayout>
#include <QWidget>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "ImageViewerWidget.h"
#include "json/json.hpp"

namespace {
using json = nlohmann::json;

class CResultGuard {
public:
    CResultGuard(DlcvInferApi& api, DlcvCResult value) : api_(api), value_(value) {}
    ~CResultGuard() { api_.freeModelResult(&value_); }

    CResultGuard(const CResultGuard&) = delete;
    CResultGuard& operator=(const CResultGuard&) = delete;

    DlcvCResult& get() { return value_; }
    const DlcvCResult& get() const { return value_; }

private:
    DlcvInferApi& api_;
    DlcvCResult value_{};
};

class CStringGuard {
public:
    CStringGuard(DlcvInferApi& api, const char* value) : api_(api), value_(value) {}
    ~CStringGuard() { api_.freeString(value_); }

    CStringGuard(const CStringGuard&) = delete;
    CStringGuard& operator=(const CStringGuard&) = delete;

    const char* get() const { return value_; }

private:
    DlcvInferApi& api_;
    const char* value_ = nullptr;
};

QString prettyJson(const char* value, int indent = 2) {
    if (value == nullptr) {
        return {};
    }
    try {
        return QString::fromUtf8(json::parse(value).dump(indent).c_str());
    } catch (...) {
        return QString::fromUtf8(value);
    }
}

QString describeOpenCvImageForUi(const cv::Mat& image, bool threeChannelIsRgb = false) {
    if (image.empty()) {
        return QStringLiteral("(空)");
    }

    QString depth;
    switch (image.depth()) {
    case CV_8U:
        depth = QStringLiteral("8bit");
        break;
    case CV_16U:
        depth = QStringLiteral("16bit");
        break;
    case CV_16S:
        depth = QStringLiteral("16bit(有符号)");
        break;
    case CV_32F:
        depth = QStringLiteral("32bit浮点");
        break;
    case CV_64F:
        depth = QStringLiteral("64bit浮点");
        break;
    default:
        depth = QStringLiteral("depth=%1").arg(image.depth());
        break;
    }

    QString channels;
    switch (image.channels()) {
    case 1:
        channels = QStringLiteral("单通道");
        break;
    case 3:
        channels = threeChannelIsRgb ? QStringLiteral("三通道(RGB)") : QStringLiteral("三通道(BGR)");
        break;
    case 4:
        channels = threeChannelIsRgb ? QStringLiteral("四通道(RGBA)") : QStringLiteral("四通道(BGRA)");
        break;
    default:
        channels = QStringLiteral("%1通道").arg(image.channels());
        break;
    }
    return channels + QStringLiteral("，") + depth;
}

cv::Mat prepareImageForInference(const cv::Mat& decodedImage) {
    if (decodedImage.empty()) {
        return {};
    }

    cv::Mat output;
    if (decodedImage.channels() == 3) {
        cv::cvtColor(decodedImage, output, cv::COLOR_BGR2RGB);
    } else if (decodedImage.channels() == 4) {
        cv::cvtColor(decodedImage, output, cv::COLOR_BGRA2RGB);
    } else {
        output = decodedImage.clone();
    }

    if (!output.empty() && !output.isContinuous()) {
        output = output.clone();
    }
    return output;
}

DlcvCImage makeCImage(const cv::Mat& image) {
    DlcvCImage result{};
    result.data_ptr = static_cast<long long>(reinterpret_cast<uintptr_t>(image.data));
    result.height = image.rows;
    result.width = image.cols;
    result.channel = image.channels();
    return result;
}

std::string resultMessage(const DlcvCResult& result) {
    return result.message == nullptr ? std::string{} : std::string(result.message);
}

}  // namespace

MainWindow::MainWindow(QWidget* parent) : QMainWindow(parent) {
    setupUi();
    bindSignals();
    if (!api_.load()) {
        reportError("加载推理库失败", QString::fromStdWString(api_.lastError()));
        return;
    }
    initializeDevicesAsync();
}

void MainWindow::closeEvent(QCloseEvent* event) {
    settings_.setValue("Geometry", saveGeometry());
    settings_.setValue("WindowState", saveState());
    stopPressureTest();
    if (deviceInitializationThread_.joinable()) {
        deviceInitializationThread_.join();
    }
    if (api_.isLoaded()) {
        api_.freeAllModels();
        api_.unload();
    }
    modelIndex_ = -1;
    QMainWindow::closeEvent(event);
}

void MainWindow::setupUi() {
    setWindowTitle("C测试程序");
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
    buttonCheckDog_ = new QPushButton("检查加密狗", this);

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
    spinThreshold_->setMinimum(0.0);
    spinThreshold_->setMaximum(1.0);
    spinThreshold_->setValue(0.5);

    checkCalcMean_ = new QCheckBox("计算均值", this);
    checkCalcMean_->setChecked(false);

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
        buttonCheckDog_,
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
    checkCalcMean_->setFixedHeight(kControlHeight);

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
    row2Layout->addWidget(checkCalcMean_, 0, Qt::AlignVCenter);
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
    row3Layout->addWidget(buttonCheckDog_, 0, Qt::AlignVCenter);
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

        bool visible = false;
        const QRect currentGeometry = frameGeometry();
        for (QScreen* screen : QGuiApplication::screens()) {
            if (screen->availableGeometry().intersects(currentGeometry)) {
                visible = true;
                break;
            }
        }
        if (!visible) {
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
    connect(buttonCheckDog_, &QPushButton::clicked, this, &MainWindow::onCheckDog);
    connect(
        spinThreshold_,
        static_cast<void (QDoubleSpinBox::*)(double)>(&QDoubleSpinBox::valueChanged),
        this,
        [this](double) {
            if (pressureTestRunning_ || modelIndex_ < 0 || imagePath_.isEmpty() || !QFileInfo::exists(imagePath_)) {
                return;
            }
            onInfer();
        });
}

void MainWindow::initializeDevicesAsync() {
    const QPointer<MainWindow> self(this);
    deviceInitializationThread_ = std::thread([this, self]() {
        struct GpuDeviceItem {
            QString name;
            int id = -1;
        };

        std::vector<GpuDeviceItem> devices;
        QString warning;
        api_.keepMaxClock();

        const char* raw = api_.getGpuInfo();
        std::string rawCopy = raw == nullptr ? std::string{} : std::string(raw);
        if (raw != nullptr) {
            api_.freeSystemString(raw);
        }

        try {
            const json gpuInfo = json::parse(rawCopy);
            if (gpuInfo.value("code", -1) == 0 && gpuInfo.contains("devices") && gpuInfo["devices"].is_array()) {
                for (const auto& item : gpuInfo["devices"]) {
                    if (!item.is_object()) {
                        continue;
                    }
                    devices.push_back({
                        QString::fromUtf8(item.value("device_name", std::string{}).c_str()),
                        item.value("device_id", -1),
                    });
                }
            } else {
                warning = QString::fromUtf8(gpuInfo.dump(2).c_str());
            }
        } catch (const std::exception& ex) {
            warning = rawCopy.empty() ? QStringLiteral("C接口未返回GPU信息") : QString::fromLocal8Bit(ex.what());
        }

        QMetaObject::invokeMethod(
            QCoreApplication::instance(),
            [self, devices, warning]() {
                if (self.isNull()) {
                    return;
                }
                self->comboDevice_->clear();
                self->deviceNameToId_.clear();
                self->comboDevice_->addItem("CPU");
                self->deviceNameToId_.insert("CPU", -1);
                for (const auto& device : devices) {
                    self->comboDevice_->addItem(device.name);
                    self->deviceNameToId_.insert(device.name, device.id);
                }
                self->comboDevice_->setCurrentIndex(devices.empty() ? 0 : 1);
                if (!warning.isEmpty()) {
                    self->outputText_->setPlainText("GPU信息获取失败：\n" + warning);
                }
            },
            Qt::QueuedConnection);
    });
}

int MainWindow::selectedDeviceId() const {
    return deviceNameToId_.value(comboDevice_->currentText(), -1);
}

bool MainWindow::ensureModelLoaded() {
    if (modelIndex_ >= 0) {
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

bool MainWindow::loadCurrentImage(cv::Mat& image, bool silentOnDecodeFail) const {
    if (imagePath_.isEmpty()) {
        return false;
    }
    image = cv::imread(imagePath_.toLocal8Bit().toStdString(), cv::IMREAD_UNCHANGED);
    if (image.empty() && !silentOnDecodeFail) {
        QMessageBox::warning(
            const_cast<MainWindow*>(this),
            "错误",
            "图像解码失败！");
    }
    return !image.empty();
}

void MainWindow::freeCurrentModel() {
    if (modelIndex_ >= 0 && api_.isLoaded()) {
        api_.freeModel(modelIndex_);
    }
    modelIndex_ = -1;
}

void MainWindow::reportError(const QString& title, const QString& detail) {
    outputText_->setPlainText(title + "\n" + detail);
    QMessageBox::critical(this, "错误", title + ": " + detail);
}

QString MainWindow::lastCError() const {
    const char* error = api_.getLastError();
    return error == nullptr ? QString{} : QString::fromLocal8Bit(error);
}

QString MainWindow::formatResultText(const std::vector<DisplayObjectResult>& results) const {
    constexpr double kPi = 3.14159265358979323846;
    if (results.empty()) {
        return "未检测到目标。\n";
    }

    QString text;
    for (int index = 0; index < static_cast<int>(results.size()); ++index) {
        const DisplayObjectResult& object = results[static_cast<size_t>(index)];
        text += QString("[%1] %2")
                    .arg(index + 1)
                    .arg(QString::fromLocal8Bit(object.categoryName.c_str()), -12);
        text += QString("  score=%1  ").arg(object.score, 0, 'f', 2);

        if (!object.withBbox || object.bbox.size() < 4) {
            text += "bbox=(N/A)";
        } else if (object.withAngle) {
            text += QString("rbox=(cx=%1, cy=%2, w=%3, h=%4, angle=%5)")
                        .arg(object.bbox[0], 0, 'f', 1)
                        .arg(object.bbox[1], 0, 'f', 1)
                        .arg(object.bbox[2], 0, 'f', 1)
                        .arg(object.bbox[3], 0, 'f', 1)
                        .arg(object.angle, 0, 'f', 3);
        } else {
            text += QString("bbox=(%1, %2, %3, %4)")
                        .arg(object.bbox[0], 0, 'f', 1)
                        .arg(object.bbox[1], 0, 'f', 1)
                        .arg(object.bbox[2], 0, 'f', 1)
                        .arg(object.bbox[3], 0, 'f', 1);
        }

        text += QString("  area=%1").arg(object.area, 0, 'f', 1);
        if (object.withAngle) {
            const double degrees = object.angle * 180.0 / kPi;
            text += QString("  angle=%1rad(%2deg)")
                        .arg(object.angle, 0, 'f', 3)
                        .arg(degrees, 0, 'f', 1);
        }
        if (object.withMean) {
            text += QString("  前景均值=%1  背景均值=%2")
                        .arg(object.foregroundMean, 0, 'f', 4)
                        .arg(object.backgroundMean, 0, 'f', 4);
        }
        text += "\n";
    }
    return text;
}

std::vector<DisplayObjectResult> MainWindow::copyFirstSample(const DlcvCResult& result) const {
    std::vector<DisplayObjectResult> output;
    if (result.code != 0 || result.sample_results == nullptr || result.n <= 0) {
        return output;
    }

    const DlcvCSampleResult& sample = result.sample_results[0];
    if (sample.results == nullptr || sample.n <= 0) {
        return output;
    }

    output.reserve(static_cast<size_t>(sample.n));
    for (int index = 0; index < sample.n; ++index) {
        const DlcvCObjectResult& source = sample.results[index];
        DisplayObjectResult target;
        target.categoryId = source.category_id;
        target.categoryName = source.category_name == nullptr ? std::string{} : std::string(source.category_name);
        target.score = source.score;
        target.withBbox = source.with_bbox;
        if (source.with_bbox) {
            target.bbox = {source.x, source.y, source.w, source.h};
        }
        target.withMask = source.with_mask;
        if (source.with_mask && source.mask.mask_ptr != 0 && source.mask.width > 0 && source.mask.height > 0) {
            cv::Mat mask(
                source.mask.height,
                source.mask.width,
                CV_8UC1,
                reinterpret_cast<void*>(static_cast<uintptr_t>(source.mask.mask_ptr)));
            target.mask = mask.clone();
        }
        target.withAngle = source.with_angle;
        target.angle = source.angle;
        target.area = source.area;
        target.withMean = source.with_mean;
        target.foregroundMean = source.foreground_mean;
        target.backgroundMean = source.background_mean;
        output.push_back(std::move(target));
    }
    return output;
}

void MainWindow::onLoadModel() {
    stopPressureTest();
    if (!api_.isLoaded()) {
        reportError("加载模型失败", QString::fromStdWString(api_.lastError()));
        return;
    }

    QFileDialog dialog(this, "选择模型");
    dialog.setNameFilter("AI模型 (*.dvt *.dvo *.dvr *.dvst);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);

    const QString lastPath = settings_.value("LastModelPath").toString();
    if (!lastPath.isEmpty()) {
        const QFileInfo fileInfo(lastPath);
        dialog.setDirectory(fileInfo.absolutePath());
        dialog.selectFile(fileInfo.fileName());
    }

    if (dialog.exec() != QDialog::Accepted) {
        return;
    }

    const QString selectedPath = dialog.selectedFiles().front();
    settings_.setValue("LastModelPath", selectedPath);
    freeCurrentModel();

    const QByteArray pathBytes = selectedPath.toLocal8Bit();
    const int modelIndex = api_.loadModel(pathBytes.constData(), selectedDeviceId());
    if (modelIndex < 0) {
        outputText_->setPlainText(lastCError());
        return;
    }

    modelIndex_ = modelIndex;
    modelPath_ = selectedPath;
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
        const QFileInfo fileInfo(lastPath);
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
    imageViewer_->clearInspectionStatus();

    if (!ensureModelLoaded() || !ensureImageSelected()) {
        return;
    }

    cv::Mat decodedImage;
    if (!loadCurrentImage(decodedImage, false)) {
        return;
    }

    const cv::Mat inferImage = prepareImageForInference(decodedImage);
    if (inferImage.empty()) {
        reportError("推理失败", "输入图像通道转换失败！");
        return;
    }

    const int batchSize = spinBatchSize_->value();
    std::vector<DlcvCImage> images(static_cast<size_t>(batchSize));
    for (DlcvCImage& image : images) {
        image = makeCImage(inferImage);
    }
    DlcvCImageList imageList{};
    imageList.images = images.data();
    imageList.n = batchSize;

    const json params = {
        {"threshold", spinThreshold_->value()},
        {"with_mask", true},
        {"batch_size", batchSize},
        {"calc_mean", checkCalcMean_->isChecked()},
    };
    const std::string paramsText = params.dump();

    const auto start = std::chrono::steady_clock::now();
    CResultGuard result(api_, api_.inferWithParams(modelIndex_, &imageList, paramsText.c_str()));
    const auto end = std::chrono::steady_clock::now();

    if (result.get().code != 0) {
        reportError("推理失败", QString::fromLocal8Bit(resultMessage(result.get()).c_str()));
        return;
    }

    const double elapsedMs = std::chrono::duration<double, std::milli>(end - start).count();
    const std::vector<DisplayObjectResult> firstResults = copyFirstSample(result.get());
    currentBgrImage_ = decodedImage;
    imageViewer_->setImageAndResults(currentBgrImage_, firstResults);

    QString text;
    text += QString("模型: %1\n").arg(modelPath_);
    text += QString("图片: %1\n").arg(imagePath_);
    text += QString("输入图像: %1\n").arg(describeOpenCvImageForUi(inferImage, true));
    text += QString("batch_size: %1\n").arg(batchSize);
    text += QString("threshold: %1\n").arg(spinThreshold_->value(), 0, 'f', 2);
    text += QString("推理时间: %1ms\n").arg(elapsedMs, 0, 'f', 2);
    text += QString("推理结果: %1个\n").arg(static_cast<int>(firstResults.size()));
    text += "\n";
    text += formatResultText(firstResults);
    outputText_->setPlainText(text);
}

void MainWindow::onInferJson() {
    if (pressureTestRunning_) {
        return;
    }
    imageViewer_->clearInspectionStatus();

    if (!ensureModelLoaded() || !ensureImageSelected()) {
        return;
    }

    cv::Mat decodedImage;
    if (!loadCurrentImage(decodedImage, true)) {
        return;
    }

    const cv::Mat inferImage = prepareImageForInference(decodedImage);
    if (inferImage.empty()) {
        reportError("推理JSON失败", "输入图像通道转换失败！");
        return;
    }

    const json params = {
        {"threshold", spinThreshold_->value()},
        {"with_mask", true},
        {"batch_size", 1},
        {"calc_mean", checkCalcMean_->isChecked()},
    };
    const std::string paramsText = params.dump();
    DlcvCImage image = makeCImage(inferImage);

    CStringGuard result(api_, api_.inferJson(modelIndex_, &image, paramsText.c_str()));
    if (result.get() == nullptr) {
        reportError("推理JSON失败", lastCError());
        return;
    }

    outputText_->setPlainText(prettyJson(result.get(), 4));
}

void MainWindow::onPressureTest() {
    if (pressureTestRunning_) {
        stopPressureTest();
        return;
    }
    startPressureTest();
}

void MainWindow::startPressureTest() {
    if (!ensureModelLoaded() || !ensureImageSelected()) {
        return;
    }

    cv::Mat decodedImage;
    if (!loadCurrentImage(decodedImage, false)) {
        return;
    }

    const cv::Mat inferImage = prepareImageForInference(decodedImage);
    if (inferImage.empty()) {
        reportError("多线程测试失败", "输入图像通道转换失败！");
        return;
    }

    pressureThreadCount_ = spinThreadCount_->value();
    pressureBatchSize_ = spinBatchSize_->value();
    pressureThreshold_ = spinThreshold_->value();
    pressureCalcMean_ = checkCalcMean_->isChecked();
    pressureModelIndex_ = modelIndex_;
    pressureBaseImage_ = inferImage.clone();

    pressureStopRequested_.store(false, std::memory_order_relaxed);
    pressureError_.store(false, std::memory_order_relaxed);
    pressureCompletedRequests_.store(0, std::memory_order_relaxed);
    pressureTotalLatencyUs_.store(0, std::memory_order_relaxed);
    pressureErrorDetail_.clear();
    pressureThreads_.clear();

    pressureStartTime_ = std::chrono::steady_clock::now();
    pressureLastTickTime_ = pressureStartTime_;
    pressureLastCompletedRequests_ = 0;
    pressureTestRunning_ = true;
    buttonPressureTest_->setText("停止");
    setUiEnabledForPressureTest(false);

    if (pressureTimer_ == nullptr) {
        pressureTimer_ = new QTimer(this);
        pressureTimer_->setInterval(500);
        connect(pressureTimer_, &QTimer::timeout, this, &MainWindow::updatePressureTestStatistics);
    }

    const json params = {
        {"threshold", pressureThreshold_},
        {"with_mask", true},
        {"batch_size", pressureBatchSize_},
        {"calc_mean", pressureCalcMean_},
    };
    const std::string paramsText = params.dump();

    for (int threadIndex = 0; threadIndex < pressureThreadCount_; ++threadIndex) {
        pressureThreads_.emplace_back([this, paramsText]() {
            std::vector<DlcvCImage> images(static_cast<size_t>(pressureBatchSize_));
            for (DlcvCImage& image : images) {
                image = makeCImage(pressureBaseImage_);
            }
            DlcvCImageList imageList{};
            imageList.images = images.data();
            imageList.n = pressureBatchSize_;

            while (!pressureStopRequested_.load(std::memory_order_relaxed)) {
                const auto start = std::chrono::steady_clock::now();
                CResultGuard result(
                    api_,
                    api_.inferWithParams(pressureModelIndex_, &imageList, paramsText.c_str()));
                const auto end = std::chrono::steady_clock::now();

                if (result.get().code != 0) {
                    const QString detail = QString::fromLocal8Bit(resultMessage(result.get()).c_str());
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
                                QString detail;
                                {
                                    std::lock_guard<std::mutex> lock(pressureErrorMutex_);
                                    detail = pressureErrorDetail_;
                                }
                                reportError("多线程测试过程中发生错误", detail);
                            },
                            Qt::QueuedConnection);
                    }
                    return;
                }

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
        completedRequests > 0
            ? static_cast<double>(totalLatencyUs) / 1000.0 / static_cast<double>(completedRequests)
            : 0.0;

    const double tickSeconds = std::chrono::duration<double>(now - pressureLastTickTime_).count();
    const long long deltaRequests = completedRequests - pressureLastCompletedRequests_;
    const double recentRate =
        tickSeconds > 1e-9
            ? static_cast<double>(deltaRequests) * static_cast<double>(pressureBatchSize_) / tickSeconds
            : 0.0;

    pressureLastTickTime_ = now;
    pressureLastCompletedRequests_ = completedRequests;

    QString text;
    text += "多线程测试统计:\n";
    text += QString("线程数: %1\n").arg(pressureThreadCount_);
    text += QString("批量大小: %1\n").arg(pressureBatchSize_);
    text += QString("运行时间: %1 秒\n").arg(elapsedSeconds, 0, 'f', 2);
    text += QString("完成请求: %1\n")
                .arg(completedRequests * static_cast<long long>(pressureBatchSize_));
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
    checkCalcMean_->setEnabled(enabled);
}

void MainWindow::onGetModelInfo() {
    if (!ensureModelLoaded()) {
        return;
    }

    CStringGuard modelInfo(api_, api_.getModelInfo(modelIndex_));
    if (modelInfo.get() == nullptr) {
        outputText_->setPlainText(lastCError());
        return;
    }

    try {
        const json value = json::parse(modelInfo.get());
        if (value.contains("model_info")) {
            outputText_->setPlainText(QString::fromUtf8(value["model_info"].dump(2).c_str()));
        } else {
            outputText_->setPlainText(QString::fromUtf8(value.dump(2).c_str()));
        }
    } catch (...) {
        outputText_->setPlainText(QString::fromUtf8(modelInfo.get()));
    }
}

void MainWindow::onFreeModel() {
    stopPressureTest();
    freeCurrentModel();
    outputText_->setPlainText("模型已释放");
}

void MainWindow::onFreeAllModels() {
    stopPressureTest();
    if (!api_.isLoaded()) {
        reportError("释放模型失败", QString::fromStdWString(api_.lastError()));
        return;
    }
    api_.freeAllModels();
    modelIndex_ = -1;
    outputText_->setPlainText("所有模型已释放");
}

void MainWindow::onOpenDoc() {
    QDesktopServices::openUrl(QUrl("https://docs.dlcv.com.cn/deploy/sdk/csharp_sdk"));
}

void MainWindow::onCheckDog() {
    if (!api_.isLoaded()) {
        reportError("检查加密狗失败", QString::fromStdWString(api_.lastError()));
        return;
    }
    CStringGuard allInfo(api_, api_.getAllDogInfo());
    if (allInfo.get() == nullptr) {
        reportError("检查加密狗失败", lastCError());
        return;
    }

    try {
        const json value = json::parse(allInfo.get());
        const json sentinel =
            value.value("sentinel", json{{"devices", json::array()}, {"features", json::array()}});
        const json virbox =
            value.value("virbox", json{{"devices", json::array()}, {"features", json::array()}});

        QString text;
        text += QStringLiteral("Sentinel加密狗ID：\n") +
            QString::fromUtf8(sentinel.value("devices", json::array()).dump(2).c_str()) +
            QStringLiteral("\n\n");
        text += QStringLiteral("Sentinel加密狗特性：\n") +
            QString::fromUtf8(sentinel.value("features", json::array()).dump(2).c_str()) +
            QStringLiteral("\n\n");
        text += QStringLiteral("Virbox加密狗ID：\n") +
            QString::fromUtf8(virbox.value("devices", json::array()).dump(2).c_str()) +
            QStringLiteral("\n\n");
        text += QStringLiteral("Virbox加密狗特性：\n") +
            QString::fromUtf8(virbox.value("features", json::array()).dump(2).c_str());
        outputText_->setPlainText(text);
    } catch (const std::exception& ex) {
        reportError("检查加密狗失败", QString::fromLocal8Bit(ex.what()));
    }
}
