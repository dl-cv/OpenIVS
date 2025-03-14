# SimpleLogger

一个简单但功能完善的C#日志记录库，基于.NET Framework 4.7.2开发。

## 功能特点

- 支持五种日志级别：Debug、Info、Warning、Error、Fatal
- 线程安全的日志记录
- 自动创建日志文件和日志目录
- 可配置是否输出到控制台
- 支持记录异常信息及堆栈跟踪
- 简洁易用的API

## 使用方法

### 基本用法

```csharp
// 创建日志记录器实例
var logger = new SimpleLogger.Logger();

// 记录不同级别的日志
logger.Debug("这是一条调试信息");
logger.Info("这是一条一般信息");
logger.Warning("这是一条警告信息");
logger.Error("这是一条错误信息");
logger.Fatal("这是一条严重错误信息");

// 记录异常
try {
    // 可能发生异常的代码
} catch (Exception ex) {
    logger.LogException(ex, "发生异常");
}
```

### 自定义配置

```csharp
// 创建自定义配置的日志记录器
var logger = new SimpleLogger.Logger(
    logDirectory: "自定义日志目录",
    consoleOutput: true,
    minLogLevel: LogLevel.Warning // 只记录警告及以上级别的日志
);

// 获取日志文件路径
string logFilePath = logger.GetLogFilePath();

// 启用或禁用控制台输出
logger.EnableConsoleOutput(false);

// 修改日志级别
logger.SetMinLogLevel(LogLevel.Error);
```

## 演示应用

项目包含一个Windows Forms演示应用，展示了SimpleLogger的各项功能：

- 记录不同级别的日志
- 查看日志文件内容
- 打开日志文件目录

## 开发环境

- Visual Studio
- .NET Framework 4.7.2

## 许可证

MIT 