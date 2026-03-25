using Common.CPlc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S7.Net;


namespace Drivers.DPlc.Siemens
{
    // 继承 IDisposable 接口
    public class RealPlcDriver : IPlc, IDisposable
    {
        // --- 重新补全这些缺失的成员字段 ---
        private readonly ILogger<RealPlcDriver> _logger;
        private readonly PlcConfig _config;
        private Plc? _client;
        private bool _disposed = false;

        // 使用 SemaphoreSlim 代替原来的 lock object
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public RealPlcDriver(ILogger<RealPlcDriver> logger, IOptions<PlcConfig> options)
        {
            _logger = logger;
            _config = options.Value;
            // 初始化西门子 S7-1200 实例
            _client = new Plc(CpuType.S71200, _config.IpAddress, 0, 1);
        }

        // 修复 IsConnected 属性，不要直接抛出异常
        public bool IsConnected => _client?.IsConnected ?? false;

        public async Task<bool> ConnectAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (IsConnected) return true;
                await Task.Run(() => _client!.Open());
                return IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError("PLC 异步连接异常: {Msg}", ex.Message); //
                return false;
            }
            finally { _semaphore.Release(); }
        }

        public async Task<byte[]?> ReadBytesAsync(int db, int startByte, int count)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!IsConnected) return null;
                return await Task.Run(() => _client!.ReadBytes(DataType.DataBlock, db, startByte, count));
            }
            catch (Exception ex)
            {
                _logger.LogError("底层异步读取失败: {Msg}", ex.Message); //
                return null;
            }
            finally { _semaphore.Release(); }
        }

        // 兼容原有的同步接口（如果需要）
        public void ReadClass(object instance, int db, int startByte = 0)
        {
            _client?.ReadClass(instance, db, startByte);
        }

        // ... 构造函数与异步方法 ...

        public void Dispose()
        {
            Dispose(true);
            // 告诉 GC 不需要再调用此对象的析构函数，提高性能
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 1. 释放托管资源（如 SemaphoreSlim）
                _semaphore?.Dispose();

                // 2. 释放硬件连接 (最重要的工业要求)
                try
                {
                    if (_client != null)
                    {
                        _client.Close(); // 显式关闭西门子 TCP 连接
                        _logger.LogInformation("PLC 连接已安全关闭并释放资源。");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "关闭 PLC 连接时发生异常");
                }
            }
            _disposed = true;
        }
    }
}