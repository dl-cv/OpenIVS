#pragma once

#include <QColor>
#include <QDir>
#include <QFileInfo>
#include <QImage>
#include <QPainter>
#include <QString>
#include <iostream>
#include <opencv2/core.hpp>

// 在实际 Demo 的绘制控件中检查完整图掩码，避免对已带原图坐标的掩码再次偏移。
template<typename ObjectResult>
int RunQtMaskVisualizationSelfTest(const QString& outputPath, ObjectResult object) {
    const QFileInfo outputInfo(outputPath);
    if (outputPath.isEmpty() || !outputInfo.dir().exists()) {
        std::cerr << "mask test output directory does not exist\n";
        return 2;
    }

    cv::Mat image(80, 100, CV_8UC3, cv::Scalar(0, 0, 0));
    cv::Mat mask(80, 100, CV_8UC1, cv::Scalar(0));
    mask(cv::Rect(10, 15, 12, 8)).setTo(cv::Scalar(255));
    object.categoryId = 0;
    object.categoryName.clear();
    object.score = 0.99f;
    object.bbox = {10.0, 15.0, 12.0, 8.0};
    object.withBbox = true;
    object.withMask = true;
    object.mask = mask;
    object.withAngle = false;

    ImageViewerWidget viewer;
    viewer.resize(500, 400);
    viewer.setShowStatusText(false);
    viewer.setLabelDisplayMode(ImageViewerWidget::LabelTextMode::None);
    viewer.setImageAndResults(image, {object});
    viewer.ensurePolished();
    QImage rendered(viewer.size(), QImage::Format_RGB32);
    rendered.fill(Qt::black);
    QPainter painter(&rendered);
    viewer.render(&painter);
    painter.end();
    if (rendered.isNull() || !rendered.save(outputPath)) {
        std::cerr << "mask test could not save image\n";
        return 1;
    }
    const QColor expected(rendered.pixel(72, 92));
    const QColor doubleOffset(rendered.pixel(122, 167));
    if (expected.green() <= expected.red() || expected.green() <= expected.blue()) {
        std::cerr << "full-image mask was not drawn at original coordinates\n";
        return 3;
    }
    if (doubleOffset.green() != 0 || doubleOffset.red() != 0 || doubleOffset.blue() != 0) {
        std::cerr << "full-image mask received bbox offset\n";
        return 3;
    }
    std::cout << "mask visualization selftest passed\n";
    return 0;
}
