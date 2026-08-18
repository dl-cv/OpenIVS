#include "MainWindow.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <exception>
#include <map>
#include <utility>

#include <QCloseEvent>
#include <QCoreApplication>
#include <QDialog>
#include <QFileDialog>
#include <QFileInfo>
#include <QGuiApplication>
#include <QHBoxLayout>
#include <QLabel>
#include <QLineEdit>
#include <QMessageBox>
#include <QMetaObject>
#include <QPlainTextEdit>
#include <QProgressBar>
#include <QPushButton>
#include <QRegularExpression>
#include <QScreen>
#include <QSpinBox>
#include <QSplitter>
#include <QVBoxLayout>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include "../dlcv_infer_cpp_qt_demo/ImageViewerWidget.h"

namespace {
using json = nlohmann::json;

constexpr double kMergeIouThreshold = 0.2;
constexpr double kMergeIosThreshold = 0.2;

void applyDialogInitialPath(QFileDialog& dialog, const QString& path) {
    if (path.isEmpty()) {
        return;
    }
    QFileInfo fileInfo(path);
    dialog.setDirectory(fileInfo.absolutePath());
    dialog.selectFile(fileInfo.fileName());
}

cv::Mat prepareImageForInference(const cv::Mat& decodedImage) {
    if (decodedImage.empty()) {
        return {};
    }
    if (decodedImage.channels() == 3) {
        cv::Mat rgb;
        cv::cvtColor(decodedImage, rgb, cv::COLOR_BGR2RGB);
        return rgb;
    }
    if (decodedImage.channels() == 4) {
        cv::Mat rgb;
        cv::cvtColor(decodedImage, rgb, cv::COLOR_BGRA2RGB);
        return rgb;
    }
    return decodedImage.clone();
}

QString categoryToQString(const std::string& name) {
    return QString::fromLocal8Bit(name.c_str());
}

std::string qStringToCategory(const QString& name) {
    const QByteArray bytes = name.toLocal8Bit();
    return std::string(bytes.constData(), static_cast<size_t>(bytes.size()));
}

int clampProgressPercent(int percent) {
    if (percent < 0) {
        return 0;
    }
    if (percent > 100) {
        return 100;
    }
    return percent;
}

std::vector<int> buildStartPositions(int totalSize, int windowSize, int overlap) {
    std::vector<int> positions;
    if (totalSize <= 0 || windowSize <= 0) {
        return positions;
    }
    if (windowSize >= totalSize) {
        positions.push_back(0);
        return positions;
    }

    const int step = std::max(1, windowSize - std::max(0, overlap));
    int current = 0;
    while (true) {
        if (current + windowSize >= totalSize) {
            const int tail = totalSize - windowSize;
            if (positions.empty() || positions.back() != tail) {
                positions.push_back(tail);
            }
            break;
        }
        positions.push_back(current);
        current += step;
    }
    return positions;
}

std::vector<cv::Rect> buildSlidingWindows(
    int imageWidth,
    int imageHeight,
    int configuredWindowWidth,
    int configuredWindowHeight,
    int overlapX,
    int overlapY) {
    std::vector<cv::Rect> windows;
    if (imageWidth <= 0 || imageHeight <= 0) {
        return windows;
    }

    const int windowW = std::min(std::max(1, configuredWindowWidth), std::max(1, imageWidth));
    const int windowH = std::min(std::max(1, configuredWindowHeight), std::max(1, imageHeight));
    const std::vector<int> xs = buildStartPositions(imageWidth, windowW, overlapX);
    const std::vector<int> ys = buildStartPositions(imageHeight, windowH, overlapY);
    windows.reserve(xs.size() * ys.size());
    for (int y : ys) {
        for (int x : xs) {
            windows.emplace_back(x, y, windowW, windowH);
        }
    }
    return windows;
}

int normalizeRightAngle(int angle) {
    int normalized = angle % 360;
    if (normalized < 0) {
        normalized += 360;
    }
    if (normalized != 0 && normalized != 90 && normalized != 180 && normalized != 270) {
        return 0;
    }
    return normalized;
}

void parseCategoryAndAngle(const QString& categoryName, QString& baseName, int& angle) {
    const QString normalized = categoryName.trimmed();
    angle = 0;
    if (normalized.isEmpty()) {
        baseName.clear();
        return;
    }

    static const QRegularExpression kCategoryAngleRegex("^(.*?)(0|90|180|270)$");
    const QRegularExpressionMatch match = kCategoryAngleRegex.match(normalized);
    if (!match.hasMatch()) {
        baseName = normalized;
        return;
    }

    bool ok = false;
    int parsedAngle = match.captured(2).toInt(&ok);
    if (!ok) {
        parsedAngle = 0;
    }
    baseName = match.captured(1);
    if (baseName.trimmed().isEmpty()) {
        baseName = normalized;
    }
    angle = normalizeRightAngle(parsedAngle);
}

bool shouldUseIcDetectModel(const QString& baseName) {
    return baseName.compare(QStringLiteral("IC"), Qt::CaseInsensitive) == 0
        || baseName.compare(QStringLiteral("IC-BGA"), Qt::CaseInsensitive) == 0
        || baseName.compare(QStringLiteral("IC-排阻"), Qt::CaseInsensitive) == 0
        || baseName == QStringLiteral("座子")
        || baseName == QStringLiteral("开关")
        || baseName == QStringLiteral("晶振");
}

bool shouldMapBackToExtractCategory(const QString& detectionCategoryName) {
    const QString normalized = detectionCategoryName.trimmed();
    if (normalized.isEmpty()) {
        return false;
    }
    return normalized == QStringLiteral("元件")
        || normalized.compare(QStringLiteral("IC"), Qt::CaseInsensitive) == 0;
}

cv::Rect2d getAabbFromObject(const dlcv_infer::ObjectResult& obj) {
    if (obj.bbox.size() < 4) {
        return {};
    }
    const double w = std::abs(obj.bbox[2]);
    const double h = std::abs(obj.bbox[3]);
    if (w <= 0.0 || h <= 0.0) {
        return {};
    }
    if (obj.withAngle || obj.bbox.size() == 5) {
        const double cx = obj.bbox[0];
        const double cy = obj.bbox[1];
        return cv::Rect2d(cx - w / 2.0, cy - h / 2.0, w, h);
    }
    return cv::Rect2d(obj.bbox[0], obj.bbox[1], w, h);
}

double getObjectArea(const dlcv_infer::ObjectResult& obj) {
    if (obj.bbox.size() >= 4) {
        return std::abs(obj.bbox[2] * obj.bbox[3]);
    }
    return obj.area > 0.0f ? static_cast<double>(obj.area) : 0.0;
}

double intersectionArea(const cv::Rect2d& a, const cv::Rect2d& b) {
    const double x1 = std::max(a.x, b.x);
    const double y1 = std::max(a.y, b.y);
    const double x2 = std::min(a.x + a.width, b.x + b.width);
    const double y2 = std::min(a.y + a.height, b.y + b.height);
    if (x2 <= x1 || y2 <= y1) {
        return 0.0;
    }
    return (x2 - x1) * (y2 - y1);
}

cv::Rect2d unionRect(const cv::Rect2d& a, const cv::Rect2d& b) {
    const double minX = std::min(a.x, b.x);
    const double minY = std::min(a.y, b.y);
    const double maxX = std::max(a.x + a.width, b.x + b.width);
    const double maxY = std::max(a.y + a.height, b.y + b.height);
    return cv::Rect2d(minX, minY, std::max(0.0, maxX - minX), std::max(0.0, maxY - minY));
}

bool canMerge(const cv::Rect2d& a, const cv::Rect2d& b) {
    const double inter = intersectionArea(a, b);
    if (inter <= 0.0) {
        return false;
    }
    const double areaA = std::max(0.0, a.width) * std::max(0.0, a.height);
    const double areaB = std::max(0.0, b.width) * std::max(0.0, b.height);
    const double unionArea = areaA + areaB - inter;
    if (unionArea <= 0.0) {
        return false;
    }
    const double iou = inter / unionArea;
    const double ios = inter / std::max(1e-6, std::min(areaA, areaB));
    return iou >= kMergeIouThreshold || ios >= kMergeIosThreshold;
}

dlcv_infer::ObjectResult buildAabbObject(const dlcv_infer::ObjectResult& source, const cv::Rect2d& aabb) {
    std::vector<double> bbox{
        aabb.x,
        aabb.y,
        std::max(0.0, aabb.width),
        std::max(0.0, aabb.height)};
    return dlcv_infer::ObjectResult(
        source.categoryId,
        source.categoryName,
        source.score,
        static_cast<float>(bbox[2] * bbox[3]),
        bbox,
        false,
        cv::Mat(),
        true,
        false,
        -100.0f);
}

bool shouldPreferRepresentative(
    double areaCandidate,
    float scoreCandidate,
    int orderCandidate,
    double areaCurrent,
    float scoreCurrent,
    int orderCurrent) {
    if (areaCandidate > areaCurrent + 1e-6) {
        return true;
    }
    if (std::abs(areaCandidate - areaCurrent) <= 1e-6) {
        if (scoreCandidate > scoreCurrent + 1e-6f) {
            return true;
        }
        if (std::abs(scoreCandidate - scoreCurrent) <= 1e-6f && orderCandidate < orderCurrent) {
            return true;
        }
    }
    return false;
}

std::vector<MainWindow::ExtractDetection> mergeExtractResults(
    const std::vector<MainWindow::ExtractDetection>& fullImageDetections) {
    std::vector<MainWindow::ExtractDetection> mergedAll;
    if (fullImageDetections.empty()) {
        return mergedAll;
    }

    std::map<std::string, std::vector<MainWindow::ExtractDetection>> groups;
    for (const auto& item : fullImageDetections) {
        groups[item.objectResult.categoryName].push_back(item);
    }

    for (auto& groupPair : groups) {
        auto& group = groupPair.second;
        std::sort(group.begin(), group.end(), [](const MainWindow::ExtractDetection& a, const MainWindow::ExtractDetection& b) {
            const double areaA = getObjectArea(a.objectResult);
            const double areaB = getObjectArea(b.objectResult);
            if (areaA != areaB) {
                return areaA > areaB;
            }
            if (a.objectResult.score != b.objectResult.score) {
                return a.objectResult.score > b.objectResult.score;
            }
            return a.order < b.order;
        });

        std::vector<MainWindow::ExtractDetection> clusters;
        for (const auto& detection : group) {
            bool merged = false;
            for (auto& cluster : clusters) {
                if (!canMerge(cluster.mergeAabb, detection.mergeAabb)) {
                    continue;
                }
                cluster.mergeAabb = unionRect(cluster.mergeAabb, detection.mergeAabb);
                if (shouldPreferRepresentative(
                        getObjectArea(detection.objectResult),
                        detection.objectResult.score,
                        detection.order,
                        getObjectArea(cluster.objectResult),
                        cluster.objectResult.score,
                        cluster.order)) {
                    cluster.objectResult = detection.objectResult;
                    cluster.order = std::min(cluster.order, detection.order);
                }
                merged = true;
                break;
            }
            if (!merged) {
                clusters.push_back(detection);
            }
        }

        for (auto& cluster : clusters) {
            if (!cluster.objectResult.withAngle || cluster.objectResult.bbox.size() < 4) {
                cluster.objectResult = buildAabbObject(cluster.objectResult, cluster.mergeAabb);
            }
        }
        mergedAll.insert(mergedAll.end(), clusters.begin(), clusters.end());
    }

    std::sort(mergedAll.begin(), mergedAll.end(), [](const MainWindow::ExtractDetection& a, const MainWindow::ExtractDetection& b) {
        return a.order < b.order;
    });
    return mergedAll;
}

dlcv_infer::ObjectResult liftExtractObjectToFull(const dlcv_infer::ObjectResult& localObject, const cv::Rect& windowRect) {
    if (localObject.bbox.size() < 4) {
        return localObject;
    }
    std::vector<double> bbox = localObject.bbox;
    bbox[0] += windowRect.x;
    bbox[1] += windowRect.y;
    bool withAngle = localObject.withAngle || bbox.size() == 5;
    float angle = localObject.angle;
    if (!withAngle) {
        angle = -100.0f;
    } else if (std::abs(angle + 100.0f) < 1e-4f && bbox.size() >= 5) {
        angle = static_cast<float>(bbox[4]);
    }
    return dlcv_infer::ObjectResult(
        localObject.categoryId,
        localObject.categoryName,
        localObject.score,
        localObject.area,
        bbox,
        false,
        cv::Mat(),
        true,
        withAngle,
        angle);
}

cv::Rect clampRectToImage(const cv::Rect2d& rect, int imageWidth, int imageHeight) {
    int left = static_cast<int>(std::floor(rect.x));
    int top = static_cast<int>(std::floor(rect.y));
    int right = static_cast<int>(std::ceil(rect.x + rect.width));
    int bottom = static_cast<int>(std::ceil(rect.y + rect.height));
    left = std::max(0, std::min(imageWidth - 1, left));
    top = std::max(0, std::min(imageHeight - 1, top));
    right = std::max(left + 1, std::min(imageWidth, right));
    bottom = std::max(top + 1, std::min(imageHeight, bottom));
    return cv::Rect(left, top, right - left, bottom - top);
}

std::vector<double> matrixFromAffineMat(const cv::Mat& affine) {
    return {
        affine.at<double>(0, 0),
        affine.at<double>(0, 1),
        affine.at<double>(0, 2),
        affine.at<double>(1, 0),
        affine.at<double>(1, 1),
        affine.at<double>(1, 2)};
}

bool tryBuildRotatedCrop(
    const cv::Mat& fullImage,
    const dlcv_infer::ObjectResult& obj,
    int padding,
    cv::Mat& roi,
    std::vector<double>& fullToCropAffine) {
    roi.release();
    fullToCropAffine.clear();
    if (obj.bbox.size() < 4) {
        return false;
    }
    const bool hasAngle = obj.withAngle || obj.bbox.size() == 5;
    if (!hasAngle) {
        return false;
    }

    const double cx = obj.bbox[0];
    const double cy = obj.bbox[1];
    const double w = std::abs(obj.bbox[2]) + std::max(0, padding) * 2.0;
    const double h = std::abs(obj.bbox[3]) + std::max(0, padding) * 2.0;
    if (w <= 1.0 || h <= 1.0) {
        return false;
    }

    double angleRad = obj.angle;
    if (std::abs(angleRad + 100.0) < 1e-6 && obj.bbox.size() >= 5) {
        angleRad = obj.bbox[4];
    }
    if (std::abs(angleRad + 100.0) < 1e-6) {
        angleRad = 0.0;
    }
    const double angleDeg = angleRad * 180.0 / CV_PI;
    cv::Mat rotMat = cv::getRotationMatrix2D(cv::Point2f(static_cast<float>(cx), static_cast<float>(cy)), angleDeg, 1.0);
    rotMat.at<double>(0, 2) += w / 2.0 - cx;
    rotMat.at<double>(1, 2) += h / 2.0 - cy;
    const int outW = std::max(1, static_cast<int>(std::llround(w)));
    const int outH = std::max(1, static_cast<int>(std::llround(h)));
    cv::warpAffine(fullImage, roi, rotMat, cv::Size(outW, outH));
    if (roi.empty()) {
        roi.release();
        return false;
    }
    fullToCropAffine = matrixFromAffineMat(rotMat);
    return true;
}

cv::Mat rotateRoiByRightAngle(const cv::Mat& roi, int angle) {
    if (angle == 0) {
        return roi.clone();
    }
    cv::Mat rotated;
    if (angle == 90) {
        cv::rotate(roi, rotated, cv::ROTATE_90_COUNTERCLOCKWISE);
        return rotated;
    }
    if (angle == 180) {
        cv::rotate(roi, rotated, cv::ROTATE_180);
        return rotated;
    }
    if (angle == 270) {
        cv::rotate(roi, rotated, cv::ROTATE_90_CLOCKWISE);
        return rotated;
    }
    return roi.clone();
}

std::vector<double> buildRightAngleAffine(int srcW, int srcH, int angle, int& dstW, int& dstH) {
    angle = normalizeRightAngle(angle);
    if (angle == 90) {
        dstW = srcH;
        dstH = srcW;
        return {0.0, 1.0, 0.0, -1.0, 0.0, static_cast<double>(srcW)};
    }
    if (angle == 180) {
        dstW = srcW;
        dstH = srcH;
        return {-1.0, 0.0, static_cast<double>(srcW), 0.0, -1.0, static_cast<double>(srcH)};
    }
    if (angle == 270) {
        dstW = srcH;
        dstH = srcW;
        return {0.0, -1.0, static_cast<double>(srcH), 1.0, 0.0, 0.0};
    }
    dstW = srcW;
    dstH = srcH;
    return {1.0, 0.0, 0.0, 0.0, 1.0, 0.0};
}

std::vector<double> invertAffine2x3(const std::vector<double>& a) {
    if (a.size() != 6) {
        return {1.0, 0.0, 0.0, 0.0, 1.0, 0.0};
    }
    const double det = a[0] * a[4] - a[1] * a[3];
    if (std::abs(det) < 1e-12) {
        return {1.0, 0.0, 0.0, 0.0, 1.0, 0.0};
    }
    const double inv00 = a[4] / det;
    const double inv01 = -a[1] / det;
    const double inv10 = -a[3] / det;
    const double inv11 = a[0] / det;
    const double inv02 = -(inv00 * a[2] + inv01 * a[5]);
    const double inv12 = -(inv10 * a[2] + inv11 * a[5]);
    return {inv00, inv01, inv02, inv10, inv11, inv12};
}

std::vector<double> composeAffine(const std::vector<double>& first, const std::vector<double>& second) {
    auto to3x3 = [](const std::vector<double>& a) {
        if (a.size() != 6) {
            return std::vector<double>{1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0};
        }
        return std::vector<double>{a[0], a[1], a[2], a[3], a[4], a[5], 0.0, 0.0, 1.0};
    };
    const std::vector<double> m1 = to3x3(first);
    const std::vector<double> m2 = to3x3(second);
    std::vector<double> m(9, 0.0);
    for (int r = 0; r < 3; ++r) {
        for (int c = 0; c < 3; ++c) {
            m[r * 3 + c] =
                m1[r * 3 + 0] * m2[0 * 3 + c] +
                m1[r * 3 + 1] * m2[1 * 3 + c] +
                m1[r * 3 + 2] * m2[2 * 3 + c];
        }
    }
    return {m[0], m[1], m[2], m[3], m[4], m[5]};
}

cv::Point2d applyAffine(const std::vector<double>& a, const cv::Point2d& p) {
    return cv::Point2d(a[0] * p.x + a[1] * p.y + a[2], a[3] * p.x + a[4] * p.y + a[5]);
}

bool tryMapObjectToFull(
    const dlcv_infer::ObjectResult& obj,
    const std::vector<double>& normToFullAffine,
    dlcv_infer::ObjectResult& mapped) {
    if (obj.bbox.size() < 4) {
        return false;
    }

    std::vector<cv::Point2d> points;
    if (obj.withAngle || obj.bbox.size() == 5) {
        const double cx = obj.bbox[0];
        const double cy = obj.bbox[1];
        const double w = std::abs(obj.bbox[2]);
        const double h = std::abs(obj.bbox[3]);
        if (w <= 0.0 || h <= 0.0) {
            return false;
        }
        double angle = obj.angle;
        if (std::abs(angle + 100.0) < 1e-6 && obj.bbox.size() >= 5) {
            angle = obj.bbox[4];
        }
        if (std::abs(angle + 100.0) < 1e-6) {
            angle = 0.0;
        }
        const double cosV = std::cos(angle);
        const double sinV = std::sin(angle);
        const cv::Point2d offsets[] = {
            {-w / 2.0, -h / 2.0},
            {w / 2.0, -h / 2.0},
            {w / 2.0, h / 2.0},
            {-w / 2.0, h / 2.0}};
        for (const auto& offset : offsets) {
            points.emplace_back(
                cx + offset.x * cosV - offset.y * sinV,
                cy + offset.x * sinV + offset.y * cosV);
        }
    } else {
        const double x = obj.bbox[0];
        const double y = obj.bbox[1];
        const double w = std::abs(obj.bbox[2]);
        const double h = std::abs(obj.bbox[3]);
        if (w <= 0.0 || h <= 0.0) {
            return false;
        }
        points.emplace_back(x, y);
        points.emplace_back(x + w, y);
        points.emplace_back(x + w, y + h);
        points.emplace_back(x, y + h);
    }

    double minX = 1e300;
    double minY = 1e300;
    double maxX = -1e300;
    double maxY = -1e300;
    for (const auto& p : points) {
        const cv::Point2d mappedPoint = applyAffine(normToFullAffine, p);
        minX = std::min(minX, mappedPoint.x);
        minY = std::min(minY, mappedPoint.y);
        maxX = std::max(maxX, mappedPoint.x);
        maxY = std::max(maxY, mappedPoint.y);
    }
    const double outW = maxX - minX;
    const double outH = maxY - minY;
    if (outW <= 1e-6 || outH <= 1e-6) {
        return false;
    }
    mapped = dlcv_infer::ObjectResult(
        obj.categoryId,
        obj.categoryName,
        obj.score,
        static_cast<float>(outW * outH),
        std::vector<double>{minX, minY, outW, outH},
        false,
        cv::Mat(),
        true,
        false,
        -100.0f);
    return true;
}

dlcv_infer::ObjectResult resolveFinalDetectionObject(
    const dlcv_infer::ObjectResult& mappedObject,
    const dlcv_infer::ObjectResult& extractFallback) {
    if (!shouldMapBackToExtractCategory(categoryToQString(mappedObject.categoryName))) {
        return mappedObject;
    }
    return dlcv_infer::ObjectResult(
        extractFallback.categoryId,
        extractFallback.categoryName,
        mappedObject.score,
        mappedObject.area,
        mappedObject.bbox,
        mappedObject.withMask,
        mappedObject.mask,
        mappedObject.withBbox,
        mappedObject.withAngle,
        mappedObject.angle);
}

dlcv_infer::Result buildDisplayResult(const std::vector<dlcv_infer::ObjectResult>& finalObjects) {
    return dlcv_infer::Result(std::vector<dlcv_infer::SampleResult>{dlcv_infer::SampleResult(finalObjects)});
}

QString buildObjectLocationText(const dlcv_infer::ObjectResult& obj) {
    if (obj.bbox.size() < 4) {
        return "rect=(N/A)";
    }
    if (obj.withAngle || obj.bbox.size() == 5) {
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
}

int readIntSetting(const QSettings& settings, const char* key, int fallback, int minimum, int maximum) {
    const int configured = settings.value(key, fallback).toInt();
    const int value = configured > 0 ? configured : fallback;
    return std::max(minimum, std::min(maximum, value));
}

}  // namespace

MainWindow::MainWindow(QWidget* parent) : QMainWindow(parent) {
    setupUi();
    bindSignals();
    restoreUiSettings();
    setStatus("空闲", 0);
}

void MainWindow::closeEvent(QCloseEvent* event) {
    if (inferenceRunning_.load() || modelLoading_.load()) {
        QMessageBox::information(this, "提示", "当前正在执行推理或加载模型，请等待完成后再关闭。");
        event->ignore();
        return;
    }
    saveUiSettings();
    settings_.setValue("Geometry", saveGeometry());
    settings_.setValue("WindowState", saveState());
    if (inferenceThread_.joinable()) {
        inferenceThread_.join();
    }
    if (loadThread_.joinable()) {
        loadThread_.join();
    }
    releaseModels();
    dlcv_infer::Utils::FreeAllModels();
    QMainWindow::closeEvent(event);
}

void MainWindow::setupUi() {
    setWindowTitle("C++测试程序2");
    setMinimumSize(1200, 900);

    QWidget* centralWidget = new QWidget(this);
    auto* rootLayout = new QVBoxLayout(centralWidget);
    rootLayout->setContentsMargins(12, 12, 12, 12);
    rootLayout->setSpacing(8);

    editExtractModelPath_ = new QLineEdit(this);
    editComponentModelPath_ = new QLineEdit(this);
    editIcModelPath_ = new QLineEdit(this);
    editImagePath_ = new QLineEdit(this);

    buttonBrowseExtractModel_ = new QPushButton("浏览...", this);
    buttonBrowseComponentModel_ = new QPushButton("浏览...", this);
    buttonBrowseIcModel_ = new QPushButton("浏览...", this);
    buttonBrowseImage_ = new QPushButton("浏览...", this);
    buttonLoadExtractModel_ = new QPushButton("加载模型", this);
    buttonLoadComponentModel_ = new QPushButton("加载模型", this);
    buttonLoadIcModel_ = new QPushButton("加载模型", this);
    buttonInfer_ = new QPushButton("执行推理", this);
    buttonReleaseModels_ = new QPushButton("释放模型", this);
    buttonLoadAllModels_ = new QPushButton("一键加载三个模型", this);

    spinWindowWidth_ = new QSpinBox(this);
    spinWindowHeight_ = new QSpinBox(this);
    spinOverlapX_ = new QSpinBox(this);
    spinOverlapY_ = new QSpinBox(this);
    spinWindowWidth_->setRange(1, 30000);
    spinWindowHeight_->setRange(1, 30000);
    spinOverlapX_->setRange(0, 30000);
    spinOverlapY_->setRange(0, 30000);
    spinWindowWidth_->setValue(kDefaultWindowWidth);
    spinWindowHeight_->setValue(kDefaultWindowHeight);
    spinOverlapX_->setValue(kDefaultOverlapX);
    spinOverlapY_->setValue(kDefaultOverlapY);

    progressBar_ = new QProgressBar(this);
    progressBar_->setRange(0, 100);
    progressBar_->setValue(0);
    labelStatus_ = new QLabel("0% 空闲", this);

    constexpr int kControlHeight = 34;
    auto setupButton = [kControlHeight](QPushButton* button, int minWidth) {
        button->setMinimumWidth(minWidth);
        button->setFixedHeight(kControlHeight);
    };
    setupButton(buttonBrowseExtractModel_, 90);
    setupButton(buttonBrowseComponentModel_, 90);
    setupButton(buttonBrowseIcModel_, 90);
    setupButton(buttonBrowseImage_, 90);
    setupButton(buttonLoadExtractModel_, 120);
    setupButton(buttonLoadComponentModel_, 120);
    setupButton(buttonLoadIcModel_, 120);
    setupButton(buttonInfer_, 120);
    setupButton(buttonReleaseModels_, 120);
    setupButton(buttonLoadAllModels_, 180);

    editExtractModelPath_->setPlaceholderText("请选择元件提取模型路径");
    editComponentModelPath_->setPlaceholderText("请选择元件检测模型路径");
    editIcModelPath_->setPlaceholderText("请选择IC检测模型路径");
    editImagePath_->setPlaceholderText("请选择图片路径");
    editExtractModelPath_->setMinimumHeight(kControlHeight);
    editComponentModelPath_->setMinimumHeight(kControlHeight);
    editIcModelPath_->setMinimumHeight(kControlHeight);
    editImagePath_->setMinimumHeight(kControlHeight);
    spinWindowWidth_->setMinimumHeight(kControlHeight);
    spinWindowHeight_->setMinimumHeight(kControlHeight);
    spinOverlapX_->setMinimumHeight(kControlHeight);
    spinOverlapY_->setMinimumHeight(kControlHeight);

    auto* row1 = new QHBoxLayout();
    row1->setSpacing(8);
    row1->addWidget(new QLabel("元件提取模型", this));
    row1->addWidget(editExtractModelPath_, 1);
    row1->addWidget(buttonBrowseExtractModel_);
    row1->addWidget(buttonLoadExtractModel_);

    auto* row2 = new QHBoxLayout();
    row2->setSpacing(8);
    row2->addWidget(new QLabel("元件检测模型", this));
    row2->addWidget(editComponentModelPath_, 1);
    row2->addWidget(buttonBrowseComponentModel_);
    row2->addWidget(buttonLoadComponentModel_);

    auto* row3 = new QHBoxLayout();
    row3->setSpacing(8);
    row3->addWidget(new QLabel("IC检测模型", this));
    row3->addWidget(editIcModelPath_, 1);
    row3->addWidget(buttonBrowseIcModel_);
    row3->addWidget(buttonLoadIcModel_);

    auto* row4 = new QHBoxLayout();
    row4->setSpacing(8);
    row4->addWidget(new QLabel("图片路径", this));
    row4->addWidget(editImagePath_, 1);
    row4->addWidget(buttonBrowseImage_);
    row4->addWidget(buttonInfer_);

    auto* row5 = new QHBoxLayout();
    row5->setSpacing(8);
    row5->addWidget(new QLabel("窗口宽", this));
    row5->addWidget(spinWindowWidth_);
    row5->addWidget(new QLabel("窗口高", this));
    row5->addWidget(spinWindowHeight_);
    row5->addWidget(new QLabel("水平重叠", this));
    row5->addWidget(spinOverlapX_);
    row5->addWidget(new QLabel("垂直重叠", this));
    row5->addWidget(spinOverlapY_);
    row5->addWidget(buttonReleaseModels_);
    row5->addWidget(buttonLoadAllModels_);
    row5->addStretch(1);

    auto* row6 = new QHBoxLayout();
    row6->setSpacing(8);
    row6->addWidget(new QLabel("推理进度", this));
    row6->addWidget(progressBar_, 1);
    row6->addWidget(labelStatus_, 1);

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
    rootLayout->addLayout(row6);
    rootLayout->addWidget(splitter, 1);
    setCentralWidget(centralWidget);

    if (settings_.contains("Geometry")) {
        restoreGeometry(settings_.value("Geometry").toByteArray());
        restoreState(settings_.value("WindowState").toByteArray());
        bool visible = false;
        const QRect geo = frameGeometry();
        for (QScreen* screen : QGuiApplication::screens()) {
            if (screen->availableGeometry().intersects(geo)) {
                visible = true;
                break;
            }
        }
        if (!visible) {
            if (QScreen* screen = QGuiApplication::primaryScreen()) {
                move(screen->availableGeometry().center() - rect().center());
            }
        }
    } else if (QScreen* screen = QGuiApplication::primaryScreen()) {
        move(screen->availableGeometry().center() - rect().center());
    }
}

void MainWindow::bindSignals() {
    connect(buttonBrowseExtractModel_, &QPushButton::clicked, this, &MainWindow::onBrowseExtractModel);
    connect(buttonBrowseComponentModel_, &QPushButton::clicked, this, &MainWindow::onBrowseComponentModel);
    connect(buttonBrowseIcModel_, &QPushButton::clicked, this, &MainWindow::onBrowseIcModel);
    connect(buttonBrowseImage_, &QPushButton::clicked, this, &MainWindow::onBrowseImage);
    connect(buttonLoadExtractModel_, &QPushButton::clicked, this, &MainWindow::onLoadExtractModel);
    connect(buttonLoadComponentModel_, &QPushButton::clicked, this, &MainWindow::onLoadComponentModel);
    connect(buttonLoadIcModel_, &QPushButton::clicked, this, &MainWindow::onLoadIcModel);
    connect(buttonLoadAllModels_, &QPushButton::clicked, this, &MainWindow::onLoadAllModels);
    connect(buttonInfer_, &QPushButton::clicked, this, &MainWindow::onInfer);
    connect(buttonReleaseModels_, &QPushButton::clicked, this, &MainWindow::onReleaseModels);
}

bool MainWindow::tryEnsureIdle(const QString& busyMessage) {
    if (modelLoading_.load()) {
        outputText_->setPlainText("当前正在加载模型，请稍后再试。");
        return false;
    }
    if (inferenceRunning_.load()) {
        outputText_->setPlainText(busyMessage);
        return false;
    }
    return true;
}

void MainWindow::appendLog(const QString& text) {
    if (text.trimmed().isEmpty()) {
        return;
    }
    const QString old = outputText_->toPlainText();
    outputText_->setPlainText(old.isEmpty() ? text : (old + "\n" + text));
}

void MainWindow::setStatus(const QString& text, int progressValue) {
    const int percent = clampProgressPercent(progressValue);
    progressBar_->setValue(percent);
    labelStatus_->setText(text.trimmed().isEmpty() ? QString("%1%").arg(percent) : QString("%1% %2").arg(percent).arg(text));
}

void MainWindow::reportError(const QString& title, const QString& detail) {
    appendLog(title + "\n" + detail);
    QMessageBox::critical(this, "错误", title + ": " + detail);
}

void MainWindow::setControlsEnabled(bool enabled) {
    buttonBrowseExtractModel_->setEnabled(enabled);
    buttonBrowseComponentModel_->setEnabled(enabled);
    buttonBrowseIcModel_->setEnabled(enabled);
    buttonBrowseImage_->setEnabled(enabled);
    buttonLoadExtractModel_->setEnabled(enabled);
    buttonLoadComponentModel_->setEnabled(enabled);
    buttonLoadIcModel_->setEnabled(enabled);
    buttonInfer_->setEnabled(enabled);
    buttonReleaseModels_->setEnabled(enabled);
    buttonLoadAllModels_->setEnabled(enabled);
    editExtractModelPath_->setEnabled(enabled);
    editComponentModelPath_->setEnabled(enabled);
    editIcModelPath_->setEnabled(enabled);
    editImagePath_->setEnabled(enabled);
    spinWindowWidth_->setEnabled(enabled);
    spinWindowHeight_->setEnabled(enabled);
    spinOverlapX_->setEnabled(enabled);
    spinOverlapY_->setEnabled(enabled);
}

void MainWindow::updateBusyControlState() {
    setControlsEnabled(!(inferenceRunning_.load() || modelLoading_.load()));
}

bool MainWindow::ensureExtractModelLoaded() const {
    if (extractModel_) {
        return true;
    }
    QMessageBox::information(const_cast<MainWindow*>(this), "提示", "请先加载元件提取模型。");
    return false;
}

bool MainWindow::ensureComponentModelLoaded() const {
    if (componentModel_) {
        return true;
    }
    QMessageBox::information(const_cast<MainWindow*>(this), "提示", "请先加载元件检测模型。");
    return false;
}

bool MainWindow::ensureIcModelLoaded() const {
    if (icModel_) {
        return true;
    }
    QMessageBox::information(const_cast<MainWindow*>(this), "提示", "请先加载IC检测模型。");
    return false;
}

bool MainWindow::ensureImageSelected() const {
    if (!imagePath_.trimmed().isEmpty() && QFileInfo::exists(imagePath_)) {
        return true;
    }
    QMessageBox::information(const_cast<MainWindow*>(this), "提示", "请先选择有效图片。");
    return false;
}

bool MainWindow::loadCurrentImage(cv::Mat& bgrImage, cv::Mat& rgbImage) const {
    bgrImage = cv::imread(imagePath_.toLocal8Bit().toStdString(), cv::IMREAD_UNCHANGED);
    if (bgrImage.empty()) {
        return false;
    }
    rgbImage = prepareImageForInference(bgrImage);
    return !rgbImage.empty();
}

void MainWindow::onBrowseExtractModel() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能切换元件提取模型。")) {
        return;
    }
    QFileDialog dialog(this, "选择元件提取模型");
    dialog.setNameFilter("AI模型 (*.dvt *.dvo *.dvp *.dvst *.dvso *.dvsp);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);
    applyDialogInitialPath(dialog, editExtractModelPath_->text().trimmed());
    if (dialog.exec() != QDialog::Accepted) {
        return;
    }
    editExtractModelPath_->setText(dialog.selectedFiles().front());
    saveUiSettings();
    onLoadExtractModel();
}

