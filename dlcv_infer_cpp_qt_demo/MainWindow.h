#pragma once

#include <atomic>
#include <chrono>
#include <mutex>
#include <thread>
#include <vector>
#include <memory>

#include <QHash>
#include <QMainWindow>
#include <QSettings>

#include <opencv2/core.hpp>

#include "dlcv_infer.h"

class QCloseEvent;
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
    explicit MainWindow(QWidget* parent = nullptr);
    ~MainWindow() override = default;

protected:
    void closeEvent(QCloseEvent* event) override;

private:
    void setupUi();
    void bindSignals();
    void initializeDevicesAsync();

    int selectedDeviceId() const;
    bool ensureModelLoaded();
    bool ensureImageSelected();
    bool loadCurrentImage(cv::Mat& bgrImage, cv::Mat& rgbImage, bool silentOnDecodeFail) const;

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

    void startPressureTest();
    void stopPressureTest();
    void updatePressureTestStatistics();
    void setUiEnabledForPressureTest(bool enabled);

    std::unique_ptr<dlcv_infer::Model> model_;
    QSettings settings_{"dlcv", "DlcvDemoQt"};
    QHash<QString, int> deviceNameToId_;

    QString imagePath_;
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

    QLabel* labelDevice_ = nullptr;
    QLabel* labelBatchSize_ = nullptr;
    QLabel* labelThreshold_ = nullptr;
    QLabel* labelThreadCount_ = nullptr;

    QComboBox* comboDevice_ = nullptr;
    QSpinBox* spinBatchSize_ = nullptr;
    QSpinBox* spinThreadCount_ = nullptr;
    QDoubleSpinBox* spinThreshold_ = nullptr;

    QPlainTextEdit* outputText_ = nullptr;
    ImageViewerWidget* imageViewer_ = nullptr;

    // 压力测试状态
    bool pressureTestRunning_ = false;
    std::atomic<bool> pressureStopRequested_{false};
    std::atomic<bool> pressureError_{false};
    std::atomic<long long> pressureCompletedRequests_{0};
    std::atomic<long long> pressureTotalLatencyUs_{0};
    int pressureThreadCount_ = 1;
    int pressureBatchSize_ = 1;
    double pressureThreshold_ = 0.05;
    int pressureModelIndex_ = -1;
    cv::Mat pressureBaseImage_;
    QTimer* pressureTimer_ = nullptr;
    std::vector<std::thread> pressureThreads_;
    std::mutex pressureErrorMutex_;
    QString pressureErrorDetail_;
    std::chrono::steady_clock::time_point pressureStartTime_{};
    std::chrono::steady_clock::time_point pressureLastTickTime_{};
    long long pressureLastCompletedRequests_ = 0;
};
