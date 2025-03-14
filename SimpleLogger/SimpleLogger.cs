using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SimpleLogger
{
    /// <summary>
    /// 日志级别枚举
    /// </summary>
    public enum LogLevel
    {
        Debug,    // 调试信息
        Info,     // 一般信息
        Warning,  // 警告信息
        Error,    // 错误信息
        Fatal     // 严重错误信息
    }

    /// <summary>
    /// 简易日志记录器
    /// </summary>
    public class Logger
    {
        #region 私有字段

        private static readonly object _lock = new object(); // 线程同步锁
        private string _logDirectory = "logs"; // 默认日志目录
        private string _logFilePath; // 当前日志文件路径
        private bool _consoleOutput = true; // 是否输出到控制台
        private LogLevel _minLogLevel = LogLevel.Debug; // 最低记录的日志级别
        private bool _includeTimestamp = true; // 是否包含时间戳
        private string _dateFormat = "yyyy-MM-dd HH:mm:ss.fff"; // 时间格式
        
        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public Logger()
        {
            InitializeLogFile();
        }

        /// <summary>
        /// 带参数的构造函数
        /// </summary>
        /// <param name="logDirectory">日志目录</param>
        /// <param name="consoleOutput">是否输出到控制台</param>
        /// <param name="minLogLevel">最低记录的日志级别</param>
        public Logger(string logDirectory, bool consoleOutput = true, LogLevel minLogLevel = LogLevel.Debug)
        {
            _logDirectory = logDirectory;
            _consoleOutput = consoleOutput;
            _minLogLevel = minLogLevel;
            InitializeLogFile();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 初始化日志文件
        /// </summary>
        private void InitializeLogFile()
        {
            try
            {
                // 创建日志目录
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                // 初始化日志文件，使用日期作为文件名
                string fileName = string.Format("log_{0}.txt", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                _logFilePath = Path.Combine(_logDirectory, fileName);

                // 创建日志文件
                if (!File.Exists(_logFilePath))
                {
                    using (File.Create(_logFilePath)) { }
                }
            }
            catch (Exception ex)
            {
                if (_consoleOutput)
                {
                    Console.WriteLine("初始化日志文件失败: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 写入日志记录
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        private void WriteLog(LogLevel level, string message)
        {
            // 检查日志级别
            if (level < _minLogLevel)
            {
                return;
            }

            // 格式化日志消息
            string logMessage = FormatLogMessage(level, message);

            // 线程安全地写入文件
            lock (_lock)
            {
                try
                {
                    // 写入文件
                    using (StreamWriter writer = new StreamWriter(_logFilePath, true, Encoding.UTF8))
                    {
                        writer.WriteLine(logMessage);
                    }

                    // 根据配置输出到控制台
                    if (_consoleOutput)
                    {
                        Console.WriteLine(logMessage);
                    }
                }
                catch (Exception ex)
                {
                    if (_consoleOutput)
                    {
                        Console.WriteLine("写入日志失败: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// 格式化日志消息
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        /// <returns>格式化后的日志消息</returns>
        private string FormatLogMessage(LogLevel level, string message)
        {
            StringBuilder sb = new StringBuilder();
            
            // 添加时间戳
            if (_includeTimestamp)
            {
                sb.Append(DateTime.Now.ToString(_dateFormat));
                sb.Append(" ");
            }
            
            // 添加日志级别
            sb.Append("[");
            sb.Append(level.ToString().ToUpper());
            sb.Append("] ");
            
            // 添加日志消息
            sb.Append(message);
            
            return sb.ToString();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 记录调试信息
        /// </summary>
        /// <param name="message">日志消息</param>
        public void Debug(string message)
        {
            WriteLog(LogLevel.Debug, message);
        }

        /// <summary>
        /// 记录一般信息
        /// </summary>
        /// <param name="message">日志消息</param>
        public void Info(string message)
        {
            WriteLog(LogLevel.Info, message);
        }

        /// <summary>
        /// 记录警告信息
        /// </summary>
        /// <param name="message">日志消息</param>
        public void Warning(string message)
        {
            WriteLog(LogLevel.Warning, message);
        }

        /// <summary>
        /// 记录错误信息
        /// </summary>
        /// <param name="message">日志消息</param>
        public void Error(string message)
        {
            WriteLog(LogLevel.Error, message);
        }

        /// <summary>
        /// 记录严重错误信息
        /// </summary>
        /// <param name="message">日志消息</param>
        public void Fatal(string message)
        {
            WriteLog(LogLevel.Fatal, message);
        }

        /// <summary>
        /// 记录异常信息
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="additionalInfo">附加信息</param>
        public void LogException(Exception ex, string additionalInfo = "")
        {
            StringBuilder sb = new StringBuilder();
            
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                sb.Append(additionalInfo);
                sb.Append(" - ");
            }
            
            sb.Append("异常: ");
            sb.Append(ex.Message);
            
            if (ex.StackTrace != null)
            {
                sb.AppendLine();
                sb.Append("堆栈跟踪: ");
                sb.Append(ex.StackTrace);
            }
            
            Error(sb.ToString());
        }

        /// <summary>
        /// 设置最低日志级别
        /// </summary>
        /// <param name="level">日志级别</param>
        public void SetMinLogLevel(LogLevel level)
        {
            _minLogLevel = level;
        }

        /// <summary>
        /// 启用或禁用控制台输出
        /// </summary>
        /// <param name="enable">是否启用</param>
        public void EnableConsoleOutput(bool enable)
        {
            _consoleOutput = enable;
        }

        /// <summary>
        /// 获取当前日志文件路径
        /// </summary>
        /// <returns>日志文件路径</returns>
        public string GetLogFilePath()
        {
            return _logFilePath;
        }

        #endregion
    }
} 