void MainWindow::onBrowseComponentModel() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能切换元件检测模型。")) {
        return;
    }
    QFileDialog dialog(this, "选择元件检测模型");
    dialog.setNameFilter("AI模型 (*.dvt *.dvo *.dvp *.dvst *.dvso *.dvsp);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);
    applyDialogInitialPath(dialog, editComponentModelPath_->text().trimmed());
    if (dialog.exec() != QDialog::Accepted) {
        return;
    }
    editComponentModelPath_->setText(dialog.selectedFiles().front());
    saveUiSettings();
    onLoadComponentModel();
}

void MainWindow::onBrowseIcModel() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能切换IC检测模型。")) {
        return;
    }
    QFileDialog dialog(this, "选择IC检测模型");
    dialog.setNameFilter("AI模型 (*.dvt *.dvo *.dvp *.dvst *.dvso *.dvsp);;所有文件 (*.*)");
    dialog.setFileMode(QFileDialog::ExistingFile);
    applyDialogInitialPath(dialog, editIcModelPath_->text().trimmed());
    if (dialog.exec() != QDialog::Accepted) {
        return;
    }
    editIcModelPath_->setText(dialog.selectedFiles().front());
    saveUiSettings();
    onLoadIcModel();
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
    saveUiSettings();
    onInfer();
}

