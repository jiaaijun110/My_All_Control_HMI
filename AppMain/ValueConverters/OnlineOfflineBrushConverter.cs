using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AppMain.ValueConverters
{
    public sealed class OnlineOfflineBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = Brushes.Transparent;
        public Brush FalseBrush { get; set; } = Brushes.Transparent;
        /// <param name="value">源值（通常为 bool）。</param>
        /// <param name="targetType">目标类型。</param>
        /// <param name="parameter">转换参数（未使用）。</param>
        /// <param name="culture">区域性。</param>
        /// <returns>对应的画刷。</returns>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isConnected = value is bool b && b;
            return isConnected ? TrueBrush : FalseBrush;
        }
        /// <param name="value">目标值。</param>
        /// <param name="targetType">源类型。</param>
        /// <param name="parameter">转换参数。</param>
        /// <param name="culture">区域性。</param>
        /// <returns>始终返回 <c>Binding.DoNothing</c>。</returns>
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}


