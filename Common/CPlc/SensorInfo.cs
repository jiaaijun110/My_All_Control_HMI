namespace Common.CPlc
{
    public class SensorDetail
    {
        public byte StationAddr { get; set; }  // 偏移 0.0
        public float Temperature { get; set; } // 偏移 2.0
        public float Humidity { get; set; }    // 偏移 6.0
        public ushort RawTemp { get; set; }    // 偏移 10.0
        public ushort RawHumidity { get; set; } // 偏移 12.0
        // ... 其他字段补全至 22 字节
    }

    public class PlcData
    {
        public short SensorCount { get; set; }
        // 建议在读取时分段映射，而不是一次性映射 100 个对象
    }
}