void MainWindow::onLoadExtractModel() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能加载元件提取模型。")) {
        return;
    }
    saveUiSettings();
    try {
        if (loadExtractModel()) {
            outputText_->setPlainText("元件提取模型加载成功:\n" + editExtractModelPath_->text().trimmed());
        }
    } catch (const std::exception& ex) {
        outputText_->setPlainText(QString("元件提取模型加载失败:\n%1").arg(QString::fromLocal8Bit(ex.what())));
    }
}

void MainWindow::onLoadComponentModel() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能加载元件检测模型。")) {
        return;
    }
    saveUiSettings();
    try {
        if (loadComponentModel()) {
            outputText_->setPlainText("元件检测模型加载成功:\n" + editComponentModelPath_->text().trimmed());
        }
    } catch (const std::exception& ex) {
        outputText_->setPlainText(QString("元件检测模型加载失败:\n%1").arg(QString::fromLocal8Bit(ex.what())));
    }
}

void MainWindow::onLoadIcModel() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能加载IC检测模型。")) {
        return;
    }
    saveUiSettings();
    try {
        if (loadIcModel()) {
            outputText_->setPlainText("IC检测模型加载成功:\n" + editIcModelPath_->text().trimmed());
        }
    } catch (const std::exception& ex) {
        outputText_->setPlainText(QString("IC检测模型加载失败:\n%1").arg(QString::fromLocal8Bit(ex.what())));
    }
}

