using Common.CPlc;
using System;

namespace Services.Core
{
    public sealed class PlcPollingEngine : IDisposable
    {
        // 采集 DB7 上的传感器数据（当前现场布局固定在代码里）。
        private const int SensorDbNumber = 7;
        private const int SensorStartByte = 0;
        private const int BaseSensorReadLength = 46;

        // DB_Data_Status：DB9
        private const int StatusDbNumber = 9;
        // Start_Status / Start_Stop 对应物理输入 %I0.0 / %I0.1
        // 默认假设在 DB9 的第 0 个字节（DBX0.0 / DBX0.1）
        private const int StatusStartByte = 0;
        private const int StatusReadLength = 1;

        private const int StatusPollIntervalMs = 200;
        private const int SensorPollIntervalMs = 1000;

        private readonly IPlc _plc;
        private readonly PlcConfig _config;
        private readonly CancellationTokenSource _cts = new();
        private Task? _pollingTask;
        private bool _faulted;
        private int _logCycle;

        // 暂时取消 Heartbeat 检测：Fault/恢复仅依赖“读取传感器数据是否成功”
        private readonly int _reconnectIntervalMs;
        private readonly int _sensorReadLength;

        // 记录 DB9 状态上次值：当 System_Running / Start_Status / Start_Stop 变化时打印日志
        private bool? _lastStartStatus;
        private bool? _lastStartStop;
        private bool? _lastSystemRunning;

        public PlcTelemetryModel Telemetry { get; } = new();
        public bool IsRunning => _pollingTask is { IsCompleted: false };
        /// <param name="plc">PLC 驱动实例。</param>
        public PlcPollingEngine(IPlc plc, PlcConfig config)
        {
            _plc = plc;
            _config = config;
            _reconnectIntervalMs = config.ReconnectIntervalMs;

            // 采集 DB7 传感器固定长度
            _sensorReadLength = BaseSensorReadLength;
        }
        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            _pollingTask = Task.Run(async () =>
            {
                var nextSensorReadUtc = DateTime.UtcNow;
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (_faulted)
                        {
                            await ReconnectUntilHeartbeatRestoredAsync(_cts.Token).ConfigureAwait(false);
                            continue;
                        }

                        // 200ms 刷新 DB9 启停状态
                        await PollStatusOnceAsync(_cts.Token).ConfigureAwait(false);

                        // 1s 刷新 DB7 传感器数据
                        var nowUtc = DateTime.UtcNow;
                        if (nowUtc >= nextSensorReadUtc)
                        {
                            await PollSensorOnceAsync(_cts.Token).ConfigureAwait(false);
                            nextSensorReadUtc = nowUtc.AddMilliseconds(SensorPollIntervalMs);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        LoggingDatabaseInitializer.LogInformation("PLC 轮询已取消（正常退出）。");
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 驱动层报错 => 立即 Fault（不等待心跳超时）
                        SetFault(ex, "驱动层异常，立即进入通讯故障状态。");
                    }

                    // 200ms 节拍
                    try
                    {
                        await Task.Delay(StatusPollIntervalMs, _cts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, _cts.Token);
        }

        private async Task EnsureConnectedOrFaultAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (_plc.IsConnected)
            {
                return;
            }

            LoggingDatabaseInitializer.LogInformation("正在尝试连接 PLC...");
            var ok = await _plc.ConnectAsync().ConfigureAwait(false);
            if (!ok)
            {
                SetFault(null, "PLC 连接失败，进入通讯故障状态。");
                return;
            }

            // 给 TCP 会话与首次扫描留出稳定时间
            await Task.Delay(3000, token).ConfigureAwait(false);
        }

        private async Task PollStatusOnceAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await EnsureConnectedOrFaultAsync(token).ConfigureAwait(false);
            if (_faulted)
            {
                return;
            }

            try
            {
                var statusBytes = await _plc.ReadBytesAsync(StatusDbNumber, StatusStartByte, StatusReadLength)
                    .ConfigureAwait(false);
                if (statusBytes == null || statusBytes.Length < 1)
                {
                    SetFault(null, "读取 DB9 启停状态失败。");
                    return;
                }

                var b0 = statusBytes[0];
                ApplyStatusFromByte0(b0, fromReconnect: false);
            }
            catch (Exception ex)
            {
                SetFault(ex, "读取 DB9 启停状态异常。");
            }
        }

