using System;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace DlcvCSharpTest
{
    internal static class GpuProcessMemorySample
    {
        internal static JObject Read(bool requirePositive, out long bytes)
        {
            string prefix = "pid_" + Process.GetCurrentProcess().Id + "_";
            var instances = new JObject();
            var category = new PerformanceCounterCategory("GPU Process Memory");
            foreach (string name in category.GetInstanceNames())
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                using (var counter = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", name, true))
                    instances[name] = counter.RawValue;
            }
            bytes = Validate(instances, requirePositive);
            return instances;
        }

        internal static long Validate(JObject instances, bool requirePositive)
        {
            long total = 0;
            foreach (var instance in instances.Properties())
            {
                long value = instance.Value.Value<long>();
                if (value < 0) throw new InvalidOperationException("显存计数器返回负数: " + instance.Name);
                total = checked(total + value);
            }
            if (requirePositive && !instances.HasValues)
                throw new InvalidOperationException("模型加载或推理后未找到当前 PID 的显存计数器实例");
            if (requirePositive && total <= 0)
                throw new InvalidOperationException("模型加载或推理后进程专用显存必须大于零");
            return total;
        }
    }
}
