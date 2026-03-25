using AppMain.ViewModels.Infrastructure;
using Services.Core;
using System;
using System.Threading;
using System.Windows.Media;

namespace AppMain.ViewModels
{
    public sealed class SettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly LocalizationService _localization;
        private readonly SynchronizationContext _uiContext;

        private bool _isChineseLanguage = true;
        private bool _isEnglishLanguage;

        private bool _isDarkTheme = true;
        private bool _isModernGrayTheme;

        private string _localizedSampleText = string.Empty;
        public int SelectedLanguageIndex
        {
            get => _isChineseLanguage ? 0 : 1;
            set
            {
                // 仅当用户真正改变选择时才触发桩逻辑。
                if (value <= 0 && _isChineseLanguage)
                {
                    return;
                }
                if (value >= 1 && _isEnglishLanguage)
                {
                    return;
                }

                if (value <= 0)
                {
                    IsChineseLanguage = true;
                }
                else
                {
                    IsEnglishLanguage = true;
                }

                RaisePropertyChanged(nameof(SelectedLanguageIndex));
            }
        }
        public int SelectedThemeIndex
        {
            get => _isDarkTheme ? 0 : 1;
            set
            {
                // 仅当用户真正改变选择时才触发桩逻辑。
                if (value <= 0 && _isDarkTheme)
                {
                    return;
                }
                if (value >= 1 && _isModernGrayTheme)
                {
                    return;
                }

                if (value <= 0)
                {
                    IsDarkTheme = true;
                }
                else
                {
                    IsModernGrayTheme = true;
                }

                RaisePropertyChanged(nameof(SelectedThemeIndex));
            }
        }
        public bool IsChineseLanguage
        {
            get => _isChineseLanguage;
            set
            {
                if (SetProperty(ref _isChineseLanguage, value) && value)
                {
                    _isEnglishLanguage = false;
                    RaisePropertyChanged(nameof(IsEnglishLanguage));
                    _localization.SetLanguage(Language.Chinese);
                    ApplyLocalizedText();
                }
            }
        }
        public bool IsEnglishLanguage
        {
            get => _isEnglishLanguage;
            set
            {
                if (SetProperty(ref _isEnglishLanguage, value) && value)
                {
                    _isChineseLanguage = false;
                    RaisePropertyChanged(nameof(IsChineseLanguage));
                    _localization.SetLanguage(Language.English);
                    ApplyLocalizedText();
                }
            }
        }
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value) && value)
                {
                    _isModernGrayTheme = false;
                    RaisePropertyChanged(nameof(IsModernGrayTheme));
                    ThemeService.ApplyDarkTheme();
                }
            }
        }
        public bool IsModernGrayTheme
        {
            get => _isModernGrayTheme;
            set
            {
                if (SetProperty(ref _isModernGrayTheme, value) && value)
                {
                    _isDarkTheme = false;
                    RaisePropertyChanged(nameof(IsDarkTheme));
                    ThemeService.ApplyModernGrayTheme();
                }
            }
        }
        public string LocalizedSampleText
        {
            get => _localizedSampleText;
            private set => SetProperty(ref _localizedSampleText, value);
        }
        public SettingsViewModel()
        {
            _localization = new LocalizationService();
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

            // 默认：中文 + 深邃科技黑
            _localization.SetLanguage(Language.Chinese);
            ThemeService.ApplyDarkTheme();
            ApplyLocalizedText();
        }

        private void ApplyLocalizedText()
        {
            // 由于 ThemeService 会修改 Application.Resources，使用 UI 线程更安全。
            _uiContext.Post(_ =>
            {
                LocalizedSampleText = _localization.T("LocalizedSample");
            }, null);
        }
        public void Dispose()
        {
            // no-op
        }
    }
}


