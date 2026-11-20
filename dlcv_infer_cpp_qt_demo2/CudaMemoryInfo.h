#pragma once

#include <QString>

namespace cuda_memory {

// 生成“CUDA 显存: 本程序 X / 共 Y”显示文本，deviceIndex 为当前使用的显卡编号。
QString describe(int deviceIndex);

QString formatSize(unsigned long long bytes);

}  // namespace cuda_memory