bool MainWindow::loadExtractModel() {
    const QString path = editExtractModelPath_->text().trimmed();
    if (path.isEmpty() || !QFileInfo::exists(path)) {
        QMessageBox::information(this, "提示", "请先选择有效的元件提取模型文件。");
        return false;
    }
    extractModel_ = std::make_unique<dlcv_infer::Model>(path.toStdWString(), kFixedDeviceId);
    saveUiSettings();
    return true;
}

bool MainWindow::loadComponentModel() {
    const QString path = editComponentModelPath_->text().trimmed();
    if (path.isEmpty() || !QFileInfo::exists(path)) {
        QMessageBox::information(this, "提示", "请先选择有效的元件检测模型文件。");
        return false;
    }
    componentModel_ = std::make_unique<dlcv_infer::Model>(path.toStdWString(), kFixedDeviceId);
    saveUiSettings();
    return true;
}

bool MainWindow::loadIcModel() {
    const QString path = editIcModelPath_->text().trimmed();
    if (path.isEmpty() || !QFileInfo::exists(path)) {
        QMessageBox::information(this, "提示", "请先选择有效的IC检测模型文件。");
        return false;
    }
    icModel_ = std::make_unique<dlcv_infer::Model>(path.toStdWString(), kFixedDeviceId);
    saveUiSettings();
    return true;
}

