#pragma once

#include <QPoint>
#include <QPointF>
#include <QString>
#include <QtGlobal>
#include <QWidget>

#include <opencv2/core.hpp>

#include "DisplayResult.h"

class QKeyEvent;
class QMouseEvent;
class QPaintEvent;
class QPainter;
class QResizeEvent;
class QWheelEvent;
class QColor;
class QImage;

class ImageViewerWidget : public QWidget {
public:
    enum class LabelTextMode {
        CategoryAndScore = 0,
        CategoryOnly = 1,
        None = 2,
    };

    explicit ImageViewerWidget(QWidget* parent = nullptr);

    void setImageAndResults(const cv::Mat& bgrImage, const std::vector<DisplayObjectResult>& results);
    void setImage(const cv::Mat& bgrImage);
    void setResults(const std::vector<DisplayObjectResult>& results);
    void clearResults();

    void setShowStatusText(bool enabled);
    bool showStatusText() const { return showStatusText_; }

    void setInspectionStatus(bool ok);
    void clearInspectionStatus();

    void setShowVisualization(bool enabled);
    bool showVisualization() const { return showVisualization_; }

    void setLabelDisplayMode(LabelTextMode mode);
    LabelTextMode labelDisplayMode() const { return labelDisplayMode_; }

    void setShowLabelText(bool enabled);
    bool showLabelText() const { return labelDisplayMode_ != LabelTextMode::None; }

    void setLabelFontScale(float scale);
    float labelFontScale() const { return labelFontScale_; }

    float maxScale() const { return maxScale_; }
    float minScale() const { return minScale_; }

protected:
    void paintEvent(QPaintEvent* event) override;
    void wheelEvent(QWheelEvent* event) override;
    void mousePressEvent(QMouseEvent* event) override;
    void mouseMoveEvent(QMouseEvent* event) override;
    void mouseReleaseEvent(QMouseEvent* event) override;
    void mouseDoubleClickEvent(QMouseEvent* event) override;
    void keyPressEvent(QKeyEvent* event) override;
    void resizeEvent(QResizeEvent* event) override;

private:
    static QImage bgrToQImage(const cv::Mat& bgrImage);
    static QImage createMaskOverlayImage(const cv::Mat& mask);
    static QColor categoryColor(const QString& categoryName);

    void fitToPanel();
    void calculateMinScale();
    void adjustImagePosition();
    qreal labelFontSizeInImageSpace() const;
    void drawResults(QPainter& painter, QString& statusText, bool& shouldDrawStatus) const;

    QImage image_;
    std::vector<DisplayObjectResult> results_;

    float scale_ = 1.0f;
    float maxScale_ = 100.0f;
    float minScale_ = 0.5f;
    QPointF imagePosition_ = QPointF(0.0, 0.0);

    bool isDragging_ = false;
    QPoint lastMousePosition_;

    bool showStatusText_ = false;
    bool hasInspectionStatus_ = false;
    bool inspectionOk_ = false;
    bool showVisualization_ = true;
    LabelTextMode labelDisplayMode_ = LabelTextMode::CategoryAndScore;

    float visualizationBaseFontSize_ = 24.0f;
    float visualizationMinFontSize_ = 8.0f;
    float labelFontScale_ = 1.0f;
    float minLabelFontScale_ = 0.3f;
    float maxLabelFontScale_ = 5.0f;
    float labelFontScaleStep_ = 1.1f;
};