        private void ApplyStatusFromByte0(byte b0, bool fromReconnect)
        {
            var newStartStatus = (b0 & 0x01) != 0; // %I0.0
            var newStartStop = (b0 & 0x02) != 0;  // %I0.1
            var newSystemRunning = (b0 & 0x04) != 0; // %I0.2 (System_Running)

            var changed = _lastStartStatus != newStartStatus
                           || _lastStartStop != newStartStop
                           || _lastSystemRunning != newSystemRunning;

            if (changed)
            {
                _lastStartStatus = newStartStatus;
                _lastStartStop = newStartStop;
                _lastSystemRunning = newSystemRunning;

                LoggingDatabaseInitializer.LogInformation(
                    $"DB9 状态变化{(fromReconnect ? "(重连中)" : string.Empty)}：Start_Status={newStartStatus}, Start_Stop={newStartStop}, System_Running={newSystemRunning}, byte0=0x{b0:X2}");
            }

            Telemetry.StartStatus = newStartStatus;
            Telemetry.StartStop = newStartStop;
            Telemetry.SystemRunning = newSystemRunning;
        }

        private async Task PollSensorOnceAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await EnsureConnectedOrFaultAsync(token).ConfigureAwait(false);
            if (_faulted)
            {
                return;
            }

            try
            {
                var sensorBytes = await _plc.ReadBytesAsync(SensorDbNumber, SensorStartByte, _sensorReadLength)
                    .ConfigureAwait(false);
                if (sensorBytes == null || sensorBytes.Length < 34)
                {
                    SetFault(null, "读取 DB7 字节流失败/长度不足，立即进入通讯故障状态。");
                    return;
                }

                Telemetry.UpdateFromDb7(sensorBytes);

                Telemetry.IsConnected = true;
                Telemetry.IsFaulted = false;

                if (_faulted)
                {
                    _faulted = false;
                }

                _logCycle++;
                if (_logCycle % 5 == 0)
                {
                    LoggingDatabaseInitializer.LogInformation($"成功读取 DB7，长度: {sensorBytes.Length}");
                }
            }
            catch (Exception ex)
            {
                SetFault(ex, "驱动层异常（读取 DB7），立即进入通讯故障状态。");
            }
        }

        private async Task ReconnectUntilHeartbeatRestoredAsync(CancellationToken token)
        {
            // Fault 状态下：每隔一段时间尝试重连 + 读取 DB7；读到数据即恢复
            Telemetry.IsConnected = false;
            Telemetry.IsFaulted = true;

            while (!_cts.Token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    await Task.Delay(_reconnectIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                LoggingDatabaseInitializer.LogInformation("通讯故障：尝试重连并读取 DB7。");

                if (!_plc.IsConnected)
                {
                    try
                    {
                        await _plc.ConnectAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LoggingDatabaseInitializer.LogError("重连尝试失败（继续等待下一轮）。", ex);
                    }
                }
                else
                {
                    // 已连接：仍需读心跳确认“心跳变化恢复”
                }

                if (_plc.IsConnected)
                {
                    try
                    {
                        // Fault 状态下仍然尽量刷新 DB9 的运行状态，
                        // 这样“启动自锁/停止自锁”时，UI 指示不会卡住。
                        try
                        {
                            var statusBytes = await _plc.ReadBytesAsync(StatusDbNumber, StatusStartByte, StatusReadLength)
                                .ConfigureAwait(false);
                            if (statusBytes != null && statusBytes.Length >= 1)
                            {
                                var b0 = statusBytes[0];
                                ApplyStatusFromByte0(b0, fromReconnect: true);
                            }
                        }
                        catch (Exception ex)
                        {
                            // DB9 读取失败不影响故障恢复判定（仅用于 UI 指示）
                            LoggingDatabaseInitializer.LogError("重连循环中读取 DB9 失败（忽略）。", ex);
                        }

                        var sensorBytes = await _plc.ReadBytesAsync(SensorDbNumber, SensorStartByte, _sensorReadLength)
                            .ConfigureAwait(false);

                        if (sensorBytes != null && sensorBytes.Length >= 34)
                        {
                            Telemetry.UpdateFromDb7(sensorBytes);
                            _faulted = false;
                            Telemetry.IsConnected = true;
                            Telemetry.IsFaulted = false;

                            LoggingDatabaseInitializer.LogInformation("通讯恢复：读取 DB7 成功，退出通讯故障状态。");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Fault 状态下只记录，不直接退出重连循环
                        LoggingDatabaseInitializer.LogError("重连后读取 DB7 失败（继续等待下一轮）。", ex);
                    }
                }

                // 由循环头部的 Task.Delay(_reconnectIntervalMs) 控制重连间隔
            }
        }

        private int? TryReadHeartbeatInt(byte[] sensorBytes, byte[]? heartbeatBytes)
        {
            // Heartbeat 检测已取消：保留方法仅避免大范围重构。
            return null;
        }

        private static int? ReadHeartbeatFromBytes(byte[] bytes, int offset, int byteSize)
        {
            // Heartbeat 检测已取消：保留方法仅避免大范围重构。
            return null;
        }

        private void SetFault(Exception? ex, string message)
        {
            _faulted = true;
            Telemetry.IsConnected = false;
            Telemetry.IsFaulted = true;

            if (ex != null)
            {
                LoggingDatabaseInitializer.LogError(message, ex);
            }
            else
            {
                LoggingDatabaseInitializer.LogInformation(message);
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