void MainWindow::releaseModels() {
    extractModel_.reset();
    componentModel_.reset();
    icModel_.reset();
}

void MainWindow::onReleaseModels() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能释放模型。")) {
        return;
    }
    releaseModels();
    outputText_->setPlainText("模型已释放");
}

void MainWindow::loadAllModelsSequentially(
    const QString& extractPath,
    const QString& componentPath,
    const QString& icPath,
    const std::function<void(const QString&)>& report) {
    releaseModels();

    const auto loadOne = [&](const char* displayName, const QString& path, std::unique_ptr<dlcv_infer::Model>& model) {
        report(QString("开始加载%1: %2").arg(QString::fromUtf8(displayName), path));
        const auto start = std::chrono::steady_clock::now();
        model = std::make_unique<dlcv_infer::Model>(path.toStdWString(), kFixedDeviceId);
        const auto end = std::chrono::steady_clock::now();
        const double seconds = std::chrono::duration<double>(end - start).count();
        report(QString("%1加载完成，耗时 %2 秒").arg(QString::fromUtf8(displayName)).arg(seconds, 0, 'f', 2));
        return seconds;
    };

    const double extractSeconds = loadOne("元件提取模型", extractPath, extractModel_);
    const double componentSeconds = loadOne("元件检测模型", componentPath, componentModel_);
    const double icSeconds = loadOne("IC检测模型", icPath, icModel_);
    report(QString("三个模型加载完成，总耗时 %1 秒").arg(extractSeconds + componentSeconds + icSeconds, 0, 'f', 2));
}

