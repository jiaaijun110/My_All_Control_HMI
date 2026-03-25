using System.Diagnostics;

namespace Services.Core
{
    public sealed class SystemResourceMonitorService
    {
        private readonly Process _process = Process.GetCurrentProcess();
        private TimeSpan _lastTotalProcessorTime;
        private DateTime _lastSampleAt;
        public SystemResourceMonitorService()
        {
            _lastSampleAt = DateTime.UtcNow;
            _lastTotalProcessorTime = _process.TotalProcessorTime;
        }
        /// <returns>CPU 与内存占用信息。</returns>
        public string GetUsageText()
        {
            _process.Refresh();
            var now = DateTime.UtcNow;
            var totalCpu = _process.TotalProcessorTime;
            var elapsedMs = (now - _lastSampleAt).TotalMilliseconds;

            double cpu = 0;
            if (elapsedMs > 0)
            {
                var cpuMs = (totalCpu - _lastTotalProcessorTime).TotalMilliseconds;
                cpu = cpuMs / (Environment.ProcessorCount * elapsedMs) * 100.0;
            }

            _lastSampleAt = now;
            _lastTotalProcessorTime = totalCpu;

            var memMb = _process.WorkingSet64 / 1024.0 / 1024.0;
            return $"CPU: {cpu:0.0}% | MEM: {memMb:0} MB";
        }
    }
}

