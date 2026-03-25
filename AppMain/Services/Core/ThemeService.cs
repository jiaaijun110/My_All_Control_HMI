using System;
using System.Windows;
using System.Windows.Media;

namespace Services.Core
{
    public static class ThemeService
    {
        public static void ApplyDarkTheme()
        {
            ApplyTheme(bgDark: "#0F172A", surfaceCard: "#1E293B", borderLine: "#334155");
        }
        public static void ApplyModernGrayTheme()
        {
            ApplyTheme(bgDark: "#1F2937", surfaceCard: "#374151", borderLine: "#4B5563");
        }

        private static void ApplyTheme(string bgDark, string surfaceCard, string borderLine)
        {
            var app = Application.Current;
            if (app == null)
            {
                return;
            }

            // 说明：UI 侧使用 DynamicResource 引用时，可自动刷新。
            app.Resources["BgDark"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgDark)!);
            app.Resources["SurfaceCard"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(surfaceCard)!);
            app.Resources["BorderLine"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderLine)!);
        }
    }
}


