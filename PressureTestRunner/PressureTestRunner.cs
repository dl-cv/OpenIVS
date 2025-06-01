using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace DLCV
{
    /// <summary>
    /// 压力测试运行器类，用于执行可配置的多线程压力测试
    /// </summary>
    public class PressureTestRunner
    {
        #region 私有成员

        private int _threadCount;
        private int _targetRate;
        private Action<object> _testAction;
        private object _actionParameter;
        private volatile bool _isRunning;
        private List<Thread> _workerThreads;
        private int _completedRequests;
        private int _batchSize;
        private object _lockObject = new object();
        private DateTime _startTime;
        private TimeSpan _duration;
        private Queue<double> _recentLatencies;
        private Queue<DateTime> _requestTimestamps; // 记录请求完成时间戳
        private const int MAX_LATENCY_SAMPLES = 100; // 保存最近几次请求的延迟
        private const int RECENT_RATE_WINDOW_SECONDS = 3; // 最近速率统计窗口（秒）

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取或设置测试使用的线程数量
        /// </summary>
        public int ThreadCount
        {
            get { return _threadCount; }
            set { _threadCount = Math.Max(1, value); }
        }

        /// <summary>
        /// 获取或设置目标每秒请求速率
        /// </summary>
        public int TargetRate
        {
            get { return _targetRate; }
            set { _targetRate = Math.Max(1, value); }
        }

        /// <summary>
        /// 获取测试是否正在运行
        /// </summary>
        public bool IsRunning
        {
            get { return _isRunning; }
        }

        /// <summary>
        /// 获取已完成的请求总数
        /// </summary>
        public int CompletedRequests
        {
            get { return _completedRequests; }
        }

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化压力测试运行器的新实例
        /// </summary>
        /// <param name="threadCount">要使用的线程数量</param>
        /// <param name="targetRate">目标每秒请求数</param>
        public PressureTestRunner(int threadCount, int targetRate = 100, int batchSize = 1)
        {
            ThreadCount = threadCount;
            TargetRate = targetRate;
            _workerThreads = new List<Thread>();
            _completedRequests = 0;
            _batchSize = batchSize;
            _recentLatencies = new Queue<double>(MAX_LATENCY_SAMPLES);
            _requestTimestamps = new Queue<DateTime>();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置要执行的测试操作
        /// </summary>
        /// <param name="action">测试操作的回调函数</param>
        /// <param name="parameter">传递给回调函数的参数</param>
        public void SetTestAction(Action<object> action, object parameter = null)
        {
            if (action == null)
                throw new ArgumentNullException("action", "测试操作不能为空");

            _testAction = action;
            _actionParameter = parameter;
        }

        /// <summary>
        /// 开始执行压力测试
        /// </summary>
        public void Start()
        {
            if (_isRunning)
                return;

            if (_testAction == null)
                throw new InvalidOperationException("未设置测试操作，请先调用SetTestAction方法");

            _isRunning = true;
            _completedRequests = 0;
            _startTime = DateTime.Now;
            
            // 清理之前的数据
            lock (_lockObject)
            {
                _recentLatencies.Clear();
                _requestTimestamps.Clear();
            }

            // 创建并启动工作线程
            for (int i = 0; i < _threadCount; i++)
            {
                Thread thread = new Thread(WorkerThreadFunc);
                thread.IsBackground = true;
                _workerThreads.Add(thread);
                thread.Start(i);
            }
        }

        /// <summary>
        /// 停止执行压力测试
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _duration = DateTime.Now - _startTime;

            // 等待所有线程完成
            foreach (var thread in _workerThreads)
            {
                if (thread.IsAlive)
                    thread.Join(1000);
            }

            _workerThreads.Clear();
        }

        /// <summary>
        /// 获取测试结果统计信息
        /// </summary>
        /// <returns>包含测试统计数据的字符串</returns>
        public string GetStatistics(bool target_rate = true)
        {
            TimeSpan elapsed = _isRunning ? (DateTime.Now - _startTime) : _duration;
            
            // 计算最近3秒的速率
            double recentRate = 0;
            DateTime now = DateTime.Now;
            DateTime cutoffTime = now.AddSeconds(-RECENT_RATE_WINDOW_SECONDS);
            
            lock (_lockObject)
            {
                // 清理过期的时间戳
                while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < cutoffTime)
                {
                    _requestTimestamps.Dequeue();
                }
                
                // 计算最近时间窗口内的请求数量和速率
                int recentRequestCount = _requestTimestamps.Count;
                if (recentRequestCount > 0)
                {
                    // 获取最早的时间戳
                    DateTime earliestTime = _requestTimestamps.Peek();
                    
                    // 计算实际的时间窗口（秒）
                    double actualTimeWindow = (now - earliestTime).TotalSeconds;
                    
                    // 确保时间窗口不为0
                    if (actualTimeWindow > 0)
                    {
                        recentRate = (double)(recentRequestCount * _batchSize) / actualTimeWindow;
                    }
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("压力测试统计:");
            sb.AppendLine($"线程数: {_threadCount}");
            sb.AppendLine($"批量大小: {_batchSize}");
            if (target_rate) sb.AppendLine($"目标速率: {_targetRate} 请求/秒");
            sb.AppendLine($"运行时间: {elapsed.TotalSeconds:F2} 秒");
            sb.AppendLine($"完成请求: {_completedRequests * _batchSize}");
            
            // 计算最近请求的平均延迟（毫秒）
            double averageLatency = 0;
            lock (_lockObject)
            {
                if (_recentLatencies.Count > 0)
                {
                    averageLatency = _recentLatencies.Average();
                }
            }
            sb.AppendLine($"平均延迟: {averageLatency:F2}ms");
            sb.AppendLine($"实时速率: {recentRate:F2} 请求/秒");

            return sb.ToString();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 工作线程的执行函数
        /// </summary>
        private void WorkerThreadFunc(object threadId)
        {
            // 计算每个线程的目标速率
            double targetRatePerThread = (double)_targetRate / _threadCount;
            int requestsPerSecond = (int)Math.Ceiling(targetRatePerThread);

            Stopwatch stopwatch = new Stopwatch();

            while (_isRunning)
            {
                stopwatch.Restart();

                // 执行当前批次的请求
                for (int i = 0; i < requestsPerSecond && _isRunning; i++)
                {
                    try
                    {
                        Stopwatch requestStopwatch = Stopwatch.StartNew();
                        _testAction(_actionParameter);
                        requestStopwatch.Stop();
                        
                        double latency = requestStopwatch.Elapsed.TotalMilliseconds;
                        DateTime requestCompletionTime = DateTime.Now;
                        
                        lock (_lockObject)
                        {
                            _completedRequests++;
                            
                            // 记录请求延迟
                            if (_recentLatencies.Count >= MAX_LATENCY_SAMPLES)
                            {
                                _recentLatencies.Dequeue(); // 移除最早的样本
                            }
                            _recentLatencies.Enqueue(latency);
                            
                            // 记录请求完成时间戳
                            _requestTimestamps.Enqueue(requestCompletionTime);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 在实际应用中，可能需要记录异常或通知调用者
                        Debug.WriteLine($"线程 {threadId} 执行测试操作时出错: {ex.Message}");
                    }
                }

                stopwatch.Stop();

                // 简单的速率控制 - 如果一秒内完成了所有请求，则等待剩余时间
                long elapsedMs = stopwatch.ElapsedMilliseconds;
                if (elapsedMs < 1000 && _isRunning)
                {
                    Thread.Sleep((int)(1000 - elapsedMs));
                }
            }
        }

        #endregion
    }
}
