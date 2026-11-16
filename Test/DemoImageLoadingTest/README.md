# C# Demo 图像文件回归

通过链接生产 `ImageFileReader.cs` 检查真实 OpenCvSharp 4.10 解码结果，不需要模型或 GPU。

1. 使用项目 `build_script/build.py` 构建 `Test/DemoImageLoadingTest/DemoImageLoadingTest.csproj`（Debug/x64）。
2. 直接运行测试 EXE，检查 JPEG 方向 1–8、灰度、同名覆盖、复制改名以及 PNG 透明与位深。
3. 在现有 Python 环境运行 `python Test/DemoImageLoadingTest/generate_cases.py <输出目录>`，需要 Pillow 与 NumPy。
4. 运行测试 EXE，参数为生成的 `cases.tsv`。追加 `--baseline` 使用旧的 `ImreadModes.Unchanged` 进行对照。

生成器以 Pillow 和独立坐标变换构建参考，不使用生产读图函数生成预期结果。覆盖 JPG/JPEG、PNG、BMP、TIF/TIFF、WebP、GIF；包含实际像素旋转、EXIF 1–8、透明、16 位、灰度、GIF 第一帧、大/小端 EXIF 和异常元数据。

无损格式逐像素相等；JPEG 允许两种解码器每通道最大 2 个灰度值差异。8 位透明 TIFF 按 OpenCV/libtiff 原有预乘颜色行为检查，允许整数舍入差 1；本修复不改变其颜色语义。GIF 透明像素的 RGB 统一按 0 比较，alpha 必须匹配。

生成材料写到指定输出目录，不提交客户图片或模型。测试不验证 Windows 右键旋转的写文件过程；它验证各种旋转文件进入程序后的像素、通道和位深。
