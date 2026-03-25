using Services.Core;
using Services;
using Serilog;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AppMain.ViewModels
{
    public sealed class MotorDebugViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly PlcTelemetryModel _telemetry;
        private readonly PlcService _plcService;
        private readonly SynchronizationContext _uiContext;
        private readonly CancellationTokenSource _cts = new();

        private bool _isSystemBusy;
        private bool _isServoEnabled;
        private bool _isServoFaulted;

        private double _servoPosition;
        private double _servoSpeed;
        private double _servoLoadRate;

        private double _servoAnimAngle;
        private double _servoAnimOpacity = 1;

        private long _stepperPulsePosition;
        private double _stepperRunFrequency;

        private double _stepperAnimOffset;
        private double _stepperAnimOpacity = 1;

        private string _servoPositionText = "--";
        private string _servoSpeedText = "--";
        private string _servoLoadRateText = "--";

        private string _stepperPulsePositionText = "--";
        private string _stepperRunFrequencyText = "--";

        private string _stepperTargetPositionText = "0";
        public bool IsSystemBusy
        {
            get => _isSystemBusy;
            private set
            {
                if (SetField(ref _isSystemBusy, value))
                {
                    OnPropertyChanged(nameof(IsUiEnabled));
                }
            }
        }
        public bool IsUiEnabled => !IsSystemBusy;
        public bool IsServoEnabled
        {
            get => _isServoEnabled;
            set => SetField(ref _isServoEnabled, value);
        }
        public bool IsServoFaulted
        {
            get => _isServoFaulted;
            private set => SetField(ref _isServoFaulted, value);
        }
        public string ServoPositionText
        {
            get => _servoPositionText;
            private set => SetField(ref _servoPositionText, value);
        }
        public string ServoSpeedText
        {
            get => _servoSpeedText;
            private set => SetField(ref _servoSpeedText, value);
        }
        public double ServoLoadRate
        {
            get => _servoLoadRate;
            private set => SetField(ref _servoLoadRate, value);
        }
        public string ServoLoadRateText
        {
            get => _servoLoadRateText;
            private set => SetField(ref _servoLoadRateText, value);
        }
        public double ServoAnimAngle
        {
            get => _servoAnimAngle;
            private set => SetField(ref _servoAnimAngle, value);
        }
        public double ServoAnimOpacity
        {
            get => _servoAnimOpacity;
            private set => SetField(ref _servoAnimOpacity, value);
        }
        public string StepperPulsePositionText
        {
            get => _stepperPulsePositionText;
            private set => SetField(ref _stepperPulsePositionText, value);
        }
        public string StepperRunFrequencyText
        {
            get => _stepperRunFrequencyText;
            private set => SetField(ref _stepperRunFrequencyText, value);
        }
        public double StepperAnimOffset
        {
            get => _stepperAnimOffset;
            private set => SetField(ref _stepperAnimOffset, value);
        }
        public double StepperAnimOpacity
        {
            get => _stepperAnimOpacity;
            private set => SetField(ref _stepperAnimOpacity, value);
        }
        public string StepperTargetPositionText
        {
            get => _stepperTargetPositionText;
            set => SetField(ref _stepperTargetPositionText, value);
        }

        // -----------------------------
        // PLC Tag（建议联调时替换为真实点位）
        // -----------------------------
        private const string ServoEnableTag = "SERVO_ENABLE";
        private const string ServoFaultTag = "SERVO_FAULT";
        private const string ServoPositionTag = "SERVO_POS";
        private const string ServoSpeedTag = "SERVO_SPEED";
        private const string ServoLoadTag = "SERVO_LOAD";

        private const string ServoHomeTag = "SERVO_HOME";
        private const string ServoJogPlusTag = "SERVO_JOG_PLUS";
        private const string ServoJogMinusTag = "SERVO_JOG_MINUS";

        private const string StepperPulsePositionTag = "STEPPER_POS";
        private const string StepperRunFrequencyTag = "STEPPER_FREQ";
        private const string StepperJogPlusTag = "STEPPER_JOG_PLUS";
        private const string StepperJogMinusTag = "STEPPER_JOG_MINUS";
        private const string StepperTargetPositionTag = "STEPPER_TARGET_POS";

        private const string SystemStartTag = "SYSTEM_START";
        private const string SystemStopTag = "SYSTEM_STOP";

        // -----------------------------
        // Commands
        // -----------------------------
        public ICommand SystemStartCommand { get; }
        public ICommand SystemStopCommand { get; }

        public ICommand ServoEnableCommand { get; }
        public ICommand ServoHomeCommand { get; }
        public ICommand ServoJogPlusCommand { get; }
        public ICommand ServoJogMinusCommand { get; }

        public ICommand StepperJogPlusCommand { get; }
        public ICommand StepperJogMinusCommand { get; }
        public ICommand StepperExecuteCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
        /// <param name="telemetry">全局 PLC 遥测模型（本 ViewModel 主要依赖 PlcService 的电机读写）。</param>
        /// <param name="plcService">PLC 服务层：负责读写/仿真 fallback。</param>
        public MotorDebugViewModel(PlcTelemetryModel telemetry, PlcService plcService)
        {
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));

            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

            // 命令初始化（所有按钮都走统一的忙碌保护包装，避免并发写入导致状态错乱）
            SystemStartCommand = new AsyncCommand(DoSystemStartAsync);
            SystemStopCommand = new AsyncCommand(DoSystemStopAsync);

            ServoEnableCommand = new AsyncCommand<bool>(DoServoEnableAsync);
            ServoHomeCommand = new AsyncCommand(DoServoHomeAsync);
            ServoJogPlusCommand = new AsyncCommand(DoServoJogPlusAsync);
            ServoJogMinusCommand = new AsyncCommand(DoServoJogMinusAsync);

            StepperJogPlusCommand = new AsyncCommand(DoStepperJogPlusAsync);
            StepperJogMinusCommand = new AsyncCommand(DoStepperJogMinusAsync);
            StepperExecuteCommand = new AsyncCommand(DoStepperExecuteAsync);

            // 后台轮询实时数据
            _ = Task.Run(PollingLoopAsync, _cts.Token);
        }

        private async Task PollingLoopAsync()
        {
            try
            {
                await _plcService.EnsureConnectedAsync().ConfigureAwait(false);

                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        // 读取实时数据（全部在后台线程）
                        var servoEnabled = await _plcService.ReadBoolAsync(ServoEnableTag).ConfigureAwait(false);
                        var servoFaulted = await _plcService.ReadBoolAsync(ServoFaultTag).ConfigureAwait(false);
                        var servoPos = await _plcService.ReadDoubleAsync(ServoPositionTag).ConfigureAwait(false);
                        var servoSpeed = await _plcService.ReadDoubleAsync(ServoSpeedTag).ConfigureAwait(false);
                        var servoLoad = await _plcService.ReadDoubleAsync(ServoLoadTag).ConfigureAwait(false);

                        var stepperPos = await _plcService.ReadInt64Async(StepperPulsePositionTag).ConfigureAwait(false);
                        var stepperFreq = await _plcService.ReadDoubleAsync(StepperRunFrequencyTag).ConfigureAwait(false);

                        _uiContext.Post(_ =>
                        {
                            IsServoEnabled = servoEnabled;
                            IsServoFaulted = servoFaulted;

                            _servoPosition = servoPos;
                            ServoPositionText = $"{_servoPosition.ToString("0.###", CultureInfo.InvariantCulture)}";

                            _servoSpeed = servoSpeed;
                            ServoSpeedText = $"{_servoSpeed.ToString("0.###", CultureInfo.InvariantCulture)}";

                            _servoLoadRate = Math.Clamp(servoLoad, 0, 100);
                            ServoLoadRate = _servoLoadRate;
                            ServoLoadRateText = $"{_servoLoadRate.ToString("0.##", CultureInfo.InvariantCulture)} %";

                            // 伺服动画：角度随转速更新；未使能或故障时降低可见度。
                            _servoAnimAngle = (_servoAnimAngle + (_servoSpeed * 0.02)) % 360;
                            ServoAnimAngle = _servoAnimAngle;
                            ServoAnimOpacity = (servoFaulted || !servoEnabled) ? 0.35 : 1.0;

                            _stepperPulsePosition = stepperPos;
                            StepperPulsePositionText = _stepperPulsePosition.ToString(CultureInfo.InvariantCulture);

                            _stepperRunFrequency = stepperFreq;
                            StepperRunFrequencyText = $"{_stepperRunFrequency.ToString("0.##", CultureInfo.InvariantCulture)}";

                            // 步进动画：位移随脉冲位置模值变化，形成“滑块”视觉效果。
                            var mod = ((stepperPos % 1000) + 1000) % 1000;
                            _stepperAnimOffset = (mod / 1000.0) * 120;
                            StepperAnimOffset = _stepperAnimOffset;
                            StepperAnimOpacity = IsUiEnabled ? 1.0 : 0.6;
                        }, null);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "电机轮询读取失败（继续轮询）。");
                    }

                    await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
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
                LoggingDatabaseInitializer.LogError("电机命令执行失败", ex);
                // 防止异常冒泡到 UI 线程导致点击异常
            }
            finally
            {
                IsSystemBusy = false;
            }
        }

        private async Task RunWithBusyGuardAsync<T>(Func<T, Task> action, T parameter)
        {
            await RunWithBusyGuardAsync(() => action(parameter));
        }
        private Task DoSystemStartAsync()
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(SystemStartTag, true));
        private Task DoSystemStopAsync()
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(SystemStopTag, true));
        private Task DoServoEnableAsync(bool enabled)
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(ServoEnableTag, enabled));
        private Task DoServoHomeAsync()
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(ServoHomeTag, true));
        private Task DoServoJogPlusAsync()
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(ServoJogPlusTag, true));
        private Task DoServoJogMinusAsync()
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(ServoJogMinusTag, true));
        private Task DoStepperExecuteAsync()
        {
            return RunWithBusyGuardAsync(async () =>
            {
                if (!long.TryParse(_stepperTargetPositionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
                {
                    // 输入非法：不写入，直接返回，保持 UI 稳定。
                    return;
                }

                await _plcService.WriteNumberAsync(StepperTargetPositionTag, target).ConfigureAwait(false);
            });
        }
        private Task DoStepperJogPlusAsync()
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(StepperJogPlusTag, true));
        private Task DoStepperJogMinusAsync()
            => RunWithBusyGuardAsync(() => _plcService.WriteBoolAsync(StepperJogMinusTag, true));
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged(string? propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // -----------------------------
        // Async Command Implementations
        // -----------------------------
        private sealed class AsyncCommand : ICommand
        {
            private readonly Func<Task> _executeAsync;

            public AsyncCommand(Func<Task> executeAsync)
            {
                _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => true;

            public async void Execute(object? parameter)
            {
                try
                {
                    await _executeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 兜底：具体异常在 ViewModel 的 RunWithBusyGuardAsync 已记录，这里不再重复抛出。
                }
            }
        }
        private sealed class AsyncCommand<T> : ICommand
        {
            private readonly Func<T, Task> _executeAsync;

            public AsyncCommand(Func<T, Task> executeAsync)
            {
                _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => true;

            public async void Execute(object? parameter)
            {
                try
                {
                    // 允许 ToggleButton 的 CommandParameter 传入 bool 或 bool?
                    T value;
                    if (parameter is T direct)
                    {
                        value = direct;
                    }
                    else
                    {
                        // 仅处理 bool/nullable bool 兼容场景
                        if (typeof(T) == typeof(bool))
                        {
                            bool b;
                            if (parameter is bool bb)
                            {
                                b = bb;
                            }
                            else
                            {
                                // 避免在 pattern 中使用 bool?（CS8116）：直接 as 可空类型进行安全转换。
                                var nb = parameter as bool?;
                                b = nb.GetValueOrDefault();
                            }

                            value = (T)(object)b;
                        }
                        else
                        {
                            value = default!;
                        }
                    }

                    await _executeAsync(value).ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}


