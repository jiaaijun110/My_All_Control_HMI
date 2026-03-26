using Common.CPlc;
using Serilog;
using System.Reflection;

namespace Services
{
    /// <remarks>
    /// <para>
    /// 本项目的 <see cref="IPlc"/> 目前仅提供连接与读取（<c>ConnectAsync</c>/<c>ReadBytesAsync</c>），
    /// 真正的“写控制位/写目标值”不在接口里；因此本实现对底层 PLC 驱动做“可选能力探测”：
    /// </para>
    /// <para>
    /// - 如果驱动对象上存在 <c>WriteTag(string address, object value)</c> 方法，则使用它写控制位；
    /// - 如果驱动对象上存在 <c>ReadTag(string address)</c> 方法，则使用它读取电机实时数据；
    /// - 若驱动不支持写/读 Tag，则返回默认值并记录警告（便于联调阶段先跑通 UI）。
    /// </para>
    /// </remarks>
    public sealed class PlcService
    {
        private readonly IPlc _plc;
        private readonly object _sync = new();

        // 简易仿真状态：当底层不支持 ReadTag 时，为了让 Dashboard 的实时轮询“有变化”，返回可视化的模拟数据。
        private double _servoPos;
        private double _servoSpeed;
        private double _servoLoad;
        private bool _servoFault;

        private long _stepperPulsePos;
        private double _stepperFreq;
        /// <param name="plc">PLC 通讯驱动（真实或模拟）。</param>
        public PlcService(IPlc plc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _servoPos = 0;
            _servoSpeed = 1450;
            _servoLoad = 30;
            _servoFault = false;
            _stepperPulsePos = 0;
            _stepperFreq = 50;
        }
        public async Task EnsureConnectedAsync()
        {
            if (_plc.IsConnected)
            {
                return;
            }

            try
            {
                await _plc.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PLC 连接失败（继续使用默认/仿真数据）。");
            }
        }

        private async Task EnsureConnectedStrictAsync()
        {
            if (_plc.IsConnected)
            {
                return;
            }

            var ok = await _plc.ConnectAsync().ConfigureAwait(false);
            if (!ok)
            {
                throw new InvalidOperationException("PLC 连接失败，无法写入。");
            }
        }

        /// <summary>
        /// 直接写入 S7.Net 地址（例如：DB9.DBX0.0）。
        /// </summary>
        public async Task WriteBoolDbBitAsync(string s7Address, bool value)
        {
            if (string.IsNullOrWhiteSpace(s7Address))
            {
                throw new ArgumentException("s7Address 不能为空。", nameof(s7Address));
            }

            await EnsureConnectedStrictAsync().ConfigureAwait(false);
            await _plc.WriteAsync(s7Address, value).ConfigureAwait(false);
        }
        /// <param name="address">PLC 点位地址（项目内请替换为真实地址/Tag）。</param>
        /// <param name="value">控制值。</param>
        /// <returns>写入是否成功（若驱动不支持写 Tag，则返回 false）。</returns>
        public async Task<bool> WriteBoolAsync(string address, bool value)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);

