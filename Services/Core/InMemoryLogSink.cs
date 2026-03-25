using Serilog.Core;
using Serilog.Events;

namespace Services.Core
{
    public sealed class InMemoryLogSink : ILogEventSink
    {
        public event Action<LogEvent>? LogEmitted;
        /// <param name="logEvent">日志事件。</param>
        public void Emit(LogEvent logEvent)
        {
            LogEmitted?.Invoke(logEvent);
        }
    }
}

