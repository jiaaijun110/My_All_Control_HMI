using Common.CPlc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drivers.DPlc.Siemens
{
    public class MockPlcDriver : IPlc
    {
        private readonly ILogger<MockPlcDriver> _logger;
        private readonly PlcConfig _config;
        private bool _connected;
        private int _readCount;

        // 【最标准写法】同时注入日志和配置
        public MockPlcDriver(ILogger<MockPlcDriver> logger, IOptions<PlcConfig> options)
        {
            _logger = logger;
            _config = options.Value;
        }

        /// <inheritdoc />
        public bool IsConnected => _connected;

        public bool Connect()
        {
            // 使用配置里的 IP 和端口，并通过日志记录
            _logger.LogInformation($"模拟PLC：正在尝试连接到 {_config.IpAddress}:{_config.Port}...");
            return true;
        }

        public bool WriteTag(string address, object value)
        {
            _logger.LogInformation($"模拟PLC写入：地址={address}, 值={value}");
            return true;
        }

        public object ReadTag(string address)
        {
            _logger.LogInformation($"模拟PLC读取：地址={address}");
            return "MockValue_OK";
        }

        public void ReadClass(object instance, int db, int startByte = 0)
        {
            // 模拟环境下，我们直接什么都不做，或者手动给 instance 赋点假值
            _logger.LogInformation($"[模拟读取] 正在批量读取 DB{db}");

            // 如果你想让模拟环境也有数据，可以在这里用反射给 instance 赋值，
            // 但目前为了跑通编译，留空即可。
        }

        public byte[]? ReadBytes(int db, int startByte, int count)
        {
            _logger.LogInformation("[模拟读取] 读取 DB{db} 的字节流", db);
            return new byte[count]; // 返回一个空数组，保证流程不崩
        }
        public Task<bool> ConnectAsync()
        {
            _logger.LogInformation("模拟PLC：异步连接到 {Ip}:{Port}...", _config.IpAddress, _config.Port);
            _connected = true;
            return Task.FromResult(true);
        }
        public Task<byte[]?> ReadBytesAsync(int db, int startByte, int count)
        {
            if (!_connected)
            {
                return Task.FromResult<byte[]?>(null);
            }

            _readCount++;
            var buffer = new byte[count];
            // 逻辑意图：按 DB7 与 Struct 布局写入两路温湿度（与现场地址 DBD4/8/26/30 一致）。
            if (db == 7 && count >= 34 && startByte == 0)
            {
                WriteReal(buffer, 4, 27.5f + (_readCount % 10) * 0.05f);
                WriteReal(buffer, 8, 45.0f + (_readCount % 6) * 0.1f);
                WriteReal(buffer, 26, 26.8f + (_readCount % 8) * 0.05f);
                WriteReal(buffer, 30, 44.2f + (_readCount % 5) * 0.1f);
            }

            _logger.LogDebug("[模拟读取] DB{Db} 偏移 {Start} 长度 {Count}", db, startByte, count);
            return Task.FromResult<byte[]?>(buffer);
        }

        private static void WriteReal(byte[] buffer, int offset, float value)
        {
            var raw = BitConverter.GetBytes(value);
            buffer[offset] = raw[3];
            buffer[offset + 1] = raw[2];
            buffer[offset + 2] = raw[1];
            buffer[offset + 3] = raw[0];
        }
    }
}
