using AppMain.ViewModels.Infrastructure;
using Services.Core;
using Serilog;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AppMain.ViewModels
{
    public sealed class DashboardViewModel : ViewModelBase, IDisposable
    {
        private readonly PlcTelemetryModel _telemetry;
        private readonly LocalizationService _localization;
        private readonly CancellationTokenSource _cts = new();
        private readonly SynchronizationContext _uiContext;

        private bool _isSystemBusy;
        private bool _isSystemRunning;

        private bool _isServoEnabled;
        private bool _isServoFaulted;
        private bool _isServoAlarmed;
        private double _servoPositionMm;
        private double _servoSpeedMmPerS;
        private double _servoLoadRate;
        private int _servoJogDir;
        private DateTime _servoJogUntilUtc;

        private long _stepperPulsePosition;
        private double _stepperRunFrequency;
        private bool _isStepperFaulted;
        private bool _isStepperAlarmed;
        private int _stepperJogDir;
        private DateTime _stepperJogUntilUtc;

        private string _stepperTargetPositionText = "0";

        private bool _isChineseLanguage = true;
        private bool _isEnglishLanguage;

        private bool _isDarkTheme = true;
        private bool _isModernGrayTheme;

        private string _localizedSampleText = string.Empty;

        private Brush _servoLoadBarBrush = Brushes.LimeGreen;
        private Brush _servoIndicatorGreenBrush = Brushes.LimeGreen;
        private Brush _servoIndicatorYellowBrush = Brushes.Gray;
        private Brush _servoIndicatorRedBrush = Brushes.Gray;

        private Brush _stepperIndicatorGreenBrush = Brushes.LimeGreen;
        private Brush _stepperIndicatorYellowBrush = Brushes.Gray;
        private Brush _stepperIndicatorRedBrush = Brushes.Gray;

        private readonly Random _random = new();
        private DateTime _lastTickUtc;
        public ICommand SystemStartCommand { get; }
        public ICommand SystemStopCommand { get; }
        public ICommand ServoEnableCommand { get; }
        public ICommand ServoHomeCommand { get; }
        public ICommand ServoJogPlusCommand { get; }
        public ICommand ServoJogMinusCommand { get; }
        public ICommand StepperJogPlusCommand { get; }
        public ICommand StepperJogMinusCommand { get; }
        public ICommand StepperExecuteCommand { get; }
        public bool IsSystemBusy
        {
            get => _isSystemBusy;
            private set
            {
                if (SetProperty(ref _isSystemBusy, value))
                {
                    // 派生属性刷新（用于按钮 IsEnabled 绑定）
                    RaisePropertyChanged(nameof(IsUiEnabled));
                    RaisePropertyChanged(nameof(IsServoControlsEnabled));
                    RaisePropertyChanged(nameof(IsStepperControlsEnabled));
                }
            }
        }
        public bool IsUiEnabled => !IsSystemBusy;
        public bool IsSystemRunning
        {
            get => _isSystemRunning;
            private set
            {
                if (SetProperty(ref _isSystemRunning, value))
                {
                    RaisePropertyChanged(nameof(IsServoControlsEnabled));
                    RaisePropertyChanged(nameof(IsStepperControlsEnabled));
                }
            }
        }
        public bool IsServoEnabled
        {
            get => _isServoEnabled;
            set => SetProperty(ref _isServoEnabled, value);
        }
        public bool IsServoFaulted
        {
            get => _isServoFaulted;
            private set => SetProperty(ref _isServoFaulted, value);
        }
        public bool IsServoAlarmed
        {
            get => _isServoAlarmed;
            private set => SetProperty(ref _isServoAlarmed, value);
        }
        public double ServoPositionMm
        {
            get => _servoPositionMm;
            private set => SetProperty(ref _servoPositionMm, value);
        }
        public string ServoPositionMmText => $"{ServoPositionMm.ToString("0.##", CultureInfo.InvariantCulture)} mm";
        public double ServoSpeedMmPerS
        {
            get => _servoSpeedMmPerS;
            private set => SetProperty(ref _servoSpeedMmPerS, value);
        }
        public string ServoSpeedMmPerSText => $"{ServoSpeedMmPerS.ToString("0.##", CultureInfo.InvariantCulture)} mm/s";
        public double ServoLoadRate
        {
            get => _servoLoadRate;
            private set => SetProperty(ref _servoLoadRate, value);
        }
        public string ServoLoadRateText => $"{ServoLoadRate.ToString("0.##", CultureInfo.InvariantCulture)} %";
        public Brush ServoLoadBarBrush
        {
            get => _servoLoadBarBrush;
            private set => SetProperty(ref _servoLoadBarBrush, value);
        }
        public Brush ServoIndicatorGreenBrush
        {
            get => _servoIndicatorGreenBrush;
            private set => SetProperty(ref _servoIndicatorGreenBrush, value);
        }
        public Brush ServoIndicatorYellowBrush
        {
            get => _servoIndicatorYellowBrush;
            private set => SetProperty(ref _servoIndicatorYellowBrush, value);
        }
        public Brush ServoIndicatorRedBrush
        {
            get => _servoIndicatorRedBrush;
            private set => SetProperty(ref _servoIndicatorRedBrush, value);
        }
        public bool IsServoControlsEnabled => IsUiEnabled && IsSystemRunning && !IsServoFaulted;
        public Brush StepperIndicatorYellowBrush
        {
            get => _stepperIndicatorYellowBrush;
            private set => SetProperty(ref _stepperIndicatorYellowBrush, value);
        }
        public Brush StepperIndicatorGreenBrush
        {
            get => _stepperIndicatorGreenBrush;
            private set => SetProperty(ref _stepperIndicatorGreenBrush, value);
        }
        public Brush StepperIndicatorRedBrush
        {
            get => _stepperIndicatorRedBrush;
            private set => SetProperty(ref _stepperIndicatorRedBrush, value);
        }
        public bool IsStepperFaulted
        {
            get => _isStepperFaulted;
            private set => SetProperty(ref _isStepperFaulted, value);
        }
        public bool IsStepperAlarmed
        {
            get => _isStepperAlarmed;
            private set => SetProperty(ref _isStepperAlarmed, value);
        }
        public long StepperPulsePosition
        {
            get => _stepperPulsePosition;
            private set => SetProperty(ref _stepperPulsePosition, value);
        }
        public string StepperPulsePositionText => StepperPulsePosition.ToString(CultureInfo.InvariantCulture);
        public double StepperRunFrequency
        {
            get => _stepperRunFrequency;
            private set => SetProperty(ref _stepperRunFrequency, value);
        }
        public string StepperRunFrequencyText => $"{StepperRunFrequency.ToString("0.##", CultureInfo.InvariantCulture)} Hz";
        public string StepperTargetPositionText
        {
            get => _stepperTargetPositionText;
            set => SetProperty(ref _stepperTargetPositionText, value);
        }
        public bool IsStepperControlsEnabled => IsUiEnabled && IsSystemRunning && !IsStepperFaulted;
        public bool IsChineseLanguage
        {
            get => _isChineseLanguage;
            set
            {
                if (SetProperty(ref _isChineseLanguage, value) && value)
                {
                    _isEnglishLanguage = false;
                    _localization.SetLanguage(Language.Chinese);
                    RebuildLocalizationText();
                    RaisePropertyChanged(nameof(IsEnglishLanguage));
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
                    _localization.SetLanguage(Language.English);
                    RebuildLocalizationText();
                    RaisePropertyChanged(nameof(IsChineseLanguage));
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
                    ThemeService.ApplyDarkTheme();
                    RaisePropertyChanged(nameof(IsModernGrayTheme));
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
                    ThemeService.ApplyModernGrayTheme();
                    RaisePropertyChanged(nameof(IsDarkTheme));
                }
            }
        }
        public string LocalizedSampleText
        {
            get => _localizedSampleText;
            private set => SetProperty(ref _localizedSampleText, value);
        }
        /// <param name="telemetry">全局 PLC 遥测模型（用于展示连接状态等）。</param>
        public DashboardViewModel(PlcTelemetryModel telemetry)
        {
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _localization = new LocalizationService();
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

            ThemeService.ApplyDarkTheme();
            _localization.SetLanguage(Language.Chinese);
            RebuildLocalizationText();

            var accentOk = (Brush)Application.Current!.FindResource("SuccessGreen");
            var warnOrange = (Brush)Application.Current.FindResource("WarnOrange");
            var faultRed = (Brush)Application.Current.FindResource("ErrorRed");
            var dim = (Brush)Application.Current.FindResource("BorderLine");

            ServoLoadBarBrush = accentOk;
            ServoIndicatorGreenBrush = accentOk;
            ServoIndicatorYellowBrush = dim;
            ServoIndicatorRedBrush = dim;

            StepperIndicatorGreenBrush = accentOk;
            StepperIndicatorYellowBrush = dim;
            StepperIndicatorRedBrush = dim;

            // 命令初始化
            SystemStartCommand = new AsyncCommand(DoSystemStartAsync);
            SystemStopCommand = new AsyncCommand(DoSystemStopAsync);

            ServoEnableCommand = new AsyncCommand<bool?>(DoServoEnableAsync);
            ServoHomeCommand = new AsyncCommand(DoServoHomeAsync);
            ServoJogPlusCommand = new AsyncCommand(DoServoJogPlusAsync);
            ServoJogMinusCommand = new AsyncCommand(DoServoJogMinusAsync);

            StepperJogPlusCommand = new AsyncCommand(DoStepperJogPlusAsync);
            StepperJogMinusCommand = new AsyncCommand(DoStepperJogMinusAsync);
            StepperExecuteCommand = new AsyncCommand(DoStepperExecuteAsync);

            // 初始状态
            _lastTickUtc = DateTime.UtcNow;
            _servoJogDir = 0;
            _stepperJogDir = 0;

            // 启动轮询仿真
            _ = Task.Run(PollingLoopAsync, _cts.Token);
        }
        private async Task PollingLoopAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var nowUtc = DateTime.UtcNow;
                    var dt = Math.Clamp((nowUtc - _lastTickUtc).TotalSeconds, 0.0, 1.0);
                    _lastTickUtc = nowUtc;

                    if (IsSystemRunning)
                    {
                        SimulateServo(dt, nowUtc);
                        SimulateStepper(dt, nowUtc);
                    }

                    // 在 UI 线程刷新派生字段/颜色。
                    _uiContext.Post(_ =>
                    {
                        UpdateDerivedBrushes();
                        UpdateModuleEnableStates();
                    }, null);

                    await Task.Delay(250, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Dashboard 轮询仿真异常（继续运行）。");
            }
        }

        private void SimulateServo(double dt, DateTime nowUtc)
        {
            // Jog 指令在短时间窗口内生效
            if (nowUtc <= _servoJogUntilUtc && _servoJogDir != 0 && IsServoEnabled && !IsServoFaulted)
            {
                // Jog 时保持一定速度趋势
                var target = 80 + _random.NextDouble() * 60;
                _servoSpeedMmPerS = target * _servoJogDir;
            }
            else
            {
                // 自动运行：速度与负载有轻微随机漂移
                if (IsServoEnabled && !IsServoFaulted)
                {
                    var drift = (_random.NextDouble() - 0.5) * 30;
                    _servoSpeedMmPerS = Math.Clamp(_servoSpeedMmPerS + drift, -600, 600);
                }
                else
                {
                    _servoSpeedMmPerS = 0;
                }
            }

            // 位置更新（mm）
            _servoPositionMm += _servoSpeedMmPerS * dt * 0.3; // 缩放系数让数值更“可读”

            // 负载更新（与速度相关 + 随机扰动）
            var loadBase = 25 + Math.Abs(_servoSpeedMmPerS) * 0.03;
            var loadNoise = (_random.NextDouble() - 0.5) * 6;
            _servoLoadRate = Math.Clamp(loadBase + loadNoise, 0, 100);

            // 故障/报警判定
            if (!IsServoFaulted)
            {
                // 故障：负载极高或少量随机事件
                if (_servoLoadRate >= 98 || _random.NextDouble() < 0.002)
                {
                    _isServoFaulted = true;
                    _isServoAlarmed = false;
                    _servoSpeedMmPerS = 0;
                }
                else if (_servoLoadRate >= 80)
                {
                    _isServoAlarmed = true;
                }
                else if (_servoLoadRate <= 65)
                {
                    _isServoAlarmed = false;
                }
            }
            else
            {
                // 故障状态：负载归零并保持报警关闭
                _servoLoadRate = Math.Clamp(_servoLoadRate * 0.98, 0, 100);
                _isServoAlarmed = false;
            }

            // 将字段同步到属性（触发 UI 变更）
            ServoPositionMm = _servoPositionMm;
            ServoSpeedMmPerS = _servoSpeedMmPerS;
            ServoLoadRate = _servoLoadRate;
            IsServoAlarmed = _isServoAlarmed;
            IsServoFaulted = _isServoFaulted;
        }

        private void SimulateStepper(double dt, DateTime nowUtc)
        {
            if (nowUtc <= _stepperJogUntilUtc && _stepperJogDir != 0 && !IsStepperFaulted)
            {
                // Jog 时提高频率
                _stepperRunFrequency = 60 + _random.NextDouble() * 80;
            }
            else
            {
                if (!IsStepperFaulted)
                {
                    // 自由漂移
                    var drift = (_random.NextDouble() - 0.5) * 20;
                    _stepperRunFrequency = Math.Clamp(_stepperRunFrequency + drift, 0, 200);
                }
                else
                {
                    _stepperRunFrequency = 0;
                }
            }

            // 脉冲更新：频率与方向共同决定增量
            var dir = _stepperJogDir == 0 ? 1 : _stepperJogDir;
            _stepperPulsePosition += (long)(_stepperRunFrequency * dt * 25 * dir);

            // 报警/故障：频率过高
            if (!IsStepperFaulted)
            {
                if (_stepperRunFrequency >= 190 || _random.NextDouble() < 0.0015)
                {
                    _isStepperFaulted = true;
                    _isStepperAlarmed = false;
                    _stepperRunFrequency = 0;
                }
                else if (_stepperRunFrequency >= 130)
                {
                    _isStepperAlarmed = true;
                }
                else if (_stepperRunFrequency <= 95)
                {
                    _isStepperAlarmed = false;
                }
            }
            else
            {
                _stepperRunFrequency = Math.Clamp(_stepperRunFrequency * 0.99, 0, 200);
                _isStepperAlarmed = false;
            }

            StepperPulsePosition = _stepperPulsePosition;
            StepperRunFrequency = _stepperRunFrequency;
            IsStepperAlarmed = _isStepperAlarmed;
            IsStepperFaulted = _isStepperFaulted;
        }

        private void UpdateDerivedBrushes()
        {
            var ok = (Brush)Application.Current!.FindResource("SuccessGreen");
            var warn = (Brush)Application.Current!.FindResource("WarnOrange");
            var fault = (Brush)Application.Current!.FindResource("ErrorRed");
            var dim = (Brush)Application.Current!.FindResource("BorderLine");

            // Servo load bar：高负载变黄（非致命报警）
            ServoLoadBarBrush = ServoLoadRate >= 80 ? warn : ok;

            // Servo indicator：单灯常亮，其余熄灭为 BorderLine
            ServoIndicatorGreenBrush = (!IsServoFaulted && !IsServoAlarmed) ? ok : dim;
            ServoIndicatorYellowBrush = (!IsServoFaulted && IsServoAlarmed) ? warn : dim;
            ServoIndicatorRedBrush = IsServoFaulted ? fault : dim;

            // Stepper indicator
            StepperIndicatorGreenBrush = (!IsStepperFaulted && !IsStepperAlarmed) ? ok : dim;
            StepperIndicatorYellowBrush = (!IsStepperFaulted && IsStepperAlarmed) ? warn : dim;
            StepperIndicatorRedBrush = IsStepperFaulted ? fault : dim;
        }

        private void UpdateModuleEnableStates()
        {
            // 触发只读派生属性（用于按钮 IsEnabled）
            RaisePropertyChanged(nameof(IsServoControlsEnabled));
            RaisePropertyChanged(nameof(IsStepperControlsEnabled));
        }

        private async Task RunWithBusyGuardAsync(Func<Task> action)
        {
            if (IsSystemBusy)
            {
                return;
            }

            try
            {
                IsSystemBusy = true;
                await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Dashboard 命令执行失败。");
            }
            finally
            {
                IsSystemBusy = false;
            }
        }

        private async Task DoSystemStartAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                IsSystemRunning = true;

                // 启动后默认维持使能态，若故障则清除（调试桩逻辑）
                if (IsServoFaulted)
                {
                    IsServoFaulted = false;
                    IsServoAlarmed = false;
                    _servoLoadRate = 30;
                    _servoSpeedMmPerS = 0;
                }

                if (IsStepperFaulted)
                {
                    IsStepperFaulted = false;
                    IsStepperAlarmed = false;
                    _stepperRunFrequency = 0;
                }

                await Task.Delay(80);
            });
        }

        private async Task DoSystemStopAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                IsSystemRunning = false;
                _servoJogDir = 0;
                _stepperJogDir = 0;
                _servoSpeedMmPerS = 0;
                _stepperRunFrequency = 0;
                await Task.Delay(80);
            });
        }

        private async Task DoServoEnableAsync(bool? enabled)
        {
            await RunWithBusyGuardAsync(async () =>
            {
                var isOn = enabled == true;
                IsServoEnabled = isOn;
                if (isOn && IsServoFaulted)
                {
                    // 使能时清除故障（桩逻辑）
                    IsServoFaulted = false;
                    IsServoAlarmed = false;
                    _servoLoadRate = 35;
                }

                await Task.Delay(50);
            });
        }

        private async Task DoServoHomeAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                if (!IsServoControlsEnabled)
                {
                    return;
                }

                _servoPositionMm = 0;
                _servoSpeedMmPerS = 0;
                _servoLoadRate = 20;
                IsServoAlarmed = false;
                await Task.Delay(50);
            });
        }

        private async Task DoServoJogPlusAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                if (!IsServoControlsEnabled)
                {
                    return;
                }

                _servoJogDir = 1;
                _servoJogUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
                IsServoEnabled = true;
                await Task.Delay(1);
            });
        }

        private async Task DoServoJogMinusAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                if (!IsServoControlsEnabled)
                {
                    return;
                }

                _servoJogDir = -1;
                _servoJogUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
                IsServoEnabled = true;
                await Task.Delay(1);
            });
        }

        private async Task DoStepperJogPlusAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                if (!IsStepperControlsEnabled)
                {
                    return;
                }

                _stepperJogDir = 1;
                _stepperJogUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
                await Task.Delay(1);
            });
        }

        private async Task DoStepperJogMinusAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                if (!IsStepperControlsEnabled)
                {
                    return;
                }

                _stepperJogDir = -1;
                _stepperJogUntilUtc = DateTime.UtcNow.AddMilliseconds(600);
                await Task.Delay(1);
            });
        }

        private async Task DoStepperExecuteAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                if (!IsStepperControlsEnabled)
                {
                    return;
                }

                if (!long.TryParse(_stepperTargetPositionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
                {
                    return;
                }

                _stepperPulsePosition = target;
                _stepperRunFrequency = 80 + _random.NextDouble() * 40;
                IsStepperAlarmed = false;
                await Task.Delay(50);
            });
        }
        private void RebuildLocalizationText()
        {
            LocalizedSampleText = _localization.T("LocalizedSample");
        }
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        // 注意：属性变更通知由基类 ViewModelBase.RaisePropertyChanged 统一处理。
    }
}