void MainWindow::onLoadAllModels() {
    if (!tryEnsureIdle("当前正在执行推理，暂不能加载三个模型。")) {
        return;
    }

    const QString extractPath = editExtractModelPath_->text().trimmed();
    const QString componentPath = editComponentModelPath_->text().trimmed();
    const QString icPath = editIcModelPath_->text().trimmed();
    if (!QFileInfo::exists(extractPath)) {
        QMessageBox::information(this, "提示", "请先选择有效的元件提取模型文件。");
        return;
    }
    if (!QFileInfo::exists(componentPath)) {
        QMessageBox::information(this, "提示", "请先选择有效的元件检测模型文件。");
        return;
    }
    if (!QFileInfo::exists(icPath)) {
        QMessageBox::information(this, "提示", "请先选择有效的IC检测模型文件。");
        return;
    }

    saveUiSettings();
    modelLoading_.store(true);
    updateBusyControlState();
    outputText_->clear();
    if (loadThread_.joinable()) {
        loadThread_.join();
    }

    loadThread_ = std::thread([this, extractPath, componentPath, icPath]() {
        QString error;
        try {
            loadAllModelsSequentially(extractPath, componentPath, icPath, [this](const QString& message) {
                QMetaObject::invokeMethod(
                    this,
                    [this, message]() { appendLog(message); },
                    Qt::QueuedConnection);
            });
        } catch (const std::exception& ex) {
            error = QString("加载失败: %1").arg(QString::fromLocal8Bit(ex.what()));
        }

        QMetaObject::invokeMethod(
            this,
            [this, error]() {
                if (!error.isEmpty()) {
                    appendLog(error);
                }
                modelLoading_.store(false);
                updateBusyControlState();
            },
            Qt::QueuedConnection);
    });
}

MainWindow::SlidingWindowConfig MainWindow::captureSlidingWindowConfig() const {
    SlidingWindowConfig config;
    config.windowWidth = spinWindowWidth_->value();
    config.windowHeight = spinWindowHeight_->value();
    config.overlapX = spinOverlapX_->value();
    config.overlapY = spinOverlapY_->value();
    return config;
}

void MainWindow::restoreUiSettings() {
    editExtractModelPath_->setText(settings_.value("LastExtractModelPath").toString());
    editComponentModelPath_->setText(settings_.value("LastComponentDetectModelPath").toString());
    editIcModelPath_->setText(settings_.value("LastIcDetectModelPath").toString());
    imagePath_ = settings_.value("LastImagePath").toString();
    editImagePath_->setText(imagePath_);
    spinWindowWidth_->setValue(readIntSetting(settings_, "SlidingWindowWidth", kDefaultWindowWidth, 1, 30000));
    spinWindowHeight_->setValue(readIntSetting(settings_, "SlidingWindowHeight", kDefaultWindowHeight, 1, 30000));
    spinOverlapX_->setValue(readIntSetting(settings_, "SlidingOverlapX", kDefaultOverlapX, 0, 30000));
    spinOverlapY_->setValue(readIntSetting(settings_, "SlidingOverlapY", kDefaultOverlapY, 0, 30000));
}

