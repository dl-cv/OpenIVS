using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class PressureTestRunner
{
    private List<string> _imagePaths;
    private ConcurrentQueue<long> _timeStamps;
    private int _currentImageIndex;
    private int _totalProcessed;
    private CancellationTokenSource _cts;
    private volatile bool _isRunning; // 新增的运行状态标记

    public event Action<Bitmap, string> ImageUpdatedEvent;
    public event Action<string> ErrorMessageEvent;
    public event Action TestCompleted;
    public Stopwatch _stopwatch;

    public PressureTestRunner(List<string> imagePaths)
    {
        _imagePaths = imagePaths;
        _timeStamps = new ConcurrentQueue<long>();
        _currentImageIndex = 0;
        _totalProcessed = 0;
        _cts = null; // 初始化为null
        _isRunning = false;
        _stopwatch = new Stopwatch();
    }

    public void RunPressureTest(int threadCount, int targetRate)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("测试已经在运行中，无法启动新的测试。");
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _isRunning = true;

        try
        {
            // 计算基础间隔时间（单位：ms）
            int baseInterval = (int)(1000.0 * threadCount / targetRate);
            var threads = new List<Thread>();

            _currentImageIndex = 0;

            _stopwatch.Start();
            for (int i = 0; i < threadCount; i++)
            {
                Thread thread = new Thread(() => WorkerThread(token, baseInterval));
                thread.Start();
                threads.Add(thread);
            }

            // 等待所有线程完成
            foreach (var thread in threads)
            {
                thread.Join();
            }

        }
        catch (OperationCanceledException)
        {
            // 处理取消操作
        }
        finally
        {
            OnTestCompleted();
            _cts?.Dispose();
            _cts = null;
            _stopwatch?.Stop();

        }
    }

    // 新增：记录上次的统计值
    private long previousTotal = 0;
    private long previousTime = 0;
    private readonly object lockObj = new object();

    // 修改方法
    public List<int> GetTotalProcessedAndTime()
    {
        lock (lockObj)
        {
            int currentTotal = _totalProcessed;
            long currentTime = _stopwatch.ElapsedMilliseconds;

            // 计算差值
            int deltaTotal = currentTotal - (int)previousTotal;
            int deltaTime = (int)(currentTime - previousTime);

            // 更新上次记录值
            previousTotal = currentTotal;
            previousTime = currentTime;

            return new List<int> { deltaTotal, deltaTime };
        }
    }

    // 新增的取消方法
    public void Cancel()
    {
        _cts?.Cancel();
    }

    // 新增的查询是否在运行的方法
    public bool IsRunning => _isRunning;

    private void WorkerThread(CancellationToken token, int baseInterval)
    {
        while (!token.IsCancellationRequested)
        {
            int index = Interlocked.Increment(ref _currentImageIndex) % _imagePaths.Count;
            var path = _imagePaths[index];

            var sw = Stopwatch.StartNew();
            ProcessImage(path);
            sw.Stop();

            // 计算剩余等待时间
            int remainingWait = baseInterval - (int)sw.ElapsedMilliseconds;
            if (remainingWait > 0)
            {
                Thread.Sleep(remainingWait);
            }
        }
    }

    private void ProcessImage(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            { 
                var bmp = new Bitmap(fs);
                ImageUpdatedEvent?.Invoke(bmp, path);
            }

            _timeStamps.Enqueue(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
            Interlocked.Increment(ref _totalProcessed);
        }
        catch (Exception ex)
        {
            ErrorMessageEvent?.Invoke($"处理失败: {ex.Message}");
        }
    }

    private void OnTestCompleted()
    {
        _isRunning = false;
        TestCompleted?.Invoke(); // 触发完成事件
    }
}
