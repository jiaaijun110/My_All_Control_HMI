using Drivers.DVision.Siemens;

namespace Services.VisionServices
{
    public sealed class VisionControlService : IDisposable
    {
        private readonly ICameraDriver _camera;
        private readonly object _statsLock = new();
        private int _okCount;
        private int _ngCount;
        private int _totalCount;
        private bool _lastResultOk = true;
        private bool _disposed;
        public int OkCount
        {
            get
            {
                lock (_statsLock)
                {
                    return _okCount;
                }
            }
        }
        public int NgCount
        {
            get
            {
                lock (_statsLock)
                {
                    return _ngCount;
                }
            }
        }
        public int TotalInspections
        {
            get
            {
                lock (_statsLock)
                {
                    return _totalCount;
                }
            }
        }
        public bool LastResultOk
        {
            get
            {
                lock (_statsLock)
                {
                    return _lastResultOk;
                }
            }
        }
        public event EventHandler? StatisticsChanged;
        public event EventHandler<FramePresentEventArgs>? FrameReadyForDisplay;
        /// <param name="camera">相机驱动实例。</param>
        public VisionControlService(ICameraDriver camera)
        {
            _camera = camera;
            _camera.ImageGrabbed += OnImageGrabbed;
        }
        public void Start()
        {
            ThrowIfDisposed();
            _camera.Open();
            _camera.StartGrabbing();
        }
        public void Stop()
        {
            _camera.StopGrabbing();
            _camera.Close();
        }
        private void OnImageGrabbed(object? sender, ImageGrabbedEventArgs e)
        {
            _ = ProcessFrameAsync(e);
        }
        /// <param name="e">原始帧参数。</param>
        private async Task ProcessFrameAsync(ImageGrabbedEventArgs e)
        {
            var pixelCopy = new byte[e.PixelBuffer.Length];
            Buffer.BlockCopy(e.PixelBuffer, 0, pixelCopy, 0, e.PixelBuffer.Length);
            var w = e.Width;
            var h = e.Height;
            var frameId = e.FrameId;

            try
            {
                // 逻辑意图：模拟视觉算法耗时，使用线程池等待而非阻塞 UI。
                await Task.Delay(50).ConfigureAwait(false);

                // 骨架判定：对像素和取模，仅作演示。
                var ok = (SumPixels(pixelCopy) + frameId) % 17 != 0;

                lock (_statsLock)
                {
                    _totalCount++;
                    if (ok)
                    {
                        _okCount++;
                    }
                    else
                    {
                        _ngCount++;
                    }

                    _lastResultOk = ok;
                }

                StatisticsChanged?.Invoke(this, EventArgs.Empty);
                FrameReadyForDisplay?.Invoke(this, new FramePresentEventArgs(pixelCopy, w, h, ok, frameId));
            }
            catch
            {
                // 骨架阶段吞异常，避免 Task 静默失败；后续可接日志。
            }
        }
        /// <param name="bgr32">BGR32 字节。</param>
        /// <returns>字节和（截断为合理范围）。</returns>
        private static int SumPixels(byte[] bgr32)
        {
            var sum = 0;
            for (var i = 0; i < bgr32.Length; i++)
            {
                sum += bgr32[i];
            }

            return sum;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VisionControlService));
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
            _camera.ImageGrabbed -= OnImageGrabbed;
            Stop();
            if (_camera is IDisposable d)
            {
                d.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
    public sealed class FramePresentEventArgs : EventArgs
    {
        /// <param name="bgr32Pixels">独立拷贝的 BGR32 像素，长度 width×height×4。</param>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        /// <param name="isOk">判定 OK。</param>
        /// <param name="frameId">帧号。</param>
        public FramePresentEventArgs(byte[] bgr32Pixels, int width, int height, bool isOk, long frameId)
        {
            Bgr32Pixels = bgr32Pixels;
            Width = width;
            Height = height;
            IsOk = isOk;
            FrameId = frameId;
        }
        public byte[] Bgr32Pixels { get; }
        public int Width { get; }
        public int Height { get; }
        public bool IsOk { get; }
        public long FrameId { get; }
    }
}

