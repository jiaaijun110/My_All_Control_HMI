namespace Services.Core
{
    public sealed class LogEventDto
    {
        public DateTime Timestamp { get; init; }
        public string Level { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}

