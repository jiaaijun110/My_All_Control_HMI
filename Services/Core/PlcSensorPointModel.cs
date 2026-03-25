using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Services.Core
{
    public sealed class PlcSensorPointModel : INotifyPropertyChanged
    {
        private double _temperature;
        private double _humidity;
        public string Name { get; }
        public double Temperature
        {
            get => _temperature;
            set => SetField(ref _temperature, value);
        }
        public double Humidity
        {
            get => _humidity;
            set => SetField(ref _humidity, value);
        }
        /// <param name="name">传感器显示名称。</param>
        public PlcSensorPointModel(string name)
        {
            Name = name;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

