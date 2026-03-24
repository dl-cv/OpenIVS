using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// 日志级别枚举
public enum LogLevel
{
    Debug,    // 调试信息
    Info,     // 一般信息
    Warning,  // 警告信息
    Error,    // 错误信息
    Fatal     // 严重错误信息
}

namespace DlcvCamDemo
{
    public static class Logger
    {
        private static readonly object _lock = new object(); // 用于线程安全
        private static string _logFolderPath = "logs"; // 日志文件夹路径
        private static string _logFilePath; // 日志文件路径

        static Logger()
        {
            // 初始化日志文件夹和文件
            InitializeLogFile();
        }

        /// <summary>
        /// 初始化日志文件夹和文件
        /// </summary>
        private static void InitializeLogFile()
        {
            try
            {
                // 确保日志文件夹存在
                if (!Directory.Exists(_logFolderPath))
                {
                    Directory.CreateDirectory(_logFolderPath);
                }

                // 生成以当前时间命名的日志文件
                string logFileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                _logFilePath = Path.Combine(_logFolderPath, logFileName);

                // 创建日志文件（如果不存在）
                if (!File.Exists(_logFilePath))
                {
                    using (File.Create(_logFilePath)) { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize log file: {ex.Message}");
            }
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志信息</param>
        public static void Log(LogLevel level, string message)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

            // 输出到控制台
            Console.WriteLine(logEntry);

            // 写入文件
            lock (_lock)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(_logFilePath, true))
                    {
                        writer.WriteLine(logEntry);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to write log to file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 记录调试信息
        /// </summary>
        /// <param name="args">调试信息（支持多个参数）</param>
        public static void Debug(params object[] args)
        {
            string message = string.Join(" ", args); // 将所有参数拼接成一个字符串
            Log(LogLevel.Debug, message);
        }

        /// <summary>
        /// 记录一般信息
        /// </summary>
        /// <param name="args">一般信息（支持多个参数）</param>
        public static void Info(params object[] args)
        {
            string message = string.Join(" ", args); // 将所有参数拼接成一个字符串
            Log(LogLevel.Info, message);
        }

        /// <summary>
        /// 记录警告信息
        /// </summary>
        /// <param name="args">警告信息（支持多个参数）</param>
        public static void Warning(params object[] args)
        {
            string message = string.Join(" ", args); // 将所有参数拼接成一个字符串
            Log(LogLevel.Warning, message);
        }

        /// <summary>
        /// 记录错误信息
        /// </summary>
        /// <param name="args">错误信息（支持多个参数）</param>
        public static void Error(params object[] args)
        {
            string message = string.Join(" ", args); // 将所有参数拼接成一个字符串
            Log(LogLevel.Error, message);
        }

        /// <summary>
        /// 记录严重错误信息
        /// </summary>
        /// <param name="args">严重错误信息（支持多个参数）</param>
        public static void Fatal(params object[] args)
        {
            string message = string.Join(" ", args); // 将所有参数拼接成一个字符串
            Log(LogLevel.Fatal, message);
        }
    }
}
