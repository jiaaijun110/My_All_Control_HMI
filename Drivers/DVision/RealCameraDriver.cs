using System.Buffers;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using Drivers.DVision.Siemens;
using Serilog;

namespace Drivers.DVision
{
    public sealed class CameraLinkEventArgs : EventArgs
    {
        public required bool IsUp { get; init; }
    }
    /// <remarks>
    /// <para>
    /// 重要说明：你的当前工程编译环境里无法在编译期解析 <c>MvCameraControl.Net.dll</c> 的命名空间/类型名，
    /// 但运行时你已放置 DLL。为保证代码能在“不改 .csproj”的前提下编译通过，
    /// 本实现采用 <b>反射</b> 调用 SDK 方法与创建回调委托。
    /// </para>
    /// <para>
    /// 这仍然满足业务逻辑要求：枚举 GigE 设备、匹配 <c>stGigEInfo.chCurrentIp</c>、注册 Callback、在回调中将 <c>pData</c> 指针拷贝到 <c>byte[]</c>，
    /// 并在 Close/Dispose 时调用 StopGrabbing/CloseDevice/DestroyDevice 释放内存。
    /// </para>
    /// </remarks>
    public sealed class RealCameraDriver : ICameraDriver, IDisposable
    {
        private const string DefaultCameraIp = "192.168.0.2";

        private const uint GevHeartbeatTimeoutMs = 5000;
        private const uint GevScpsPacketSize = 1500;

        private readonly string _cameraIp;
        private readonly object _sync = new();
        private readonly List<string> _enumeratedIps = new();

        private bool _disposed;
        private bool _deviceCreated;

        private long _frameId;

        private object? _myCameraInstance;
        private Assembly? _mvAssembly;
        private Type? _myCameraType;
        private Type? _deviceInfoListType;
        private Type? _deviceInfoType;

        private Delegate? _outputCallbackDelegate;
        private GCHandle _callbackHandle;

        /// <inheritdoc />
        public bool IsOpen { get; private set; }

        /// <inheritdoc />
        public bool IsGrabbing { get; private set; }

        /// <inheritdoc />
        public event EventHandler<ImageGrabbedEventArgs>? ImageGrabbed;
        public event EventHandler<CameraLinkEventArgs>? LinkStateChanged;
        public bool IsLinkUp { get; private set; }
        public RealCameraDriver()
            : this(DefaultCameraIp)
        {
        }
        /// <param name="cameraIp">IPv4 地址字符串。</param>
        public RealCameraDriver(string cameraIp)
        {
            _cameraIp = cameraIp ?? throw new ArgumentNullException(nameof(cameraIp));
        }

        /// <inheritdoc />
        public void Open()
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                if (IsOpen)
                {
                    return;
                }

