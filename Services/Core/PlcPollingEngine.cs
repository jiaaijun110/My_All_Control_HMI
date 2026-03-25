using Common.CPlc;

namespace Services.Core
{
    public sealed class PlcPollingEngine : IDisposable
    {
        private const int DbNumber = 7;

        private const int StartByte = 0;

        private const int ReadLength = 46;

        private readonly IPlc _plc;
        private readonly CancellationTokenSource _cts = new();
        private Task? _pollingTask;
        private int _logCycle;
        public PlcTelemetryModel Telemetry { get; } = new();
        public bool IsRunning => _pollingTask is { IsCompleted: false };
        /// <param name="plc">PLC 驱动实例。</param>
        public PlcPollingEngine(IPlc plc)
        {
            _plc = plc;
        }
        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _pollingTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await PollPlcAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常关闭：PollPlcAsync 内 ThrowIfCancellationRequested / Task.Delay 取消。
                        LoggingDatabaseInitializer.LogInformation("PLC 轮询已取消（正常退出）。");
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 逻辑意图：单次失败不退出线程，标记掉线并记录，下一轮再试。
                        Telemetry.IsConnected = false;
                        LoggingDatabaseInitializer.LogError("PLC 轮询发生异常。", ex);
                    }

                    try
                    {
                        // 逻辑意图：与采集服务节拍对齐，降低 PLC 负载；取消时必须吞掉 OCE，避免任务 Fault 刷屏。
                        await Task.Delay(1000, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        LoggingDatabaseInitializer.LogInformation("PLC 轮询已取消（正常退出）。");
                        break;
                    }
                }
            }, _cts.Token);
        }
        private async Task PollPlcAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!_plc.IsConnected)
            {
                LoggingDatabaseInitializer.LogInformation("正在尝试连接 PLC...");
                var ok = await _plc.ConnectAsync().ConfigureAwait(false);
                if (!ok)
                {
                    Telemetry.IsConnected = false;
                    LoggingDatabaseInitializer.LogInformation("PLC 连接失败，等待重试。");
                    return;
                }

                // 逻辑意图：给 TCP 会话与首次扫描留出稳定时间（与 SortingService 一致）；取消时抛 OCE，由外层 while 统一捕获。
                await Task.Delay(3000, token).ConfigureAwait(false);
            }

            var bytes = await _plc.ReadBytesAsync(DbNumber, StartByte, ReadLength).ConfigureAwait(false);
            if (bytes == null || bytes.Length < 34)
            {
                Telemetry.IsConnected = false;
                LoggingDatabaseInitializer.LogInformation("读取 DB7 失败或长度不足，判定为通讯异常。");
                return;
            }

            Telemetry.IsConnected = true;
            Telemetry.UpdateFromDb7(bytes);

            _logCycle++;
            if (_logCycle % 5 == 0)
            {
                LoggingDatabaseInitializer.LogInformation($"成功读取 DB7 字节流，长度: {bytes.Length}");
                LoggingDatabaseInitializer.LogInformation($"【传感器 1】 温度: {Telemetry.Sensor1.Temperature:F1} ℃ | 湿度: {Telemetry.Sensor1.Humidity:F1} %");
                LoggingDatabaseInitializer.LogInformation($"【传感器 2】 温度: {Telemetry.Sensor2.Temperature:F1} ℃ | 湿度: {Telemetry.Sensor2.Humidity:F1} %");
            }
        }
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            if (_plc is IDisposable d)
            {
                d.Dispose();
            }
        }
    }
}

