

namespace Common.CPlc
{
    public class PlcConfig
    {
        // 属性名必须与 JSON 中的 Key 完全一致（忽略大小写）
        public string IpAddress { get; set; } = string.Empty;

        public int Port { get; set; }

        // 建议增加这个，方便在代码里判断
        public bool UseMock { get; set; }
    }
}
