using System;
using System.Globalization;
using System.Windows.Data;

namespace AppMain.ValueConverters
{
    public sealed class OnlineOfflineTextConverter : IValueConverter
    {
        /// <param name="value">源值（通常为 bool）。</param>
        /// <param name="targetType">目标类型。</param>
        /// <param name="parameter">转换参数（未使用）。</param>
        /// <param name="culture">区域性。</param>
        /// <returns>“在线”或“离线”。</returns>
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isConnected = value is bool b && b;
            return isConnected ? "在线" : "离线";
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


