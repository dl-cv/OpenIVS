#pragma once

#include <QPoint>
#include <QPointF>
#include <QString>
#include <QWidget>

#include <opencv2/core.hpp>

#include "dlcv_infer.h"

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
    explicit ImageViewerWidget(QWidget* parent = nullptr);

    void setImageAndResults(const cv::Mat& bgrImage, const std::vector<dlcv_infer::ObjectResult>& results);
    void setImage(const cv::Mat& bgrImage);
    void setResults(const std::vector<dlcv_infer::ObjectResult>& results);
    void clearResults();

    void setShowStatusText(bool enabled);
    bool showStatusText() const { return showStatusText_; }

    void setShowVisualization(bool enabled);
    bool showVisualization() const { return showVisualization_; }

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
    void drawResults(QPainter& painter, QString& statusText, bool& shouldDrawStatus) const;

    QImage image_;
    std::vector<dlcv_infer::ObjectResult> results_;

    float scale_ = 1.0f;
    float maxScale_ = 100.0f;
    float minScale_ = 0.5f;
    QPointF imagePosition_ = QPointF(0.0, 0.0);

    bool isDragging_ = false;
    QPoint lastMousePosition_;

    bool showStatusText_ = false;
    bool showVisualization_ = true;
};
