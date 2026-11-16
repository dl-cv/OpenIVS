#pragma once

#include <QBuffer>
#include <QImageReader>
#include <QtEndian>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

namespace ImageFileReader {

inline int ReadExifOrientation(QByteArray data) {
    if (data.startsWith(QByteArray("Exif\0\0", 6))) data.remove(0, 6);
    if (data.size() < 8) return 1;
    const bool little = data.startsWith("II");
    if (!little && !data.startsWith("MM")) return 1;
    const auto read16 = [&](qsizetype at) {
        return little ? qFromLittleEndian<quint16>(data.constData() + at)
                      : qFromBigEndian<quint16>(data.constData() + at);
    };
    const auto read32 = [&](qsizetype at) {
        return little ? qFromLittleEndian<quint32>(data.constData() + at)
                      : qFromBigEndian<quint32>(data.constData() + at);
    };
    if (read16(2) != 42) return 1;
    const quint32 ifd = read32(4);
    if (ifd < 8 || ifd > data.size() - 2) return 1;
    const int count = read16(ifd);
    qsizetype entry = qsizetype(ifd) + 2;
    for (int i = 0; i < count && entry <= data.size() - 12; ++i, entry += 12) {
        if (read16(entry) != 274) continue;
        if (read16(entry + 2) != 3 || read32(entry + 4) != 1) return 1;
        const int orientation = read16(entry + 8);
        return orientation >= 1 && orientation <= 8 ? orientation : 1;
    }
    return 1;
}

inline int ReadChunkOrientation(const QByteArray& bytes) {
    const bool png = bytes.startsWith(QByteArray("\x89PNG\r\n\x1a\n", 8));
    const bool webp = bytes.startsWith("RIFF") && bytes.mid(8, 4) == "WEBP";
    if (!png && !webp) return 1;
    qsizetype end = bytes.size();
    if (webp) {
        const quint32 size = qFromLittleEndian<quint32>(bytes.constData() + 4);
        if (size < 4 || size > bytes.size() - 8) return 1;
        end = qsizetype(size) + 8;
    }
    const int overhead = png ? 12 : 8;
    for (qsizetype offset = png ? 8 : 12; offset <= end - overhead;) {
        const quint32 length = png ? qFromBigEndian<quint32>(bytes.constData() + offset)
            : qFromLittleEndian<quint32>(bytes.constData() + offset + 4);
        if (length > end - offset - overhead) return 1;
        if (bytes.mid(offset + (png ? 4 : 0), 4) == (png ? "eXIf" : "EXIF"))
            return ReadExifOrientation(bytes.mid(offset + 8, length));
        offset += overhead + qsizetype(length) + (png ? 0 : length % 2);
    }
    return 1;
}

inline cv::Mat Decode(const QByteArray& bytes) {
    if (bytes.isEmpty()) return {};
    // OpenCV 4.10 has no GIF decoder. Qt reads the first displayed frame.
    if (bytes.startsWith("GIF87a") || bytes.startsWith("GIF89a")) {
        QBuffer buffer;
        buffer.setData(bytes);
        buffer.open(QIODevice::ReadOnly);
        QImageReader reader(&buffer, "gif");
        QImage frame = reader.read().convertToFormat(QImage::Format_ARGB32);
        if (frame.isNull()) return {};
        cv::Mat image(frame.height(), frame.width(), CV_8UC4, frame.bits(), frame.bytesPerLine());
        // Qt retains palette colors under transparent GIF pixels; inference drops alpha.
        image.forEach<cv::Vec4b>([](cv::Vec4b& pixel, const int*) {
            if (pixel[3] == 0) pixel = cv::Vec4b(0, 0, 0, 0);
        });
        return image.clone();
    }
    // JPEG has no alpha; other formats retain their channels and depth.
    const bool jpeg = bytes.startsWith(QByteArray("\xff\xd8\xff", 3));
    cv::Mat image = cv::imdecode(std::vector<uchar>(bytes.begin(), bytes.end()),
        jpeg ? cv::IMREAD_ANYCOLOR | cv::IMREAD_ANYDEPTH : cv::IMREAD_UNCHANGED);
    if (image.empty()) return image;
    // TIFF already handles orientation in its decoder; only PNG/WebP need this step.
    switch (ReadChunkOrientation(bytes)) {
    case 2: cv::flip(image, image, 1); break;
    case 3: cv::flip(image, image, -1); break;
    case 4: cv::flip(image, image, 0); break;
    case 5: cv::transpose(image, image); break;
    case 6: cv::transpose(image, image); cv::flip(image, image, 1); break;
    case 7: cv::transpose(image, image); cv::flip(image, image, -1); break;
    case 8: cv::transpose(image, image); cv::flip(image, image, 0); break;
    }
    return image;
}

}  // namespace ImageFileReader
