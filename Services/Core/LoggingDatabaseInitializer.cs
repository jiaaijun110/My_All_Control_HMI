using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using Serilog;
using Serilog.Events;

namespace Services.Core
{
    public static class LoggingDatabaseInitializer
    {
        private static readonly object SyncRoot = new();
        private static string _logFilePath = string.Empty;
        private static bool _initialized;
        public static string LogFilePath => _logFilePath;
        public static InMemoryLogSink MemorySink { get; private set; } = null!;
        public static event Action<LogEventDto>? RealtimeLogAppended;
        public static event Action<LogEventDto>? LatestInfoLog;
        public static void InitializeSerilog()
        {
            lock (SyncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, "log-.txt");

                MemorySink = new InMemoryLogSink();
                MemorySink.LogEmitted += OnMemoryLogEmitted;

                // 逻辑意图：与 appsettings 中 outputTemplate 对齐，便于历史正则解析。
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .Enrich.FromLogContext()
                    .Enrich.WithThreadId()
                    .WriteTo.Async(a => a.File(
                        Path.Combine(logDir, "log-.txt"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [ThreadId:{ThreadId}] {Message:lj}{NewLine}{Exception}"))
                    .WriteTo.Sink(MemorySink)
                    .CreateLogger();

                _initialized = true;
            }
        }
        public static void InitializeSqlite()
        {
            // TODO: 创建 Data/Logs.db 及 LogEntries 表后，可与 LogHistoryQueryService.TryQuerySqliteAsync 对接。
        }
        private static void OnMemoryLogEmitted(LogEvent logEvent)
        {
            var dto = LogEventMapper.ToDto(logEvent);
            RealtimeLogAppended?.Invoke(dto);
            if (logEvent.Level == LogEventLevel.Information)
            {
                LatestInfoLog?.Invoke(dto);
            }
        }
        /// <param name="message">日志内容。</param>
        public static void LogInformation(string message)
        {
            EnsureLogger();
            Log.Information(message);
        }
        /// <param name="message">日志内容。</param>
        public static void LogWarning(string message)
        {
            EnsureLogger();
            Log.Warning(message);
        }
        /// <param name="message">日志内容。</param>
        /// <param name="exception">异常对象。</param>
        public static void LogError(string message, Exception? exception = null)
        {
            EnsureLogger();
            if (exception == null)
            {
                Log.Error(message);
            }
            else
            {
                Log.Error(exception, message);
            }
        }
        /// <param name="environmentName">运行环境名称。</param>
        public static void LogStartupAudit(string environmentName)
        {
            EnsureLogger();
            var version = Assembly.GetEntryAssembly()?.GetName().Version
                          ?? Assembly.GetExecutingAssembly().GetName().Version;
            var ipAddresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString());

            Log.Information(">>> 软件启动 | 环境: {Env} <<<", environmentName);
            Log.Information("============================================================");
            Log.Information("【系统审计】软件版本: {Version}", version);
            Log.Information("【系统审计】机器名称: {Machine} | 用户: {User}", Environment.MachineName, Environment.UserName);
            Log.Information("【系统审计】操作系统: {Os} ({Arch})", Environment.OSVersion, Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
            Log.Information("【系统审计】运行时环境: .NET {Ver}", Environment.Version);
            Log.Information("【系统审计】本机 IP 地址: {Ips}", string.Join(", ", ipAddresses));
            Log.Information("【系统审计】物理内存: {Ram} GB | 核心数: {Cpu}",
                Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d / 1024d, 2),
                Environment.ProcessorCount);
            Log.Information("【系统审计】运行目录: {Dir}", AppDomain.CurrentDomain.BaseDirectory);
            Log.Information("============================================================");
        }
        public static void Shutdown()
        {
            Log.CloseAndFlush();
        }

        private static void EnsureLogger()
        {
            if (!_initialized)
            {
                InitializeSerilog();
            }
        }
    }
}

