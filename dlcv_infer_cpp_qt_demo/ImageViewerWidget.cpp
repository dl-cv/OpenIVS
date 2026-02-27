#include "ImageViewerWidget.h"

#include <algorithm>
#include <cmath>

#include <QColor>
#include <QFont>
#include <QFontMetricsF>
#include <QImage>
#include <QKeyEvent>
#include <QMouseEvent>
#include <QPaintEvent>
#include <QPainter>
#include <QPen>
#include <QPolygonF>
#include <QWheelEvent>
#include <QtGlobal>

#include <opencv2/imgproc.hpp>

ImageViewerWidget::ImageViewerWidget(QWidget* parent) : QWidget(parent) {
    setFocusPolicy(Qt::StrongFocus);
    setMouseTracking(true);
    setMinimumSize(300, 300);
}

void ImageViewerWidget::setImageAndResults(const cv::Mat& bgrImage, const std::vector<dlcv_infer::ObjectResult>& results) {
    setImage(bgrImage);
    setResults(results);
}

void ImageViewerWidget::setImage(const cv::Mat& bgrImage) {
    image_ = bgrToQImage(bgrImage);
    fitToPanel();
    update();
}

void ImageViewerWidget::setResults(const std::vector<dlcv_infer::ObjectResult>& results) {
    results_ = results;
    update();
}

void ImageViewerWidget::clearResults() {
    results_.clear();
    update();
}

void ImageViewerWidget::setShowStatusText(bool enabled) {
    showStatusText_ = enabled;
    update();
}

void ImageViewerWidget::setShowVisualization(bool enabled) {
    showVisualization_ = enabled;
    update();
}

void ImageViewerWidget::paintEvent(QPaintEvent* event) {
    Q_UNUSED(event);
    QPainter painter(this);
    painter.fillRect(rect(), QColor(20, 20, 20));

    QString statusText = "OK";
    bool shouldDrawStatus = showStatusText_;

    if (!image_.isNull()) {
        painter.save();
        painter.translate(imagePosition_);
        painter.scale(scale_, scale_);
        painter.drawImage(QPointF(0, 0), image_);
        drawResults(painter, statusText, shouldDrawStatus);
        painter.restore();
    } else if (showStatusText_) {
        statusText = "No Image";
        shouldDrawStatus = true;
    }

    if (shouldDrawStatus) {
        painter.save();
        QFont font("Microsoft YaHei", 24);
        painter.setFont(font);
        painter.setPen(statusText == "OK" ? Qt::green : Qt::red);
        painter.drawText(QPointF(10, 42), statusText);
        painter.restore();
    }
}

void ImageViewerWidget::wheelEvent(QWheelEvent* event) {
    if (image_.isNull()) {
        event->ignore();
        return;
    }

    const float oldScale = scale_;
    if (event->angleDelta().y() > 0 && scale_ < maxScale_) {
        scale_ *= 1.1f;
    } else if (event->angleDelta().y() < 0 && scale_ > minScale_) {
        scale_ /= 1.1f;
    }
    scale_ = std::max(minScale_, std::min(maxScale_, scale_));

    if (std::abs(scale_ - oldScale) < 1e-6f) {
        event->accept();
        return;
    }

#if QT_VERSION >= QT_VERSION_CHECK(6, 0, 0)
    const QPointF cursorPos = event->position();
#else
    const QPointF cursorPos = event->posF();
#endif
    const float ratio = scale_ / oldScale;
    imagePosition_.setX(cursorPos.x() - ratio * (cursorPos.x() - imagePosition_.x()));
    imagePosition_.setY(cursorPos.y() - ratio * (cursorPos.y() - imagePosition_.y()));

    adjustImagePosition();
    update();
    event->accept();
}

void ImageViewerWidget::mousePressEvent(QMouseEvent* event) {
    setFocus();
    if (event->button() == Qt::LeftButton) {
        isDragging_ = true;
        lastMousePosition_ = event->pos();
    } else if (event->button() == Qt::RightButton && !image_.isNull()) {
        fitToPanel();
        update();
    }
    QWidget::mousePressEvent(event);
}

void ImageViewerWidget::mouseMoveEvent(QMouseEvent* event) {
    if (isDragging_) {
        const QPoint delta = event->pos() - lastMousePosition_;
        imagePosition_.setX(imagePosition_.x() + delta.x());
        imagePosition_.setY(imagePosition_.y() + delta.y());
        lastMousePosition_ = event->pos();
        adjustImagePosition();
        update();
    }
    QWidget::mouseMoveEvent(event);
}

