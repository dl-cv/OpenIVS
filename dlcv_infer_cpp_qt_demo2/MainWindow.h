#pragma once

#include <atomic>
#include <functional>
#include <memory>
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

class ImageViewerWidget;

class MainWindow : public QMainWindow {
public:
    struct ExtractDetection {
        dlcv_infer::ObjectResult objectResult = dlcv_infer::ObjectResult(
            0, "", 0.0f, 0.0f, {}, false, cv::Mat(), true, false, -100.0f);
        cv::Rect2d mergeAabb;
        int order = 0;
    };

    explicit MainWindow(QWidget* parent = nullptr);
    ~MainWindow() override = default;

protected:
    void closeEvent(QCloseEvent* event) override;

private:
    struct PipelineProgressInfo {
        int percent = 0;
        QString stage;
    };

    struct SlidingWindowConfig {
        int windowWidth = 2560;
        int windowHeight = 2560;
        int overlapX = 1024;
        int overlapY = 1024;
    };

    struct RoiProcessResult {
        bool isValid = false;
        QString invalidReason;
        cv::Mat normalizedRoi;
        std::vector<double> normToFullAffine;
    };

    struct PipelineRunResult {
        dlcv_infer::Result displayResult = dlcv_infer::Result(std::vector<dlcv_infer::SampleResult>{});
        std::vector<dlcv_infer::ObjectResult> finalObjects;
        QStringList logs;
        int slidingWindowCount = 0;
        int mergedExtractCount = 0;
        int componentModelResultCount = 0;
        int icModelResultCount = 0;
    };

    void setupUi();
    void bindSignals();

    bool ensureExtractModelLoaded() const;
    bool ensureComponentModelLoaded() const;
    bool ensureIcModelLoaded() const;
    bool ensureImageSelected() const;
    bool loadCurrentImage(cv::Mat& bgrImage, cv::Mat& rgbImage) const;
    bool tryEnsureIdle(const QString& busyMessage);

    void appendLog(const QString& text);
    void setStatus(const QString& text, int progressValue);
    void reportError(const QString& title, const QString& detail);
    void setControlsEnabled(bool enabled);
    void updateBusyControlState();

    void onBrowseExtractModel();
    void onBrowseComponentModel();
    void onBrowseIcModel();
    void onBrowseImage();
    void onLoadExtractModel();
    void onLoadComponentModel();
    void onLoadIcModel();
    void onLoadAllModels();
    void onInfer();
    void onReleaseModels();

    bool loadExtractModel();
    bool loadComponentModel();
    bool loadIcModel();
    void releaseModels();
    void loadAllModelsSequentially(
        const QString& extractPath,
        const QString& componentPath,
        const QString& icPath,
        const std::function<void(const QString&)>& report);

    SlidingWindowConfig captureSlidingWindowConfig() const;
    void restoreUiSettings();
    void saveUiSettings();

    PipelineRunResult runPipeline(
        const cv::Mat& fullImageRgb,
        dlcv_infer::Model& extractModel,
        dlcv_infer::Model& componentModel,
        dlcv_infer::Model& icModel,
        const SlidingWindowConfig& config,
        const std::function<void(const PipelineProgressInfo&)>& progressCallback);

    std::vector<ExtractDetection> inferExtractModelOnWindows(
        const cv::Mat& fullImageRgb,
        dlcv_infer::Model& extractModel,
        const std::vector<cv::Rect>& windows,
        const std::function<void(const PipelineProgressInfo&)>& progressCallback);

    RoiProcessResult cropAndRotateRoi(
        const cv::Mat& fullImageRgb,
        const ExtractDetection& target,
        int normalizeAngle);

    std::vector<dlcv_infer::ObjectResult> inferDetectionModelAndMapBack(
        const RoiProcessResult& roiContext,
        const dlcv_infer::ObjectResult& extractFallback,
        bool useIcDetectModel,
        dlcv_infer::Model& componentModel,
        dlcv_infer::Model& icModel);

private:
    static constexpr int kDefaultWindowWidth = 2560;
    static constexpr int kDefaultWindowHeight = 2560;
    static constexpr int kDefaultOverlapX = 1024;
    static constexpr int kDefaultOverlapY = 1024;
    static constexpr int kFixedDeviceId = 0;

    std::unique_ptr<dlcv_infer::Model> extractModel_;
    std::unique_ptr<dlcv_infer::Model> componentModel_;
    std::unique_ptr<dlcv_infer::Model> icModel_;
    QSettings settings_{"dlcv", "DlcvDemoQt2"};

    QString imagePath_;
    cv::Mat currentBgrImage_;

    QLineEdit* editExtractModelPath_ = nullptr;
    QLineEdit* editComponentModelPath_ = nullptr;
    QLineEdit* editIcModelPath_ = nullptr;
    QLineEdit* editImagePath_ = nullptr;

    QPushButton* buttonBrowseExtractModel_ = nullptr;
    QPushButton* buttonBrowseComponentModel_ = nullptr;
    QPushButton* buttonBrowseIcModel_ = nullptr;
    QPushButton* buttonBrowseImage_ = nullptr;
    QPushButton* buttonLoadExtractModel_ = nullptr;
    QPushButton* buttonLoadComponentModel_ = nullptr;
    QPushButton* buttonLoadIcModel_ = nullptr;
    QPushButton* buttonInfer_ = nullptr;
    QPushButton* buttonReleaseModels_ = nullptr;
    QPushButton* buttonLoadAllModels_ = nullptr;

    QLabel* labelStatus_ = nullptr;
    QSpinBox* spinWindowWidth_ = nullptr;
    QSpinBox* spinWindowHeight_ = nullptr;
    QSpinBox* spinOverlapX_ = nullptr;
    QSpinBox* spinOverlapY_ = nullptr;
    QProgressBar* progressBar_ = nullptr;
    QPlainTextEdit* outputText_ = nullptr;
    ImageViewerWidget* imageViewer_ = nullptr;

    std::atomic<bool> inferenceRunning_{false};
    std::atomic<bool> modelLoading_{false};
    std::thread inferenceThread_;
    std::thread loadThread_;
};
