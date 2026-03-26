using System;
using System.Globalization;
using System.Windows.Data;

namespace AppMain.ValueConverters
{
    public sealed class BoolToTextConverter : IValueConverter
    {
        public string TrueText { get; set; } = "True";
        public string FalseText { get; set; } = "False";

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var b = value is bool bb && bb;
            return b ? TrueText : FalseText;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}