void ImageViewerWidget::mouseReleaseEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        isDragging_ = false;
    }
    QWidget::mouseReleaseEvent(event);
}

void ImageViewerWidget::mouseDoubleClickEvent(QMouseEvent* event) {
    QWidget::mouseDoubleClickEvent(event);
}

void ImageViewerWidget::keyPressEvent(QKeyEvent* event) {
    if (event->key() == Qt::Key_V) {
        showVisualization_ = !showVisualization_;
        update();
        event->accept();
        return;
    }
    QWidget::keyPressEvent(event);
}

void ImageViewerWidget::resizeEvent(QResizeEvent* event) {
    QWidget::resizeEvent(event);
    calculateMinScale();
}

QImage ImageViewerWidget::bgrToQImage(const cv::Mat& bgrImage) {
    if (bgrImage.empty()) {
        return {};
    }

    cv::Mat rgb;
    if (bgrImage.channels() == 3) {
        cv::cvtColor(bgrImage, rgb, cv::COLOR_BGR2RGB);
    } else if (bgrImage.channels() == 4) {
        cv::cvtColor(bgrImage, rgb, cv::COLOR_BGRA2RGBA);
    } else {
        cv::cvtColor(bgrImage, rgb, cv::COLOR_GRAY2RGB);
    }

    if (!rgb.isContinuous()) {
        rgb = rgb.clone();
    }

    QImage image(rgb.data, rgb.cols, rgb.rows, static_cast<int>(rgb.step), QImage::Format_RGB888);
    return image.copy();
}

QImage ImageViewerWidget::createMaskOverlayImage(const cv::Mat& mask) {
    if (mask.empty()) {
        return {};
    }

    cv::Mat grayMask;
    if (mask.type() == CV_8UC1) {
        grayMask = mask;
    } else if (mask.channels() == 1) {
        mask.convertTo(grayMask, CV_8UC1);
    } else {
        cv::cvtColor(mask, grayMask, cv::COLOR_BGR2GRAY);
    }

    // 与 C# Demo 的 PixelFormat.Format32bppArgb 对齐：使用非预乘 alpha，避免不同平台下的混合差异。
    QImage overlay(grayMask.cols, grayMask.rows, QImage::Format_ARGB32);
    overlay.fill(Qt::transparent);

    for (int y = 0; y < grayMask.rows; ++y) {
        const uchar* src = grayMask.ptr<uchar>(y);
        QRgb* dst = reinterpret_cast<QRgb*>(overlay.scanLine(y));
        for (int x = 0; x < grayMask.cols; ++x) {
            dst[x] = src[x] > 0 ? qRgba(0, 255, 0, 128) : qRgba(0, 0, 0, 0);
        }
    }

    return overlay;
}

QColor ImageViewerWidget::categoryColor(const QString& categoryName) {
    const QString lower = categoryName.toLower();
    if (lower.contains("ok")) {
        return QColor(0, 255, 0);
    }
    return QColor(255, 0, 0);
}

void ImageViewerWidget::fitToPanel() {
    if (image_.isNull() || width() <= 0 || height() <= 0) {
        return;
    }

    const float panelAspect = static_cast<float>(width()) / static_cast<float>(height());
    const float imageAspect = static_cast<float>(image_.width()) / static_cast<float>(image_.height());
    if (panelAspect > imageAspect) {
        scale_ = static_cast<float>(height()) / static_cast<float>(image_.height());
    } else {
        scale_ = static_cast<float>(width()) / static_cast<float>(image_.width());
    }

    imagePosition_.setX((width() - image_.width() * scale_) / 2.0f);
    imagePosition_.setY((height() - image_.height() * scale_) / 2.0f);
    calculateMinScale();
    adjustImagePosition();
}

void ImageViewerWidget::calculateMinScale() {
    if (image_.isNull()) {
        minScale_ = 0.5f;
        return;
    }
    const float panelMin = static_cast<float>(std::min(width(), height()));
    const float imageMax = static_cast<float>(std::max(image_.width(), image_.height()));
    if (imageMax <= 0.0f) {
        minScale_ = 0.5f;
        return;
    }
    minScale_ = (panelMin / 2.0f) / imageMax;
    minScale_ = std::max(0.05f, minScale_);
    scale_ = std::max(scale_, minScale_);
}