void MainWindow::saveUiSettings() {
    settings_.setValue("LastExtractModelPath", editExtractModelPath_->text().trimmed());
    settings_.setValue("LastComponentDetectModelPath", editComponentModelPath_->text().trimmed());
    settings_.setValue("LastIcDetectModelPath", editIcModelPath_->text().trimmed());
    settings_.setValue("LastImagePath", editImagePath_->text().trimmed());
    settings_.setValue("SlidingWindowWidth", spinWindowWidth_->value());
    settings_.setValue("SlidingWindowHeight", spinWindowHeight_->value());
    settings_.setValue("SlidingOverlapX", spinOverlapX_->value());
    settings_.setValue("SlidingOverlapY", spinOverlapY_->value());
}

void MainWindow::onInfer() {
    if (modelLoading_.load()) {
        outputText_->setPlainText("当前正在加载模型，请等待完成后再执行推理。");
        return;
    }
    if (inferenceRunning_.exchange(true)) {
        outputText_->setPlainText("当前正在执行推理，请稍后再试。");
        return;
    }

    imagePath_ = editImagePath_->text().trimmed();
    if (!ensureExtractModelLoaded() || !ensureComponentModelLoaded() || !ensureIcModelLoaded() || !ensureImageSelected()) {
        inferenceRunning_.store(false);
        return;
    }

    saveUiSettings();
    if (inferenceThread_.joinable()) {
        inferenceThread_.join();
    }

    cv::Mat bgrImage;
    cv::Mat rgbImage;
    if (!loadCurrentImage(bgrImage, rgbImage)) {
        inferenceRunning_.store(false);
        outputText_->setPlainText("执行推理失败:\n图片解码失败。");
        setStatus("空闲", 0);
        return;
    }

    updateBusyControlState();
    setStatus("准备推理", 0);
    const SlidingWindowConfig config = captureSlidingWindowConfig();
    const QString imagePath = imagePath_;
    const QString extractPath = editExtractModelPath_->text().trimmed();
    const QString componentPath = editComponentModelPath_->text().trimmed();
    const QString icPath = editIcModelPath_->text().trimmed();
    dlcv_infer::Model* extractPtr = extractModel_.get();
    dlcv_infer::Model* componentPtr = componentModel_.get();
    dlcv_infer::Model* icPtr = icModel_.get();

    inferenceThread_ = std::thread([this, bgrImage, rgbImage, config, imagePath, extractPath, componentPath, icPath, extractPtr, componentPtr, icPtr]() {
        QString errorDetail;
        PipelineRunResult runResult;
        double elapsedMs = 0.0;
        try {
            const auto start = std::chrono::steady_clock::now();
            runResult = runPipeline(
                rgbImage,
                *extractPtr,
                *componentPtr,
                *icPtr,
                config,
                [this](const PipelineProgressInfo& info) {
                    QMetaObject::invokeMethod(
                        this,
                        [this, info]() { setStatus(info.stage, info.percent); },
                        Qt::QueuedConnection);
                });
            const auto end = std::chrono::steady_clock::now();
            elapsedMs = std::chrono::duration<double, std::milli>(end - start).count();
        } catch (const std::exception& ex) {
            errorDetail = QString::fromLocal8Bit(ex.what());
        }

        QMetaObject::invokeMethod(
            this,
            [this, bgrImage, imagePath, extractPath, componentPath, icPath, config, runResult, elapsedMs, errorDetail]() {
                inferenceRunning_.store(false);
                updateBusyControlState();
                if (!errorDetail.isEmpty()) {
                    outputText_->setPlainText("执行推理失败:\n" + errorDetail);
                    setStatus("空闲", 0);
                    return;
                }

                currentBgrImage_ = bgrImage;
                imageViewer_->setImageAndResults(currentBgrImage_, runResult.finalObjects);

                QString text;
                text += QString("图片: %1\n").arg(imagePath);
                text += QString("元件提取模型: %1\n").arg(extractPath);
                text += QString("元件检测模型: %1\n").arg(componentPath);
                text += QString("IC检测模型: %1\n").arg(icPath);
                text += QString("滑窗参数: %1 x %2, overlap=(%3, %4)\n")
                            .arg(config.windowWidth)
                            .arg(config.windowHeight)
                            .arg(config.overlapX)
                            .arg(config.overlapY);
                text += QString("滑窗数量: %1\n").arg(runResult.slidingWindowCount);
                text += QString("元件提取模型合并后目标数: %1\n").arg(runResult.mergedExtractCount);
                text += QString("元件检测模型结果数: %1\n").arg(runResult.componentModelResultCount);
                text += QString("IC检测模型结果数: %1\n").arg(runResult.icModelResultCount);
                text += QString("最终结果数: %1\n").arg(static_cast<int>(runResult.finalObjects.size()));
                text += QString("推理耗时: %1 ms\n\n").arg(elapsedMs, 0, 'f', 2);

                if (runResult.finalObjects.empty()) {
                    text += "未检测到结果。\n";
                } else {
                    for (int i = 0; i < static_cast<int>(runResult.finalObjects.size()); ++i) {
                        const auto& obj = runResult.finalObjects[i];
                        text += QString("[%1] %2  score=%3  %4\n")
                                    .arg(i + 1)
                                    .arg(categoryToQString(obj.categoryName))
                                    .arg(obj.score, 0, 'f', 2)
                                    .arg(buildObjectLocationText(obj));
                    }
                }
                if (!runResult.logs.isEmpty()) {
                    text += "\n日志:\n";
                    for (const QString& log : runResult.logs) {
                        text += "- " + log + "\n";
                    }
                }
                outputText_->setPlainText(text);
                setStatus("完成", 100);
            },
            Qt::QueuedConnection);
    });
}

std::vector<MainWindow::ExtractDetection> MainWindow::inferExtractModelOnWindows(
    const cv::Mat& fullImageRgb,
    dlcv_infer::Model& extractModel,
    const std::vector<cv::Rect>& windows,
    const std::function<void(const PipelineProgressInfo&)>& progressCallback) {
    std::vector<ExtractDetection> output;
    const json inferParams = json{{"with_mask", false}};
    const int totalWindows = static_cast<int>(windows.size());
    if (totalWindows == 0) {
        if (progressCallback) {
            progressCallback({60, "元件提取模型滑窗推理 0/0"});
        }
        return output;
    }

    int order = 0;
    int lastPercent = -1;
    for (int i = 0; i < totalWindows; ++i) {
        const cv::Rect window = windows[i];
        const cv::Mat tile = fullImageRgb(window).clone();
        const dlcv_infer::Result tileResult = extractModel.Infer(tile, inferParams);
        if (!tileResult.sampleResults.empty()) {
            for (const auto& obj : tileResult.sampleResults[0].results) {
                const dlcv_infer::ObjectResult mapped = liftExtractObjectToFull(obj, window);
                const cv::Rect2d aabb = getAabbFromObject(mapped);
                if (aabb.width <= 0.0 || aabb.height <= 0.0) {
                    continue;
                }
                ExtractDetection detection;
                detection.objectResult = mapped;
                detection.mergeAabb = aabb;
                detection.order = order++;
                output.push_back(detection);
            }
        }

        const int percent = 15 + static_cast<int>(std::llround(45.0 * (i + 1) / totalWindows));
        if (percent != lastPercent && progressCallback) {
            progressCallback({percent, QString("元件提取模型滑窗推理 %1/%2").arg(i + 1).arg(totalWindows)});
            lastPercent = percent;
        }
    }
    return output;
}

