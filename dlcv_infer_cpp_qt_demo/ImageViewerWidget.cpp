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

void ImageViewerWidget::setLabelDisplayMode(LabelTextMode mode) {
    if (labelDisplayMode_ == mode) {
        return;
    }
    labelDisplayMode_ = mode;
    update();
}

void ImageViewerWidget::setShowLabelText(bool enabled) {
    setLabelDisplayMode(enabled ? LabelTextMode::CategoryAndScore : LabelTextMode::None);
}

void ImageViewerWidget::setLabelFontScale(float scale) {
    const float clamped = std::max(minLabelFontScale_, std::min(maxLabelFontScale_, scale));
    if (std::abs(clamped - labelFontScale_) < 1e-4f) {
        return;
    }
    labelFontScale_ = clamped;
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

    if ((event->modifiers() & Qt::ControlModifier) != 0) {
        if (event->angleDelta().y() > 0) {
            setLabelFontScale(labelFontScale_ * labelFontScaleStep_);
        } else if (event->angleDelta().y() < 0) {
            setLabelFontScale(labelFontScale_ / labelFontScaleStep_);
        }
        event->accept();
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
    if (event->key() == Qt::Key_C) {
        const int nextMode = (static_cast<int>(labelDisplayMode_) + 1) % 3;
        setLabelDisplayMode(static_cast<LabelTextMode>(nextMode));
        event->accept();
        return;
    }
    if (event->key() == Qt::Key_Plus || event->key() == Qt::Key_Equal) {
        setLabelFontScale(labelFontScale_ * labelFontScaleStep_);
        event->accept();
        return;
    }
    if (event->key() == Qt::Key_Minus) {
        setLabelFontScale(labelFontScale_ / labelFontScaleStep_);
        event->accept();
        return;
    }
    if (event->key() == Qt::Key_0) {
        setLabelFontScale(1.0f);
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

    cv::Mat src8 = bgrImage;
    cv::Mat converted;
    if (bgrImage.depth() != CV_8U) {
        if (bgrImage.depth() == CV_16U) {
            bgrImage.convertTo(converted, CV_8U, 1.0 / 256.0);
        } else if (bgrImage.depth() == CV_16S) {
            cv::normalize(bgrImage, converted, 0, 255, cv::NORM_MINMAX, CV_8U);
        } else if (bgrImage.depth() == CV_32F || bgrImage.depth() == CV_64F) {
            double mn = 0.0;
            double mx = 0.0;
            cv::minMaxLoc(bgrImage, &mn, &mx);
            if (mx <= 1.0 + 1e-6 && mn >= -1e-6) {
                bgrImage.convertTo(converted, CV_8U, 255.0);
            } else {
                cv::normalize(bgrImage, converted, 0, 255, cv::NORM_MINMAX, CV_8U);
            }
        } else {
            bgrImage.convertTo(converted, CV_8U);
        }
        src8 = converted;
    }

    // 预览与叠加仅按三通道 RGB888 绘制；四通道须先去掉 alpha，不能按四通道步长配合 RGB888。
    cv::Mat rgb;
    if (src8.channels() == 3) {
        cv::cvtColor(src8, rgb, cv::COLOR_BGR2RGB);
    } else if (src8.channels() == 4) {
        cv::cvtColor(src8, rgb, cv::COLOR_BGRA2RGB);
    } else {
        cv::cvtColor(src8, rgb, cv::COLOR_GRAY2RGB);
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

qreal ImageViewerWidget::labelFontSizeInImageSpace() const {
    const qreal safeScale = std::max<qreal>(static_cast<qreal>(scale_), 1e-6);
    const qreal baseFontSize = static_cast<qreal>(visualizationBaseFontSize_);
    const qreal maxFontSize = 128.0;
    const qreal screenFontSize =
        std::min(std::max(baseFontSize * safeScale, baseFontSize), maxFontSize) *
        static_cast<qreal>(labelFontScale_);
    return std::max(static_cast<qreal>(visualizationMinFontSize_), screenFontSize / safeScale);
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
    QFont labelFont("Microsoft YaHei");
    labelFont.setPointSizeF(labelFontSizeInImageSpace());
    painter.setFont(labelFont);
    const QFontMetricsF metrics(labelFont);

    auto buildLabelText = [&](const QString& categoryName, double score) {
        if (labelDisplayMode_ == LabelTextMode::None) {
            return QString();
        }
        if (labelDisplayMode_ == LabelTextMode::CategoryOnly) {
            return categoryName;
        }
        return QString("%1 %2").arg(categoryName).arg(score, 0, 'f', 2);
    };

    auto paintLabelBar = [&](qreal textLeftX, qreal textTopY, const QString& label, const QColor& color) {
        if (label.isEmpty()) {
            return;
        }

        const QSizeF textSize = metrics.size(Qt::TextSingleLine, label);
        const QRectF textRect(textLeftX, textTopY, textSize.width(), textSize.height());
        painter.fillRect(textRect, QColor(0, 0, 0, 160));
        painter.setPen(color);
        painter.drawText(textRect, Qt::AlignLeft | Qt::AlignTop, label);
    };

    for (const dlcv_infer::ObjectResult& obj : results_) {
        const QString categoryName = QString::fromLocal8Bit(obj.categoryName.c_str());
        const QString categoryLower = categoryName.toLower();
        if (!obj.withBbox || obj.bbox.size() < 4) {
            statusText = categoryLower.contains("ok") ? "OK" : "NG";
            break;
        }

        if (!categoryLower.contains("ok")) {
            statusText = "NG";
        }

        const QColor color = categoryColor(categoryName);
        QPen pen(color);
        pen.setWidthF(borderWidth);
        painter.setPen(pen);

        if (obj.withMask && !obj.mask.empty()) {
            const QImage overlay = createMaskOverlayImage(obj.mask);
            if (!overlay.isNull()) {
                if (!obj.withAngle) {
                    const double x = obj.bbox[0];
                    const double y = obj.bbox[1];
                    const double w = obj.bbox[2];
                    const double h = obj.bbox[3];
                    painter.drawImage(QRectF(x, y, w, h), overlay);
                }
            }
        }

        const double bx = obj.bbox[0];
        const double by = obj.bbox[1];
        const double bw = obj.bbox[2];
        const double bh = obj.bbox[3];
        const QRectF bboxRect(bx, by, bw, bh);
        const QString label = buildLabelText(categoryName, obj.score);
        const QSizeF labelSize = metrics.size(Qt::TextSingleLine, label);

        if (obj.withAngle) {
            const double cx = bx;
            const double cy = by;
            const double w = bw;
            const double h = bh;
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

            const qreal textLeftX = static_cast<qreal>(cx) - labelSize.width() / 2.0;
            const qreal textTopY = static_cast<qreal>(cy) - h / 2.0 - labelSize.height() - 2.0;
            paintLabelBar(textLeftX, textTopY, label, color);
            continue;
        }

        painter.drawRect(bboxRect);

        const qreal textTopY = static_cast<qreal>(by) - labelSize.height() - 2.0;
        paintLabelBar(static_cast<qreal>(bx), textTopY, label, color);
    }
}