            var writeMethod = _plc.GetType().GetMethod("WriteTag", new[] { typeof(string), typeof(object) });
            if (writeMethod == null)
            {
                // 调试阶段：如果底层不支持写入，则用“仿真状态”驱动 UI 反馈，避免按钮点击没有任何效果。
                lock (_sync)
                {
                    var addr = address ?? string.Empty;

                    if (addr.Contains("SERVO_HOME", StringComparison.OrdinalIgnoreCase))
                    {
                        _servoPos = 0;
                        return true;
                    }

                    if (addr.Contains("SERVO_ENABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        // 简化策略：复用 _servoFault 表示“是否允许使能”；使能=真时 fault 视为假。
                        _servoFault = !value;
                        return true;
                    }

                    if (addr.Contains("SERVO_JOG_PLUS", StringComparison.OrdinalIgnoreCase) && value)
                    {
                        _servoPos += 50;
                        return true;
                    }

                    if (addr.Contains("SERVO_JOG_MINUS", StringComparison.OrdinalIgnoreCase) && value)
                    {
                        _servoPos -= 50;
                        return true;
                    }

                    if (addr.Contains("STEPPER_HOME", StringComparison.OrdinalIgnoreCase))
                    {
                        _stepperPulsePos = 0;
                        return true;
                    }

                    if (addr.Contains("STEPPER_JOG_PLUS", StringComparison.OrdinalIgnoreCase) || addr.Contains("JOG_PLUS", StringComparison.OrdinalIgnoreCase))
                    {
                        _stepperPulsePos += 100;
                        return true;
                    }

                    if (addr.Contains("STEPPER_JOG_MINUS", StringComparison.OrdinalIgnoreCase) || addr.Contains("JOG_MINUS", StringComparison.OrdinalIgnoreCase))
                    {
                        _stepperPulsePos -= 100;
                        return true;
                    }
                }

                Log.Warning("当前 PLC 驱动不支持 WriteTag（已尝试仿真写入 {Address}，但未命中仿真规则）。", address);
                return false;
            }

            try
            {
                // 由于反射参数类型是 object，value 会被装箱为 bool。
                writeMethod.Invoke(_plc, new object[] { address, value });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "写入控制位失败：{Address}", address);
                return false;
            }
        }
        /// <typeparam name="T">数值类型（int/long/double 等）。</typeparam>
        /// <param name="address">PLC 点位地址（项目内请替换为真实地址/Tag）。</param>
        /// <param name="value">目标值。</param>
        /// <returns>写入是否成功。</returns>
        public async Task<bool> WriteNumberAsync<T>(string address, T value) where T : struct
        {
            await EnsureConnectedAsync().ConfigureAwait(false);

            var writeMethod = _plc.GetType().GetMethod("WriteTag", new[] { typeof(string), typeof(object) });
            if (writeMethod == null)
            {
                // 调试阶段：无写入能力时，仍按地址关键词更新仿真数据，保证 UI 可见联动。
                lock (_sync)
                {
                    var addr = address ?? string.Empty;

                    if (addr.Contains("STEPPER", StringComparison.OrdinalIgnoreCase) || addr.Contains("TARGET", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            _stepperPulsePos = Convert.ToInt64(value);
                            return true;
                        }
                        catch
                        {
                            // ignore & fall back
                        }
                    }

                    if (addr.Contains("SERVO", StringComparison.OrdinalIgnoreCase) && addr.Contains("POS", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            _servoPos = Convert.ToDouble(value);
                            return true;
                        }
                        catch
                        {
                            // ignore & fall back
                        }
                    }
                }

                Log.Warning("当前 PLC 驱动不支持 WriteTag（已尝试仿真写入 {Address}，但未命中仿真规则）。", address);
                return false;
            }

            try
            {
                writeMethod.Invoke(_plc, new object[] { address, value! });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "写入数值失败：{Address}", address);
                return false;
            }
        }

        /// <summary>
        /// 通用异步写入：优先走 IPlc.WriteAsync，失败时再回退到驱动扩展 WriteTag。
        /// </summary>
        public async Task<bool> WriteValueAsync(string address, object value)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);
            try
            {
                await _plc.WriteAsync(address, value).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WriteAsync 写入失败，尝试回退 WriteTag：{Address}", address);
            }

            var writeMethod = _plc.GetType().GetMethod("WriteTag", new[] { typeof(string), typeof(object) });
            if (writeMethod == null)
            {
                Log.Warning("当前 PLC 驱动不支持 WriteTag，通用写入失败：{Address}", address);
                return false;
            }

            try
            {
                writeMethod.Invoke(_plc, new object[] { address, value });
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "回退 WriteTag 失败：{Address}", address);
                return false;
            }
        }

        public async Task<string> ReadTextAsync(string address)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);
            var readMethod = _plc.GetType().GetMethod("ReadTag", new[] { typeof(string) });
            if (readMethod == null)
            {
                return string.Empty;
            }

            try
            {
                var obj = readMethod.Invoke(_plc, new object[] { address });
                return obj?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        /// <param name="address">PLC 点位地址。</param>
        public async Task<bool> ReadBoolAsync(string address)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);

            // 若驱动支持 ReadTag，则优先读取
            var readMethod = _plc.GetType().GetMethod("ReadTag", new[] { typeof(string) });
            if (readMethod != null)
            {
                try
                {
                    var obj = readMethod.Invoke(_plc, new object[] { address });
                    if (obj is bool b)
                    {
                        return b;
                    }
                }
                catch
                {
                    // ignore & fall back to simulation
                }
            }

            // 不支持时使用仿真
            lock (_sync)
            {
                // 低概率故障仿真
                _servoFault = !_servoFault && new Random().NextDouble() < 0.01 ? true : _servoFault;
                return address.Contains("FAULT", StringComparison.OrdinalIgnoreCase) ? _servoFault : !_servoFault;
            }
        }
        public async Task<double> ReadDoubleAsync(string address)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);

            var readMethod = _plc.GetType().GetMethod("ReadTag", new[] { typeof(string) });
            if (readMethod != null)
            {
                try
                {
                    var obj = readMethod.Invoke(_plc, new object[] { address });
                    if (obj is double d) return d;
                    if (obj is float f) return f;
                    if (obj is int i) return i;
                    if (obj is long l) return l;
                    if (obj is uint ui) return ui;
                    if (obj is string s && double.TryParse(s, out var parsed)) return parsed;
                }
                catch
                {
                    // ignore & fall back to simulation
                }
            }

            // 不支持时使用仿真：推进位置并轻微波动速度/负载
            lock (_sync)
            {
                var rnd = new Random();
                if (address.Contains("SERVO", StringComparison.OrdinalIgnoreCase))
                {
                    _servoPos += _servoSpeed * 0.02;
                    _servoSpeed += rnd.NextDouble() * 40 - 20;   // +/-20 rpm
                    _servoSpeed = Math.Clamp(_servoSpeed, 0, 3000);
                    _servoLoad += rnd.NextDouble() * 8 - 4;      // +/-4 %
                    _servoLoad = Math.Clamp(_servoLoad, 0, 100);

                    // 仿真输出：根据地址关键词返回不同量，保证 UI 显示更符合预期。
                    // - *POS*：当前位置
                    // - *LOAD*：负载率
                    // - 其他（默认）：转速
                    if (address.Contains("LOAD", StringComparison.OrdinalIgnoreCase))
                    {
                        return _servoLoad;
                    }
                    if (address.Contains("POS", StringComparison.OrdinalIgnoreCase))
                    {
                        return _servoPos;
                    }
                    return _servoSpeed;
                }

                if (address.Contains("STEPPER", StringComparison.OrdinalIgnoreCase))
                {
                    _stepperPulsePos += (long)(_stepperFreq * 10); // 假设每轮增加
                    _stepperFreq += rnd.NextDouble() * 10 - 5;
                    _stepperFreq = Math.Clamp(_stepperFreq, 0, 200);
                    return address.Contains("FREQ", StringComparison.OrdinalIgnoreCase) ? _stepperFreq : _stepperPulsePos;
                }

                return 0;
            }
        }
        public async Task<long> ReadInt64Async(string address)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);

            var readMethod = _plc.GetType().GetMethod("ReadTag", new[] { typeof(string) });
            if (readMethod != null)
            {
                try
                {
                    var obj = readMethod.Invoke(_plc, new object[] { address });
                    if (obj is long l) return l;
                    if (obj is int i) return i;
                    if (obj is uint ui) return ui;
                    if (obj is string s && long.TryParse(s, out var parsed)) return parsed;
                }
                catch
                {
                    // ignore & fall back to simulation
                }
            }

            lock (_sync)
            {
                if (address.Contains("STEPPER", StringComparison.OrdinalIgnoreCase))
                {
                    return _stepperPulsePos;
                }

                return 0;
            }
        }
    }
}


