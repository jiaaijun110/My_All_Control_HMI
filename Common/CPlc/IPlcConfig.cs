

namespace Common.CPlc
{
    public class PlcConfig
    {
        // 属性名必须与 JSON 中的 Key 完全一致（忽略大小写）
        public string IpAddress { get; set; } = string.Empty;

        public int Port { get; set; }

        // 建议增加这个，方便在代码里判断
        public bool UseMock { get; set; }

        // -----------------------------
        // Heartbeat Check 配置
        // -----------------------------
        // 采用 PLC DB 中的自增变量做心跳监测（如 Heartbeat_Int）。
        // 心跳变量所在的 DB 号/字节偏移/字节长度由现场 PLC 定义决定。
        public int HeartbeatDbNumber { get; set; } = 7;
        public int HeartbeatByteOffset { get; set; } = 0;

        // 读取心跳变量的字节长度：常见为 INT(2字节) 或 DINT(4字节)。
        public int HeartbeatValueByteSize { get; set; } = 4;

        // 5秒内心跳无变化 => Fault
        public int HeartbeatTimeoutMs { get; set; } = 5000;

        // Fault 状态下，每隔 5 秒尝试重连并检查心跳恢复
        public int ReconnectIntervalMs { get; set; } = 5000;
    }
}
