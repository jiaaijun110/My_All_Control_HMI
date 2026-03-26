using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Services.Core
{
    public sealed class PlcTelemetryModel : INotifyPropertyChanged
    {
        private bool _isConnected;
        private bool _isFaulted;
        private bool _startStatus;
        private bool _startStop;
        private bool _systemRunning;

        // 通讯故障（心跳超时/驱动异常）=> Fault
        public bool IsFaulted
        {
            get => _isFaulted;
            set
            {
                if (SetField(ref _isFaulted, value))
                {
                    // 与 IsConnected 相关的显示字段一起刷新
                    RaiseDashboardFields();
                }
            }
        }

        public PlcSensorPointModel Sensor1 { get; } = new("1号传感器");
        public PlcSensorPointModel Sensor2 { get; } = new("2号传感器");
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetField(ref _isConnected, value))
                {
                    RaiseDashboardFields();
                }
            }
        }

        // DB_Data_Status (DB9) 状态反映：只用于前端指示灯显示。
        // Start_Status 对应物理输入点 %I0.0
        public bool StartStatus
        {
            get => _startStatus;
            set => SetField(ref _startStatus, value, nameof(StartStatus));
        }

        // Start_Stop 对应物理输入点 %I0.1
        public bool StartStop
        {
            get => _startStop;
            set => SetField(ref _startStop, value, nameof(StartStop));
        }

        // DB9.DBX0.2：System_Running（用于启动/停止按钮状态显示）
        public bool SystemRunning
        {
            get => _systemRunning;
            set => SetField(ref _systemRunning, value, nameof(SystemRunning));
        }
        public string Sensor1TemperatureText => IsConnected ? $"{Sensor1.Temperature:0.0} ℃" : "---";
        public string Sensor1HumidityText => IsConnected ? $"{Sensor1.Humidity:0.0} %" : "---";
        public string Sensor2TemperatureText => IsConnected ? $"{Sensor2.Temperature:0.0} ℃" : "---";
        public string Sensor2HumidityText => IsConnected ? $"{Sensor2.Humidity:0.0} %" : "---";
        public PlcTelemetryModel()
        {
            Sensor1.PropertyChanged += OnSensorPropertyChanged;
            Sensor2.PropertyChanged += OnSensorPropertyChanged;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        /// <param name="db7Bytes">DB7 原始字节流。</param>
        public void UpdateFromDb7(byte[] db7Bytes)
        {
            // 逻辑意图：确保读取前先做长度校验，防止无效帧导致越界访问（DBD30 末字节为 offset 33）。
            if (db7Bytes.Length < 34)
            {
                return;
            }

            Sensor1.Temperature = ReadReal(db7Bytes, 4);   // DB7.DBD4
            Sensor1.Humidity = ReadReal(db7Bytes, 8);      // DB7.DBD8
            Sensor2.Temperature = ReadReal(db7Bytes, 26);  // DB7.DBD26
            Sensor2.Humidity = ReadReal(db7Bytes, 30);     // DB7.DBD30
        }
        private static double ReadReal(byte[] bytes, int startIndex)
        {
            var buffer = new[] { bytes[startIndex + 3], bytes[startIndex + 2], bytes[startIndex + 1], bytes[startIndex] };
            return BitConverter.ToSingle(buffer, 0);
        }

        private void OnSensorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaiseDashboardFields();
        }
        private void RaiseDashboardFields()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sensor1TemperatureText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sensor1HumidityText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sensor2TemperatureText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sensor2HumidityText)));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }
    }
}