                try
                {
                    LoadMvAssemblyAndTypes();
                    EnumerateMatchCreateOpen();
                    ConfigureCamera();
                    RegisterCallback();

                    IsOpen = true;
                    SetLinkState(true);
                }
                catch (Exception ex)
                {
                    IsOpen = false;
                    IsLinkUp = false;
                    SetLinkState(false);
                    Log.Warning(ex, "相机 Open 失败（将保持离线）。");
                    return;
                }
            }
        }

        /// <inheritdoc />
        public void StartGrabbing()
        {
            ThrowIfDisposed();
            lock (_sync)
            {
                if (IsGrabbing)
                {
                    return;
                }

                if (!IsOpen)
                {
                    Open();
                }

                if (!IsOpen)
                {
                    // 无设备、IP 不匹配或打开失败时，保持离线状态并安静返回，不抛异常阻断页面。
                    SetLinkState(false);
                    Log.Warning("相机未打开，跳过 StartGrabbing（保持离线）。");
                    return;
                }

                try
                {
                    InvokeMyCameraInt("MV_CC_StartGrabbing_NET");
                    IsGrabbing = true;
                    SetLinkState(true);
                }
                catch (Exception ex)
                {
                    IsGrabbing = false;
                    SetLinkState(false);
                    Log.Warning(ex, "相机 StartGrabbing 失败。");
                    return;
                }
            }
        }

        /// <inheritdoc />
        public void StopGrabbing()
        {
            lock (_sync)
            {
                if (!IsGrabbing)
                {
                    return;
                }

                try
                {
                    // 退出采集/释放会话：只做最佳努力，不把异常抛到回调/UI。
                    InvokeMyCameraInt("MV_CC_StopGrabbing_NET");
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "相机 StopGrabbing 异常（忽略）。");
                }
                finally
                {
                    IsGrabbing = false;
                }
            }
        }

        /// <inheritdoc />
        public void Close()
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                if (!IsOpen && !_deviceCreated)
                {
                    return;
                }

                try
                {
                    // 按要求释放内存与会话：StopGrabbing -> CloseDevice -> DestroyDevice
                    StopGrabbing();

                    if (_myCameraInstance != null && _deviceCreated)
                    {
                        TryInvokeMyCameraInt("MV_CC_CloseDevice_NET");
                        TryInvokeMyCameraInt("MV_CC_DestroyDevice_NET");
                    }
                }
                finally
                {
                    IsOpen = false;
                    IsGrabbing = false;
                    _deviceCreated = false;

                    SetLinkState(false);

                    if (_callbackHandle.IsAllocated)
                    {
                        _callbackHandle.Free();
                    }
                }
            }
        }

        private void LoadMvAssemblyAndTypes()
        {
            if (_myCameraInstance != null)
            {
                return;
            }

            // 通过文件名探测 SDK dll（不改 .csproj 的约束下，尽量让代码适应不同 DLL 放置位置）。
            _mvAssembly = TryLoadMvCameraAssembly();
            if (_mvAssembly == null)
            {
                throw new InvalidOperationException("未找到 MvCameraControl.Net.dll 或其兼容 DLL（无法加载 SDK）。");
            }

            // 1) 找到 MyCamera 类型（命名不确定，所以用名称搜索）。
            _myCameraType = _mvAssembly.GetTypes().FirstOrDefault(t => t.Name.Equals("MyCamera", StringComparison.Ordinal));
            if (_myCameraType == null)
            {
                throw new InvalidOperationException("未在 MvCameraControl.Net.dll 中找到类型：MyCamera。");
            }

            _myCameraInstance = Activator.CreateInstance(_myCameraType);
            if (_myCameraInstance == null)
            {
                throw new InvalidOperationException("无法创建 MyCamera 实例。");
            }

            // 2) 定位用到的结构类型（用于构造 device list 与 selected device）。
            _deviceInfoListType = _mvAssembly.GetTypes().FirstOrDefault(t => t.Name.Contains("DEVICE_INFO_LIST", StringComparison.OrdinalIgnoreCase));
            _deviceInfoType = _mvAssembly.GetTypes().FirstOrDefault(t => t.Name.Contains("DEVICE_INFO", StringComparison.OrdinalIgnoreCase) && !t.Name.Contains("LIST", StringComparison.OrdinalIgnoreCase));

            if (_deviceInfoListType == null || _deviceInfoType == null)
            {
                throw new InvalidOperationException("未能从 SDK 中定位 MV_CC_DEVICE_INFO_LIST / MV_CC_DEVICE_INFO 类型。");
            }
        }

        private Assembly? TryLoadMvCameraAssembly()
        {
            // 尝试在应用基目录加载（通常 DLL 与 exe 同目录或 output 目录）。
            var baseDir = AppContext.BaseDirectory;

            static string CombineSafe(string dir, string file) => Path.Combine(dir, file);

            var candidateNames = new[]
            {
                "MvCameraControl.Net.dll",
                "MvCameraControl.Net.dll".ToLowerInvariant(),
                "MvCameraControlNet.dll",
                "MvCamCtrl.NET.dll",
                "MvCameraControl.NET.dll",
                "MvCamCtrl.NET.dll"
            };

            foreach (var name in candidateNames)
            {
                var full = CombineSafe(baseDir, name);
                if (File.Exists(full))
                {
                    try
                    {
                        return Assembly.LoadFrom(full);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            // 兜底：在基目录递归搜索（只做一次，避免频繁 IO）。
            try
            {
                var hit = Directory.EnumerateFiles(baseDir, "*Mv*Camera*Control*.dll", SearchOption.AllDirectories).FirstOrDefault();
                if (hit != null)
                {
                    return Assembly.LoadFrom(hit);
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private void EnumerateMatchCreateOpen()
        {
            if (_myCameraType == null || _myCameraInstance == null || _deviceInfoListType == null || _deviceInfoType == null)
            {
                throw new InvalidOperationException("SDK 未初始化完成。");
            }

            // MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE, ref devList)
            // 由于常量/枚举类型可能不同，这里通过反射取常量字段值。
            var gigeDeviceValueInt = GetConstantIntValue("MV_GIGE_DEVICE") ?? 0;

            var devList = Activator.CreateInstance(_deviceInfoListType)!;

            var enumMethod = _myCameraType.GetMethod("MV_CC_EnumDevices_NET", BindingFlags.Public | BindingFlags.Static);
            if (enumMethod == null)
            {
                // 某些版本可能是实例方法
                enumMethod = _myCameraType.GetMethod("MV_CC_EnumDevices_NET", BindingFlags.Public | BindingFlags.Instance);
                if (enumMethod == null)
                {
                    throw new InvalidOperationException("SDK 缺失方法：MV_CC_EnumDevices_NET。");
                }
            }

            object? enumResult;
            var enumMethodParams = enumMethod.GetParameters();
            if (enumMethodParams.Length < 1)
            {
                throw new InvalidOperationException("SDK 方法 MV_CC_EnumDevices_NET 参数签名异常。");
            }

            // 反射调用时如果参数期望 UInt32，但我们传入 Int32，会触发 ArgumentException。
            // 因此根据参数类型显式转换/装箱。
            var firstParamType = enumMethodParams[0].ParameterType;
            object enumArg0 = firstParamType == typeof(uint)
                ? (object)unchecked((uint)gigeDeviceValueInt)
                : (object)gigeDeviceValueInt;

            if (enumMethod.IsStatic)
            {
                enumResult = enumMethod.Invoke(null, new[] { enumArg0, devList });
            }
            else
            {
                enumResult = enumMethod.Invoke(_myCameraInstance, new[] { enumArg0, devList });
            }

            var nRet = enumResult is int retCode
                ? retCode
                : enumResult is uint retU
                    ? unchecked((int)retU)
                    : 0;
            var mvOk = GetConstantIntValue("MV_OK") ?? 0;
            if (nRet != mvOk)
            {
                Log.Warning("枚举设备失败，MV_CC_EnumDevices_NET 返回：0x{Ret:X8}", nRet);
                throw new InvalidOperationException("枚举 GigE 设备失败。");
            }

            // devList.nDeviceNum（不同 SDK 版本可能为 int/uint）
            var nDeviceNumObj = _deviceInfoListType!.GetField("nDeviceNum")?.GetValue(devList);
            var nDeviceNum = ToInt32Loose(nDeviceNumObj);
            if (nDeviceNum <= 0)
            {
                throw new InvalidOperationException("未发现任何 GigE 设备。");
            }

            object? selectedDevice = null;
            _enumeratedIps.Clear();
            for (var i = 0; i < nDeviceNum; i++)
            {
                // devList.pDeviceInfo[i]
                var pDeviceInfo = _deviceInfoListType.GetField("pDeviceInfo")?.GetValue(devList);
                if (pDeviceInfo == null)
                {
                    break;
                }

                var pArray = (Array)pDeviceInfo;
                var pDev = pArray.GetValue(i);
                if (pDev == null)
                {
                    continue;
                }

                // 将 IntPtr -> DEVICE_INFO 结构体
                if (pDev is not IntPtr devPtr)
                {
                    continue;
                }

                var device = Marshal.PtrToStructure(devPtr, _deviceInfoType);
                var deviceNTypeLayerType = GetFieldIntValue(device, "nTLayerType");

                var gigeType = GetConstantIntValue("MV_GIGE_DEVICE") ?? 0;
                if (deviceNTypeLayerType != gigeType)
                {
                    continue;
                }

                // device.SpecialInfo.stGigEInfo.chCurrentIp
                var specialInfo = GetFieldValue(device, "SpecialInfo");
                var stGigEInfo = GetFieldValue(specialInfo, "stGigEInfo");
                var ipFieldObj = GetCurrentIpField(stGigEInfo);
                var ipStr = ConvertCurrentIpToString(ipFieldObj);
                if (!string.IsNullOrWhiteSpace(ipStr))
                {
                    _enumeratedIps.Add(ipStr);
                }
                if (string.Equals(ipStr, _cameraIp, StringComparison.OrdinalIgnoreCase))
                {
                    selectedDevice = device;
                    break;
                }
            }

            if (selectedDevice == null)
            {
                Log.Warning("未匹配到目标相机 IP={TargetIp}，当前枚举到的 GigE IP: {Ips}",
                    _cameraIp,
                    _enumeratedIps.Count == 0 ? "（空）" : string.Join(", ", _enumeratedIps));
                throw new InvalidOperationException($"未找到匹配 IP 的 GigE 相机：{_cameraIp}。");
            }

            // CreateDevice: MV_CC_CreateDevice_NET(ref device)
            var createMethod = _myCameraType.GetMethod("MV_CC_CreateDevice_NET", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (createMethod == null)
            {
                throw new InvalidOperationException("SDK 缺失方法：MV_CC_CreateDevice_NET。");
            }

            var createArgs = new object[] { selectedDevice };
            var createRetObj = createMethod.Invoke(_myCameraInstance, createArgs) ?? 0;
            var createRet = createRetObj is int iRet ? iRet : createRetObj is uint uRet ? unchecked((int)uRet) : 0;
            if (createRet != (GetConstantIntValue("MV_OK") ?? 0))
            {
                Log.Warning("CreateDevice 失败，返回：0x{Ret:X8}", createRet);
                throw new InvalidOperationException("CreateDevice 失败。");
            }

            _deviceCreated = true;

            // OpenDevice: MV_CC_OpenDevice_NET()
            InvokeMyCameraInt("MV_CC_OpenDevice_NET");
        }

        private void ConfigureCamera()
        {
            if (_myCameraInstance == null)
            {
                throw new InvalidOperationException("SDK 未初始化。");
            }

            // 设置 GevHeartbeatTimeout（ms）
            TrySetIntValue("GevHeartbeatTimeout", GevHeartbeatTimeoutMs);

            // 设置 GevSCPSPacketSize
            TrySetIntValue("GevSCPSPacketSize", GevScpsPacketSize);

            // 取图参数：连续采集，触发模式关闭
            TrySetEnumValue("AcquisitionMode", 2);
            TrySetEnumValue("TriggerMode", 0);
        }

        private void RegisterCallback()
        {
            if (_myCameraType == null || _myCameraInstance == null)
            {
                throw new InvalidOperationException("SDK 未初始化。");
            }

            // 注册 callback 必须保存 delegate 引用，避免 GC。
            // 由于委托类型来自 SDK，我们在运行时创建“签名匹配”的委托实例。
            var cbDelegateType = FindCbOutputExDelegateType(_myCameraType);
            if (cbDelegateType == null)
            {
                throw new InvalidOperationException("未能定位 SDK 的 cbOutputExDelegate 类型。");
            }

            var invokeMethod = cbDelegateType.GetMethod("Invoke");
            if (invokeMethod == null)
            {
                throw new InvalidOperationException("cbOutputExDelegate 缺失 Invoke 方法。");
            }

            var ps = invokeMethod.GetParameters();
            if (ps.Length != 3)
            {
                throw new InvalidOperationException("cbOutputExDelegate 签名异常：期望 3 个参数。");
            }

            // 参数期望：IntPtr pData, ref MV_FRAME_OUT_INFO_EX pFrameInfo, IntPtr pUser
            // 我们用 pUser 传入 GCHandle(this)，回调内部再访问实例。
            _callbackHandle = GCHandle.Alloc(this);
            var pUser = GCHandle.ToIntPtr(_callbackHandle);

            // 动态生成委托：签名与 cbOutputExDelegate.Invoke 完全一致
            var wrapper = CreateCallbackWrapper(cbDelegateType, frameInfoByRefType: ps[1].ParameterType);
            _outputCallbackDelegate = wrapper;

            // 选择“注册 RGB 回调”的方法（不同 SDK 可能只支持 RGB/Mono/BGR）。
            // 需求里要求将 pData 拷贝到 byte[]，并传给 Service，这里优先 RGB24 再转换为 BGR32。
            var registerMethod = FindRegisterMethod("RegisterImageCallBackForRGB");
            if (registerMethod == null)
            {
                // 兜底：尝试任意 RegisterImageCallBackFor* 相关方法
                registerMethod = FindRegisterMethod("RegisterImageCallBackForBGR");
            }
            if (registerMethod == null)
            {
                throw new InvalidOperationException("未能定位图像回调注册方法（RegisterImageCallBackForXXX）。");
            }

            var args = BuildRegisterArgs(registerMethod, wrapper, pUser);
            var retObj = registerMethod.Invoke(_myCameraInstance, args) ?? 0;
            var ret = retObj is int retI ? retI : retObj is uint u ? unchecked((int)u) : 0;
            if (ret != (GetConstantIntValue("MV_OK") ?? 0))
            {
                Log.Warning("注册回调失败，返回：0x{Ret:X8}", ret);
                throw new InvalidOperationException("注册图像回调失败。");
            }
        }

        private Delegate CreateCallbackWrapper(Type cbDelegateType, Type frameInfoByRefType)
        {
            // frameInfoByRefType: ref MV_FRAME_OUT_INFO_EX
            var frameInfoValueType = frameInfoByRefType.GetElementType() ?? frameInfoByRefType;

            // 生成包装器：与 SDK cbOutputExDelegate 完全同签名
            var invokePs = cbDelegateType.GetMethod("Invoke")!.GetParameters();
            var dm = new DynamicMethod(
                "HikCallbackWrapper",
                typeof(void),
                new[] { invokePs[0].ParameterType, invokePs[1].ParameterType, invokePs[2].ParameterType },
                typeof(RealCameraDriver).Module,
                true);

            var il = dm.GetILGenerator();

            // 取出 this：pUser -> GCHandle.FromIntPtr -> Target
            // 然后调用实例方法 HandleFrameFromSdk(this, pData, frameInfoValue)
            var handleFromIntPtr = typeof(GCHandle).GetMethod(nameof(GCHandle.FromIntPtr), new[] { typeof(IntPtr) })!;
            var targetGetter = typeof(GCHandle).GetProperty(nameof(GCHandle.Target))!.GetGetMethod()!;

            var driverHandleMethod = typeof(RealCameraDriver).GetMethod(nameof(HandleFrameFromSdk), BindingFlags.NonPublic | BindingFlags.Static)!;

            // (1) driver = (RealCameraDriver)((GCHandle)pUser).Target
            il.Emit(OpCodes.Ldarg_2); // pUser
            il.Emit(OpCodes.Call, handleFromIntPtr);
            il.Emit(OpCodes.Call, targetGetter);
            il.Emit(OpCodes.Castclass, typeof(RealCameraDriver));

            // (2) pData
            il.Emit(OpCodes.Ldarg_0); // pData

            // (3) frameInfo：对 ref 参数做 ldobj 取值，再装箱为 object
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldobj, frameInfoValueType);
            il.Emit(OpCodes.Box, frameInfoValueType);

            // 调用：HandleFrameFromSdk(driver, pData, boxedFrameInfo)
            il.Emit(OpCodes.Call, driverHandleMethod);
            il.Emit(OpCodes.Ret);

            return dm.CreateDelegate(cbDelegateType);
        }
        /// <param name="driver">驱动实例。</param>
        /// <param name="pData">RGB 数据指针。</param>
        /// <param name="boxedFrameInfo">装箱后的 MV_FRAME_OUT_INFO_EX。</param>
        private static void HandleFrameFromSdk(RealCameraDriver driver, IntPtr pData, object boxedFrameInfo)
        {
            driver.SafeHandleFrameInternal(pData, boxedFrameInfo);
        }
        /// <param name="pData">指向 SDK 输出像素数据的非托管内存指针（通常为 RGB24，连续排布）。</param>
        /// <param name="frameInfo">
        /// SDK 输出的帧信息对象（装箱后的 <c>MV_FRAME_OUT_INFO_EX</c>），用于读取宽高与 <c>nFrameLen</c> 等字段。
        /// </param>
        /// <remarks>
        /// <para>
        /// 为什么必须拷贝：<c>pData</c> 指针由 SDK 管理，其生命周期不受 .NET 托管代码控制。
        /// 回调返回后 SDK 可能复用/释放缓冲，因此不能把指针直接传递给后续异步任务。
        /// </para>
        ///
        /// <para>
        /// 图像内存转换逻辑（重点，严格对应 UI/算法的 BGR32 约定）：
        /// </para>
        /// <para>
        /// 1) 从 <paramref name="frameInfo"/> 读取 <c>nWidth</c>、<c>nHeight</c>、<c>nFrameLen</c>，
        ///    校验 <paramref name="pData"/> 对应的字节长度至少覆盖 <c>width × height × 3</c>（RGB24）。
        /// </para>
        /// <para>
        /// 2) 为降低每帧临时内存的 GC 压力，使用 <see cref="ArrayPool{T}"/> 租用一个临时缓冲 <c>rgb24</c>，
        ///    并通过 <see cref="Marshal.Copy(IntPtr,byte[],int,int)"/> 将 <c>pData</c> 指针数据一次性拷贝到 <c>rgb24</c>。
        /// </para>
        /// <para>
        /// 3) 将 RGB24 转换为 BGR32：
        ///    - 每像素 4 字节：<c>B, G, R, Padding</c>
        ///    - 对像素 <c>i</c>：
        ///      <c>dst = i×4</c>，<c>src = i×3</c>
        ///      <c>bgr32[dst+0] = rgb24[src+2]</c>（B 来自 R）
        ///      <c>bgr32[dst+1] = rgb24[src+1]</c>（G 不变）
        ///      <c>bgr32[dst+2] = rgb24[src+0]</c>（R 来自 B）
        ///      <c>bgr32[dst+3] = 255</c>（Padding 用于对齐/可视化兼容）
        /// </para>
        /// <para>
        /// 4) 为什么临时 <c>rgb24</c> 可以归还池，而 <c>bgr32</c> 不能归还：
        ///    - 回调线程结束后，Service 层会在后台任务中使用 <see cref="ImageGrabbedEventArgs.PixelBuffer"/> 做再次复制与算法，
        ///      因此传出的 <c>bgr32</c> 必须在后续异步期间保持有效；
        ///    - 临时 <c>rgb24</c> 只用于本回调内的转换步骤，转换完成后即可归还 <see cref="ArrayPool{T}"/>。
        /// </para>
        ///
        /// <para>
        /// 最终行为：该方法只执行必要的拷贝/转换，并同步触发 <see cref="ImageGrabbed"/>。
        /// VisionManagerService 的订阅方会把后续 OK/NG 算法放到后台线程，从而避免阻塞相机回调线程；
        /// UI 更新则由 <c>VisionDisplayControl</c> 内部通过 Dispatcher 完成。
        /// </para>
        /// </remarks>
        private void SafeHandleFrameInternal(IntPtr pData, object frameInfo)
        {
            if (_disposed || pData == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var width = GetFieldIntValue(frameInfo, "nWidth");
                var height = GetFieldIntValue(frameInfo, "nHeight");
                var frameLen = GetFieldIntValue(frameInfo, "nFrameLen");

                if (width <= 0 || height <= 0)
                {
                    return;
                }

                var pixelCount = checked(width * height);

                // 假设注册的是 RGB24：理论 RGB bytes = width * height * 3
                var rgbLen = checked(pixelCount * 3);
                if (frameLen <= 0 || frameLen < rgbLen)
                {
                    return;
                }

                // 高效内存转换：SDK 原生指针 -> 托管 rgb24（一次 Marshal.Copy），再转为 BGR32（每像素 4 字节）。
                // 注意：传给 Service 的 PixelBuffer 必须在回调结束后仍有效，所以 bgr32 不能归还池。
                byte[] rgb24 = ArrayPool<byte>.Shared.Rent(rgbLen);
                try
                {
                    Marshal.Copy(pData, rgb24, 0, rgbLen);

                    var bgr32 = new byte[pixelCount * 4];
                    for (var i = 0; i < pixelCount; i++)
                    {
                        var rgbIndex = i * 3;
                        var dstIndex = i * 4;
                        // rgb24: R,G,B -> bgr32: B,G,R,Padding
                        bgr32[dstIndex + 0] = rgb24[rgbIndex + 2];
                        bgr32[dstIndex + 1] = rgb24[rgbIndex + 1];
                        bgr32[dstIndex + 2] = rgb24[rgbIndex + 0];
                        bgr32[dstIndex + 3] = 255;
                    }

                    var id = Interlocked.Increment(ref _frameId);
                    ImageGrabbed?.Invoke(this, new ImageGrabbedEventArgs
                    {
                        Width = width,
                        Height = height,
                        PixelBuffer = bgr32,
                        FrameId = id
                    });
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rgb24);
                }
            }
            catch
            {
                // 回调线程不要抛异常：避免 SDK 内部回调链路中断导致持续刷屏。
            }
        }

        private void SetLinkState(bool up)
        {
            if (IsLinkUp == up)
            {
                return;
            }

            IsLinkUp = up;
            LinkStateChanged?.Invoke(this, new CameraLinkEventArgs { IsUp = up });
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RealCameraDriver));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "RealCameraDriver Dispose 发生异常（忽略）。");
            }
        }

        private void LogHikError(string action, int errorCode)
        {
            // 错误码映射在不同 SDK 版本略有差异；这里给出可读中文提示，避免“刷出一堆十六进制”。
            Log.Error("海康相机：{Action} 失败，错误码=0x{Code:X8}", action, errorCode);
        }

        private int? GetConstantIntValue(string name)
        {
            if (_myCameraType == null)
            {
                return null;
            }

            var field = _myCameraType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.FieldType == typeof(int))
            {
                return (int)field.GetValue(null)!;
            }

            // 有些常量可能是 uint
            if (field != null && field.FieldType == typeof(uint))
            {
                return unchecked((int)(uint)field.GetValue(null)!);
            }

            return null;
        }

        private int InvokeMyCameraInt(string methodName)
        {
            if (_myCameraInstance == null || _myCameraType == null)
            {
                throw new InvalidOperationException("MyCamera 未初始化。");
            }

            var m = _myCameraType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (m == null)
            {
                m = _myCameraType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (m == null)
                {
                    throw new InvalidOperationException($"SDK 缺失方法：{methodName}。");
                }
            }

            var retObj = m.Invoke(_myCameraInstance, Array.Empty<object>());
            return ToInt32Loose(retObj);
        }

        private static int ToInt32Loose(object? value)
        {
            if (value == null)
            {
                return 0;
            }

            return value switch
            {
                int i => i,
                uint u => unchecked((int)u),
                short s => s,
                ushort us => us,
                byte b => b,
                sbyte sb => sb,
                long l => unchecked((int)l),
                ulong ul => unchecked((int)ul),
                _ => Convert.ToInt32(value)
            };
        }

        private void TryInvokeMyCameraInt(string methodName)
        {
            if (_myCameraInstance == null || _myCameraType == null)
            {
                return;
            }

            try
            {
                InvokeMyCameraInt(methodName);
            }
            catch
            {
                // ignore
            }
        }

        private void TrySetIntValue(string key, uint value)
        {
            if (_myCameraType == null || _myCameraInstance == null)
            {
                return;
            }

            var m = _myCameraType.GetMethod("MV_CC_SetIntValue_NET", BindingFlags.Public | BindingFlags.Instance);
            if (m == null)
            {
                return;
            }

            // MV_CC_SetIntValue_NET(string pKey, uint value) -> int/uint（SDK 版本可能不同）
            var retObj = m.Invoke(_myCameraInstance, new object[] { key, value }) ?? 0;
            var ret = retObj is int i ? i : retObj is uint u ? unchecked((int)u) : 0;
            if (ret != (GetConstantIntValue("MV_OK") ?? 0))
            {
                Log.Warning("设置 {Key} 失败（继续），ret=0x{Ret:X8}", key, ret);
            }
        }

        private void TrySetEnumValue(string key, int value)
        {
            if (_myCameraType == null || _myCameraInstance == null)
            {
                return;
            }

            var m = _myCameraType.GetMethod("MV_CC_SetEnumValue_NET", BindingFlags.Public | BindingFlags.Instance);
            if (m == null)
            {
                return;
            }

            var retObj = m.Invoke(_myCameraInstance, new object[] { key, value }) ?? 0;
            var ret = retObj is int i ? i : retObj is uint u ? unchecked((int)u) : 0;
            if (ret != (GetConstantIntValue("MV_OK") ?? 0))
            {
                Log.Warning("设置 {Key} 失败（继续），ret=0x{Ret:X8}", key, ret);
            }
        }

        private Type? FindCbOutputExDelegateType(Type myCameraType)
        {
            // 通过委托签名（Invoke 参数类型包含：IntPtr, byref某帧结构, IntPtr）来定位。
            var all = myCameraType.Assembly.GetTypes();
            foreach (var t in all)
            {
                if (!typeof(Delegate).IsAssignableFrom(t))
                {
                    continue;
                }

                if (!t.Name.Contains("cbOutputEx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var invoke = t.GetMethod("Invoke");
                if (invoke == null)
                {
                    continue;
                }

                var ps = invoke.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(IntPtr) && ps[2].ParameterType == typeof(IntPtr))
                {
                    return t;
                }
            }

            return null;
        }

        private MethodInfo? FindRegisterMethod(string keyword)
        {
            if (_myCameraType == null)
            {
                return null;
            }

            // 从 MyCamera 中查找包含 keyword 且签名为 (delegate, IntPtr) -> int 的方法。
            var methods = _myCameraType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            foreach (var m in methods)
            {
                if (!m.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType.IsSubclassOf(typeof(Delegate)) && ps[1].ParameterType == typeof(IntPtr))
                {
                    return m;
                }
            }

            return null;
        }

        private object[] BuildRegisterArgs(MethodInfo registerMethod, Delegate callbackDelegate, IntPtr pUser)
        {
            var ps = registerMethod.GetParameters();
            var args = new object[ps.Length];
            args[0] = callbackDelegate;
            args[1] = pUser;
            return args;
        }

        private int GetFieldIntValue(object obj, string fieldName)
        {
            var f = obj.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null)
            {
                // 也可能是属性
                var p = obj.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p == null)
                {
                    return 0;
                }

                var pv = p.GetValue(obj);
                return pv is int i ? i : pv is uint u ? unchecked((int)u) : 0;
            }

            var v = f.GetValue(obj);
            return v is int i2 ? i2 : v is uint u2 ? unchecked((int)u2) : 0;
        }

        private object? GetFieldValue(object? obj, string fieldName)
        {
            if (obj == null)
            {
                return null;
            }

            var t = obj.GetType();
            var f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                return f.GetValue(obj);
            }

            var p = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p?.GetValue(obj);
        }

        private string ConvertCurrentIpToString(object? ipField)
        {
            if (ipField == null)
            {
                return string.Empty;
            }

            // chCurrentIp 可能是 byte[]（'\0' 结尾）
            if (ipField is byte[] b)
            {
                // 兼容两类常见布局：
                // 1) ASCII 字符串（"192.168.0.2\0"）
                // 2) 4 字节 IPv4（C0 A8 00 02）
                if (b.Length == 4)
                {
                    return $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
                }

                var ascii = Encoding.ASCII.GetString(b).TrimEnd('\0', ' ');
                if (ascii.Contains('.'))
                {
                    return ascii;
                }

                // 部分 SDK 会给大块字节数组，前 4 字节即 IPv4
                if (b.Length >= 4)
                {
                    return $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
                }

                return string.Empty;
            }

            // 或者可能是固定长度字符串
            if (ipField is string s)
            {
                return s.Trim();
            }

            // 常见海康结构字段：nCurrentIp (UInt32)
            if (ipField is uint ipU32)
            {
                return UInt32ToIpv4(ipU32);
            }

            if (ipField is int ipI32)
            {
                return UInt32ToIpv4(unchecked((uint)ipI32));
            }

            return ipField.ToString() ?? string.Empty;
        }

        private static object? GetCurrentIpField(object? stGigEInfo)
        {
            if (stGigEInfo == null)
            {
                return null;
            }

            // 优先字符串字段，再尝试数值字段
            var chCurrentIp = GetFieldValueStatic(stGigEInfo, "chCurrentIp");
            if (chCurrentIp != null)
            {
                return chCurrentIp;
            }

            var nCurrentIp = GetFieldValueStatic(stGigEInfo, "nCurrentIp");
            if (nCurrentIp != null)
            {
                return nCurrentIp;
            }

            // 兼容不同命名风格
            return GetFieldValueStatic(stGigEInfo, "CurrentIp");
        }

        private static object? GetFieldValueStatic(object obj, string fieldName)
        {
            var t = obj.GetType();
            var f = t.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null)
            {
                return f.GetValue(obj);
            }

            var p = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p?.GetValue(obj);
        }

        private static string UInt32ToIpv4(uint ip)
        {
            // SDK 常见顺序为网络字节序，高位在前。
            var b1 = (ip >> 24) & 0xFF;
            var b2 = (ip >> 16) & 0xFF;
            var b3 = (ip >> 8) & 0xFF;
            var b4 = ip & 0xFF;
            return $"{b1}.{b2}.{b3}.{b4}";
        }
    }
}


