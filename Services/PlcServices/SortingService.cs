using Common.CPlc;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class SortingService
    {
        private readonly IPlc _plc;
        private readonly ILogger<SortingService> _logger;
        private CancellationTokenSource? _cts;

        public SortingService(IPlc plc, ILogger<SortingService> logger)
        {
            _plc = plc;
            _logger = logger;
        }

        // 启动异步采集引擎，绝不卡死界面
        public void Start()
        {
            _cts = new CancellationTokenSource();
            // 轮询获取数据，区别订阅模式
            Task.Run(() => PollingLoop(_cts.Token));
            _logger.LogInformation(">>> 2路传感器后台采集引擎已启动 <<<");
        }


        private async Task PollingLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // --- 必须把这里的连接逻辑补上 ---
                    if (!_plc.IsConnected)
                    {
                        _logger.LogInformation("正在尝试连接 PLC...");
                        await _plc.ConnectAsync(); // 调用异步连接
                        try
                        {
                            await Task.Delay(3000, token); // 给连接一点反应时间
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("传感器采集已取消（正常退出）。");
                            return;
                        }

                        continue;
                    }

                    int group = 0;
                    int startAddress = 0;
                    int readLength = 46;

                    var bytes = await _plc.ReadBytesAsync(7, startAddress, readLength);

                    if (bytes != null)
                    {
                        // 添加一条成功日志，确认读取到了数据
                        _logger.LogInformation("成功读取 DB7 字节流，长度: {Len}", bytes.Length);
                        ParseGroupData(group, bytes);
                    }
                }
                catch (OperationCanceledException)
                {
                    // TaskCanceledException 继承此类；取消不是故障。
                    _logger.LogInformation("传感器采集已取消（正常退出）。");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError("采集循环异常: {Msg}", ex.Message);
                }

                try
                {
                    await Task.Delay(1000, token); // 工业标准：1秒采集一次
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("传感器采集已取消（正常退出）。");
                    return;
                }
            }
        }

        private void ParseGroupData(int groupIndex, byte[] bytes)
        {
            // 每个传感器 Struct 长度是 22 字节
            int structSize = 22;
            // 注意：DB 块开头有个 SensorCount(Int)，所以传感器的起点是从第 2 个字节开始
            int dbOffset = 2;

            for (int i = 0; i < 2; i++)
            {
                int baseOffset = dbOffset + (i * structSize);

                // 1. 温度：在结构体内部偏移 2.0，所以绝对偏移是 baseOffset + 2
                float temp = S7.Net.Types.Real.FromByteArray(
                    bytes.Skip(baseOffset + 2).Take(4).ToArray()
                );

                // 2. 湿度：在结构体内部偏移 6.0，所以绝对偏移是 baseOffset + 6
                float humidity = S7.Net.Types.Real.FromByteArray(
                    bytes.Skip(baseOffset + 6).Take(4).ToArray()
                );

                _logger.LogInformation("【传感器 {Id}】 温度: {Temp:F1} ℃ | 湿度: {Hum:F1} %",
                                        i + 1, temp, humidity);
            }
        }
    }
}