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
        
        // S7-1200 固定参数（本项目目前写死）
        private const CpuType Cpu = CpuType.S71200;
        private const short Rack = 0;
        private const short Slot = 1;

        public RealPlcDriver(ILogger<RealPlcDriver> logger, IOptions<PlcConfig> options)
        {
            _logger = logger;
            _config = options.Value;
            // 初始化西门子 S7-1200 实例
            _client = new Plc(CpuType.S71200, _config.IpAddress, 0, 1);
        }

        // 修复 IsConnected 属性，不要直接抛出异常
        public bool IsConnected => _client?.IsConnected ?? false;

        private void ResetClientUnsafe()
        {
            // 网线拔插后，S7.Net 的客户端有时会进入“逻辑上仍连着/但实际不可读”的卡死状态。
            // 重连时主动 Close 并重建 Plc 实例，避免一直复用旧 socket/session。
            try
            {
                _client?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ResetClientUnsafe: Close 旧客户端失败（忽略）。");
            }

            _client = new Plc(Cpu, _config.IpAddress, Rack, Slot);
        }

        public async Task<bool> ConnectAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_disposed) return false;
                if (IsConnected) return true;
                
                // 主动重建客户端，解决拔插后“无法重新连上”的问题
                ResetClientUnsafe();

                await Task.Run(() => _client!.Open());
                return IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PLC 异步连接异常");
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
            finally { _semaphore.Release(); }
        }

        public async Task WriteAsync(string address, object value)
        {
            await _semaphore.WaitAsync();
            try
            {
                if (!IsConnected)
                {
                    throw new InvalidOperationException("PLC 未连接，无法写入。");
                }

                if (string.IsNullOrWhiteSpace(address))
                {
                    throw new ArgumentException("address 不能为空。", nameof(address));
                }

                // 直接调用 S7.Net 的 Write：例如 "DB9.DBX0.0"
                await Task.Run(() => _client!.Write(address, value));
            }
            finally
            {
                _semaphore.Release();
            }
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
                _disposed = true;

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
        }
    }
}