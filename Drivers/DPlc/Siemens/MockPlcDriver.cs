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
        private int _heartbeatValue;
        private bool _startStatus;
        private bool _startStop;
        private bool _systemRunning;

        // 【最标准写法】同时注入日志和配置
        public MockPlcDriver(ILogger<MockPlcDriver> logger, IOptions<PlcConfig> options)
        {
            _logger = logger;
            _config = options.Value;
            _heartbeatValue = 0;
            _startStatus = false;
            _startStop = false;
            _systemRunning = false;
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

            // 用于“启动/停止”按钮点动联动（DB9 启停状态）
            if (value is bool b && !string.IsNullOrWhiteSpace(address))
            {
                if (address.Contains("SYSTEM_START", StringComparison.OrdinalIgnoreCase))
                {
                    _startStatus = b;
                    if (_startStatus)
                    {
                        _startStop = false;
                    }
                }
                else if (address.Contains("SYSTEM_STOP", StringComparison.OrdinalIgnoreCase))
                {
                    _startStop = b;
                    if (_startStop)
                    {
                        _startStatus = false;
                    }
                }
            }

            return true;
        }

        public Task WriteAsync(string address, object value)
        {
            _logger.LogInformation($"模拟PLC写入(WriteAsync)：address={address}, value={value}");

            if (value is bool b)
            {
                // 直接映射 DB9 的位写入：DB9.DBX0.0 / DB9.DBX0.1
                if (!string.IsNullOrWhiteSpace(address))
                {
                    if (address.Equals("DB9.DBX0.0", StringComparison.OrdinalIgnoreCase))
                    {
                        // 点击发送一次 true：PLC 内部会自动复位 HMI_Start；
                        // 这里用“写入 Start 触发运行”来模拟 System_Running。
                        _systemRunning = b;
                    }
                    else if (address.Equals("DB9.DBX0.1", StringComparison.OrdinalIgnoreCase))
                    {
                        _systemRunning = !b ? _systemRunning : false;
                    }
                    else if (address.Equals("DB9.DBX0.2", StringComparison.OrdinalIgnoreCase))
                    {
                        _systemRunning = b;
                    }
                }
            }

            return Task.CompletedTask;
        }

        public object ReadTag(string address)
        {
            _logger.LogInformation($"模拟PLC读取：地址={address}");
            if (!string.IsNullOrWhiteSpace(address))
            {
                // 默认错误码为 0，避免 UI 灯长期处于“故障”态（联调/仿真友好）。
                if (address.Contains("SERVO_ERROR_ID", StringComparison.OrdinalIgnoreCase) ||
                    address.Contains("STEPPER_ERROR_ID", StringComparison.OrdinalIgnoreCase))
                {
                    return "0";
                }

                // 默认脉冲模式
                if (address.Contains("SERVO_PULSE_MODE", StringComparison.OrdinalIgnoreCase) ||
                    address.Contains("STEPPER_PULSE_MODE", StringComparison.OrdinalIgnoreCase))
                {
                    return "Pulse/Dir";
                }
            }
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

            // DB9：DB_Data_Status（Start_Status / Start_Stop）
            if (db == 9 && startByte == 0 && count >= 1)
            {
                buffer[0] = (byte)(
                    (_startStatus ? 0x01 : 0x00) |
                    (_startStop ? 0x02 : 0x00) |
                    (_systemRunning ? 0x04 : 0x00)
                );
            }

            // 按配置写入心跳变量（自增整数），用于 PLC 心跳监测。
            // 注意：Mock 需要写入“大端字节序”，以匹配真实 S7 返回的数据解析逻辑。
            var hbRelativeOffset = _config.HeartbeatByteOffset - startByte;
            if (db == _config.HeartbeatDbNumber && hbRelativeOffset >= 0 && hbRelativeOffset + _config.HeartbeatValueByteSize <= count)
            {
                unchecked
                {
                    if (_config.HeartbeatValueByteSize == 2)
                    {
                        var v = (short)_heartbeatValue;
                        var raw = BitConverter.GetBytes(v);
                        // raw[0..1] 是小端；写入到 S7 的字节数组使用大端
                        buffer[hbRelativeOffset] = raw[1];
                        buffer[hbRelativeOffset + 1] = raw[0];
                    }
                    else
                    {
                        var v = _heartbeatValue;
                        var raw = BitConverter.GetBytes(v);
                        buffer[hbRelativeOffset] = raw[3];
                        buffer[hbRelativeOffset + 1] = raw[2];
                        buffer[hbRelativeOffset + 2] = raw[1];
                        buffer[hbRelativeOffset + 3] = raw[0];
                    }
                }
                _heartbeatValue++;
            }

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
