using Services.Core;
using Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly SemaphoreSlim _startStopWriteSemaphore = new SemaphoreSlim(1, 1);

        private bool _isSystemBusy;
        private bool _isServoEnabled;
        private bool _isServoFaulted;
        private bool _isPlcFaulted;

        // DB9 状态反映（只读指示灯）
        private bool _startStatus;
        private bool _startStop;
        private bool _systemRunning;

        private double _servoPosition;
        private double _servoSpeed;
        private double _servoLoadRate;

        private double _servoAnimAngle;
        private double _servoAnimOpacity = 1;

        private long _stepperPulsePosition;
        private double _stepperRunFrequency;

        private double _stepperAnimOffset;
        private double _stepperAnimOpacity = 1;
        private double _stepperAnimAngle;

        private string _servoPositionText = "---";
        private string _servoSpeedText = "---";
        private string _servoLoadRateText = "---";

        private string _stepperPulsePositionText = "---";
        private string _stepperRunFrequencyText = "---";

        private string _stepperTargetPositionText = "0";
        private bool _isServoSettingOpen;
        private bool _isStepperSettingOpen;

        private double _servoTargetPosMm;
        private double _servoSpeedSet;
        private double _servoAccelSet;
        private double _servoDecelSet;
        private double _servoJogSpeedSet;

        private double _stepperTargetPosMm;
        private double _stepperSpeedSet;
        private double _stepperAccelSet;
        private double _stepperDecelSet;
        private double _stepperJogSpeedSet;

        private double _servoPulsePerRev = 10000;
        private double _servoDisplacementPerRev = 10;
        private double _servoHomeOffsetMm;
        private double _servoSoftLimitPosMm = 500;
        private double _servoSoftLimitNegMm = -500;
        private double _servoTorqueLimit = 80;
        private string _servoPulseMode = "Pulse/Dir";
        private string _servoErrorId = "0";
        private string _servoErrorDescription = "正常";
        private bool _servoOutOfSoftLimit;

        private double _stepperPulsePerRev = 10000;
        private double _stepperDisplacementPerRev = 10;
        private double _stepperHomeOffsetMm;
        private double _stepperSoftLimitPosMm = 500;
        private double _stepperSoftLimitNegMm = -500;
        private string _stepperPulseMode = "Pulse/Dir";
        private string _stepperErrorId = "0";
        private string _stepperErrorDescription = "正常";
        private bool _stepperOutOfSoftLimit;

        public ObservableCollection<string> PulseModeOptions { get; } = new ObservableCollection<string> { "Pulse/Dir", "CW/CCW" };
        private static readonly Dictionary<string, string> ErrorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = "正常",
            ["1"] = "过流保护",
            ["2"] = "过压保护",
            ["3"] = "编码器异常",
            ["4"] = "过载报警",
            ["5"] = "超程报警"
        };

        // -----------------------------
        // DB9 状态指示灯
        // -----------------------------
        public bool StartStatus
        {
            get => _startStatus;
            private set => SetField(ref _startStatus, value);
        }

        public bool StartStop
        {
            get => _startStop;
            private set => SetField(ref _startStop, value);
        }

        // DB9.DBX0.2：System_Running
        public bool SystemRunning
        {
            get => _systemRunning;
            private set
            {
                if (SetField(ref _systemRunning, value))
                {
                    OnPropertyChanged(nameof(IsServoNormal));
                    OnPropertyChanged(nameof(IsServoStopped));
                    OnPropertyChanged(nameof(IsStepperNormal));
                    OnPropertyChanged(nameof(IsStepperStopped));
                }
            }
        }
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
        public bool IsPlcFaulted
        {
            get => _isPlcFaulted;
            private set
            {
                if (SetField(ref _isPlcFaulted, value))
                {
                    OnPropertyChanged(nameof(IsUiEnabled));
                    OnPropertyChanged(nameof(IsAnyFaulted));

                    OnPropertyChanged(nameof(IsServoNormal));
                    OnPropertyChanged(nameof(IsServoStopped));
                    OnPropertyChanged(nameof(IsServoEnabledState));
                    OnPropertyChanged(nameof(IsServoFaultState));
                    OnPropertyChanged(nameof(IsServoOffline));

                    OnPropertyChanged(nameof(IsStepperFaulted));
                    OnPropertyChanged(nameof(IsStepperEnabled));
                    OnPropertyChanged(nameof(IsStepperNormal));
                    OnPropertyChanged(nameof(IsStepperStopped));
                    OnPropertyChanged(nameof(IsStepperOffline));
                }
            }
        }

        public bool IsUiEnabled => !IsSystemBusy && !IsPlcFaulted;

        public bool IsAnyFaulted => IsPlcFaulted || IsServoFaulted || IsStepperFaulted;

        public bool IsServoNormal => !IsPlcFaulted && SystemRunning && IsServoEnabled && !IsServoFaulted;
        public bool IsServoStopped => !IsPlcFaulted && !SystemRunning;
        public bool IsServoEnabledState => !IsPlcFaulted && IsServoEnabled;
        public bool IsServoFaultState => !IsPlcFaulted && IsServoFaulted;
        public bool IsServoOffline => IsPlcFaulted;

        public bool IsStepperFaulted => !IsPlcFaulted && !string.IsNullOrWhiteSpace(StepperErrorId) && StepperErrorId.Trim() != "0";
        public bool IsStepperEnabled => !IsPlcFaulted && !IsStepperFaulted;
        public bool IsStepperNormal => IsStepperEnabled && SystemRunning;
        public bool IsStepperStopped => !IsPlcFaulted && !SystemRunning;
        public bool IsStepperOffline => IsPlcFaulted;

        public bool IsServoEnabled
        {
            get => _isServoEnabled;
            set
            {
                if (SetField(ref _isServoEnabled, value))
                {
                    OnPropertyChanged(nameof(IsServoNormal));
                    OnPropertyChanged(nameof(IsServoEnabledState));
                }
            }
        }
        public bool IsServoFaulted
        {
            get => _isServoFaulted;
            private set
            {
                if (SetField(ref _isServoFaulted, value))
                {
                    OnPropertyChanged(nameof(IsAnyFaulted));
                    OnPropertyChanged(nameof(IsServoNormal));
                    OnPropertyChanged(nameof(IsServoFaultState));
                }
            }
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
        public double StepperAnimAngle
        {
            get => _stepperAnimAngle;
            private set => SetField(ref _stepperAnimAngle, value);
        }
        public string StepperTargetPositionText
        {
            get => _stepperTargetPositionText;
            set => SetField(ref _stepperTargetPositionText, value);
        }
        public bool IsServoSettingOpen { get => _isServoSettingOpen; set => SetField(ref _isServoSettingOpen, value); }
        public bool IsStepperSettingOpen { get => _isStepperSettingOpen; set => SetField(ref _isStepperSettingOpen, value); }

        public double ServoTargetPosMm { get => _servoTargetPosMm; set => SetField(ref _servoTargetPosMm, value); }
        public double ServoSpeedSet { get => _servoSpeedSet; set => SetField(ref _servoSpeedSet, value); }
        public double ServoAccelSet { get => _servoAccelSet; set => SetField(ref _servoAccelSet, value); }
        public double ServoDecelSet { get => _servoDecelSet; set => SetField(ref _servoDecelSet, value); }
        public double ServoJogSpeedSet { get => _servoJogSpeedSet; set => SetField(ref _servoJogSpeedSet, value); }

        public double StepperTargetPosMm { get => _stepperTargetPosMm; set => SetField(ref _stepperTargetPosMm, value); }
        public double StepperSpeedSet { get => _stepperSpeedSet; set => SetField(ref _stepperSpeedSet, value); }
        public double StepperAccelSet { get => _stepperAccelSet; set => SetField(ref _stepperAccelSet, value); }
        public double StepperDecelSet { get => _stepperDecelSet; set => SetField(ref _stepperDecelSet, value); }
        public double StepperJogSpeedSet { get => _stepperJogSpeedSet; set => SetField(ref _stepperJogSpeedSet, value); }

        public double ServoPulsePerRev { get => _servoPulsePerRev; set => SetField(ref _servoPulsePerRev, Math.Max(1, value)); }
        public double ServoDisplacementPerRev { get => _servoDisplacementPerRev; set => SetField(ref _servoDisplacementPerRev, Math.Max(0.001, value)); }
        public double ServoHomeOffsetMm { get => _servoHomeOffsetMm; set => SetField(ref _servoHomeOffsetMm, value); }
        public double ServoSoftLimitPosMm { get => _servoSoftLimitPosMm; set => SetField(ref _servoSoftLimitPosMm, value); }
        public double ServoSoftLimitNegMm { get => _servoSoftLimitNegMm; set => SetField(ref _servoSoftLimitNegMm, value); }
        public double ServoTorqueLimit { get => _servoTorqueLimit; set => SetField(ref _servoTorqueLimit, Math.Clamp(value, 0, 100)); }
        public string ServoPulseMode { get => _servoPulseMode; set => SetField(ref _servoPulseMode, value ?? "Pulse/Dir"); }
        public string ServoErrorId { get => _servoErrorId; private set => SetField(ref _servoErrorId, value); }
        public string ServoErrorDescription { get => _servoErrorDescription; private set => SetField(ref _servoErrorDescription, value); }
        public bool ServoOutOfSoftLimit { get => _servoOutOfSoftLimit; private set => SetField(ref _servoOutOfSoftLimit, value); }

        public double StepperPulsePerRev { get => _stepperPulsePerRev; set => SetField(ref _stepperPulsePerRev, Math.Max(1, value)); }
        public double StepperDisplacementPerRev { get => _stepperDisplacementPerRev; set => SetField(ref _stepperDisplacementPerRev, Math.Max(0.001, value)); }
        public double StepperHomeOffsetMm { get => _stepperHomeOffsetMm; set => SetField(ref _stepperHomeOffsetMm, value); }
        public double StepperSoftLimitPosMm { get => _stepperSoftLimitPosMm; set => SetField(ref _stepperSoftLimitPosMm, value); }
        public double StepperSoftLimitNegMm { get => _stepperSoftLimitNegMm; set => SetField(ref _stepperSoftLimitNegMm, value); }
        public string StepperPulseMode { get => _stepperPulseMode; set => SetField(ref _stepperPulseMode, value ?? "Pulse/Dir"); }
        public string StepperErrorId
        {
            get => _stepperErrorId;
            private set
            {
                if (SetField(ref _stepperErrorId, value))
                {
                    OnPropertyChanged(nameof(IsAnyFaulted));
                    OnPropertyChanged(nameof(IsStepperFaulted));
                    OnPropertyChanged(nameof(IsStepperEnabled));
                    OnPropertyChanged(nameof(IsStepperNormal));
                }
            }
        }
        public string StepperErrorDescription { get => _stepperErrorDescription; private set => SetField(ref _stepperErrorDescription, value); }
        public bool StepperOutOfSoftLimit { get => _stepperOutOfSoftLimit; private set => SetField(ref _stepperOutOfSoftLimit, value); }

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
        private const string ServoTargetPositionTag = "SERVO_TARGET_POS";
        private const string ServoSetSpeedTag = "SERVO_SET_SPEED";
        private const string ServoSetAccelTag = "SERVO_SET_ACCEL";
        private const string ServoSetDecelTag = "SERVO_SET_DECEL";
        private const string ServoSetJogSpeedTag = "SERVO_SET_JOG_SPEED";
        private const string StepperSetSpeedTag = "STEPPER_SET_SPEED";
        private const string StepperSetAccelTag = "STEPPER_SET_ACCEL";
        private const string StepperSetDecelTag = "STEPPER_SET_DECEL";
        private const string StepperSetJogSpeedTag = "STEPPER_SET_JOG_SPEED";
        private const string ServoGearPulsePerRevTag = "SERVO_GEAR_PULSE_PER_REV";
        private const string ServoGearDispPerRevTag = "SERVO_GEAR_DISP_PER_REV";
        private const string ServoHomeOffsetTag = "SERVO_HOME_OFFSET";
        private const string ServoSoftLimitPosTag = "SERVO_SOFT_LIMIT_POS";
        private const string ServoSoftLimitNegTag = "SERVO_SOFT_LIMIT_NEG";
        private const string ServoTorqueLimitTag = "SERVO_TORQUE_LIMIT";
        private const string ServoPulseModeTag = "SERVO_PULSE_MODE";
        private const string ServoAlarmResetTag = "SERVO_ALARM_RESET";
        private const string ServoErrorIdTag = "SERVO_ERROR_ID";
        private const string StepperGearPulsePerRevTag = "STEPPER_GEAR_PULSE_PER_REV";
        private const string StepperGearDispPerRevTag = "STEPPER_GEAR_DISP_PER_REV";
        private const string StepperHomeOffsetTag = "STEPPER_HOME_OFFSET";
        private const string StepperSoftLimitPosTag = "STEPPER_SOFT_LIMIT_POS";
        private const string StepperSoftLimitNegTag = "STEPPER_SOFT_LIMIT_NEG";
        private const string StepperPulseModeTag = "STEPPER_PULSE_MODE";
        private const string StepperAlarmResetTag = "STEPPER_ALARM_RESET";
        private const string StepperErrorIdTag = "STEPPER_ERROR_ID";

        // DB9.DBX0.0 / DB9.DBX0.1：对应该物理输入点的位
        private const string SystemStartDbBitAddress = "DB9.DBX0.0";
        private const string SystemStopDbBitAddress = "DB9.DBX0.1";

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
        public ICommand ServoExecuteCommand { get; }
        public ICommand ServoApplyOperationParamsCommand { get; }
        public ICommand StepperApplyOperationParamsCommand { get; }
        public ICommand ServoSaveConfigCommand { get; }
        public ICommand StepperSaveConfigCommand { get; }
        public ICommand ServoResetAlarmCommand { get; }
        public ICommand StepperResetAlarmCommand { get; }
        public ICommand SystemResetAlarmCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;
        /// <param name="telemetry">全局 PLC 遥测模型（本 ViewModel 主要依赖 PlcService 的电机读写）。</param>
        /// <param name="plcService">PLC 服务层：负责读写/仿真 fallback。</param>
        public MotorDebugViewModel(PlcTelemetryModel telemetry, PlcService plcService)
        {
            _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));

            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();

            IsPlcFaulted = _telemetry.IsFaulted;
            _startStatus = _telemetry.StartStatus;
            _startStop = _telemetry.StartStop;
            _systemRunning = _telemetry.SystemRunning;
            _telemetry.PropertyChanged += TelemetryOnPropertyChanged;

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
            ServoExecuteCommand = new AsyncCommand(DoServoExecuteAsync);
            ServoApplyOperationParamsCommand = new AsyncCommand(DoServoApplyOperationParamsAsync);
            StepperApplyOperationParamsCommand = new AsyncCommand(DoStepperApplyOperationParamsAsync);
            ServoSaveConfigCommand = new AsyncCommand(DoServoSaveConfigAsync);
            StepperSaveConfigCommand = new AsyncCommand(DoStepperSaveConfigAsync);
            ServoResetAlarmCommand = new AsyncCommand(DoServoAlarmResetAsync);
            StepperResetAlarmCommand = new AsyncCommand(DoStepperAlarmResetAsync);
            SystemResetAlarmCommand = new AsyncCommand(DoSystemResetAlarmAsync);

            // 后台轮询实时数据
            _ = Task.Run(PollingLoopAsync, _cts.Token);
        }

        private void TelemetryOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlcTelemetryModel.IsFaulted))
            {
                IsPlcFaulted = _telemetry.IsFaulted;

                // 通讯故障：实时数值显示 ---，并通过 IsUiEnabled 自动禁用所有 PLC 写入按钮。
                if (IsPlcFaulted)
                {
                    _uiContext.Post(_ =>
                    {
                        ServoPositionText = "---";
                        ServoSpeedText = "---";
                        ServoLoadRateText = "---";
                        ServoLoadRate = 0;
                        // 离线时，状态类指示以“离线”为主，避免残留上次故障/使能态。
                        IsServoEnabled = false;
                        IsServoFaulted = false;
                        ServoErrorId = "0";
                        ServoErrorDescription = "正常";

                        StepperPulsePositionText = "---";
                        StepperRunFrequencyText = "---";
                        StepperErrorId = "0";
                        StepperErrorDescription = "正常";
                        StepperOutOfSoftLimit = false;

                        ServoAnimOpacity = 0.35;
                        StepperAnimOpacity = 0.6;
                    }, null);
                }
                return;
            }

            if (e.PropertyName == nameof(PlcTelemetryModel.StartStatus))
            {
                Log.Information("UI: PlcTelemetryModel.StartStatus changed -> {Value}", _telemetry.StartStatus);
                _uiContext.Post(_ => { StartStatus = _telemetry.StartStatus; }, null);
                return;
            }

            if (e.PropertyName == nameof(PlcTelemetryModel.StartStop))
            {
                Log.Information("UI: PlcTelemetryModel.StartStop changed -> {Value}", _telemetry.StartStop);
                _uiContext.Post(_ => { StartStop = _telemetry.StartStop; }, null);
                return;
            }

            if (e.PropertyName == nameof(PlcTelemetryModel.SystemRunning))
            {
                Log.Information("UI: PlcTelemetryModel.SystemRunning changed -> {Value}", _telemetry.SystemRunning);
                _uiContext.Post(_ => { SystemRunning = _telemetry.SystemRunning; }, null);
                return;
            }
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
                        // 通讯故障时，停止读取/刷新实时数值，避免仿真数据覆盖 ---。
                        if (_telemetry.IsFaulted)
                        {
                            await Task.Delay(500, _cts.Token).ConfigureAwait(false);
                            continue;
                        }

                        // 读取实时数据（全部在后台线程）
                        var servoEnabled = await _plcService.ReadBoolAsync(ServoEnableTag).ConfigureAwait(false);
                        var servoFaulted = await _plcService.ReadBoolAsync(ServoFaultTag).ConfigureAwait(false);
                        var servoPos = await _plcService.ReadDoubleAsync(ServoPositionTag).ConfigureAwait(false);
                        var servoSpeed = await _plcService.ReadDoubleAsync(ServoSpeedTag).ConfigureAwait(false);
                        var servoLoad = await _plcService.ReadDoubleAsync(ServoLoadTag).ConfigureAwait(false);

                        var stepperPos = await _plcService.ReadInt64Async(StepperPulsePositionTag).ConfigureAwait(false);
                        var stepperFreq = await _plcService.ReadDoubleAsync(StepperRunFrequencyTag).ConfigureAwait(false);
                        var servoErrorId = await _plcService.ReadTextAsync(ServoErrorIdTag).ConfigureAwait(false);
                        var stepperErrorId = await _plcService.ReadTextAsync(StepperErrorIdTag).ConfigureAwait(false);

                        if (_telemetry.IsFaulted)
                        {
                            // 通讯故障在读取过程中发生：避免覆盖 ---。
                            continue;
                        }

                        _uiContext.Post(_ =>
                        {
                            IsServoEnabled = servoEnabled;
                            IsServoFaulted = servoFaulted;

                            _servoPosition = PulseToPhysical(servoPos, ServoPulsePerRev, ServoDisplacementPerRev);
                            ServoPositionText = $"{_servoPosition.ToString("0.###", CultureInfo.InvariantCulture)} mm";

                            _servoSpeed = servoSpeed;
                            ServoSpeedText = $"{_servoSpeed.ToString("0.###", CultureInfo.InvariantCulture)}";

                            _servoLoadRate = Math.Clamp(servoLoad, 0, 100);
                            ServoLoadRate = _servoLoadRate;
                            ServoLoadRateText = $"{_servoLoadRate.ToString("0.##", CultureInfo.InvariantCulture)} %";

                            // 伺服动画：角度随转速更新；未使能或故障时降低可见度。
                            _servoAnimAngle = (_servoAnimAngle + (_servoSpeed * 0.02)) % 360;
                            ServoAnimAngle = _servoAnimAngle;
                            ServoAnimOpacity = (_isPlcFaulted || servoFaulted || !servoEnabled) ? 0.35 : 1.0;

                            _stepperPulsePosition = stepperPos;
                            var stepperPosMm = PulseToPhysical(stepperPos, StepperPulsePerRev, StepperDisplacementPerRev);
                            StepperPulsePositionText = $"{stepperPosMm.ToString("0.###", CultureInfo.InvariantCulture)} mm";

                            _stepperRunFrequency = stepperFreq;
                            StepperRunFrequencyText = $"{_stepperRunFrequency.ToString("0.##", CultureInfo.InvariantCulture)}";
                            UpdateErrorDisplay(servoErrorId, isServo: true);
                            UpdateErrorDisplay(stepperErrorId, isServo: false);
                            ServoOutOfSoftLimit = _servoPosition > ServoSoftLimitPosMm || _servoPosition < ServoSoftLimitNegMm;
                            StepperOutOfSoftLimit = stepperPosMm > StepperSoftLimitPosMm || stepperPosMm < StepperSoftLimitNegMm;

                            // 步进动画：位移随脉冲位置模值变化，形成“滑块”视觉效果。
                            var mod = ((stepperPos % 1000) + 1000) % 1000;
                            _stepperAnimOffset = (mod / 1000.0) * 120;
                            StepperAnimOffset = _stepperAnimOffset;
                            _stepperAnimAngle = (stepperPos % 200) * 1.8; // 200 脉冲约一圈，用于步进转子视觉反馈
                            StepperAnimAngle = _stepperAnimAngle;
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
            => _plcService.WriteBoolDbBitAsync(SystemStartDbBitAddress, true);
        private Task DoSystemStopAsync()
            => _plcService.WriteBoolDbBitAsync(SystemStopDbBitAddress, true);

        // -----------------------------
        // 启停按钮：触发式写入（点击仅发送一次 true）
        // -----------------------------
        public async Task OnStartButtonClickAsync()
        {
            await _startStopWriteSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await _plcService.WriteBoolDbBitAsync(SystemStartDbBitAddress, true).ConfigureAwait(false);
            }
            finally
            {
                _startStopWriteSemaphore.Release();
            }
        }

        public async Task OnStopButtonClickAsync()
        {
            await _startStopWriteSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await _plcService.WriteBoolDbBitAsync(SystemStopDbBitAddress, true).ConfigureAwait(false);
            }
            finally
            {
                _startStopWriteSemaphore.Release();
            }
        }

        // -----------------------------
        // 启停按钮点动（MouseDown/Up）
        // -----------------------------
        public async Task SetStartPressedAsync(bool pressed)
        {
            await _startStopWriteSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await _plcService.WriteBoolDbBitAsync(SystemStartDbBitAddress, pressed).ConfigureAwait(false);
            }
            finally
            {
                _startStopWriteSemaphore.Release();
            }
        }

        public async Task SetStopPressedAsync(bool pressed)
        {
            await _startStopWriteSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await _plcService.WriteBoolDbBitAsync(SystemStopDbBitAddress, pressed).ConfigureAwait(false);
            }
            finally
            {
                _startStopWriteSemaphore.Release();
            }
        }
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
        private Task DoServoExecuteAsync()
        {
            return RunWithBusyGuardAsync(async () =>
            {
                var targetPulse = PhysicalToPulse(ServoTargetPosMm, ServoPulsePerRev, ServoDisplacementPerRev);
                await _plcService.WriteValueAsync(ServoTargetPositionTag, targetPulse).ConfigureAwait(false);
            });
        }
        private async Task DoServoApplyOperationParamsAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                await _plcService.WriteValueAsync(ServoTargetPositionTag, PhysicalToPulse(ServoTargetPosMm, ServoPulsePerRev, ServoDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoSetSpeedTag, ServoSpeedSet).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoSetAccelTag, ServoAccelSet).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoSetDecelTag, ServoDecelSet).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoSetJogSpeedTag, ServoJogSpeedSet).ConfigureAwait(false);
            });
        }
        private async Task DoStepperApplyOperationParamsAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                await _plcService.WriteValueAsync(StepperTargetPositionTag, PhysicalToPulse(StepperTargetPosMm, StepperPulsePerRev, StepperDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperSetSpeedTag, StepperSpeedSet).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperSetAccelTag, StepperAccelSet).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperSetDecelTag, StepperDecelSet).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperSetJogSpeedTag, StepperJogSpeedSet).ConfigureAwait(false);
            });
        }
        private async Task DoServoSaveConfigAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                await _plcService.WriteValueAsync(ServoGearPulsePerRevTag, ServoPulsePerRev).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoGearDispPerRevTag, ServoDisplacementPerRev).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoHomeOffsetTag, PhysicalToPulse(ServoHomeOffsetMm, ServoPulsePerRev, ServoDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoSoftLimitPosTag, PhysicalToPulse(ServoSoftLimitPosMm, ServoPulsePerRev, ServoDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoSoftLimitNegTag, PhysicalToPulse(ServoSoftLimitNegMm, ServoPulsePerRev, ServoDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoTorqueLimitTag, ServoTorqueLimit).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoPulseModeTag, ServoPulseMode).ConfigureAwait(false);
            });
        }
        private async Task DoStepperSaveConfigAsync()
        {
            await RunWithBusyGuardAsync(async () =>
            {
                await _plcService.WriteValueAsync(StepperGearPulsePerRevTag, StepperPulsePerRev).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperGearDispPerRevTag, StepperDisplacementPerRev).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperHomeOffsetTag, PhysicalToPulse(StepperHomeOffsetMm, StepperPulsePerRev, StepperDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperSoftLimitPosTag, PhysicalToPulse(StepperSoftLimitPosMm, StepperPulsePerRev, StepperDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperSoftLimitNegTag, PhysicalToPulse(StepperSoftLimitNegMm, StepperPulsePerRev, StepperDisplacementPerRev)).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperPulseModeTag, StepperPulseMode).ConfigureAwait(false);
            });
        }
        private Task DoServoAlarmResetAsync()
            => RunWithBusyGuardAsync(async () => { await _plcService.WriteValueAsync(ServoAlarmResetTag, true).ConfigureAwait(false); await _plcService.WriteValueAsync(ServoAlarmResetTag, false).ConfigureAwait(false); });
        private Task DoStepperAlarmResetAsync()
            => RunWithBusyGuardAsync(async () => { await _plcService.WriteValueAsync(StepperAlarmResetTag, true).ConfigureAwait(false); await _plcService.WriteValueAsync(StepperAlarmResetTag, false).ConfigureAwait(false); });

        private Task DoSystemResetAlarmAsync()
            => RunWithBusyGuardAsync(async () =>
            {
                // 没有故障时不执行复位写入（避免无意义写 PLC）。
                if (!IsAnyFaulted)
                {
                    return;
                }

                await _plcService.WriteValueAsync(ServoAlarmResetTag, true).ConfigureAwait(false);
                await _plcService.WriteValueAsync(ServoAlarmResetTag, false).ConfigureAwait(false);

                await _plcService.WriteValueAsync(StepperAlarmResetTag, true).ConfigureAwait(false);
                await _plcService.WriteValueAsync(StepperAlarmResetTag, false).ConfigureAwait(false);
            });

        private static long PhysicalToPulse(double mm, double pulsePerRev, double displacementPerRev)
            => (long)Math.Round(mm * (pulsePerRev / Math.Max(0.001, displacementPerRev)));
        private static double PulseToPhysical(double pulse, double pulsePerRev, double displacementPerRev)
            => pulse * (Math.Max(0.001, displacementPerRev) / Math.Max(1, pulsePerRev));
        private void UpdateErrorDisplay(string raw, bool isServo)
        {
            var key = string.IsNullOrWhiteSpace(raw) ? "0" : raw.Trim();
            var desc = ErrorMap.TryGetValue(key, out var text) ? text : "未知错误";
            if (isServo)
            {
                ServoErrorId = key;
                ServoErrorDescription = desc;
            }
            else
            {
                StepperErrorId = key;
                StepperErrorDescription = desc;
            }
        }
        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _telemetry.PropertyChanged -= TelemetryOnPropertyChanged;
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