MainWindow::RoiProcessResult MainWindow::cropAndRotateRoi(
    const cv::Mat& fullImageRgb,
    const ExtractDetection& target,
    int normalizeAngle) {
    RoiProcessResult result;
    result.isValid = false;
    result.invalidReason = "未知错误";

    cv::Mat roi;
    std::vector<double> fullToCropAffine;
    try {
        if (!tryBuildRotatedCrop(fullImageRgb, target.objectResult, 0, roi, fullToCropAffine)) {
            const cv::Rect roiRect = clampRectToImage(target.mergeAabb, fullImageRgb.cols, fullImageRgb.rows);
            if (roiRect.width <= 1 || roiRect.height <= 1) {
                result.invalidReason = "ROI无效";
                return result;
            }
            roi = fullImageRgb(roiRect).clone();
            fullToCropAffine = {1.0, 0.0, static_cast<double>(-roiRect.x), 0.0, 1.0, static_cast<double>(-roiRect.y)};
        }
        if (roi.empty()) {
            result.invalidReason = "ROI为空";
            return result;
        }

        cv::Mat normalized = rotateRoiByRightAngle(roi, normalizeAngle);
        if (normalized.empty()) {
            result.invalidReason = "ROI归一化失败";
            return result;
        }

        int dstW = 0;
        int dstH = 0;
        const std::vector<double> cropToNorm = buildRightAngleAffine(roi.cols, roi.rows, normalizeAngle, dstW, dstH);
        const std::vector<double> normToCrop = invertAffine2x3(cropToNorm);
        const std::vector<double> cropToFull = invertAffine2x3(fullToCropAffine);
        result.isValid = true;
        result.invalidReason.clear();
        result.normalizedRoi = normalized;
        result.normToFullAffine = composeAffine(cropToFull, normToCrop);
        return result;
    } catch (const std::exception& ex) {
        result.invalidReason = QString::fromLocal8Bit(ex.what());
        return result;
    }
}

std::vector<dlcv_infer::ObjectResult> MainWindow::inferDetectionModelAndMapBack(
    const RoiProcessResult& roiContext,
    const dlcv_infer::ObjectResult& extractFallback,
    bool useIcDetectModel,
    dlcv_infer::Model& componentModel,
    dlcv_infer::Model& icModel) {
    const json inferParams = json{{"with_mask", false}};
    dlcv_infer::Model& activeModel = useIcDetectModel ? icModel : componentModel;
    const dlcv_infer::Result roiResult = activeModel.Infer(roiContext.normalizedRoi, inferParams);
    if (roiResult.sampleResults.empty() || roiResult.sampleResults[0].results.empty()) {
        return {extractFallback};
    }

    std::vector<dlcv_infer::ObjectResult> mappedObjects;
    for (const auto& obj : roiResult.sampleResults[0].results) {
        dlcv_infer::ObjectResult mapped = dlcv_infer::ObjectResult(0, "", 0.0f, 0.0f, {}, false, cv::Mat());
        if (tryMapObjectToFull(obj, roiContext.normToFullAffine, mapped)) {
            mappedObjects.push_back(resolveFinalDetectionObject(mapped, extractFallback));
        }
    }
    if (mappedObjects.empty()) {
        return {extractFallback};
    }
    return mappedObjects;
}

MainWindow::PipelineRunResult MainWindow::runPipeline(
    const cv::Mat& fullImageRgb,
    dlcv_infer::Model& extractModel,
    dlcv_infer::Model& componentModel,
    dlcv_infer::Model& icModel,
    const SlidingWindowConfig& config,
    const std::function<void(const PipelineProgressInfo&)>& progressCallback) {
    PipelineRunResult runResult;
    auto report = [&](int percent, const QString& stage) {
        if (progressCallback) {
            progressCallback({clampProgressPercent(percent), stage});
        }
    };

    report(8, "生成滑窗");
    const std::vector<cv::Rect> windows = buildSlidingWindows(
        fullImageRgb.cols,
        fullImageRgb.rows,
        config.windowWidth,
        config.windowHeight,
        config.overlapX,
        config.overlapY);
    runResult.slidingWindowCount = static_cast<int>(windows.size());

    report(12, QString("元件提取模型滑窗推理 0/%1").arg(runResult.slidingWindowCount));
    const std::vector<ExtractDetection> extractDetections =
        inferExtractModelOnWindows(fullImageRgb, extractModel, windows, progressCallback);
    report(62, "合并元件提取结果");
    const std::vector<ExtractDetection> mergedExtract = mergeExtractResults(extractDetections);
    runResult.mergedExtractCount = static_cast<int>(mergedExtract.size());

    const int roiTotal = runResult.mergedExtractCount;
    int roiCompleted = 0;
    for (const auto& target : mergedExtract) {
        const int startPercent = 70 + static_cast<int>(std::llround(25.0 * roiCompleted / std::max(1, roiTotal)));
        report(startPercent, QString("局部模型推理 %1/%2").arg(roiCompleted).arg(roiTotal));

        QString baseName;
        int normalizeAngle = 0;
        parseCategoryAndAngle(categoryToQString(target.objectResult.categoryName), baseName, normalizeAngle);
        const bool useIcDetectModel = shouldUseIcDetectModel(baseName);
        RoiProcessResult roi = cropAndRotateRoi(fullImageRgb, target, normalizeAngle);
        if (!roi.isValid) {
            runResult.logs.push_back(
                QString("跳过目标[%1]：%2").arg(categoryToQString(target.objectResult.categoryName), roi.invalidReason));
            ++roiCompleted;
            continue;
        }

        try {
            const std::vector<dlcv_infer::ObjectResult> mapped =
                inferDetectionModelAndMapBack(roi, target.objectResult, useIcDetectModel, componentModel, icModel);
            runResult.finalObjects.insert(runResult.finalObjects.end(), mapped.begin(), mapped.end());
            const bool usedFallback = mapped.size() == 1
                && mapped[0].categoryName == target.objectResult.categoryName
                && mapped[0].bbox == target.objectResult.bbox
                && std::abs(mapped[0].score - target.objectResult.score) < 1e-6f;
            const int realResultCount = usedFallback ? 0 : static_cast<int>(mapped.size());
            if (useIcDetectModel) {
                runResult.icModelResultCount += realResultCount;
            } else {
                runResult.componentModelResultCount += realResultCount;
            }
        } catch (const std::exception& ex) {
            const QString routeName = useIcDetectModel ? QStringLiteral("IC检测模型") : QStringLiteral("元件检测模型");
            runResult.logs.push_back(
                QString("目标[%1]%2推理失败，保留元件提取结果：%3")
                    .arg(categoryToQString(target.objectResult.categoryName), routeName, QString::fromLocal8Bit(ex.what())));
            runResult.finalObjects.push_back(target.objectResult);
        }

        ++roiCompleted;
        const int finishPercent = 70 + static_cast<int>(std::llround(25.0 * roiCompleted / std::max(1, roiTotal)));
        report(finishPercent, QString("局部模型推理 %1/%2").arg(roiCompleted).arg(roiTotal));
    }

    report(95, "整理结果");
    runResult.displayResult = buildDisplayResult(runResult.finalObjects);
    report(100, "推理完成");
    return runResult;
}