void ImageViewerWidget::adjustImagePosition() {
    if (image_.isNull()) {
        return;
    }

    const float scaledWidth = image_.width() * scale_;
    const float scaledHeight = image_.height() * scale_;

    const auto clampAxis = [](float value, float scaledSize, int panelSize) {
        const float minEdge = 100.0f - scaledSize;
        const float maxEdge = static_cast<float>(panelSize) - 100.0f;
        return std::max(minEdge, std::min(maxEdge, value));
    };

    imagePosition_.setX(clampAxis(imagePosition_.x(), scaledWidth, width()));
    imagePosition_.setY(clampAxis(imagePosition_.y(), scaledHeight, height()));
}

void ImageViewerWidget::drawResults(QPainter& painter, QString& statusText, bool& shouldDrawStatus) const {
    statusText = "OK";
    if (results_.empty()) {
        statusText = "No Result";
        shouldDrawStatus = true;
        return;
    }

    if (!showVisualization_) {
        return;
    }

    const qreal borderWidth = std::max(1.0, 2.0 / static_cast<double>(scale_));
    const qreal fontSize = std::max(8.0, 24.0 / static_cast<double>(scale_));
    QFont labelFont("Microsoft YaHei", static_cast<int>(fontSize));
    painter.setFont(labelFont);
    const QFontMetricsF metrics(labelFont);

    for (const dlcv_infer::ObjectResult& obj : results_) {
        const QString categoryName = QString::fromLocal8Bit(obj.categoryName.c_str());
        const QString categoryLower = categoryName.toLower();
        if (!categoryLower.contains("ok")) {
            statusText = "NG";
        }

        if (obj.bbox.size() < 4) {
            // 分类任务没有 bbox，仅通过分类结果更新状态文本。
            statusText = categoryLower.contains("ok") ? "OK" : "NG";
            continue;
        }

        const QColor color = categoryColor(categoryName);
        QPen pen(color);
        pen.setWidthF(borderWidth);
        painter.setPen(pen);

        if (obj.withAngle) {
            const double cx = obj.bbox[0];
            const double cy = obj.bbox[1];
            const double w = obj.bbox[2];
            const double h = obj.bbox[3];
            const double angle = obj.angle;

            const double cosA = std::cos(angle);
            const double sinA = std::sin(angle);
            const QPointF offsets[4] = {
                QPointF(-w / 2.0, -h / 2.0),
                QPointF(w / 2.0, -h / 2.0),
                QPointF(w / 2.0, h / 2.0),
                QPointF(-w / 2.0, h / 2.0),
            };

            QPolygonF polygon;
            for (const QPointF& offset : offsets) {
                const double x = cx + offset.x() * cosA - offset.y() * sinA;
                const double y = cy + offset.x() * sinA + offset.y() * cosA;
                polygon << QPointF(x, y);
            }
            painter.drawPolygon(polygon);

            const QString label = QString("%1 %2").arg(categoryName).arg(obj.score, 0, 'f', 2);
            const QRectF textRect = metrics.boundingRect(label);
            const qreal textX = cx - textRect.width() / 2.0;
            const qreal textY = cy - h / 2.0 - textRect.height() - 2.0;
            painter.fillRect(QRectF(textX, textY, textRect.width(), textRect.height()), QColor(0, 0, 0, 160));
            painter.setPen(color);
            painter.drawText(QPointF(textX, textY + textRect.height() - metrics.descent()), label);
            continue;
        }

        const double x = obj.bbox[0];
        const double y = obj.bbox[1];
        const double w = obj.bbox[2];
        const double h = obj.bbox[3];
        const QRectF bboxRect(x, y, w, h);

        if (obj.withMask && !obj.mask.empty()) {
            const QImage overlay = createMaskOverlayImage(obj.mask);
            if (!overlay.isNull()) {
                painter.drawImage(bboxRect, overlay);
            }
        }

        painter.drawRect(bboxRect);

        const QString label = QString("%1 %2").arg(categoryName).arg(obj.score, 0, 'f', 2);
        const QRectF textRect = metrics.boundingRect(label);
        const qreal textY = y - textRect.height() - 2.0;
        painter.fillRect(QRectF(x, textY, textRect.width(), textRect.height()), QColor(0, 0, 0, 160));
        painter.setPen(color);
        painter.drawText(QPointF(x, textY + textRect.height() - metrics.descent()), label);
    }
}
