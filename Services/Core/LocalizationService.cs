using System;
using System.Collections.Generic;

namespace Services.Core
{
    public sealed class LocalizationService
    {
        public Language CurrentLanguage { get; private set; } = Language.Chinese;

        private readonly Dictionary<string, (string zh, string en)> _texts = new()
        {
            ["SystemSettings"] = ("系统设置", "System Settings"),
            ["Dashboard"] = ("监控总览 Dashboard", "Dashboard"),
            ["ServoModule"] = ("伺服电机模块", "Servo Module"),
            ["StepperModule"] = ("步进电机模块", "Stepper Module"),
            ["LocalizedSample"] = ("本地化桩：当前语言为中文", "Localization stub: current language is English")
        };
        /// <param name="language">目标语言。</param>
        public void SetLanguage(Language language)
        {
            CurrentLanguage = language;
        }
        /// <param name="key">文本 key。</param>
        /// <returns>本地化后的字符串；若 key 不存在则返回 key。</returns>
        public string T(string key)
        {
            if (!_texts.TryGetValue(key, out var value))
            {
                return key;
            }

            return CurrentLanguage == Language.Chinese ? value.zh : value.en;
        }
    }
    public enum Language
    {
        Chinese,
        English
    }
}


