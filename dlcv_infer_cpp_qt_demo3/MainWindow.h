#pragma once

#include <atomic>
#include <chrono>
#include <functional>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

#include <QMainWindow>
#include <QSettings>
#include <QStringList>

#include <opencv2/core.hpp>

#include "dlcv_infer.h"

class QCloseEvent;
class QLabel;
class QLineEdit;
class QPlainTextEdit;
class QProgressBar;
class QPushButton;
class QSpinBox;
class QTimer;

class ImageViewerWidget;

class MainWindow : public QMainWindow {
public:
    explicit MainWindow(QWidget* parent = nullptr);
    ~MainWindow() override = default;

protected:
    void closeEvent(QCloseEvent* event) override;

private:
    struct PipelineProgressInfo {
        int percent = 0;
        QString stage;
    };

    struct CenteredCropContext {
        bool isValid = false;
        QString invalidReason;
        cv::Mat cropRgb;
        cv::Rect requestedRect;
        double translateX = 0.0;
        double translateY = 0.0;
    };

    struct PipelineRunResult {
        dlcv_infer::Result displayResult = dlcv_infer::Result(std::vector<dlcv_infer::SampleResult>{});
        std::vector<dlcv_infer::ObjectResult> finalObjects;
        QStringList logs;
        int model1ObjectCount = 0;
        int cropCount = 0;
        int model2BatchLimit = 1;
        int model2ThreadCount = 1;
        int finalResultCount = 0;
    };

    void setupUi();
    void bindSignals();
    void initializeDevicesAsync();
    int selectedDeviceId() const;

    bool ensureModel1Loaded() const;
    bool ensureModel2Loaded() const;
    bool ensureImageSelected() const;
    bool loadCurrentImage(cv::Mat& bgrImage, cv::Mat& rgbImage) const;

    void appendLog(const QString& text);
    void setStatus(const QString& text, int progressValue);
    void reportError(const QString& title, const QString& detail);
    void setControlsEnabled(bool enabled);
    void onGetModelInfo();

    void onBrowseModel1();
    void onBrowseModel2();
    void onBrowseImage();
    void onLoadModel1();
    void onLoadModel2();
    void onInfer();
    void onPressureTestToggle();
    void onReleaseModels();
    void onPressureTick();

    void startPressureTest();
    void stopPressureTest();
    void updatePressureStatistics();

    static bool tryClampObjectToImage(const dlcv_infer::ObjectResult& inputObj, int imageW, int imageH, dlcv_infer::ObjectResult& outputObj);
    static cv::Point2d getObjectCenter(const dlcv_infer::ObjectResult& obj);
    static CenteredCropContext buildCenteredCropContext(const cv::Mat& fullImageRgb, const cv::Point2d& center, int cropW, int cropH);
    static bool tryMapObjectByTranslate(
        const dlcv_infer::ObjectResult& localObj,
        double dx,
        double dy,
        dlcv_infer::ObjectResult& mappedObj);
    static std::vector<std::vector<CenteredCropContext>> splitIntoChunks(
        const std::vector<CenteredCropContext>& source,
        int chunkSize);
    static int normalizeThreadCount(int requested);
    static int getModelMaxBatchSize(dlcv_infer::Model& model);
    static dlcv_infer::Result buildDisplayResult(const std::vector<dlcv_infer::ObjectResult>& finalObjects);

    PipelineRunResult runPipeline(
        const cv::Mat& fullImageRgb,
        dlcv_infer::Model& model1,
        dlcv_infer::Model& model2,
        int requestedModel2Threads,
        const std::function<void(const PipelineProgressInfo&)>& progressCallback);

private:
    static constexpr int kFixedCropWidth = 128;
    static constexpr int kFixedCropHeight = 192;

    std::unique_ptr<dlcv_infer::Model> model1_;
    std::unique_ptr<dlcv_infer::Model> model2_;
    QSettings settings_{"dlcv", "DlcvDemoQt3"};
    int selectedDeviceId_ = -1;
    QString selectedDeviceName_ = "CPU";

    QString imagePath_;
    cv::Mat currentBgrImage_;

    QLineEdit* editModel1Path_ = nullptr;
    QLineEdit* editModel2Path_ = nullptr;
    QLineEdit* editImagePath_ = nullptr;

    QPushButton* buttonBrowseModel1_ = nullptr;
    QPushButton* buttonBrowseModel2_ = nullptr;
    QPushButton* buttonBrowseImage_ = nullptr;
    QPushButton* buttonLoadModel1_ = nullptr;
    QPushButton* buttonLoadModel2_ = nullptr;
    QPushButton* buttonGetModelInfo_ = nullptr;
    QPushButton* buttonInfer_ = nullptr;
    QPushButton* buttonPressureTest_ = nullptr;
    QPushButton* buttonReleaseModels_ = nullptr;

    QLabel* labelFixedCrop_ = nullptr;
    QLabel* labelModel2Threads_ = nullptr;
    QLabel* labelStatus_ = nullptr;

    QSpinBox* spinModel2Threads_ = nullptr;
    QProgressBar* progressBar_ = nullptr;
    QPlainTextEdit* outputText_ = nullptr;
    ImageViewerWidget* imageViewer_ = nullptr;

    std::atomic<bool> inferenceRunning_{false};
    std::thread inferenceThread_;
    std::atomic<bool> pressureRunning_{false};
    std::atomic<bool> pressureStopRequested_{false};
    std::atomic<long long> pressureRuns_{0};
    std::atomic<long long> pressureTotalLatencyUs_{0};
    std::atomic<int> pressureLastResultCount_{0};
    std::thread pressureThread_;
    std::mutex pressureErrorMutex_;
    QString pressureErrorDetail_;
    cv::Mat pressureRgbImage_;
    QTimer* pressureTimer_ = nullptr;
    std::chrono::steady_clock::time_point pressureStartTime_{};
    std::chrono::steady_clock::time_point pressureLastTickTime_{};
    long long pressureLastRuns_ = 0;
    int pressureThreadSnapshot_ = 1;
};
