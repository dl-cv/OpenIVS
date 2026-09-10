#pragma once

#include <atomic>
#include <chrono>
#include <mutex>
#include <thread>
#include <unordered_map>
#include <vector>
#include <memory>

#include <QHash>
#include <QMainWindow>
#include <QSettings>

#include <opencv2/core.hpp>

#include "dlcv_infer.h"

class QCloseEvent;
class QCheckBox;
class QComboBox;
class QDoubleSpinBox;
class QLabel;
class QPlainTextEdit;
class QPushButton;
class QSpinBox;
class QTimer;

class ImageViewerWidget;

class MainWindow : public QMainWindow {
public:
    struct UiTestReport {
        bool deviceInitializationCompleted = false;
        bool modelLoaded = false;
        bool modelInfoRead = false;
        bool inferenceCompleted = false;
        bool batchSizeMatched = false;
        bool renderCompleted = false;
        bool releaseCompleted = false;
        bool closeCompleted = false;
        int requestedBatchSize = 0;
        int effectiveDeviceId = -1;
        int availableDeviceCount = 0;
        int sampleCount = 0;
        int firstResultCount = 0;
        int renderWidth = 0;
        int renderHeight = 0;
        double inferenceMs = 0.0;
        QString modelInfoText;
        QString inferenceText;
        QString deviceWarning;
        QString errorText;
        dlcv_infer::json displayResults = dlcv_infer::json::array();
    };

    explicit MainWindow(QWidget* parent = nullptr, bool uiTest = false);
    bool runUiTest(
        const QString& modelPath,
        const QString& imagePath,
        int deviceId,
        bool hasDeviceId,
        int deviceTimeoutMs,
        int batchSize,
        double threshold,
        bool calcMean,
        UiTestReport& report);
    ~MainWindow() override;

protected:
    void closeEvent(QCloseEvent* event) override;

private:
    struct PressureNodeAggregate {
        int nodeId = -1;
        std::string nodeType;
        std::string nodeTitle;
        double totalMs = 0.0;
        long long count = 0;
        double AverageMs() const { return count > 0 ? (totalMs / static_cast<double>(count)) : 0.0; }
    };

    void setupUi();
    void bindSignals();
    void initializeDevicesAsync();
    bool waitForDeviceInitialization(int timeoutMs);
    void joinDeviceInitialization();

    int selectedDeviceId() const;
    bool loadModelFromPath(const QString& path, int deviceId);
    bool ensureModelLoaded();
    bool ensureImageSelected();
    bool loadCurrentImage(cv::Mat& image, bool silentOnDecodeFail) const;

    void reportError(const QString& title, const QString& detail);
    QString formatResultText(const dlcv_infer::Result& output) const;

    void onLoadModel();
    void onOpenImageInfer();
    void onInfer();
    void onInferJson();
    void onPressureTest();
    void onGetModelInfo();
    void onFreeModel();
    void onFreeAllModels();
    void onOpenDoc();
    void onCheckDog();

    void startPressureTest();
    void stopPressureTest();
    void updatePressureTestStatistics();
    void setUiEnabledForPressureTest(bool enabled);

    bool uiTest_ = false;
    bool lastActionSucceeded_ = false;
    QString lastErrorText_;
    int lastSampleCount_ = 0;
    int lastFirstResultCount_ = 0;
    double lastInferenceMs_ = 0.0;
    std::vector<dlcv_infer::ObjectResult> lastDisplayedResults_;
    std::atomic<bool> devicesReady_{false};
    QString deviceInitializationWarning_;
    std::thread deviceInitThread_;
    std::unique_ptr<dlcv_infer::Model> model_;
    QSettings settings_{"dlcv", "DlcvDemoQt"};
    QHash<QString, int> deviceNameToId_;

    QString imagePath_;
    QString modelPath_;
    cv::Mat currentBgrImage_;

    QPushButton* buttonLoadModel_ = nullptr;
    QPushButton* buttonGetModelInfo_ = nullptr;
    QPushButton* buttonOpenImage_ = nullptr;
    QPushButton* buttonInfer_ = nullptr;
    QPushButton* buttonInferJson_ = nullptr;
    QPushButton* buttonPressureTest_ = nullptr;
    QPushButton* buttonFreeModel_ = nullptr;
    QPushButton* buttonFreeAllModels_ = nullptr;
    QPushButton* buttonDoc_ = nullptr;
    QPushButton* buttonCheckDog_ = nullptr;

    QLabel* labelDevice_ = nullptr;
    QLabel* labelBatchSize_ = nullptr;
    QLabel* labelThreshold_ = nullptr;
    QLabel* labelThreadCount_ = nullptr;

    QComboBox* comboDevice_ = nullptr;
    QSpinBox* spinBatchSize_ = nullptr;
    QSpinBox* spinThreadCount_ = nullptr;
    QDoubleSpinBox* spinThreshold_ = nullptr;
    QCheckBox* checkCalcMean_ = nullptr;

    QPlainTextEdit* outputText_ = nullptr;
    ImageViewerWidget* imageViewer_ = nullptr;

    // 压力测试状态
    bool pressureTestRunning_ = false;
    std::atomic<bool> pressureStopRequested_{false};
    std::atomic<bool> pressureError_{false};
    std::atomic<long long> pressureCompletedRequests_{0};
    std::atomic<long long> pressureTotalLatencyUs_{0};
    std::atomic<long long> pressureTotalSdkLatencyUs_{0};
    std::atomic<long long> pressureTotalFlowLatencyUs_{0};
    int pressureThreadCount_ = 1;
    int pressureBatchSize_ = 1;
    double pressureThreshold_ = 0.5;
    bool pressureCalcMean_ = false;
    int pressureModelIndex_ = -1;
    cv::Mat pressureBaseImage_;
    QTimer* pressureTimer_ = nullptr;
    std::vector<std::thread> pressureThreads_;
    std::mutex pressureErrorMutex_;
    std::mutex pressureNodeStatsMutex_;
    std::unordered_map<std::string, PressureNodeAggregate> pressureNodeStats_;
    QString pressureErrorDetail_;
    std::chrono::steady_clock::time_point pressureStartTime_{};
    std::chrono::steady_clock::time_point pressureLastTickTime_{};
    long long pressureLastCompletedRequests_ = 0;
};
