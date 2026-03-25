using Serilog.Events;

namespace Services.Core
{
    internal static class LogEventMapper
    {
        /// <param name="logEvent">Serilog 日志事件。</param>
        /// <returns>用于绑定与历史解析的 DTO。</returns>
        internal static LogEventDto ToDto(LogEvent logEvent)
        {
            var source = "HMI";
            if (logEvent.Properties.TryGetValue("SourceContext", out var sc))
            {
                source = sc.ToString().Trim('"');
            }

            return new LogEventDto
            {
                Timestamp = logEvent.Timestamp.LocalDateTime,
                Level = ToUiLevel(logEvent.Level),
                Source = string.IsNullOrEmpty(source) ? "HMI" : source,
                Message = logEvent.RenderMessage()
            };
        }
        private static string ToUiLevel(LogEventLevel level)
        {
            return level switch
            {
                LogEventLevel.Information => "Info",
                LogEventLevel.Warning => "Warn",
                LogEventLevel.Error or LogEventLevel.Fatal => "Error",
                LogEventLevel.Debug => "Debug",
                LogEventLevel.Verbose => "Verbose",
                _ => "Info"
            };
        }
    }
}

