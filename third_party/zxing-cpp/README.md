# ZXing-C++ 读码运行时

- NuGet：`ZXingCpp 0.5.3`，其官方 README 标明包含 ZXing-C++ `3.1.1`。
- 来源：`https://www.nuget.org/packages/ZXingCpp/0.5.3`。
- 上游：`https://github.com/zxing-cpp/zxing-cpp/tree/v3.1.1`。
- 已核对 nupkg SHA256：`8E5E837BD8B12BDFC77210C2DB4DE35AA5E9C8D27DCBBEE8DC2E41AE37A98FFC`。
- 只部署 `runtimes/win-x64/native/ZXing.dll`（3,149,312 字节）和 C# `lib/netstandard2.0/ZXingCpp.dll`（24,064 字节）。不需要 SkiaSharp，直接借用现有 OpenCV 的像素内存。

## ABI 与所有权

`BarcodeReaderModule.cpp` 的私有声明对应 `core/src/ZXingC.h`，保留其作者归属于 `NOTICE.txt`。Windows x64 下 bool、32 位枚举和 32 字节 Position 按此固定版本声明；不暴露到 OpenIVS 公共 ABI。升级 NuGet 时不可只改版本号，必须核对函数签名、枚举值、结构布局、内存释放约定并运行跨语言测试。

C++ 不链接上游 C++ ABI；从本模块目录延迟加载 DLL。返回对象以对应 `ZXing_*_delete` 释放，字符串与格式数组以 `ZXing_free` 释放，`Barcodes_at` 是借用指针。C# 使用官方包装器，reader/barcode 及时 Dispose，ImageView 遵循上游终结器并保持像素所有者存活。

## 许可证文件

`LICENSE.txt` 为上游 Apache-2.0；`NOTICE.txt` 保留 C ABI 作者、libzueci、libzint BSD-3-Clause 和随上游写码器包含的字体声明。两个文件保留在本目录，工程构建和 wheel 打包不复制到输出目录。

运行库仍按工程依赖复制 `ZXing.dll` 和 C# 包装库 `ZXingCpp.dll`。
